using EasyTrading.Abstractions;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Caches the perp + spot universe so we can resolve a human-readable symbol like <c>"BTC"</c>
/// to the integer <c>asset</c> id that HyperLiquid Exchange-endpoint actions require.
/// </summary>
/// <remarks>
/// HyperLiquid encodes the asset on every order / cancel as an integer:
/// <list type="bullet">
///   <item><description>Perpetuals: the zero-based index in <c>meta.universe</c>.</description></item>
///   <item><description>Spot pairs: <c>10000 + spot.universe[i].index</c>.</description></item>
/// </list>
/// We fetch both universes once on first use and cache for the lifetime of the client.
/// </remarks>
internal sealed class HlMetaCache(HlInfoClient info) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public void Dispose() => _lock.Dispose();

    private Dictionary<string, int>? _perpIndex;
    private Dictionary<string, int>? _spotIndex;
    private List<string>? _perpSymbols;

    /// <summary>
    /// Resolve a market symbol to its HyperLiquid asset id (integer used in action payloads).
    /// </summary>
    public async Task<int> GetAssetIdAsync(string symbol, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        if (_perpIndex!.TryGetValue(symbol, out var perp))
            return perp;
        if (_spotIndex!.TryGetValue(symbol, out var spot))
            return 10_000 + spot;

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
        try { _perpIndex = null; }
        finally { _lock.Release(); }
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_perpIndex is not null) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_perpIndex is not null) return;

            var meta = await info.PostAsync<MetaResponseRaw>(new { type = "meta" }, ct).ConfigureAwait(false);

            var perpIdx = new Dictionary<string, int>(meta.Universe.Count, StringComparer.OrdinalIgnoreCase);
            var perpSyms = new List<string>(meta.Universe.Count);
            for (var i = 0; i < meta.Universe.Count; i++)
            {
                perpIdx[meta.Universe[i].Name] = i;
                perpSyms.Add(meta.Universe[i].Name);
            }

            var spot = await info.PostAsync<SpotMetaResponseRaw>(new { type = "spotMeta" }, ct).ConfigureAwait(false);
            var spotIdx = new Dictionary<string, int>(spot.Universe.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in spot.Universe)
                spotIdx[pair.Name] = pair.Index;

            _perpIndex = perpIdx;
            _perpSymbols = perpSyms;
            _spotIndex = spotIdx;
        }
        finally
        {
            _lock.Release();
        }
    }
}
