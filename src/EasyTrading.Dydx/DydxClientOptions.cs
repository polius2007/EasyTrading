namespace EasyTrading.Dydx;

/// <summary>Options for <see cref="DydxClient"/>.</summary>
public sealed class DydxClientOptions
{
    /// <summary>Which network to talk to. Defaults to <see cref="DydxNetwork.Mainnet"/>.</summary>
    public DydxNetwork Network { get; set; } = DydxNetwork.Mainnet;

    /// <summary>Credentials for signed Cosmos transactions. Read-only Indexer calls don't require this.</summary>
    public DydxCredentials? Credentials { get; set; }

    /// <summary>Override the Indexer REST base URL. If <c>null</c>, the URL is derived from <see cref="Network"/>.</summary>
    public Uri? IndexerRestUrl { get; set; }

    /// <summary>Override the Indexer WebSocket URL. If <c>null</c>, the URL is derived from <see cref="Network"/>.</summary>
    public Uri? IndexerWebSocketUrl { get; set; }

    /// <summary>
    /// Override the validator gRPC endpoint used for broadcasting signed transactions
    /// (Phase 7.2). Currently unused.
    /// </summary>
    public Uri? ValidatorGrpcUrl { get; set; }

    /// <summary>Per-request timeout. Defaults to 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Initial delay between WebSocket reconnect attempts (exponential backoff applies). Defaults to 2 seconds.</summary>
    public TimeSpan WebSocketReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Retry policy for transient REST failures (network errors, timeouts, 5xx, 429). Defaults to 3
    /// attempts with exponential backoff starting at 200 ms. Set <c>MaxAttempts = 1</c> to disable retries.
    /// </summary>
    public DydxRetryOptions RetryPolicy { get; set; } = new();

    /// <summary>Resolves to the effective Indexer REST base URL based on <see cref="Network"/> and <see cref="IndexerRestUrl"/>.</summary>
    /// <returns>The REST base URL.</returns>
    public Uri GetEffectiveRestBaseUrl() => IndexerRestUrl ?? Network switch
    {
        DydxNetwork.Mainnet => new Uri("https://indexer.dydx.trade/v4/"),
        DydxNetwork.Testnet => new Uri("https://indexer.v4testnet.dydx.exchange/v4/"),
        _ => throw new ArgumentOutOfRangeException(nameof(Network), Network, "Unknown dYdX network."),
    };

    /// <summary>Resolves to the effective WebSocket URL based on <see cref="Network"/> and <see cref="IndexerWebSocketUrl"/>.</summary>
    /// <returns>The WebSocket URL.</returns>
    public Uri GetEffectiveWebSocketUrl() => IndexerWebSocketUrl ?? Network switch
    {
        DydxNetwork.Mainnet => new Uri("wss://indexer.dydx.trade/v4/ws"),
        DydxNetwork.Testnet => new Uri("wss://indexer.v4testnet.dydx.exchange/v4/ws"),
        _ => throw new ArgumentOutOfRangeException(nameof(Network), Network, "Unknown dYdX network."),
    };
}

/// <summary>
/// Controls how the dYdX client retries transient REST failures. Same shape and semantics as
/// the HyperLiquid and Aster retry options.
/// </summary>
public sealed class DydxRetryOptions
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
