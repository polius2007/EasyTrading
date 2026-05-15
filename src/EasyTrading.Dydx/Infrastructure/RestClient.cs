using System.Net;
using System.Text;
using System.Text.Json;
using EasyTrading.Abstractions;

namespace EasyTrading.Dydx.Infrastructure;

/// <summary>
/// REST wrapper for the dYdX v4 Indexer (read-only endpoints: markets, orderbook, candles,
/// trades, perpetualPositions, fills, etc). Signed-write Cosmos SDK transactions go through
/// a separate <see cref="CosmosClient"/> that broadcasts <c>TxRaw</c> bytes to the validator's
/// REST gateway at <c>/cosmos/tx/v1beta1/txs</c>.
/// </summary>
internal sealed class RestClient
{
    private readonly HttpClient _http;
    private readonly DydxClientOptions _options;
    private readonly Uri _baseUrl;

    public RestClient(HttpClient http, DydxClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options;
        _baseUrl = options.GetEffectiveRestBaseUrl();
    }

    /// <summary>Public GET with optional query string.</summary>
    public async Task<TResponse> GetAsync<TResponse>(string path, IDictionary<string, string>? query, CancellationToken ct)
    {
        var body = await SendRawAsync(BuildUrl(path, query), ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<TResponse>(body, JsonOptions.Default);
        return result
            ?? throw new ExchangeApiException("dYdX Indexer returned a null JSON document.");
    }

    /// <summary>Public GET returning the raw <see cref="JsonElement"/> — used when array vs object shape varies.</summary>
    public async Task<JsonElement> GetRawAsync(string path, IDictionary<string, string>? query, CancellationToken ct)
    {
        var body = await SendRawAsync(BuildUrl(path, query), ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private Uri BuildUrl(string path, IDictionary<string, string>? query)
    {
        // Indexer paths begin without a leading slash because the base URL already ends with /v4/.
        var trimmed = path.TrimStart('/');

        var ub = new UriBuilder(new Uri(_baseUrl, trimmed));
        if (query is { Count: > 0 })
        {
            var sb = new StringBuilder();
            foreach (var kv in query)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
            }
            ub.Query = sb.ToString();
        }
        return ub.Uri;
    }

    private async Task<string> SendRawAsync(Uri url, CancellationToken ct)
    {
        HttpResult result;
        try
        {
            result = await Http.SendAsync(_http, HttpMethod.Get, url, content: null, _options.RetryPolicy, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ExchangeApiException($"dYdX Indexer request failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ExchangeApiException("dYdX Indexer request timed out.", innerException: ex);
        }

        if ((int)result.StatusCode is < 200 or >= 300)
        {
            throw result.StatusCode switch
            {
                HttpStatusCode.TooManyRequests
                    => new RateLimitException($"dYdX Indexer rate-limited the request. Body: {Truncate(result.Body)}"),

                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    => new AuthenticationException($"dYdX Indexer rejected the request ({(int)result.StatusCode}). Body: {Truncate(result.Body)}"),

                _ => new ExchangeApiException(
                    $"dYdX Indexer request failed: {(int)result.StatusCode} {result.ReasonPhrase}. Body: {Truncate(result.Body)}"),
            };
        }

        return result.Body;
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
