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
    private readonly MetaCache _metaCache;
    private readonly WebSocketClient _marketWs;
    private readonly Streams _streams;

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

        var nonce = new Nonce();
        var rest = new RestClient(_http, _options, nonce);
        _metaCache = new MetaCache(rest);

        var wsBase = _options.GetEffectiveWebSocketUrl();
        _marketWs = new WebSocketClient(
            urlProvider:      _ => Task.FromResult(wsBase),
            reconnectDelay:   _options.WebSocketReconnectDelay,
            logger:           _logger);

        Markets   = new Markets(rest);
        Orders    = new Orders(rest, _metaCache);
        Positions = new Positions(rest, _metaCache);
        Trades    = new Trades(rest);
        Account   = new Account(rest);
        Transfers = new Transfers(rest);
        _streams  = new Streams(_marketWs, rest, _options);
        Streams   = _streams;
    }

    private static HttpClient CreateHttpClient(AsterClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new HttpClient { Timeout = options.RequestTimeout };
    }

    /// <inheritdoc />
    public string ExchangeId => "aster";

    /// <inheritdoc />
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
    public async ValueTask DisposeAsync()
    {
        await _streams.DisposeAsync().ConfigureAwait(false);
        await _marketWs.DisposeAsync().ConfigureAwait(false);
        _metaCache.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}
