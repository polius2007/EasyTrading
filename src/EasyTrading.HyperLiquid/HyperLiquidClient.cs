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
    /// <summary>Message used by methods that still land in a follow-up phase.</summary>
    internal const string WriteOpPhase31Message =
        "This HyperLiquid operation is being filled in. "
        + "See https://github.com/polius2007/EasyTrading/blob/main/CHANGELOG.md";

    /// <summary>Back-compat alias used by stubs that haven't migrated to the newer constant name yet.</summary>
    internal const string WriteOpPhase3Message = WriteOpPhase31Message;

    /// <summary>Message used by streaming methods, which land in Phase 4 (WebSocket).</summary>
    internal const string StreamPhase4Message =
        "HyperLiquid WebSocket streaming lands in Phase 4. "
        + "See https://github.com/polius2007/EasyTrading/blob/main/CHANGELOG.md";

    private readonly HyperLiquidClientOptions _options;
    private readonly ILogger<HyperLiquidClient> _logger;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly HlMetaCache _metaCache;

    /// <summary>Construct a HyperLiquid client with explicit options. Creates an internal <see cref="HttpClient"/>.</summary>
    public HyperLiquidClient(HyperLiquidClientOptions options, ILogger<HyperLiquidClient>? logger = null)
        : this(CreateHttpClient(options), options, ownsHttp: true, logger)
    {
    }

    /// <summary>Construct via <see cref="IOptions{TOptions}"/> (used by DI).</summary>
    public HyperLiquidClient(IOptions<HyperLiquidClientOptions> options, ILogger<HyperLiquidClient>? logger = null)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value, logger)
    {
    }

    /// <summary>Construct with a caller-supplied <see cref="HttpClient"/>. The supplied client is not disposed.</summary>
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
        var nonce = new HlNonce();
        var exchange = new HlExchangeClient(_http, _options, nonce);
        _metaCache = new HlMetaCache(info);

        Markets   = new HlMarkets(info);
        Orders    = new HlOrders(info, exchange, _metaCache, _options);
        Positions = new HlPositions(info, exchange, _metaCache, _options);
        Trades    = new HlTrades(info, _options);
        Account   = new HlAccount(info, exchange, _options);
        Transfers = new HlTransfers(exchange, _options);
        Streams   = new HlStreams();
        Vaults    = new HlVaults(info, exchange, _options);
        Staking   = new HlStaking(info, exchange, _options);
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
    public ValueTask DisposeAsync()
    {
        _metaCache.Dispose();
        if (_ownsHttp)
            _http.Dispose();

        return ValueTask.CompletedTask;
    }
}
