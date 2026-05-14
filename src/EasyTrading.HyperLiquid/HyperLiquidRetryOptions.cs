namespace EasyTrading.HyperLiquid;

/// <summary>
/// Controls how the client retries transient REST failures (network errors, timeouts,
/// 5xx responses, and 429 rate limits). Writes (Exchange-endpoint actions) are safe to
/// retry because HyperLiquid de-duplicates by signed nonce — the same envelope sent
/// twice returns the same result, not a double-execution.
/// </summary>
/// <remarks>
/// <para>The delay before the Nth retry is
/// <c>min(InitialDelay × BackoffMultiplier^(N-1), MaxDelay)</c> plus up to ±25% jitter.
/// For 429 responses the <c>Retry-After</c> header, if present, overrides the calculated
/// delay (up to <see cref="MaxDelay"/>).</para>
/// <para>Set <see cref="MaxAttempts"/> to <c>1</c> to disable retries entirely.</para>
/// </remarks>
public sealed class HyperLiquidRetryOptions
{
    /// <summary>
    /// Total number of attempts including the initial call. <c>1</c> = no retries.
    /// Default: <c>3</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay before the first retry. Default: 200 ms.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Upper bound for any retry delay (after exponential backoff). Default: 5 s.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Multiplier applied each retry. Default: <c>2.0</c> (exponential).</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Whether to retry 5xx responses. Disabled for writes would still be safe (HL nonce dedup),
    /// but some operators prefer to surface server errors fast. Default: <c>true</c>.
    /// </summary>
    public bool RetryOnServerError { get; set; } = true;

    /// <summary>
    /// Whether to retry 429 (Too Many Requests). Respects the server's <c>Retry-After</c> header.
    /// Default: <c>true</c>.
    /// </summary>
    public bool RetryOnRateLimit { get; set; } = true;
}
