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

    /// <summary>Back-compat alias retained for any reference to the Phase-4 stream message.</summary>
    internal const string StreamPhase4Message =
        "HyperLiquid WebSocket streaming is in active development; see CHANGELOG for status.";

    private readonly HyperLiquidClientOptions _options;
    private readonly ILogger<HyperLiquidClient> _logger;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly MetaCache _metaCache;
    private readonly WebSocketClient _ws;

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

        var info = new InfoClient(_http, _options);
        var nonce = new Nonce();
        var exchange = new ExchangeClient(_http, _options, nonce);
        _metaCache = new MetaCache(info);
        _ws = new WebSocketClient(_options, _logger);

        Markets   = new Markets(info);
        Orders    = new Orders(info, exchange, _metaCache, _options);
        Positions = new Positions(info, exchange, _metaCache, _options);
        Trades    = new Trades(info, _options);
        Account   = new Account(info, exchange, _options);
        Transfers = new Transfers(exchange, _options);
        Streams   = new Streams(_ws, info, _options);
        Vaults    = new Vaults(info, exchange, _options);
        Staking   = new Staking(info, exchange, _options);
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
    public async ValueTask DisposeAsync()
    {
        await _ws.DisposeAsync().ConfigureAwait(false);
        _metaCache.Dispose();
        if (_ownsHttp)
            _http.Dispose();
    }
}
