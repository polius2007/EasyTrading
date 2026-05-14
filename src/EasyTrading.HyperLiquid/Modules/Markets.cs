using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IMarkets"/> backed by the Info endpoint.</summary>
internal sealed class Markets(InfoClient info) : IMarkets
{
    public async Task<IReadOnlyList<Symbol>> GetSymbolsAsync(MarketKind kind = MarketKind.All, CancellationToken ct = default)
    {
        var results = new List<Symbol>();

        if (kind.HasFlag(MarketKind.Perpetual))
        {
            var meta = await info.PostAsync<MetaResponseRaw>(new { type = "meta" }, ct).ConfigureAwait(false);
            foreach (var asset in meta.Universe)
            {
                if (asset.IsDelisted == true)
                    continue;
                results.Add(Mapper.Map(asset));
            }
        }

        if (kind.HasFlag(MarketKind.Spot))
        {
            var spot = await info.PostAsync<SpotMetaResponseRaw>(new { type = "spotMeta" }, ct).ConfigureAwait(false);
            var byIndex = spot.Tokens.ToDictionary(t => t.Index);
            foreach (var pair in spot.Universe)
                results.Add(Mapper.MapSpot(pair, byIndex));
        }

        return results;
    }

    public async Task<Symbol> GetSymbolAsync(string symbol, CancellationToken ct = default)
    {
        var all = await GetSymbolsAsync(MarketKind.All, ct).ConfigureAwait(false);
        return all.FirstOrDefault(s => s.Name == symbol)
            ?? throw new ExchangeApiException($"Symbol '{symbol}' was not found on HyperLiquid.");
    }

    public async Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default)
    {
        var raw = await info.PostAsync<L2BookRaw>(new { type = "l2Book", coin = symbol }, ct).ConfigureAwait(false);
        var book = Mapper.Map(raw);
        if (depth >= book.Bids.Count && depth >= book.Asks.Count)
            return book;
        return book with
        {
            Bids = book.Bids.Take(depth).ToList(),
            Asks = book.Asks.Take(depth).ToList(),
        };
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var raw = await info.PostAsync<List<CandleRaw>>(new
        {
            type = "candleSnapshot",
            req = new
            {
                coin = symbol,
                interval = Mapper.SerializeInterval(interval),
                startTime = from.ToUnixTimeMilliseconds(),
                endTime = to.ToUnixTimeMilliseconds(),
            },
        }, ct).ConfigureAwait(false);
        return raw.Select(Mapper.Map).ToList();
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetAllMidsAsync(CancellationToken ct = default)
    {
        var raw = await info.PostAsync<Dictionary<string, string>>(new { type = "allMids" }, ct).ConfigureAwait(false);
        var result = new Dictionary<string, decimal>(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in raw)
            result[kv.Key] = Mapper.D(kv.Value);
        return result;
    }

    public async Task<decimal> GetMidAsync(string symbol, CancellationToken ct = default)
    {
        var mids = await GetAllMidsAsync(ct).ConfigureAwait(false);
        return mids.TryGetValue(symbol, out var mid)
            ? mid
            : throw new ExchangeApiException($"Mid price for '{symbol}' was not found on HyperLiquid.");
    }

    public async Task<FundingInfo> GetFundingAsync(string symbol, CancellationToken ct = default)
    {
        var (universe, ctxs) = await GetMetaAndAssetCtxsAsync(ct).ConfigureAwait(false);
        var idx = universe.FindIndex(a => a.Name == symbol);
        if (idx < 0)
            throw new ExchangeApiException($"Symbol '{symbol}' was not found on HyperLiquid.");

        // HyperLiquid pays funding every hour, on the hour.
        var now = DateTimeOffset.UtcNow;
        var nextFunding = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
        return Mapper.MapFunding(symbol, ctxs[idx], nextFunding);
    }

    public async Task<IReadOnlyList<FundingRecord>> GetFundingHistoryAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var raw = await info.PostAsync<List<FundingHistoryEntryRaw>>(new
        {
            type = "fundingHistory",
            coin = symbol,
            startTime = from.ToUnixTimeMilliseconds(),
            endTime = to.ToUnixTimeMilliseconds(),
        }, ct).ConfigureAwait(false);
        return raw.Select(Mapper.Map).ToList();
    }

    /// <summary>
    /// HyperLiquid does not expose recent public trades through the REST Info endpoint.
    /// Subscribe to <see cref="IStreams.TradesAsync"/> for live trade ticks instead.
    /// </summary>
    public Task<IReadOnlyList<PublicTrade>> GetRecentTradesAsync(string symbol, int limit = 100, CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<PublicTrade>>(new NotSupportedException(
            "HyperLiquid does not expose recent public trades via REST. "
            + "Subscribe via IStreams.TradesAsync(symbol, ct) for live ticks."));

    public async Task<decimal> GetOpenInterestAsync(string symbol, CancellationToken ct = default)
    {
        var (universe, ctxs) = await GetMetaAndAssetCtxsAsync(ct).ConfigureAwait(false);
        var idx = universe.FindIndex(a => a.Name == symbol);
        if (idx < 0)
            throw new ExchangeApiException($"Symbol '{symbol}' was not found on HyperLiquid.");
        return ctxs[idx].OpenInterest;
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task<(List<MetaAssetRaw> Universe, List<AssetCtxRaw> Ctxs)> GetMetaAndAssetCtxsAsync(CancellationToken ct)
    {
        // metaAndAssetCtxs returns a 2-element JSON array [metaObj, ctxs[]]
        var raw = await info.PostRawAsync(new { type = "metaAndAssetCtxs" }, ct).ConfigureAwait(false);

        var meta = raw[0].Deserialize<MetaResponseRaw>(JsonOptions.Default)
            ?? throw new ExchangeApiException("HyperLiquid metaAndAssetCtxs: meta payload was null.");

        var ctxs = raw[1].Deserialize<List<AssetCtxRaw>>(JsonOptions.Default)
            ?? throw new ExchangeApiException("HyperLiquid metaAndAssetCtxs: contexts payload was null.");

        return (meta.Universe.ToList(), ctxs);
    }
}
