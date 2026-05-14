using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyTrading.Abstractions;

namespace EasyTrading.Dydx.Infrastructure;

/// <summary>
/// Talks to a Cosmos SDK validator node's REST gateway (typically running on port 1317) to:
/// <list type="bullet">
///   <item><description>Query <c>account_number</c> + current <c>sequence</c> for the trading address.</description></item>
///   <item><description>Broadcast signed <c>TxRaw</c> bytes via <c>POST /cosmos/tx/v1beta1/txs</c>.</description></item>
/// </list>
/// </summary>
internal sealed class CosmosClient
{
    private readonly HttpClient _http;
    private readonly Uri _validatorBaseUrl;
    private readonly DydxRetryOptions _retry;

    public CosmosClient(HttpClient http, Uri validatorBaseUrl, DydxRetryOptions retry)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(validatorBaseUrl);
        _http = http;
        _validatorBaseUrl = validatorBaseUrl;
        _retry = retry;
    }

    /// <summary>Fetch the (account_number, sequence) pair for an address.</summary>
    public async Task<(ulong AccountNumber, ulong Sequence)> GetAccountAsync(string address, CancellationToken ct)
    {
        var url = new Uri(_validatorBaseUrl, $"/cosmos/auth/v1beta1/accounts/{address}");
        var result = await Http.SendAsync(_http, HttpMethod.Get, url, content: null, _retry, ct).ConfigureAwait(false);
        if ((int)result.StatusCode is < 200 or >= 300)
            throw new ExchangeApiException($"Cosmos account query failed ({(int)result.StatusCode}): {Truncate(result.Body)}");

        using var doc = JsonDocument.Parse(result.Body);
        // The shape is `{ "account": { "@type": "...BaseAccount", "address": "...", "account_number": "...", "sequence": "...", ... } }`.
        if (!doc.RootElement.TryGetProperty("account", out var account))
            throw new ExchangeApiException($"Cosmos account query: missing 'account' field. Body: {Truncate(result.Body)}");

        var num = ulong.Parse(account.GetProperty("account_number").GetString() ?? "0", CultureInfo.InvariantCulture);
        var seq = ulong.Parse(account.GetProperty("sequence").GetString() ?? "0", CultureInfo.InvariantCulture);
        return (num, seq);
    }

    /// <summary>
    /// Broadcast a signed transaction. <paramref name="txBytes"/> is the output of
    /// <see cref="TransactionBuilder.BuildAndSign"/>. <paramref name="mode"/> is one of
    /// <c>BROADCAST_MODE_SYNC</c> (recommended for trading: wait for mempool ack but not block
    /// inclusion) or <c>BROADCAST_MODE_BLOCK</c> (wait for block inclusion).
    /// </summary>
    public async Task<BroadcastResult> BroadcastAsync(byte[] txBytes, string mode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(txBytes);
        ArgumentException.ThrowIfNullOrEmpty(mode);

        var url = new Uri(_validatorBaseUrl, "/cosmos/tx/v1beta1/txs");
        var payload = new BroadcastRequest
        {
            TxBytes = Convert.ToBase64String(txBytes),
            Mode    = mode,
        };
        var json = JsonSerializer.Serialize(payload);

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var result = await Http.SendAsync(_http, HttpMethod.Post, url, content, _retry, ct).ConfigureAwait(false);

        if ((int)result.StatusCode is < 200 or >= 300)
            throw new ExchangeApiException($"Cosmos broadcast failed ({(int)result.StatusCode}): {Truncate(result.Body)}");

        // Response shape: { "tx_response": { "code": 0, "txhash": "...", "raw_log": "...", "height": "...", ... } }
        using var doc = JsonDocument.Parse(result.Body);
        if (!doc.RootElement.TryGetProperty("tx_response", out var tr))
            return new BroadcastResult(false, null, $"Unexpected response shape: {Truncate(result.Body)}");

        var code = tr.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : 0;
        var hash = tr.TryGetProperty("txhash", out var hashEl) ? hashEl.GetString() : null;
        var log  = tr.TryGetProperty("raw_log", out var logEl) ? logEl.GetString() : null;

        return new BroadcastResult(Success: code == 0, TxHash: hash, ErrorMessage: code == 0 ? null : (log ?? $"code {code}"));
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";

    private sealed class BroadcastRequest
    {
        [JsonPropertyName("tx_bytes")] public string TxBytes { get; set; } = string.Empty;
        [JsonPropertyName("mode")]     public string Mode    { get; set; } = "BROADCAST_MODE_SYNC";
    }
}

/// <summary>Outcome of a Cosmos transaction broadcast.</summary>
internal readonly record struct BroadcastResult(bool Success, string? TxHash, string? ErrorMessage);
