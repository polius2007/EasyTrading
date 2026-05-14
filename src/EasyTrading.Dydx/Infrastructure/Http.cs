using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace EasyTrading.Dydx.Infrastructure;

/// <summary>
/// Shared HTTP helper with the project's retry policy baked in. Same contract as the HyperLiquid
/// and Aster counterparts; each venue keeps its own copy so retry semantics can diverge without
/// leaking through <c>EasyTrading.Core</c>.
/// </summary>
internal static class Http
{
    /// <summary>Send a request with the configured retry policy and return the raw response envelope.</summary>
    public static async Task<HttpResult> SendAsync(
        HttpClient http,
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        DydxRetryOptions retry,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(retry);

        var attempts = Math.Max(1, retry.MaxAttempts);
        Exception? lastException = null;

        // Buffer content up-front so each retry can resend it.
        var (bodyText, mediaType) = await CaptureContentAsync(content, ct).ConfigureAwait(false);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri);
            if (bodyText is not null)
            {
                request.Content = new StringContent(bodyText, Encoding.UTF8);
                if (mediaType is not null)
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            }

            HttpResponseMessage? response = null;
            try
            {
                response = await http.SendAsync(request, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var status = response.StatusCode;
                var reason = response.ReasonPhrase;
                var retryAfter = response.Headers.RetryAfter;
                response.Dispose();
                response = null;

                if (ShouldRetryStatus(status, retry) && attempt < attempts)
                {
                    var delay = retryAfter is not null
                        ? ResolveRetryAfter(retryAfter, retry.MaxDelay)
                        : BackoffFor(retry, attempt);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    continue;
                }

                return new HttpResult(body, status, reason);
            }
            catch (HttpRequestException ex)
            {
                response?.Dispose();
                lastException = ex;
                if (attempt == attempts) throw;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                response?.Dispose();
                lastException = ex;
                if (attempt == attempts) throw;
            }

            await Task.Delay(BackoffFor(retry, attempt), ct).ConfigureAwait(false);
        }

        throw lastException ?? new InvalidOperationException("Dydx Http retry loop exited without a result.");
    }

    private static async Task<(string? Body, string? MediaType)> CaptureContentAsync(HttpContent? content, CancellationToken ct)
    {
        if (content is null) return (null, null);
        var body = await content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var mediaType = content.Headers.ContentType?.MediaType;
        return (body, mediaType);
    }

    private static bool ShouldRetryStatus(HttpStatusCode status, DydxRetryOptions retry)
    {
        if ((int)status is >= 200 and < 300) return false;
        if (status == HttpStatusCode.TooManyRequests) return retry.RetryOnRateLimit;
        if ((int)status >= 500 || status == HttpStatusCode.RequestTimeout) return retry.RetryOnServerError;
        return false;
    }

    private static TimeSpan BackoffFor(DydxRetryOptions retry, int attempt)
    {
        var raw = retry.InitialDelay.TotalMilliseconds * Math.Pow(retry.BackoffMultiplier, attempt - 1);
        var capped = Math.Min(raw, retry.MaxDelay.TotalMilliseconds);
        var jitter = (Random.Shared.NextDouble() - 0.5) * 0.5; // ±25%
        var withJitter = capped * (1.0 + jitter);
        if (withJitter < 0) withJitter = 0;
        return TimeSpan.FromMilliseconds(withJitter);
    }

    private static TimeSpan ResolveRetryAfter(RetryConditionHeaderValue header, TimeSpan maxDelay)
    {
        TimeSpan delay;
        if (header.Delta.HasValue) delay = header.Delta.Value;
        else if (header.Date.HasValue)
        {
            var d = header.Date.Value - DateTimeOffset.UtcNow;
            delay = d > TimeSpan.Zero ? d : TimeSpan.Zero;
        }
        else delay = TimeSpan.Zero;
        return delay > maxDelay ? maxDelay : delay;
    }
}

/// <summary>Outcome of an <see cref="Http.SendAsync"/> call.</summary>
internal readonly record struct HttpResult(string Body, HttpStatusCode StatusCode, string? ReasonPhrase);
