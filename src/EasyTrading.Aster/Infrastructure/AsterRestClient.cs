using System.Net;
using System.Text.Json;
using EasyTrading.Abstractions;

namespace EasyTrading.Aster.Infrastructure;

/// <summary>
/// Thin REST wrapper for Aster's V3 Futures API. Public (Market Data) GET requests don't need
/// signing — that's the only flavour supported in the initial scaffolding. Signed
/// (<c>TRADE</c> / <c>USER_DATA</c>) endpoints will pass through <c>AsterSigner</c>
/// (forthcoming) in a later phase.
/// </summary>
internal sealed class AsterRestClient
{
    private readonly HttpClient _http;
    private readonly AsterClientOptions _options;
    private readonly Uri _baseUrl;

    public AsterRestClient(HttpClient http, AsterClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options;
        _baseUrl = options.GetEffectiveRestBaseUrl();
    }

    /// <summary>
    /// Public GET against a relative path. <paramref name="query"/> is appended as a query string.
    /// </summary>
    public async Task<TResponse> GetAsync<TResponse>(string path, IDictionary<string, string>? query, CancellationToken ct)
    {
        var url = BuildUrl(path, query);
        var body = await SendAsync(HttpMethod.Get, url, content: null, ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<TResponse>(body, AsterJsonOptions.Default);
        return result
            ?? throw new ExchangeApiException("Aster returned a null JSON document.");
    }

    /// <summary>Public GET returning the raw <see cref="JsonElement"/> for heterogeneous-array responses.</summary>
    public async Task<JsonElement> GetRawAsync(string path, IDictionary<string, string>? query, CancellationToken ct)
    {
        var url = BuildUrl(path, query);
        var body = await SendAsync(HttpMethod.Get, url, content: null, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private Uri BuildUrl(string path, IDictionary<string, string>? query)
    {
        if (!path.StartsWith('/'))
            path = "/" + path;

        var ub = new UriBuilder(new Uri(_baseUrl, path));
        if (query is { Count: > 0 })
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in query)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
            }
            ub.Query = sb.ToString();
        }
        return ub.Uri;
    }

    private async Task<string> SendAsync(HttpMethod method, Uri url, HttpContent? content, CancellationToken ct)
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

        return result.Body;
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
