using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Dydx.Infrastructure;

namespace EasyTrading.Dydx.Modules;

/// <summary>dYdX implementation of <see cref="IMarkets"/> backed by the v4 Indexer REST API.</summary>
internal sealed class Markets(RestClient rest) : IMarkets
{
    public async Task<IReadOnlyList<Symbol>> GetSymbolsAsync(MarketKind kind = MarketKind.All, CancellationToken ct = default)
    {
        var raw = await rest.GetAsync<PerpetualMarketsRaw>("perpetualMarkets", query: null, ct).ConfigureAwait(false);
        var mapped = raw.Markets.Values
            .Where(m => string.Equals(m.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .Select(Mapper.MapSymbol);

        // dYdX v4 only has perpetuals — when caller asks for Spot only, return nothing.
        if (kind == MarketKind.Spot) return Array.Empty<Symbol>();
        return mapped.ToList();
    }

    public async Task<Symbol> GetSymbolAsync(string symbol, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        var query = new Dictionary<string, string> { ["ticker"] = symbol };
        var raw = await rest.GetAsync<PerpetualMarketsRaw>("perpetualMarkets", query, ct).ConfigureAwait(false);
        if (raw.Markets.TryGetValue(symbol, out var m))
            return Mapper.MapSymbol(m);
        throw new ExchangeApiException($"Symbol '{symbol}' was not found on dYdX.");
    }

    public async Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        // Indexer doesn't accept a depth parameter — we get the full book and truncate client-side.
        var raw = await rest.GetAsync<OrderbookRaw>($"orderbooks/perpetualMarket/{symbol}", query: null, ct).ConfigureAwait(false);

        var book = Mapper.MapOrderBook(symbol, raw);
        if (book.Bids.Count <= depth && book.Asks.Count <= depth) return book;

        return new OrderBook(
            Symbol:    symbol,
            Timestamp: book.Timestamp,
            Bids:      book.Bids.Take(depth).ToList(),
            Asks:      book.Asks.Take(depth).ToList());
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        var query = new Dictionary<string, string>
        {
            ["resolution"] = Mapper.ResolutionWire(interval),
            ["fromISO"]    = from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["toISO"]      = to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["limit"]      = "1000",
        };
        var raw = await rest.GetAsync<CandlesRaw>($"candles/perpetualMarkets/{symbol}", query, ct).ConfigureAwait(false);
        return raw.Candles.Select(Mapper.MapCandle).ToList();
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetAllMidsAsync(CancellationToken ct = default)
    {
        // The Indexer doesn't expose a single all-mids endpoint; oracle price on /perpetualMarkets
        // is the closest substitute and is the price dYdX uses for funding + liquidations.
        var raw = await rest.GetAsync<PerpetualMarketsRaw>("perpetualMarkets", query: null, ct).ConfigureAwait(false);
        var result = new Dictionary<string, decimal>(raw.Markets.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (ticker, m) in raw.Markets)
            if (m.OraclePrice is > 0m) result[ticker] = m.OraclePrice.Value;
        return result;
    }

    public async Task<decimal> GetMidAsync(string symbol, CancellationToken ct = default)
    {
        var sym = await GetSymbolAsync(symbol, ct).ConfigureAwait(false);
        // The Symbol record doesn't carry oracle price — refetch raw to surface it.
        var query = new Dictionary<string, string> { ["ticker"] = sym.Name };
        var raw = await rest.GetAsync<PerpetualMarketsRaw>("perpetualMarkets", query, ct).ConfigureAwait(false);
        if (raw.Markets.TryGetValue(sym.Name, out var m) && m.OraclePrice is > 0m)
            return m.OraclePrice.Value;
        throw new ExchangeApiException($"Mid price for '{symbol}' was not found on dYdX.");
    }

    public async Task<FundingInfo> GetFundingAsync(string symbol, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string> { ["ticker"] = symbol };
        var raw = await rest.GetAsync<PerpetualMarketsRaw>("perpetualMarkets", query, ct).ConfigureAwait(false);
        if (!raw.Markets.TryGetValue(symbol, out var m))
            throw new ExchangeApiException($"Symbol '{symbol}' was not found on dYdX.");

        return new FundingInfo(
            Symbol:          symbol,
            Rate:            m.NextFundingRate ?? 0m,
            NextFundingTime: DateTimeOffset.UtcNow.AddHours(1), // dYdX funds hourly; Indexer doesn't expose the exact next time
            MarkPrice:       m.OraclePrice ?? 0m,
            IndexPrice:      m.OraclePrice ?? 0m);
    }

    public async Task<IReadOnlyList<FundingRecord>> GetFundingHistoryAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        var query = new Dictionary<string, string>
        {
            ["effectiveBeforeOrAt"] = to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["limit"]               = "1000",
        };
        var raw = await rest.GetAsync<HistoricalFundingRaw>($"historicalFunding/{symbol}", query, ct).ConfigureAwait(false);
        // dYdX returns most-recent-first; filter to [from, to) and reorder ascending for the
        // cross-DEX contract.
        return raw.HistoricalFunding
            .Select(Mapper.MapFunding)
            .Where(f => f.Time >= from && f.Time < to)
            .OrderBy(f => f.Time)
            .ToList();
    }

    public async Task<IReadOnlyList<PublicTrade>> GetRecentTradesAsync(string symbol, int limit = 100, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        var query = new Dictionary<string, string>
        {
            ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture),
        };
        var raw = await rest.GetAsync<TradesRaw>($"trades/perpetualMarket/{symbol}", query, ct).ConfigureAwait(false);
        return raw.Trades.Select(t => Mapper.MapTrade(symbol, t)).ToList();
    }

    public async Task<decimal> GetOpenInterestAsync(string symbol, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string> { ["ticker"] = symbol };
        var raw = await rest.GetAsync<PerpetualMarketsRaw>("perpetualMarkets", query, ct).ConfigureAwait(false);
        if (!raw.Markets.TryGetValue(symbol, out var m))
            throw new ExchangeApiException($"Symbol '{symbol}' was not found on dYdX.");
        return m.OpenInterest ?? 0m;
    }
}
