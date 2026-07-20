using System.Collections.Concurrent;
using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid;

/// <summary>
/// Thread-safe shared catalog of public HyperLiquid metadata. The catalog never stores account
/// credentials and never performs network requests from its read methods.
/// </summary>
public sealed class HyperLiquidMetadataCatalog : IHyperLiquidMetadataCatalog, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ConcurrentDictionary<HyperLiquidMetadataScope, ScopeState> _states = new();

    /// <summary>Creates a catalog with an internally owned HTTP transport.</summary>
    public HyperLiquidMetadataCatalog()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, ownsHttpClient: true)
    {
    }

    /// <summary>Creates a catalog with a caller-owned HTTP transport.</summary>
    public HyperLiquidMetadataCatalog(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private HyperLiquidMetadataCatalog(HttpClient httpClient, bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    /// <inheritdoc />
    public Task WarmUpAsync(HyperLiquidMetadataScope scope, CancellationToken ct = default)
        => LoadAsync(scope, forceRefresh: false, ct);

    /// <inheritdoc />
    public Task RefreshAsync(HyperLiquidMetadataScope scope, CancellationToken ct = default)
        => LoadAsync(scope, forceRefresh: true, ct);

    /// <inheritdoc />
    public bool TryGetAsset(
        HyperLiquidMetadataScope scope,
        string symbol,
        out HyperLiquidAssetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (_states.TryGetValue(scope, out var state)
            && state.Snapshot is { } snapshot
            && snapshot.Assets.TryGetValue(symbol, out var found))
        {
            metadata = found;
            return true;
        }

        metadata = null!;
        return false;
    }

    /// <inheritdoc />
    public HyperLiquidAssetMetadata GetRequiredAsset(
        HyperLiquidMetadataScope scope,
        string symbol)
    {
        if (TryGetAsset(scope, symbol, out var metadata))
            return metadata;

        if (!_states.TryGetValue(scope, out var state) || state.Snapshot is null)
        {
            throw new ExchangeApiException(
                $"HyperLiquid metadata catalog for '{scope.Network}' is not loaded. "
                + "Call WarmUpAsync before creating trading actions.");
        }

        throw new ExchangeApiException($"Unknown HyperLiquid symbol: '{symbol}'.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var state in _states.Values)
            state.Dispose();

        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private async Task LoadAsync(
        HyperLiquidMetadataScope scope,
        bool forceRefresh,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var state = _states.GetOrAdd(scope, static _ => new ScopeState());

        if (!forceRefresh && state.Snapshot is not null)
            return;

        await state.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && state.Snapshot is not null)
                return;

            var options = new HyperLiquidClientOptions
            {
                Network = scope.Network,
                RestBaseUrl = scope.RestBaseUrl,
            };
            var info = new InfoClient(_httpClient, options);
            var meta = await info.PostAsync<MetaResponseRaw>(
                new { type = "meta" },
                ct).ConfigureAwait(false);
            var spot = await info.PostAsync<SpotMetaResponseRaw>(
                new { type = "spotMeta" },
                ct).ConfigureAwait(false);

            state.Publish(BuildSnapshot(meta, spot));
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private static MetadataSnapshot BuildSnapshot(
        MetaResponseRaw meta,
        SpotMetaResponseRaw spot)
    {
        var assets = new Dictionary<string, HyperLiquidAssetMetadata>(
            meta.Universe.Count + spot.Universe.Count,
            StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < meta.Universe.Count; index++)
        {
            var asset = meta.Universe[index];
            assets[asset.Name] = new HyperLiquidAssetMetadata(
                asset.Name,
                index,
                asset.SzDecimals,
                IsSpot: false);
        }

        var tokensByIndex = spot.Tokens.ToDictionary(static token => token.Index);
        foreach (var pair in spot.Universe)
        {
            var baseTokenIndex = pair.Tokens.Count > 0 ? pair.Tokens[0] : -1;
            var sizeDecimals = baseTokenIndex >= 0
                && tokensByIndex.TryGetValue(baseTokenIndex, out var token)
                    ? token.SzDecimals
                    : 0;

            assets[pair.Name] = new HyperLiquidAssetMetadata(
                pair.Name,
                10_000 + pair.Index,
                sizeDecimals,
                IsSpot: true);
        }

        return new MetadataSnapshot(assets);
    }

    private sealed class ScopeState : IDisposable
    {
        private MetadataSnapshot? _snapshot;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public MetadataSnapshot? Snapshot => Volatile.Read(ref _snapshot);

        public void Publish(MetadataSnapshot snapshot) => Volatile.Write(ref _snapshot, snapshot);

        public void Dispose() => Gate.Dispose();
    }

    private sealed record MetadataSnapshot(
        IReadOnlyDictionary<string, HyperLiquidAssetMetadata> Assets);
}
