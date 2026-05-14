using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EasyTrading.Abstractions;

namespace EasyTrading.Aster.Infrastructure;

/// <summary>
/// Thin REST wrapper for Aster's V3 Futures API. Public GETs are unsigned; TRADE / USER_DATA /
/// USER_STREAM endpoints flow through <see cref="SendSignedAsync{T}"/> which appends
/// <c>nonce</c> + <c>signer</c> to the parameters, EIP-712-signs the URL-encoded form via
/// <see cref="AsterSigner"/>, and attaches the signature.
/// </summary>
internal sealed class AsterRestClient
{
    private readonly HttpClient _http;
    private readonly AsterClientOptions _options;
    private readonly AsterNonce _nonce;
    private readonly Uri _baseUrl;

    public AsterRestClient(HttpClient http, AsterClientOptions options, AsterNonce nonce)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(nonce);

        _http = http;
        _options = options;
        _nonce = nonce;
        _baseUrl = options.GetEffectiveRestBaseUrl();
    }

    // ─── Public (unsigned) ───────────────────────────────────────────────────

    /// <summary>Public GET with optional query string. No signing.</summary>
    public async Task<TResponse> GetAsync<TResponse>(string path, IDictionary<string, string>? query, CancellationToken ct)
    {
        var url = BuildUrl(path, query);
        var body = await SendRawAsync(HttpMethod.Get, url, content: null, ct).ConfigureAwait(false);
        return DeserializeOrThrow<TResponse>(body);
    }

    /// <summary>Public GET returning the raw <see cref="JsonElement"/> for heterogeneous-array responses.</summary>
    public async Task<JsonElement> GetRawAsync(string path, IDictionary<string, string>? query, CancellationToken ct)
    {
        var url = BuildUrl(path, query);
        var body = await SendRawAsync(HttpMethod.Get, url, content: null, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    // ─── Signed (TRADE / USER_DATA / USER_STREAM) ────────────────────────────

    /// <summary>
    /// Send a signed request. <paramref name="parameters"/> are the action-specific fields
    /// (without <c>nonce</c>, <c>signer</c>, or <c>signature</c>) — this method injects them.
    /// Returns the deserialised response.
    /// </summary>
    /// <typeparam name="TResponse">Response shape.</typeparam>
    /// <param name="method">HTTP method. POST/PUT/DELETE send a form body; GET sends a query string.</param>
    /// <param name="path">Endpoint path (e.g. <c>/fapi/v3/order</c>).</param>
    /// <param name="parameters">Action-specific parameters. Modified in-place to add nonce/signer/signature.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<TResponse> SendSignedAsync<TResponse>(
        HttpMethod method,
        string path,
        IDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var body = await SignAndSendAsync(method, path, parameters, ct).ConfigureAwait(false);
        return DeserializeOrThrow<TResponse>(body);
    }

    /// <summary>Same as <see cref="SendSignedAsync{T}"/> but returns the raw response body as a <see cref="JsonElement"/>.</summary>
    public async Task<JsonElement> SendSignedRawAsync(
        HttpMethod method,
        string path,
        IDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var body = await SignAndSendAsync(method, path, parameters, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<string> SignAndSendAsync(
        HttpMethod method,
        string path,
        IDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var creds = _options.Credentials
            ?? throw new AuthenticationException(
                "AsterClientOptions.Credentials are required for signed endpoints.");

        // 1. Inject nonce (microseconds) and signer (API wallet address). Insertion-order matters
        //    for the URL-encoded message — keep nonce + signer at the end so they sit predictably.
        parameters["nonce"]  = _nonce.Next().ToString(CultureInfo.InvariantCulture);
        parameters["signer"] = creds.SignerAddress;

        // 2. URL-encode the parameters in dictionary iteration order. That's the `msg` Aster signs.
        var msg = UrlEncodeForm(parameters);

        // 3. EIP-712 sign and add the signature alongside the other parameters.
        var signature = AsterSigner.Sign(msg, creds.PrivateKey);
        parameters["signature"] = signature;

        // 4. Send.
        if (method == HttpMethod.Get || method == HttpMethod.Delete)
        {
            // For GETs (and Aster permits DELETE-by-query) send everything as a query string.
            var url = BuildUrl(path, parameters);
            return await SendRawAsync(method, url, content: null, ct).ConfigureAwait(false);
        }
        else
        {
            // POST/PUT: form-urlencoded body.
            var bodyText = UrlEncodeForm(parameters);
            var content = new StringContent(bodyText, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            var url = new Uri(_baseUrl, path);
            return await SendRawAsync(method, url, content, ct).ConfigureAwait(false);
        }
    }

    // ─── HTTP transport (shared) ─────────────────────────────────────────────

    private async Task<string> SendRawAsync(HttpMethod method, Uri url, HttpContent? content, CancellationToken ct)
    {
        AsterHttpResult result;
        try
        {
            result = await AsterHttp.SendAsync(_http, method, url, content, _options.RetryPolicy, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ExchangeApiException($"Aster request failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ExchangeApiException("Aster request timed out.", innerException: ex);
        }

        if ((int)result.StatusCode is < 200 or >= 300)
            ThrowFromStatus(result);

        // Aster sometimes returns 200 with an error envelope (e.g. order placement that
        // failed validation). Detect and map.
        MaybeThrowFromBody(result.Body);

        return result.Body;
    }

    private static void ThrowFromStatus(AsterHttpResult result)
    {
        throw result.StatusCode switch
        {
            HttpStatusCode.TooManyRequests
                => new RateLimitException($"Aster rate-limited the request. Body: {Truncate(result.Body)}"),

            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => new AuthenticationException($"Aster rejected the request ({(int)result.StatusCode}). Body: {Truncate(result.Body)}"),

            _ => new ExchangeApiException(
                $"Aster request failed: {(int)result.StatusCode} {result.ReasonPhrase}. Body: {Truncate(result.Body)}"),
        };
    }

    /// <summary>
    /// Aster sometimes returns 200 with an error JSON envelope: <c>{ "code": -1121, "msg": "Invalid symbol." }</c>.
    /// We sniff for that shape and map to a typed exception.
    /// </summary>
    private static void MaybeThrowFromBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body[0] != '{') return;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch { return; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("code", out var codeEl)) return;
            if (codeEl.ValueKind != JsonValueKind.Number) return;

            var code = codeEl.GetInt32();
            // 200 is the "all good" sentinel (e.g. POST /fapi/v3/positionSide/dual response).
            // Anything negative is an error.
            if (code >= 0) return;

            var msg = root.TryGetProperty("msg", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                ? msgEl.GetString() ?? string.Empty
                : "(no message)";

            // Map Aster error codes to typed exceptions. Codes per Aster docs §Error Codes.
            throw code switch
            {
                -1003 => new RateLimitException(msg),                          // TOO_MANY_REQUESTS
                -1021 => new ExchangeApiException(msg, code.ToString(CultureInfo.InvariantCulture)), // INVALID_TIMESTAMP / nonce
                -1022 => new AuthenticationException(msg),                     // INVALID_SIGNATURE
                -2010 or -2011 or -2013 or -2014 or -2015 or -2018 or -2019 or -2020 or -2021 or -2022 or -2023 or -2024 or -2025
                    => new InvalidOrderException(msg),                          // various order rejections
                -2018 or -2019 or -2027
                    => new InsufficientFundsException(msg),
                _ => new ExchangeApiException(msg, code.ToString(CultureInfo.InvariantCulture)),
            };
        }
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static TResponse DeserializeOrThrow<TResponse>(string body)
    {
        var result = JsonSerializer.Deserialize<TResponse>(body, AsterJsonOptions.Default);
        return result
            ?? throw new ExchangeApiException("Aster returned a null JSON document.");
    }

    private Uri BuildUrl(string path, IDictionary<string, string>? query)
    {
        if (!path.StartsWith('/'))
            path = "/" + path;

        var ub = new UriBuilder(new Uri(_baseUrl, path));
        if (query is { Count: > 0 })
            ub.Query = UrlEncodeForm(query);
        return ub.Uri;
    }

    /// <summary>
    /// URL-encode key/value pairs in iteration order (matching Python's
    /// <c>urllib.parse.urlencode(dict)</c>, which is what Aster's reference SDK uses).
    /// </summary>
    private static string UrlEncodeForm(IDictionary<string, string> kvs)
    {
        var sb = new StringBuilder();
        foreach (var kv in kvs)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kv.Value));
        }
        return sb.ToString();
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
