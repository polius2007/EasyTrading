using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EasyTrading.Abstractions;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Typed wrapper around the HyperLiquid <c>POST /info</c> endpoint.
/// </summary>
/// <remarks>
/// Every Info request is a JSON POST with a body of <c>{ "type": "&lt;requestType&gt;", ... }</c>.
/// This class serializes the supplied request object, sends it, and deserializes the response.
/// For responses with heterogeneous-array shapes (e.g. <c>metaAndAssetCtxs</c>, <c>portfolio</c>),
/// callers use <see cref="PostRawAsync"/> and parse the resulting <see cref="JsonElement"/> manually.
/// </remarks>
internal sealed class HlInfoClient
{
    private readonly HttpClient _http;
    private readonly Uri _infoUrl;

    public HlInfoClient(HttpClient http, HyperLiquidClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _infoUrl = new Uri(options.GetEffectiveRestBaseUrl(), "info");
    }

    /// <summary>Send a typed Info request and deserialize the response to <typeparamref name="TResponse"/>.</summary>
    public async Task<TResponse> PostAsync<TResponse>(object request, CancellationToken ct)
    {
        var responseStream = await SendAsync(request, ct).ConfigureAwait(false);
        await using (responseStream.ConfigureAwait(false))
        {
            var result = await JsonSerializer
                .DeserializeAsync<TResponse>(responseStream, HlJsonOptions.Default, ct)
                .ConfigureAwait(false);

            return result
                ?? throw new ExchangeApiException("HyperLiquid Info returned a null JSON document.");
        }
    }

    /// <summary>Send an Info request and return the response as a parsed <see cref="JsonElement"/>.</summary>
    public async Task<JsonElement> PostRawAsync(object request, CancellationToken ct)
    {
        var responseStream = await SendAsync(request, ct).ConfigureAwait(false);
        await using (responseStream.ConfigureAwait(false))
        {
            using var doc = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct).ConfigureAwait(false);
            return doc.RootElement.Clone();
        }
    }

    private async Task<Stream> SendAsync(object request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, HlJsonOptions.Default);

        using var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(_infoUrl, content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ExchangeApiException($"HyperLiquid Info request failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ExchangeApiException("HyperLiquid Info request timed out.", innerException: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            response.Dispose();

            throw response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests
                    => new RateLimitException($"HyperLiquid rate-limited the request. Body: {Truncate(body)}"),

                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    => new AuthenticationException($"HyperLiquid rejected the request ({(int)response.StatusCode}). Body: {Truncate(body)}"),

                _ => new ExchangeApiException(
                    $"HyperLiquid Info request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {Truncate(body)}"),
            };
        }

        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
