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
    private readonly AsterMetaCache _metaCache;
    private readonly AsterWebSocketClient _marketWs;
    private readonly AsterStreams _streams;

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

        var nonce = new AsterNonce();
        var rest = new AsterRestClient(_http, _options, nonce);
        _metaCache = new AsterMetaCache(rest);

        var wsBase = _options.GetEffectiveWebSocketUrl();
        _marketWs = new AsterWebSocketClient(
            urlProvider:      _ => Task.FromResult(wsBase),
            reconnectDelay:   _options.WebSocketReconnectDelay,
            logger:           _logger);

        Markets   = new AsterMarkets(rest);
        Orders    = new AsterOrders(rest, _metaCache);
        Positions = new AsterPositions(rest, _metaCache);
        Trades    = new AsterTrades(rest);
        Account   = new AsterAccount(rest);
        Transfers = new AsterTransfers(rest);
        _streams  = new AsterStreams(_marketWs, rest, _options);
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
