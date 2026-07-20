using Microsoft.Extensions.Logging;

namespace EasyTrading.HyperLiquid;

/// <summary>
/// Creates isolated credential-bearing clients that share the public metadata catalog registered
/// for the application.
/// </summary>
public sealed class HyperLiquidClientFactory : IHyperLiquidClientFactory
{
    private readonly IHyperLiquidMetadataCatalog _metadataCatalog;
    private readonly IHyperLiquidBuilderFeeApprovalStore? _builderFeeApprovalStore;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Creates a factory over the supplied shared metadata catalog.</summary>
    public HyperLiquidClientFactory(
        IHyperLiquidMetadataCatalog metadataCatalog,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(metadataCatalog);
        _metadataCatalog = metadataCatalog;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates a factory over the supplied shared metadata and builder-fee approval stores.
    /// </summary>
    public HyperLiquidClientFactory(
        IHyperLiquidMetadataCatalog metadataCatalog,
        IHyperLiquidBuilderFeeApprovalStore builderFeeApprovalStore,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(metadataCatalog);
        ArgumentNullException.ThrowIfNull(builderFeeApprovalStore);
        _metadataCatalog = metadataCatalog;
        _builderFeeApprovalStore = builderFeeApprovalStore;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public HyperLiquidClient Create(
        HyperLiquidCredentials credentials,
        HyperLiquidClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return CreateClient(
            CloneOptions(options, credentials),
            _builderFeeApprovalStore);
    }

    /// <inheritdoc />
    public HyperLiquidClient Create(
        HyperLiquidCredentials credentials,
        IHyperLiquidBuilderFeeApprovalStore builderFeeApprovalStore,
        HyperLiquidClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(builderFeeApprovalStore);
        return CreateClient(
            CloneOptions(options, credentials),
            builderFeeApprovalStore);
    }

    /// <inheritdoc />
    public HyperLiquidClient Create(
        HttpClient httpClient,
        HyperLiquidCredentials credentials,
        HyperLiquidClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentials);
        return CreateClient(
            httpClient,
            CloneOptions(options, credentials),
            _builderFeeApprovalStore);
    }

    /// <inheritdoc />
    public HyperLiquidClient Create(
        HttpClient httpClient,
        HyperLiquidCredentials credentials,
        IHyperLiquidBuilderFeeApprovalStore builderFeeApprovalStore,
        HyperLiquidClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(builderFeeApprovalStore);
        return CreateClient(
            httpClient,
            CloneOptions(options, credentials),
            builderFeeApprovalStore);
    }

    private ILogger<HyperLiquidClient>? CreateLogger()
        => _loggerFactory?.CreateLogger<HyperLiquidClient>();

    private HyperLiquidClient CreateClient(
        HyperLiquidClientOptions options,
        IHyperLiquidBuilderFeeApprovalStore? builderFeeApprovalStore)
        => builderFeeApprovalStore is null
            ? new HyperLiquidClient(_metadataCatalog, options, CreateLogger())
            : new HyperLiquidClient(
                _metadataCatalog,
                builderFeeApprovalStore,
                options,
                CreateLogger());

    private HyperLiquidClient CreateClient(
        HttpClient httpClient,
        HyperLiquidClientOptions options,
        IHyperLiquidBuilderFeeApprovalStore? builderFeeApprovalStore)
        => builderFeeApprovalStore is null
            ? new HyperLiquidClient(httpClient, _metadataCatalog, options, CreateLogger())
            : new HyperLiquidClient(
                httpClient,
                _metadataCatalog,
                builderFeeApprovalStore,
                options,
                CreateLogger());

    private static HyperLiquidClientOptions CloneOptions(
        HyperLiquidClientOptions? source,
        HyperLiquidCredentials credentials)
    {
        source ??= new HyperLiquidClientOptions();

        return new HyperLiquidClientOptions
        {
            Network = source.Network,
            Credentials = credentials,
            RestBaseUrl = source.RestBaseUrl,
            WebSocketUrl = source.WebSocketUrl,
            RequestTimeout = source.RequestTimeout,
            WebSocketReconnectDelay = source.WebSocketReconnectDelay,
            RetryPolicy = CloneRetryPolicy(source.RetryPolicy),
            BuilderFee = source.BuilderFee,
        };
    }

    private static HyperLiquidRetryOptions CloneRetryPolicy(HyperLiquidRetryOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new HyperLiquidRetryOptions
        {
            MaxAttempts = source.MaxAttempts,
            InitialDelay = source.InitialDelay,
            MaxDelay = source.MaxDelay,
            BackoffMultiplier = source.BackoffMultiplier,
            RetryOnServerError = source.RetryOnServerError,
            RetryOnRateLimit = source.RetryOnRateLimit,
        };
    }
}
