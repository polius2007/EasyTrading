using EasyTrading.Abstractions;
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
    /// <summary>Phase-1 message used by stubbed methods. Real implementations land in Phase 2+.</summary>
    internal const string Phase1NotImplemented =
        "EasyTrading.HyperLiquid is in Phase 1 (scaffolding). Real implementations land in Phase 2+. "
        + "See https://github.com/polius2007/EasyTrading/blob/main/CHANGELOG.md";

    private readonly HyperLiquidClientOptions _options;
    private readonly ILogger<HyperLiquidClient> _logger;

    /// <summary>Construct a HyperLiquid client with explicit options.</summary>
    /// <param name="options">Client options.</param>
    /// <param name="logger">Optional logger. Defaults to <see cref="NullLogger{T}.Instance"/>.</param>
    public HyperLiquidClient(HyperLiquidClientOptions options, ILogger<HyperLiquidClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger ?? NullLogger<HyperLiquidClient>.Instance;

        Markets   = new HlMarkets();
        Orders    = new HlOrders();
        Positions = new HlPositions();
        Trades    = new HlTrades();
        Account   = new HlAccount();
        Transfers = new HlTransfers();
        Streams   = new HlStreams();
        Vaults    = new HlVaults();
        Staking   = new HlStaking();
        Builder   = new HlBuilder();
    }

    /// <summary>Construct via <c>IOptions&lt;HyperLiquidClientOptions&gt;</c> (used by DI).</summary>
    /// <param name="options">Options accessor.</param>
    /// <param name="logger">Logger.</param>
    public HyperLiquidClient(IOptions<HyperLiquidClientOptions> options, ILogger<HyperLiquidClient>? logger = null)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)), logger)
    {
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
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
