using EasyTrading.Abstractions;
using EasyTrading.Aster.Infrastructure;
using EasyTrading.Aster.Modules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyTrading.Aster;

/// <summary>
/// Aster client. Construct directly with <see cref="AsterClientOptions"/> or register through
/// <c>services.AddEasyTrading().AddAster(...)</c>.
/// </summary>
public sealed class AsterClient : IAsterExchange
{
    private readonly AsterClientOptions _options;
    private readonly ILogger<AsterClient> _logger;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <summary>Construct with explicit options. Creates an internal <see cref="HttpClient"/>.</summary>
    public AsterClient(AsterClientOptions options, ILogger<AsterClient>? logger = null)
        : this(CreateHttpClient(options), options, ownsHttp: true, logger)
    {
    }

    /// <summary>Construct via <see cref="IOptions{TOptions}"/> (used by DI).</summary>
    public AsterClient(IOptions<AsterClientOptions> options, ILogger<AsterClient>? logger = null)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value, logger)
    {
    }

    /// <summary>Construct with a caller-supplied <see cref="HttpClient"/>. The supplied client is not disposed.</summary>
    public AsterClient(HttpClient httpClient, AsterClientOptions options, ILogger<AsterClient>? logger = null)
        : this(httpClient, options, ownsHttp: false, logger)
    {
    }

    private AsterClient(HttpClient http, AsterClientOptions options, bool ownsHttp, ILogger<AsterClient>? logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _ownsHttp = ownsHttp;
        _options = options;
        _logger = logger ?? NullLogger<AsterClient>.Instance;

        var rest = new AsterRestClient(_http, _options);

        Markets   = new AsterMarkets(rest);
        Orders    = new AsterOrders();
        Positions = new AsterPositions();
        Trades    = new AsterTrades();
        Account   = new AsterAccount();
        Transfers = new AsterTransfers();
        Streams   = new AsterStreams();
    }

    private static HttpClient CreateHttpClient(AsterClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new HttpClient { Timeout = options.RequestTimeout };
    }

    /// <inheritdoc />
    public string ExchangeId => "aster";

    /// <inheritdoc />
    /// <remarks>
    /// Capabilities reflect what the surface is contracted to do today; until Phase 6.2 lands,
    /// signed-action capabilities (Spot, BatchOperations, AgentWallets, SubAccounts) will throw
    /// <see cref="NotImplementedException"/> when called.
    /// </remarks>
    public ExchangeCapabilities Capabilities =>
        ExchangeCapabilities.Perpetuals
        | ExchangeCapabilities.AgentWallets
        | ExchangeCapabilities.SubAccounts
        | ExchangeCapabilities.BatchOperations
        | ExchangeCapabilities.ScheduleCancel;

    /// <summary>Effective options for this client.</summary>
    public AsterClientOptions Options => _options;

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
    public ValueTask DisposeAsync()
    {
        if (_ownsHttp) _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
