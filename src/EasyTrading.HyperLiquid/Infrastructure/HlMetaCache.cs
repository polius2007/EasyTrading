using EasyTrading.Abstractions;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Caches the perp + spot universe so we can resolve a human-readable symbol like <c>"BTC"</c>
/// to the integer <c>asset</c> id HyperLiquid Exchange-endpoint actions require, plus the
/// per-asset size-decimal limit needed for pre-flight order validation.
/// </summary>
/// <remarks>
/// HyperLiquid encodes the asset on every order / cancel as an integer:
/// <list type="bullet">
///   <item><description>Perpetuals: the zero-based index in <c>meta.universe</c>.</description></item>
///   <item><description>Spot pairs: <c>10000 + spot.universe[i].index</c>.</description></item>
/// </list>
/// We fetch both universes once on first use and cache them for the lifetime of the client.
/// </remarks>
internal sealed class HlMetaCache(HlInfoClient info) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public void Dispose() => _lock.Dispose();

    private Dictionary<string, HlAssetInfo>? _assets;
    private List<string>? _perpSymbols;

    /// <summary>
    /// Resolve a market symbol to its HyperLiquid asset id (integer used in action payloads).
    /// </summary>
    public async Task<int> GetAssetIdAsync(string symbol, CancellationToken ct = default)
    {
        var info = await GetAssetInfoAsync(symbol, ct).ConfigureAwait(false);
        return info.AssetId;
    }

    /// <summary>
    /// Resolve a market symbol to its full asset info (id + sz-decimals + isSpot).
    /// Used both for action payloads and for pre-flight order validation.
    /// </summary>
    public async Task<HlAssetInfo> GetAssetInfoAsync(string symbol, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        if (_assets!.TryGetValue(symbol, out var info))
            return info;

        throw new ExchangeApiException($"Unknown HyperLiquid symbol: '{symbol}'.");
    }

    /// <summary>
    /// Get the perp symbol corresponding to an asset index (for response parsing).
    /// </summary>
    public async Task<string?> GetPerpSymbolAsync(int assetIndex, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (assetIndex < 0 || assetIndex >= _perpSymbols!.Count)
            return null;
        return _perpSymbols[assetIndex];
    }

    /// <summary>Force a refresh — useful if a new perp listing is needed before the cache TTL.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { _assets = null; }
        finally { _lock.Release(); }
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_assets is not null) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_assets is not null) return;

            var meta = await info.PostAsync<MetaResponseRaw>(new { type = "meta" }, ct).ConfigureAwait(false);

            var assets = new Dictionary<string, HlAssetInfo>(meta.Universe.Count, StringComparer.OrdinalIgnoreCase);
            var perpSyms = new List<string>(meta.Universe.Count);
            for (var i = 0; i < meta.Universe.Count; i++)
            {
                var a = meta.Universe[i];
                assets[a.Name] = new HlAssetInfo(AssetId: i, SzDecimals: a.SzDecimals, IsSpot: false);
                perpSyms.Add(a.Name);
            }

            var spot = await info.PostAsync<SpotMetaResponseRaw>(new { type = "spotMeta" }, ct).ConfigureAwait(false);

            // For spot pairs the size-decimal limit comes from the BASE token (first index in `tokens`).
            // We look it up via the spotMeta.tokens table keyed by token index.
            var tokensByIndex = new Dictionary<int, SpotTokenRaw>(spot.Tokens.Count);
            foreach (var t in spot.Tokens)
                tokensByIndex[t.Index] = t;

            foreach (var pair in spot.Universe)
            {
                var baseTokenIndex = pair.Tokens.Count > 0 ? pair.Tokens[0] : -1;
                var szDecimals = baseTokenIndex >= 0 && tokensByIndex.TryGetValue(baseTokenIndex, out var tok)
                    ? tok.SzDecimals
                    : 0;
                assets[pair.Name] = new HlAssetInfo(AssetId: 10_000 + pair.Index, SzDecimals: szDecimals, IsSpot: true);
            }

            _assets = assets;
            _perpSymbols = perpSyms;
        }
        finally
        {
            _lock.Release();
        }
    }
}
