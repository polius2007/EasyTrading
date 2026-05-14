using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;

namespace EasyTrading.Aster.Infrastructure;

/// <summary>
/// Converts Aster raw DTOs into the shared <see cref="EasyTrading.Abstractions.Models"/> shapes.
/// </summary>
internal static class AsterMapper
{
    /// <summary>Aster's price/size filters live inside the symbol's <c>filters</c> array.</summary>
    public static Symbol MapSymbol(ExchangeSymbolRaw raw)
    {
        decimal priceTick = 0m;
        decimal sizeStep  = 0m;
        decimal minSize   = 0m;

        if (raw.Filters is not null)
        {
            foreach (var f in raw.Filters)
            {
                switch (f.FilterType)
                {
                    case "PRICE_FILTER" when f.TickSize is not null:
                        priceTick = f.TickSize.Value;
                        break;
                    case "LOT_SIZE" when f.StepSize is not null:
                        sizeStep = f.StepSize.Value;
                        if (f.MinQty is not null) minSize = f.MinQty.Value;
                        break;
                }
            }
        }

        // Aster's V3 Futures are all perpetuals on USDT; ContractType="PERPETUAL".
        // (Spot lives under a separate v3 spot API not covered in this initial release.)
        var kind = string.Equals(raw.ContractType, "PERPETUAL", StringComparison.OrdinalIgnoreCase)
            ? MarketKind.Perpetual
            : MarketKind.Perpetual; // default to perp for the futures endpoint

        return new Symbol(
            Name:         raw.Symbol,
            Kind:         kind,
            BaseAsset:    raw.BaseAsset,
            QuoteAsset:   raw.QuoteAsset,
            PriceTick:    priceTick,
            SizeStep:     sizeStep,
            MinSize:      minSize,
            MaxLeverage:  null); // not in exchangeInfo — comes from /leverageBracket
    }

    public static OrderBook MapOrderBook(string symbol, DepthRaw raw)
    {
        return new OrderBook(
            Symbol:    symbol,
            Timestamp: ToDt(raw.EventTime ?? raw.TransactionTime ?? 0),
            Bids:      raw.Bids.Select(MapLevel).ToList(),
            Asks:      raw.Asks.Select(MapLevel).ToList());
    }

    private static OrderBookLevel MapLevel(IReadOnlyList<decimal> arr) =>
        new(Price: arr[0], Size: arr.Count > 1 ? arr[1] : 0m, OrderCount: 0);

    public static FundingInfo MapFunding(PremiumIndexRaw raw) => new(
        Symbol:           raw.Symbol,
        Rate:             raw.LastFundingRate,
        NextFundingTime:  ToDt(raw.NextFundingTime),
        MarkPrice:        raw.MarkPrice,
        IndexPrice:       raw.IndexPrice);

    public static FundingRecord MapFundingRecord(FundingRateEntryRaw raw) => new(
        Symbol:    raw.Symbol,
        Rate:      raw.FundingRate,
        Time:      ToDt(raw.FundingTime),
        MarkPrice: 0m); // Aster's history endpoint doesn't include the mark price snapshot

    /// <summary>Aster timestamps are Unix milliseconds, same as Binance.</summary>
    public static DateTimeOffset ToDt(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
}
