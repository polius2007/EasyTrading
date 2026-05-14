using System.Net;
using System.Text.Json;
using EasyTrading.Abstractions;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Typed wrapper around the HyperLiquid <c>POST /info</c> endpoint. All requests share the
/// configured retry policy via <see cref="Http"/>.
/// </summary>
/// <remarks>
/// Every Info request is a JSON POST with a body of <c>{ "type": "&lt;requestType&gt;", ... }</c>.
/// This class serializes the supplied request object, sends it, and deserializes the response.
/// For responses with heterogeneous-array shapes (e.g. <c>metaAndAssetCtxs</c>, <c>portfolio</c>),
/// callers use <see cref="PostRawAsync"/> and parse the resulting <see cref="JsonElement"/> manually.
/// </remarks>
internal sealed class InfoClient
{
    private readonly HttpClient _http;
    private readonly HyperLiquidClientOptions _options;
    private readonly Uri _infoUrl;

    public InfoClient(HttpClient http, HyperLiquidClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options;
        _infoUrl = new Uri(options.GetEffectiveRestBaseUrl(), "info");
    }

    /// <summary>Send a typed Info request and deserialize the response to <typeparamref name="TResponse"/>.</summary>
    public async Task<TResponse> PostAsync<TResponse>(object request, CancellationToken ct)
    {
        var body = await SendAsync(request, ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<TResponse>(body, JsonOptions.Default);
        return result
            ?? throw new ExchangeApiException("HyperLiquid Info returned a null JSON document.");
    }

    /// <summary>Send an Info request and return the response as a parsed <see cref="JsonElement"/>.</summary>
    public async Task<JsonElement> PostRawAsync(object request, CancellationToken ct)
    {
        var body = await SendAsync(request, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<string> SendAsync(object request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions.Default);

        HttpResult result;
        try
        {
            result = await Http.PostJsonAsync(_http, _infoUrl, json, _options.RetryPolicy, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ExchangeApiException($"HyperLiquid Info request failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ExchangeApiException("HyperLiquid Info request timed out.", innerException: ex);
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
                    $"HyperLiquid Info request failed: {(int)result.StatusCode} {result.ReasonPhrase}. Body: {Truncate(result.Body)}"),
            };
        }

        return result.Body;
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
