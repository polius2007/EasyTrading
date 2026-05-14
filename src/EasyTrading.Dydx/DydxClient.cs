using EasyTrading.Abstractions;
using EasyTrading.Dydx.Infrastructure;
using EasyTrading.Dydx.Modules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyTrading.Dydx;

/// <summary>
/// dYdX v4 client. Construct directly with <see cref="DydxClientOptions"/> or register through
/// <c>services.AddEasyTrading().AddDydx(...)</c>.
/// </summary>
public sealed class DydxClient : IDydxExchange
{
    private readonly DydxClientOptions _options;
    private readonly ILogger<DydxClient> _logger;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly WebSocketClient _ws;

    /// <summary>Construct with explicit options. Creates an internal <see cref="HttpClient"/>.</summary>
    public DydxClient(DydxClientOptions options, ILogger<DydxClient>? logger = null)
        : this(CreateHttpClient(options), options, ownsHttp: true, logger)
    {
    }

    /// <summary>Construct via <see cref="IOptions{TOptions}"/> (used by DI).</summary>
    public DydxClient(IOptions<DydxClientOptions> options, ILogger<DydxClient>? logger = null)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value, logger)
    {
    }

    /// <summary>Construct with a caller-supplied <see cref="HttpClient"/>. The supplied client is not disposed.</summary>
    public DydxClient(HttpClient httpClient, DydxClientOptions options, ILogger<DydxClient>? logger = null)
        : this(httpClient, options, ownsHttp: false, logger)
    {
    }

    private DydxClient(HttpClient http, DydxClientOptions options, bool ownsHttp, ILogger<DydxClient>? logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _ownsHttp = ownsHttp;
        _options = options;
        _logger = logger ?? NullLogger<DydxClient>.Instance;

        var rest = new RestClient(_http, _options);
        _ws = new WebSocketClient(_options.GetEffectiveWebSocketUrl(), _options.WebSocketReconnectDelay, _logger);

        Markets   = new Markets(rest);
        Orders    = new Orders();
        Positions = new Positions();
        Trades    = new Trades();
        Account   = new Account();
        Transfers = new Transfers();
        Streams   = new Streams(_ws);
    }

    private static HttpClient CreateHttpClient(DydxClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new HttpClient { Timeout = options.RequestTimeout };
    }

    /// <inheritdoc />
    public string ExchangeId => "dydx";

    /// <inheritdoc />
    public ExchangeCapabilities Capabilities =>
        ExchangeCapabilities.Perpetuals
        | ExchangeCapabilities.SubAccounts
        | ExchangeCapabilities.BatchOperations;

    /// <summary>Effective options for this client.</summary>
    public DydxClientOptions Options => _options;

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
        await _ws.DisposeAsync().ConfigureAwait(false);
        if (_ownsHttp) _http.Dispose();
    }
}
