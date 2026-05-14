using System.Globalization;
using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.Modules;

/// <summary>Aster implementation of <see cref="IMarkets"/> backed by V3 Futures public REST endpoints.</summary>
internal sealed class Markets(RestClient rest) : IMarkets
{
    public async Task<IReadOnlyList<Symbol>> GetSymbolsAsync(MarketKind kind = MarketKind.All, CancellationToken ct = default)
    {
        var info = await rest.GetAsync<ExchangeInfoRaw>("/fapi/v3/exchangeInfo", query: null, ct).ConfigureAwait(false);
        IEnumerable<Symbol> mapped = info.Symbols
            .Where(s => string.Equals(s.Status, "TRADING", StringComparison.OrdinalIgnoreCase))
            .Select(Mapper.MapSymbol);

        if (kind != MarketKind.All)
            mapped = mapped.Where(s => s.Kind == kind || kind == MarketKind.All);

        return mapped.ToList();
    }

    public async Task<Symbol> GetSymbolAsync(string symbol, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);

        var info = await rest.GetAsync<ExchangeInfoRaw>("/fapi/v3/exchangeInfo", query: null, ct).ConfigureAwait(false);
        var match = info.Symbols.FirstOrDefault(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ExchangeApiException($"Symbol '{symbol}' was not found on Aster.");
        return Mapper.MapSymbol(match);
    }

    public async Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);

        // Aster's /fapi/v3/depth accepts limit values: 5, 10, 20, 50, 100, 500, 1000. Round up to the
        // nearest valid limit to satisfy the caller without rejecting.
        int[] allowed = [5, 10, 20, 50, 100, 500, 1000];
        var limit = allowed.FirstOrDefault(x => x >= depth);
        if (limit == 0) limit = 1000;

        var query = new Dictionary<string, string>
        {
            ["symbol"] = symbol,
            ["limit"]  = limit.ToString(CultureInfo.InvariantCulture),
        };
        var raw = await rest.GetAsync<DepthRaw>("/fapi/v3/depth", query, ct).ConfigureAwait(false);
        return Mapper.MapOrderBook(symbol, raw);
    }

    public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => throw new NotImplementedException("Markets.GetCandlesAsync — pending Phase 6.1 (Aster candles mapping).");

    public async Task<IReadOnlyDictionary<string, decimal>> GetAllMidsAsync(CancellationToken ct = default)
    {
        // /fapi/v3/ticker/price returns an array when symbol is omitted.
        var tickers = await rest.GetAsync<List<PriceTickerRaw>>("/fapi/v3/ticker/price", query: null, ct).ConfigureAwait(false);
        var result = new Dictionary<string, decimal>(tickers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var t in tickers) result[t.Symbol] = t.Price;
        return result;
    }

    public async Task<decimal> GetMidAsync(string symbol, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        var query = new Dictionary<string, string> { ["symbol"] = symbol };
        var raw = await rest.GetAsync<PriceTickerRaw>("/fapi/v3/ticker/price", query, ct).ConfigureAwait(false);
        return raw.Price;
    }

    public async Task<FundingInfo> GetFundingAsync(string symbol, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        var query = new Dictionary<string, string> { ["symbol"] = symbol };
        var raw = await rest.GetAsync<PremiumIndexRaw>("/fapi/v3/premiumIndex", query, ct).ConfigureAwait(false);
        return Mapper.MapFunding(raw);
    }

    public async Task<IReadOnlyList<FundingRecord>> GetFundingHistoryAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        var query = new Dictionary<string, string>
        {
            ["symbol"]    = symbol,
            ["startTime"] = from.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["endTime"]   = to.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["limit"]     = "1000",
        };
        var raw = await rest.GetAsync<List<FundingRateEntryRaw>>("/fapi/v3/fundingRate", query, ct).ConfigureAwait(false);
        return raw.Select(Mapper.MapFundingRecord).ToList();
    }

    public async Task<IReadOnlyList<PublicTrade>> GetRecentTradesAsync(string symbol, int limit = 100, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        var query = new Dictionary<string, string>
        {
            ["symbol"] = symbol,
            ["limit"]  = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture),
        };
        var arr = await rest.GetRawAsync("/fapi/v3/trades", query, ct).ConfigureAwait(false);
        if (arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<PublicTrade>();

        var result = new List<PublicTrade>(arr.GetArrayLength());
        foreach (var t in arr.EnumerateArray())
        {
            var price = ParseDec(t.GetProperty("price").GetString() ?? "0");
            var qty   = ParseDec(t.GetProperty("qty").GetString() ?? "0");
            var time  = t.GetProperty("time").GetInt64();
            var id    = t.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0L;
            // Aster reports "isBuyerMaker": true when the taker SOLD into a resting buy.
            var isBuyerMaker = t.TryGetProperty("isBuyerMaker", out var bmEl) && bmEl.GetBoolean();
            var side = isBuyerMaker ? OrderSide.Sell : OrderSide.Buy;
            result.Add(new PublicTrade(symbol, price, qty, side, Mapper.ToDt(time), id));
        }
        return result;
    }

    public async Task<decimal> GetOpenInterestAsync(string symbol, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol);
        var query = new Dictionary<string, string> { ["symbol"] = symbol };
        var raw = await rest.GetAsync<OpenInterestRaw>("/fapi/v3/openInterest", query, ct).ConfigureAwait(false);
        return raw.OpenInterest;
    }

    private static decimal ParseDec(string s) =>
        decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
