using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;

namespace EasyTrading.Dydx.Infrastructure;

/// <summary>Converts dYdX Indexer raw DTOs into the shared <see cref="EasyTrading.Abstractions.Models"/> shapes.</summary>
internal static class Mapper
{
    public static Symbol MapSymbol(PerpetualMarketRaw raw)
    {
        // dYdX perp tickers look like "BTC-USD". The base/quote split:
        string baseAsset = raw.Ticker, quoteAsset = "USD";
        var dash = raw.Ticker.IndexOf('-');
        if (dash > 0)
        {
            baseAsset  = raw.Ticker[..dash];
            quoteAsset = raw.Ticker[(dash + 1)..];
        }

        return new Symbol(
            Name:        raw.Ticker,
            Kind:        MarketKind.Perpetual,
            BaseAsset:   baseAsset,
            QuoteAsset:  quoteAsset,
            PriceTick:   raw.TickSize ?? 0m,
            SizeStep:    raw.StepSize ?? 0m,
            MinSize:     raw.StepSize ?? 0m,
            MaxLeverage: raw.InitialMarginFraction is { } imf && imf > 0m
                ? (int)Math.Floor(1m / imf)
                : null);
    }

    public static OrderBook MapOrderBook(string ticker, OrderbookRaw raw)
    {
        return new OrderBook(
            Symbol:    ticker,
            Timestamp: DateTimeOffset.UtcNow, // Indexer's REST orderbook doesn't carry a timestamp
            Bids:      raw.Bids.Select(l => new OrderBookLevel(l.Price, l.Size, OrderCount: 0)).ToList(),
            Asks:      raw.Asks.Select(l => new OrderBookLevel(l.Price, l.Size, OrderCount: 0)).ToList());
    }

    public static PublicTrade MapTrade(string ticker, PublicTradeRaw raw)
    {
        var side = string.Equals(raw.Side, "BUY", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Buy : OrderSide.Sell;
        var time = ParseIso(raw.CreatedAt);
        // dYdX trade ids are GUIDs; we hash to long for the cross-DEX shape.
        var tradeId = StableLongFromString(raw.Id);

        return new PublicTrade(
            Symbol:  ticker,
            Price:   raw.Price,
            Size:    raw.Size,
            Side:    side,
            Time:    time,
            TradeId: tradeId);
    }

    public static Candle MapCandle(CandleRaw raw)
    {
        var open = ParseIso(raw.StartedAt);
        var interval = ParseResolution(raw.Resolution);
        var close = open + ResolutionToDuration(raw.Resolution);

        return new Candle(
            Symbol:     raw.Ticker,
            Interval:   interval,
            OpenTime:   open,
            CloseTime:  close,
            Open:       raw.Open,
            High:       raw.High,
            Low:        raw.Low,
            Close:      raw.Close,
            Volume:     raw.BaseTokenVolume,
            TradeCount: raw.Trades ?? 0);
    }

    public static FundingRecord MapFunding(FundingEntryRaw raw) => new(
        Symbol:    raw.Ticker,
        Rate:      raw.Rate,
        Time:      ParseIso(raw.EffectiveAt),
        MarkPrice: raw.Price);

    public static DateTimeOffset ParseIso(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static string ResolutionWire(Interval i) => i switch
    {
        Interval.OneMinute      => "1MIN",
        Interval.FiveMinutes    => "5MINS",
        Interval.FifteenMinutes => "15MINS",
        Interval.ThirtyMinutes  => "30MINS",
        Interval.OneHour        => "1HOUR",
        Interval.FourHours      => "4HOURS",
        Interval.OneDay         => "1DAY",
        _ => "1MIN",
    };

    public static Interval ParseResolution(string s) => s switch
    {
        "1MIN"   => Interval.OneMinute,
        "5MINS"  => Interval.FiveMinutes,
        "15MINS" => Interval.FifteenMinutes,
        "30MINS" => Interval.ThirtyMinutes,
        "1HOUR"  => Interval.OneHour,
        "4HOURS" => Interval.FourHours,
        "1DAY"   => Interval.OneDay,
        _ => Interval.OneMinute,
    };

    private static TimeSpan ResolutionToDuration(string s) => s switch
    {
        "1MIN"   => TimeSpan.FromMinutes(1),
        "5MINS"  => TimeSpan.FromMinutes(5),
        "15MINS" => TimeSpan.FromMinutes(15),
        "30MINS" => TimeSpan.FromMinutes(30),
        "1HOUR"  => TimeSpan.FromHours(1),
        "4HOURS" => TimeSpan.FromHours(4),
        "1DAY"   => TimeSpan.FromDays(1),
        _ => TimeSpan.FromMinutes(1),
    };

    /// <summary>
    /// Hash a GUID-ish trade id down to a stable long for the cross-DEX <see cref="PublicTrade.TradeId"/>.
    /// We use FNV-1a 64 — fast, deterministic, no allocations, low collision rate for short strings.
    /// </summary>
    private static long StableLongFromString(string s)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime  = 1099511628211UL;
        var hash = fnvOffset;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= fnvPrime;
        }
        return unchecked((long)hash);
    }
}
