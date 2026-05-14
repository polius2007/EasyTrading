using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Shared HTTP-POST helper with the project's retry policy baked in. Both
/// <see cref="InfoClient"/> and <see cref="ExchangeClient"/> use this so retries,
/// timeout handling, and backoff are consistent across read and write paths.
/// </summary>
/// <remarks>
/// <para>The retry contract:</para>
/// <list type="bullet">
///   <item><description><b>Network errors</b> (<see cref="HttpRequestException"/>) — always retried.</description></item>
///   <item><description><b>Timeouts</b> (cancellation triggered by HttpClient, not the caller's CT) — retried.</description></item>
///   <item><description><b>5xx responses</b> — retried if
///       <see cref="HyperLiquidRetryOptions.RetryOnServerError"/> is true.</description></item>
///   <item><description><b>429 Too Many Requests</b> — retried if
///       <see cref="HyperLiquidRetryOptions.RetryOnRateLimit"/> is true; the server's
///       <c>Retry-After</c> header overrides the backoff delay when present.</description></item>
///   <item><description><b>Other 4xx</b> — not retried (these are caller errors).</description></item>
///   <item><description><b>2xx</b> — returned to the caller.</description></item>
/// </list>
/// <para>Writes (Exchange-endpoint actions) are safe to retry because the signed envelope
/// includes a nonce, and HyperLiquid de-duplicates by <c>(user, nonce)</c> on its side.</para>
/// </remarks>
internal static class Http
{
    /// <summary>
    /// POST <paramref name="jsonBody"/> to <paramref name="uri"/> with retry. Returns the
    /// raw response body, status code, and reason phrase from the final attempt — the caller
    /// decides how to map non-2xx into typed exceptions.
    /// </summary>
    public static async Task<HttpResult> PostJsonAsync(
        HttpClient http,
        Uri uri,
        string jsonBody,
        HyperLiquidRetryOptions retry,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(jsonBody);
        ArgumentNullException.ThrowIfNull(retry);

        var attempts = Math.Max(1, retry.MaxAttempts);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            // StringContent must be reconstructed every attempt — HttpClient consumes the
            // underlying stream on send, so reuse would throw on the second call.
            using var content = new StringContent(jsonBody, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage? response = null;
            try
            {
                response = await http.PostAsync(uri, content, ct).ConfigureAwait(false);
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
                // HttpClient raises TaskCanceledException for its internal timeout. We only
                // treat it as a transient failure when the caller's CT was NOT the trigger.
                response?.Dispose();
                lastException = ex;
                if (attempt == attempts) throw;
            }

            await Task.Delay(BackoffFor(retry, attempt), ct).ConfigureAwait(false);
        }

        // Defensive — the loop above always returns or throws.
        throw lastException ?? new InvalidOperationException("Http retry loop exited without a result.");
    }

    /// <summary>Whether a non-2xx response status should trigger a retry.</summary>
    private static bool ShouldRetryStatus(HttpStatusCode status, HyperLiquidRetryOptions retry)
    {
        if ((int)status is >= 200 and < 300)
            return false;
        if (status == HttpStatusCode.TooManyRequests)
            return retry.RetryOnRateLimit;
        if ((int)status >= 500 || status == HttpStatusCode.RequestTimeout)
            return retry.RetryOnServerError;
        return false;
    }

    /// <summary>Exponential backoff with ±25% jitter, clamped to <see cref="HyperLiquidRetryOptions.MaxDelay"/>.</summary>
    private static TimeSpan BackoffFor(HyperLiquidRetryOptions retry, int attempt)
    {
        var raw = retry.InitialDelay.TotalMilliseconds * Math.Pow(retry.BackoffMultiplier, attempt - 1);
        var capped = Math.Min(raw, retry.MaxDelay.TotalMilliseconds);

        // ±25% jitter — full-jitter would smear too much for the small initial delays we use.
        var jitter = (Random.Shared.NextDouble() - 0.5) * 0.5; // [-0.25 .. +0.25]
        var withJitter = capped * (1.0 + jitter);

        if (withJitter < 0) withJitter = 0;
        return TimeSpan.FromMilliseconds(withJitter);
    }

    /// <summary>Convert a server-supplied Retry-After header to a delay, capped at <paramref name="maxDelay"/>.</summary>
    private static TimeSpan ResolveRetryAfter(RetryConditionHeaderValue header, TimeSpan maxDelay)
    {
        TimeSpan delay;
        if (header.Delta.HasValue)
            delay = header.Delta.Value;
        else if (header.Date.HasValue)
        {
            var d = header.Date.Value - DateTimeOffset.UtcNow;
            delay = d > TimeSpan.Zero ? d : TimeSpan.Zero;
        }
        else
        {
            delay = TimeSpan.Zero;
        }

        if (delay > maxDelay) delay = maxDelay;
        return delay;
    }
}

/// <summary>Result of a (possibly retried) HTTP POST through <see cref="Http"/>.</summary>
/// <param name="Body">Response body, already buffered as a string.</param>
/// <param name="StatusCode">Final HTTP status code from the last attempt.</param>
/// <param name="ReasonPhrase">Reason phrase from the last attempt, if any.</param>
internal readonly record struct HttpResult(string Body, HttpStatusCode StatusCode, string? ReasonPhrase);
