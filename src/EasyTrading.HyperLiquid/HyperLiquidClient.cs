using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid.Infrastructure;
using EasyTrading.HyperLiquid.Modules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyTrading.HyperLiquid;

/// <summary>
/// Concrete HyperLiquid client. Construct directly with <see cref="HyperLiquidClientOptions"/> or
/// register through <c>services.AddEasyTrading().AddHyperLiquid(...)</c>.
/// </summary>
public sealed class HyperLiquidClient : IHyperLiquidExchange
{
    /// <summary>Message used by modules whose write-side operations land in Phase 3 (Exchange endpoint + EIP-712 signing).</summary>
    internal const string WriteOpPhase3Message =
        "HyperLiquid write operations (Exchange endpoint, EIP-712 signing) land in Phase 3. "
        + "See https://github.com/polius2007/EasyTrading/blob/main/CHANGELOG.md";

    /// <summary>Message used by streaming methods, which land in Phase 4 (WebSocket).</summary>
    internal const string StreamPhase4Message =
        "HyperLiquid WebSocket streaming lands in Phase 4. "
        + "See https://github.com/polius2007/EasyTrading/blob/main/CHANGELOG.md";

    private readonly HyperLiquidClientOptions _options;
    private readonly ILogger<HyperLiquidClient> _logger;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <summary>Construct a HyperLiquid client with explicit options. Creates an internal <see cref="HttpClient"/>.</summary>
    /// <param name="options">Client options.</param>
    /// <param name="logger">Optional logger. Defaults to <see cref="NullLogger{T}.Instance"/>.</param>
    public HyperLiquidClient(HyperLiquidClientOptions options, ILogger<HyperLiquidClient>? logger = null)
        : this(CreateHttpClient(options), options, ownsHttp: true, logger)
    {
    }

    /// <summary>Construct via <see cref="IOptions{TOptions}"/> (used by DI).</summary>
    /// <param name="options">Options accessor.</param>
    /// <param name="logger">Logger.</param>
    public HyperLiquidClient(IOptions<HyperLiquidClientOptions> options, ILogger<HyperLiquidClient>? logger = null)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value, logger)
    {
    }

    /// <summary>Construct with a caller-supplied <see cref="HttpClient"/>. The supplied client is not disposed.</summary>
    /// <param name="httpClient">HTTP client to use for REST calls.</param>
    /// <param name="options">Client options.</param>
    /// <param name="logger">Optional logger.</param>
    public HyperLiquidClient(HttpClient httpClient, HyperLiquidClientOptions options, ILogger<HyperLiquidClient>? logger = null)
        : this(httpClient, options, ownsHttp: false, logger)
    {
    }

    private HyperLiquidClient(HttpClient http, HyperLiquidClientOptions options, bool ownsHttp, ILogger<HyperLiquidClient>? logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _ownsHttp = ownsHttp;
        _options = options;
        _logger = logger ?? NullLogger<HyperLiquidClient>.Instance;

        var info = new HlInfoClient(_http, _options);

        Markets   = new HlMarkets(info);
        Orders    = new HlOrders(info, _options);
        Positions = new HlPositions(info, _options);
        Trades    = new HlTrades(info, _options);
        Account   = new HlAccount(info, _options);
        Transfers = new HlTransfers();
        Streams   = new HlStreams();
        Vaults    = new HlVaults(info, _options);
        Staking   = new HlStaking(info, _options);
        Builder   = new HlBuilder(info, _options);
    }

    private static HttpClient CreateHttpClient(HyperLiquidClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new HttpClient { Timeout = options.RequestTimeout };
    }

    /// <inheritdoc />
    public string ExchangeId => "hyperliquid";

    /// <inheritdoc />
    public ExchangeCapabilities Capabilities =>
        ExchangeCapabilities.Perpetuals
        | ExchangeCapabilities.Spot
        | ExchangeCapabilities.Twap
        | ExchangeCapabilities.ScheduleCancel
        | ExchangeCapabilities.SubAccounts
        | ExchangeCapabilities.Vaults
        | ExchangeCapabilities.Staking
        | ExchangeCapabilities.BuilderFees
        | ExchangeCapabilities.BatchOperations
        | ExchangeCapabilities.AgentWallets;

    /// <summary>Effective options for this client.</summary>
    public HyperLiquidClientOptions Options => _options;

    /// <inheritdoc />
    public IMarkets Markets { get; }

    /// <inheritdoc />
    public IOrders Orders { get; }

    /// <inheritdoc />
    public IPositions Positions { get; }

    /// <inheritdoc />
    public ITrades Trades { get; }

    /// <inheritdoc />
    public IAccount Account { get; }

    /// <inheritdoc />
    public ITransfers Transfers { get; }

    /// <inheritdoc />
    public IStreams Streams { get; }

    /// <inheritdoc />
    public IVaults Vaults { get; }

    /// <inheritdoc />
    public IStaking Staking { get; }

    /// <inheritdoc />
    public IBuilder Builder { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_ownsHttp)
            _http.Dispose();

        return ValueTask.CompletedTask;
    }
}
