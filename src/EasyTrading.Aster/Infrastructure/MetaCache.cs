using EasyTrading.Abstractions;

namespace EasyTrading.Aster.Infrastructure;

/// <summary>
/// Per-symbol metadata Aster's order validator needs: tick size, lot step, min/max
/// quantity, min notional. Loaded once from <c>/fapi/v3/exchangeInfo</c> and cached for
/// the lifetime of the client (no TTL — new listings are rare on Aster; force a refresh
/// via <see cref="RefreshAsync"/> when needed).
/// </summary>
internal sealed class MetaCache(RestClient rest) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public void Dispose() => _lock.Dispose();

    private Dictionary<string, SymbolInfo>? _symbols;

    /// <summary>Resolve a symbol to its filters. Throws <see cref="ExchangeApiException"/> if unknown.</summary>
    public async Task<SymbolInfo> GetAsync(string symbol, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (_symbols!.TryGetValue(symbol, out var info))
            return info;
        throw new ExchangeApiException($"Unknown Aster symbol: '{symbol}'.");
    }

    /// <summary>Force a refresh — useful if a new listing is needed before the cache TTL.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { _symbols = null; }
        finally { _lock.Release(); }
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_symbols is not null) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_symbols is not null) return;

            var raw = await rest.GetAsync<ExchangeInfoRaw>("/fapi/v3/exchangeInfo", query: null, ct).ConfigureAwait(false);
            var map = new Dictionary<string, SymbolInfo>(raw.Symbols.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var s in raw.Symbols)
            {
                decimal tick = 0m;
                decimal step = 0m;
                decimal minQty = 0m;
                decimal maxQty = 0m;
                decimal minNotional = 0m;

                if (s.Filters is not null)
                {
                    foreach (var f in s.Filters)
                    {
                        switch (f.FilterType)
                        {
                            case "PRICE_FILTER" when f.TickSize is not null:
                                tick = f.TickSize.Value;
                                break;
                            case "LOT_SIZE" when f.StepSize is not null:
                                step = f.StepSize.Value;
                                if (f.MinQty is not null) minQty = f.MinQty.Value;
                                if (f.MaxQty is not null) maxQty = f.MaxQty.Value;
                                break;
                            case "MIN_NOTIONAL" when f.Notional is not null:
                                minNotional = f.Notional.Value;
                                break;
                        }
                    }
                }

                map[s.Symbol] = new SymbolInfo(
                    Symbol:           s.Symbol,
                    Status:           s.Status,
                    PricePrecision:   s.PricePrecision,
                    QuantityPrecision: s.QuantityPrecision,
                    TickSize:         tick,
                    StepSize:         step,
                    MinQty:           minQty,
                    MaxQty:           maxQty,
                    MinNotional:      minNotional);
            }

            _symbols = map;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>Symbol metadata Aster needs for both action payloads and pre-flight validation.</summary>
/// <param name="Symbol">Trading symbol (e.g. <c>BTCUSDT</c>).</param>
/// <param name="Status">Trading status (<c>TRADING</c>, <c>PENDING_TRADING</c>, etc).</param>
/// <param name="PricePrecision">Max digits after decimal point allowed in the price field.</param>
/// <param name="QuantityPrecision">Max digits after decimal point allowed in the quantity field.</param>
/// <param name="TickSize">Price tick (granularity). 0 = disabled.</param>
/// <param name="StepSize">Lot step (granularity). 0 = disabled.</param>
/// <param name="MinQty">Minimum order size. 0 = disabled.</param>
/// <param name="MaxQty">Maximum order size. 0 = disabled.</param>
/// <param name="MinNotional">Minimum order notional (price × quantity). 0 = disabled.</param>
internal sealed record SymbolInfo(
    string Symbol,
    string Status,
    int PricePrecision,
    int QuantityPrecision,
    decimal TickSize,
    decimal StepSize,
    decimal MinQty,
    decimal MaxQty,
    decimal MinNotional);
