namespace EasyTrading.Aster;

/// <summary>Options for <see cref="AsterClient"/>.</summary>
public sealed class AsterClientOptions
{
    /// <summary>Which network to talk to. Defaults to <see cref="AsterNetwork.Mainnet"/>.</summary>
    public AsterNetwork Network { get; set; } = AsterNetwork.Mainnet;

    /// <summary>Credentials for signing Exchange requests. Read-only Market-Data calls don't require this.</summary>
    public AsterCredentials? Credentials { get; set; }

    /// <summary>Override the REST base URL. If <c>null</c>, the URL is derived from <see cref="Network"/>.</summary>
    public Uri? RestBaseUrl { get; set; }

    /// <summary>Override the WebSocket URL. If <c>null</c>, the URL is derived from <see cref="Network"/>.</summary>
    public Uri? WebSocketUrl { get; set; }

    /// <summary>Per-request timeout. Defaults to 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Initial delay between WebSocket reconnect attempts (exponential backoff applies). Defaults to 2 seconds.</summary>
    public TimeSpan WebSocketReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Retry policy for transient REST failures (network errors, timeouts, 5xx, 429). Defaults to 3
    /// attempts with exponential backoff starting at 200 ms. Set <c>MaxAttempts = 1</c> to disable retries.
    /// </summary>
    public AsterRetryOptions RetryPolicy { get; set; } = new();

    /// <summary>Resolves to the effective REST base URL based on <see cref="Network"/> and <see cref="RestBaseUrl"/>.</summary>
    /// <returns>The REST base URL.</returns>
    public Uri GetEffectiveRestBaseUrl() => RestBaseUrl ?? Network switch
    {
        AsterNetwork.Mainnet => new Uri("https://fapi.asterdex.com"),
        AsterNetwork.Testnet => new Uri("https://testnet.asterdex.com"),
        _ => throw new ArgumentOutOfRangeException(nameof(Network), Network, "Unknown Aster network."),
    };

    /// <summary>Resolves to the effective WebSocket URL based on <see cref="Network"/> and <see cref="WebSocketUrl"/>.</summary>
    /// <returns>The WebSocket URL.</returns>
    public Uri GetEffectiveWebSocketUrl() => WebSocketUrl ?? Network switch
    {
        AsterNetwork.Mainnet => new Uri("wss://fstream.asterdex.com/ws"),
        AsterNetwork.Testnet => new Uri("wss://testnet.asterdex.com/ws"),
        _ => throw new ArgumentOutOfRangeException(nameof(Network), Network, "Unknown Aster network."),
    };
}

/// <summary>
/// Controls how the Aster client retries transient REST failures. Same shape and semantics as
/// <c>HyperLiquidRetryOptions</c>; lives in this namespace so consumers can keep venue-specific
/// settings together.
/// </summary>
public sealed class AsterRetryOptions
{
    /// <summary>Total attempts including the initial call. <c>1</c> = no retries. Default: <c>3</c>.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay before the first retry. Default: 200 ms.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Upper bound on any single retry delay after backoff. Default: 5 s.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Multiplier applied each retry. Default: <c>2.0</c>.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Whether to retry 5xx and 408 responses. Default: <c>true</c>.</summary>
    public bool RetryOnServerError { get; set; } = true;

    /// <summary>Whether to retry 429 (Too Many Requests). Respects <c>Retry-After</c>. Default: <c>true</c>.</summary>
    public bool RetryOnRateLimit { get; set; } = true;
}
