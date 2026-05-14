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
    private readonly MarketsCache _marketsCache;

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
        _marketsCache = new MarketsCache(rest);

        // Build the signed-transaction pipeline if credentials with a mnemonic were supplied.
        // Read-only Indexer flows work fine without it; writes will throw AuthenticationException
        // if Orders.PlaceAsync is called with no mnemonic.
        TransactionBuilder? txBuilder = null;
        CosmosClient? cosmos = null;
        if (_options.Credentials is { Mnemonic: { Length: > 0 } } creds)
        {
            var signer = new Signer(creds.Mnemonic);
            // Sanity-check: the address derived from the mnemonic must match Credentials.Address.
            // Mismatch usually means the user pasted the wrong public address for the mnemonic.
            if (!string.IsNullOrEmpty(creds.Address) && !string.Equals(signer.Address, creds.Address, StringComparison.Ordinal))
            {
                throw new AuthenticationException(
                    $"DydxCredentials.Address ('{creds.Address}') doesn't match the address derived from the mnemonic ('{signer.Address}'). "
                    + "Verify you copied the address belonging to this wallet.");
            }
            txBuilder = new TransactionBuilder(signer, _options.GetEffectiveChainId());
            cosmos = new CosmosClient(_http, _options.GetEffectiveValidatorRestUrl(), _options.RetryPolicy);
        }

        Markets   = new Markets(rest);
        Orders    = new Orders(rest, _options, _marketsCache, txBuilder, cosmos);
        Positions = new Positions(rest, _options);
        Trades    = new Trades(rest, _options);
        Account   = new Account(rest, _options);
        Transfers = new Transfers();
        Streams   = new Streams(_ws, _options);
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
        _marketsCache.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}
