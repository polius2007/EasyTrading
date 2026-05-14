using System.Net;
using System.Text.Json;
using EasyTrading.Abstractions;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Typed wrapper around <c>POST /exchange</c> — every write action goes through here. Handles
/// signing (delegating to <see cref="HlSigner"/>), nonce allocation, request envelope assembly,
/// HTTP transport, and error mapping into typed <see cref="ExchangeApiException"/> subclasses.
/// </summary>
internal sealed class HlExchangeClient
{
    private readonly HttpClient _http;
    private readonly HyperLiquidClientOptions _options;
    private readonly HlNonce _nonce;
    private readonly Uri _exchangeUrl;

    public HlExchangeClient(HttpClient http, HyperLiquidClientOptions options, HlNonce nonce)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(nonce);

        _http = http;
        _options = options;
        _nonce = nonce;
        _exchangeUrl = new Uri(options.GetEffectiveRestBaseUrl(), "exchange");
    }

    // ─── L1 (action-signed) actions: orders, cancels, modifies, leverage, vault transfers ──

    /// <summary>
    /// Send an action-signed payload (the standard L1 / phantom-agent flavour). Returns the
    /// <c>response</c> portion of the success envelope as a <see cref="JsonElement"/>.
    /// </summary>
    public async Task<JsonElement> SendL1Async(HlMap action, long? expiresAfter, CancellationToken ct)
    {
        var creds = RequireCredentials();
        var isMainnet = _options.Network == HyperLiquidNetwork.Mainnet;
        var nonce = _nonce.Next();
        var vaultAddress = creds.VaultAddress;

        var signature = HlSigner.SignL1Action(action, vaultAddress, nonce, expiresAfter, isMainnet, creds.PrivateKey);

        var envelope = new HlMap()
            .Add("action", action)
            .Add("nonce", nonce)
            .Add("signature", new HlMap()
                .Add("r", signature.R)
                .Add("s", signature.S)
                .Add("v", signature.V));

        if (vaultAddress is not null)
            envelope.Add("vaultAddress", vaultAddress);
        if (expiresAfter is not null)
            envelope.Add("expiresAfter", expiresAfter.Value);

        return await PostAsync(envelope, ct).ConfigureAwait(false);
    }

    // ─── User-signed actions: transfers, withdrawals, approvals ────────────────────────────

    /// <summary>
    /// Send a user-signed payload (transfer, withdrawal, approval). <paramref name="action"/>
    /// must contain only the schema-defined fields; <c>hyperliquidChain</c> and
    /// <c>signatureChainId</c> are added here before signing.
    /// </summary>
    public async Task<JsonElement> SendUserAsync(
        HlMap action,
        string primaryType,
        IReadOnlyList<(string Name, string Type)> typeSchema,
        CancellationToken ct)
    {
        var creds = RequireCredentials();
        var isMainnet = _options.Network == HyperLiquidNetwork.Mainnet;

        // The user-signed flavour requires these two fields, signed as part of the message.
        action.Add("hyperliquidChain", isMainnet ? "Mainnet" : "Testnet");
        action.Add("signatureChainId", "0x66eee");

        var signature = HlSigner.SignUserAction(action, primaryType, typeSchema, creds.PrivateKey);

        // The "nonce" wire field for user-signed actions reuses the action's time/nonce field.
        // We extract whichever timestamp-shaped field is present (time, nonce, …) for the envelope.
        var envelopeNonce = ExtractEnvelopeNonce(action);

        var envelope = new HlMap()
            .Add("action", action)
            .Add("nonce", envelopeNonce)
            .Add("signature", new HlMap()
                .Add("r", signature.R)
                .Add("s", signature.S)
                .Add("v", signature.V));

        return await PostAsync(envelope, ct).ConfigureAwait(false);
    }

    private static long ExtractEnvelopeNonce(HlMap action)
    {
        if (action.TryGetValue("nonce", out var n) && n is long nL) return nL;
        if (action.TryGetValue("time", out var t) && t is long tL) return tL;
        // Fallback: convert from boxed numeric (int/uint/etc.) using invariant culture.
        if (action.TryGetValue("nonce", out var n2) && n2 is not null)
            return Convert.ToInt64(n2, System.Globalization.CultureInfo.InvariantCulture);
        if (action.TryGetValue("time", out var t2) && t2 is not null)
            return Convert.ToInt64(t2, System.Globalization.CultureInfo.InvariantCulture);
        throw new InvalidOperationException("User-signed action has neither 'nonce' nor 'time' field.");
    }

    // ─── HTTP transport + error mapping ────────────────────────────────────────────────────

    private async Task<JsonElement> PostAsync(HlMap envelope, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(envelope, HlJsonOptions.Default);

        HlHttpResult result;
        try
        {
            result = await HlHttp.PostJsonAsync(_http, _exchangeUrl, json, _options.RetryPolicy, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ExchangeApiException($"HyperLiquid Exchange request failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ExchangeApiException("HyperLiquid Exchange request timed out.", innerException: ex);
        }

        if ((int)result.StatusCode is < 200 or >= 300)
        {
            throw result.StatusCode switch
            {
                HttpStatusCode.TooManyRequests
                    => new RateLimitException($"HyperLiquid rate-limited the request. Body: {Truncate(result.Body)}"),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    => new AuthenticationException($"HyperLiquid rejected the request ({(int)result.StatusCode}). Body: {Truncate(result.Body)}"),
                _ => new ExchangeApiException(
                    $"HyperLiquid Exchange request failed: {(int)result.StatusCode} {result.ReasonPhrase}. Body: {Truncate(result.Body)}"),
            };
        }

        // Parse and unwrap the response envelope.
        using var doc = JsonDocument.Parse(result.Body);
        var root = doc.RootElement.Clone();

        if (root.TryGetProperty("status", out var statusEl) &&
            string.Equals(statusEl.GetString(), "err", StringComparison.Ordinal))
        {
            var message = root.TryGetProperty("response", out var errResp) && errResp.ValueKind == JsonValueKind.String
                ? errResp.GetString() ?? "(no message)"
                : root.GetRawText();
            ThrowTypedError(message);
        }

        if (!root.TryGetProperty("response", out var responseEl))
        {
            throw new ExchangeApiException($"HyperLiquid Exchange returned an unexpected envelope: {Truncate(result.Body)}");
        }

        return responseEl.Clone();
    }

    /// <summary>Maps an HL error string to the most appropriate typed exception.</summary>
    private static void ThrowTypedError(string message)
    {
        var m = message.ToLowerInvariant();

        if (m.Contains("insufficient") || m.Contains("balance"))
            throw new InsufficientFundsException(message);

        if (m.Contains("rate limit") || m.Contains("too many"))
            throw new RateLimitException(message);

        if (m.Contains("signature") || m.Contains("recover") || m.Contains("agent"))
            throw new AuthenticationException(message);

        if (m.Contains("tick") || m.Contains("size") || m.Contains("invalid") || m.Contains("min"))
            throw new InvalidOrderException(message);

        throw new ExchangeApiException(message);
    }

    private HyperLiquidCredentials RequireCredentials()
        => _options.Credentials
           ?? throw new AuthenticationException(
               "HyperLiquidClientOptions.Credentials are required for Exchange-endpoint (write) actions.");

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
