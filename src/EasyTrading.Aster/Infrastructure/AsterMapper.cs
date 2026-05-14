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

    // ─── order / fill / position / account mapping ───────────────────────────

    public static OrderSide ParseSide(string s) => string.Equals(s, "BUY", StringComparison.OrdinalIgnoreCase)
        ? OrderSide.Buy : OrderSide.Sell;

    public static OrderStatus ParseStatus(string s) => s.ToUpperInvariant() switch
    {
        "NEW"               => OrderStatus.Open,
        "PARTIALLY_FILLED"  => OrderStatus.PartiallyFilled,
        "FILLED"            => OrderStatus.Filled,
        "CANCELED" or "CANCELLED" => OrderStatus.Cancelled,
        "REJECTED"          => OrderStatus.Rejected,
        "EXPIRED"           => OrderStatus.Cancelled, // closest semantic match
        _                   => OrderStatus.Open,
    };

    public static OrderType ParseType(string s) => s.ToUpperInvariant() switch
    {
        "LIMIT"                 => OrderType.Limit,
        "MARKET"                => OrderType.Market,
        "STOP"                  => OrderType.StopLimit,
        "STOP_MARKET"           => OrderType.StopMarket,
        "TAKE_PROFIT"           => OrderType.TakeProfit,
        "TAKE_PROFIT_MARKET"    => OrderType.TakeProfit,
        "TRAILING_STOP_MARKET"  => OrderType.StopMarket,
        _                       => OrderType.Limit,
    };

    public static TimeInForce ParseTif(string? s) => s?.ToUpperInvariant() switch
    {
        "GTC"   => TimeInForce.Gtc,
        "IOC"   => TimeInForce.Ioc,
        "FOK"   => TimeInForce.Fok,
        "GTX"   => TimeInForce.Alo, // Aster's GTX = Good-Till-Crossing (post-only); maps to ALO
        null    => TimeInForce.Gtc,
        _       => TimeInForce.Gtc,
    };

    public static MarginMode ParseMarginMode(string? s) => s?.ToUpperInvariant() switch
    {
        "ISOLATED"  => MarginMode.Isolated,
        "CROSSED" or "CROSS" => MarginMode.Cross,
        _           => MarginMode.Cross,
    };

    public static Order MapOrder(OrderResponseRaw raw)
    {
        var createdMs = raw.Time ?? raw.UpdateTime ?? 0L;
        var updatedMs = raw.UpdateTime ?? raw.Time ?? 0L;
        return new Order(
            OrderId:        raw.OrderId,
            ClientOrderId:  raw.ClientOrderId,
            Symbol:         raw.Symbol,
            Side:           ParseSide(raw.Side),
            OrderType:      ParseType(raw.Type),
            Price:          raw.Price > 0 ? raw.Price : null,
            TriggerPrice:   raw.StopPrice > 0 ? raw.StopPrice : null,
            Size:           raw.OrigQty,
            FilledSize:     raw.ExecutedQty,
            TimeInForce:    ParseTif(raw.TimeInForce),
            ReduceOnly:     raw.ReduceOnly ?? false,
            Status:         ParseStatus(raw.Status),
            CreatedAt:      ToDt(createdMs),
            UpdatedAt:      ToDt(updatedMs));
    }

    public static Fill MapFill(UserTradeRaw raw)
    {
        // Aster reports "buyer": true when the user was on the buy side of the trade. The order's
        // side matches that. "maker": true → passive (limit-resting) execution.
        var side = ParseSide(raw.Side);
        return new Fill(
            TradeId:       raw.Id,
            OrderId:       raw.OrderId,
            ClientOrderId: null, // not present in V3 trade payload
            Symbol:        raw.Symbol,
            Side:          side,
            Price:         raw.Price,
            Size:          raw.Quantity,
            Fee:           raw.Commission ?? 0m,
            FeeAsset:      raw.CommissionAsset ?? "USDT",
            IsMaker:       raw.Maker ?? false,
            Time:          ToDt(raw.Time));
    }

    public static Position MapPosition(PositionRiskRaw raw) => new(
        Symbol:           raw.Symbol,
        Size:             raw.PositionAmt,
        EntryPrice:       raw.EntryPrice,
        MarkPrice:        raw.MarkPrice,
        UnrealizedPnl:    raw.UnrealizedProfit,
        RealizedPnl:      0m, // Aster's positionRisk doesn't carry realised PnL — call /income for history
        Leverage:         raw.Leverage,
        MarginMode:       ParseMarginMode(raw.MarginType),
        LiquidationPrice: raw.LiquidationPrice > 0 ? raw.LiquidationPrice : null,
        Margin:           raw.IsolatedMargin ?? 0m);

    public static Position MapPosition(AccountPositionRaw raw) => new(
        Symbol:           raw.Symbol,
        Size:             raw.PositionAmt,
        EntryPrice:       raw.EntryPrice,
        MarkPrice:        raw.MarkPrice ?? 0m,
        UnrealizedPnl:    raw.UnrealizedProfit ?? 0m,
        RealizedPnl:      0m,
        Leverage:         raw.Leverage ?? 1,
        MarginMode:       ParseMarginMode(raw.MarginType),
        LiquidationPrice: raw.LiquidationPrice > 0 ? raw.LiquidationPrice : null,
        Margin:           raw.IsolatedMargin ?? 0m);

    public static AccountState MapAccount(AccountInfoRaw raw)
    {
        var positions = raw.Positions?
            .Where(p => p.PositionAmt != 0m)
            .Select(MapPosition)
            .ToList()
            ?? new List<Position>();

        var balances = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (raw.Assets is not null)
        {
            foreach (var a in raw.Assets)
                balances[a.Asset] = a.WalletBalance;
        }

        return new AccountState(
            AccountValue:      raw.TotalMarginBalance,
            FreeCollateral:    raw.AvailableBalance,
            MaintenanceMargin: raw.TotalMaintMargin,
            Positions:         positions,
            Balances:          balances,
            Timestamp:         ToDt(raw.UpdateTime ?? 0L));
    }
}
