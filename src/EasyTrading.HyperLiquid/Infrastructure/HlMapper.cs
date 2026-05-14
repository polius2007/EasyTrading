using System.Globalization;
using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Maps raw HyperLiquid DTOs into the cross-DEX <c>EasyTrading.Abstractions.Models</c> types.
/// Centralising the conversion here keeps each module thin and gives a single place to fix
/// schema drift if HyperLiquid changes a payload.
/// </summary>
internal static class HlMapper
{
    // ─── primitives ──────────────────────────────────────────────────────────

    public static DateTimeOffset T(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

    public static decimal D(string s) => decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    // ─── side / interval / order type ────────────────────────────────────────

    public static OrderSide ParseSide(string s) => s switch
    {
        "B" or "b" => OrderSide.Buy,
        "A" or "a" => OrderSide.Sell,
        _ => throw new ExchangeApiException($"Unknown HyperLiquid side: '{s}'"),
    };

    public static string SerializeSide(OrderSide side) => side switch
    {
        OrderSide.Buy => "B",
        OrderSide.Sell => "A",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown side"),
    };

    public static Interval ParseInterval(string s) => s switch
    {
        "1m" => Interval.OneMinute,
        "3m" => Interval.ThreeMinutes,
        "5m" => Interval.FiveMinutes,
        "15m" => Interval.FifteenMinutes,
        "30m" => Interval.ThirtyMinutes,
        "1h" => Interval.OneHour,
        "2h" => Interval.TwoHours,
        "4h" => Interval.FourHours,
        "8h" => Interval.EightHours,
        "12h" => Interval.TwelveHours,
        "1d" => Interval.OneDay,
        "3d" => Interval.ThreeDays,
        "1w" => Interval.OneWeek,
        "1M" => Interval.OneMonth,
        _ => throw new ExchangeApiException($"Unknown HyperLiquid interval: '{s}'"),
    };

    public static string SerializeInterval(Interval interval) => interval switch
    {
        Interval.OneMinute => "1m",
        Interval.ThreeMinutes => "3m",
        Interval.FiveMinutes => "5m",
        Interval.FifteenMinutes => "15m",
        Interval.ThirtyMinutes => "30m",
        Interval.OneHour => "1h",
        Interval.TwoHours => "2h",
        Interval.FourHours => "4h",
        Interval.EightHours => "8h",
        Interval.TwelveHours => "12h",
        Interval.OneDay => "1d",
        Interval.ThreeDays => "3d",
        Interval.OneWeek => "1w",
        Interval.OneMonth => "1M",
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unknown interval"),
    };

    public static OrderType ParseOrderType(string? s, bool isTrigger) => s switch
    {
        "Limit" => OrderType.Limit,
        "Market" => OrderType.Market,
        "Stop Market" or "StopMarket" => OrderType.StopMarket,
        "Stop Limit" or "StopLimit" => OrderType.StopLimit,
        "Take Profit Market" or "TakeProfitMarket" => OrderType.TakeProfit,
        "Take Profit Limit" or "TakeProfitLimit" => OrderType.TakeProfit,
        _ => isTrigger ? OrderType.StopMarket : OrderType.Limit,
    };

    public static TimeInForce ParseTimeInForce(string? s) => s switch
    {
        "Gtc" or "GTC" => TimeInForce.Gtc,
        "Ioc" or "IOC" => TimeInForce.Ioc,
        "Fok" or "FOK" => TimeInForce.Fok,
        "Alo" or "ALO" => TimeInForce.Alo,
        "FrontendMarket" => TimeInForce.Ioc,
        _ => TimeInForce.Gtc,
    };

    public static OrderStatus ParseOrderStatus(string s) => s switch
    {
        "open" => OrderStatus.Open,
        "filled" => OrderStatus.Filled,
        "canceled" or "marginCanceled" or "vaultWithdrawalCanceled"
            or "liquidatedCanceled" or "scheduledCanceled" => OrderStatus.Cancelled,
        "rejected" => OrderStatus.Rejected,
        "triggered" => OrderStatus.Triggered,
        _ => OrderStatus.Pending,
    };

    // ─── markets ─────────────────────────────────────────────────────────────

    public static Symbol Map(MetaAssetRaw asset)
    {
        // HyperLiquid encodes only szDecimals on perp meta. priceTick is derived: 5 significant
        // digits on price, max 6 decimals — but for an authoritative tick we'd need market-specific
        // rules. As a pragmatic default we expose 10^-szDecimals for size, 10^-(6) for price.
        var sizeStep = Pow10Neg(asset.SzDecimals);
        var priceTick = Pow10Neg(6 - asset.SzDecimals);    // see HL price rules
        return new Symbol(
            Name: asset.Name,
            Kind: MarketKind.Perpetual,
            BaseAsset: asset.Name,
            QuoteAsset: "USDC",
            PriceTick: priceTick,
            SizeStep: sizeStep,
            MinSize: sizeStep,
            MaxLeverage: asset.MaxLeverage);
    }

    public static Symbol MapSpot(SpotPairRaw pair, IReadOnlyDictionary<int, SpotTokenRaw> tokensByIndex)
    {
        var baseToken = tokensByIndex[pair.Tokens[0]];
        var quoteToken = tokensByIndex[pair.Tokens[1]];
        var sizeStep = Pow10Neg(baseToken.SzDecimals);
        return new Symbol(
            Name: pair.Name,
            Kind: MarketKind.Spot,
            BaseAsset: baseToken.Name,
            QuoteAsset: quoteToken.Name,
            PriceTick: Pow10Neg(8 - baseToken.SzDecimals),
            SizeStep: sizeStep,
            MinSize: sizeStep,
            MaxLeverage: null);
    }

    public static OrderBook Map(L2BookRaw raw)
    {
        var bids = raw.Levels.Count > 0
            ? raw.Levels[0].Select(l => new OrderBookLevel(l.Px, l.Sz, l.N)).ToList()
            : new List<OrderBookLevel>();
        var asks = raw.Levels.Count > 1
            ? raw.Levels[1].Select(l => new OrderBookLevel(l.Px, l.Sz, l.N)).ToList()
            : new List<OrderBookLevel>();
        return new OrderBook(raw.Coin, T(raw.Time), bids, asks);
    }

    public static Candle Map(CandleRaw raw) => new(
        Symbol: raw.Symbol,
        Interval: ParseInterval(raw.Interval),
        OpenTime: T(raw.OpenTime),
        CloseTime: T(raw.CloseTime),
        Open: raw.Open,
        High: raw.High,
        Low: raw.Low,
        Close: raw.Close,
        Volume: raw.Volume,
        TradeCount: raw.N);

    public static FundingInfo MapFunding(string symbol, AssetCtxRaw ctx, DateTimeOffset nextFundingTime) =>
        new(Symbol: symbol,
            Rate: ctx.Funding,
            NextFundingTime: nextFundingTime,
            MarkPrice: ctx.MarkPx,
            IndexPrice: ctx.OraclePx);

    public static FundingRecord Map(FundingHistoryEntryRaw raw) => new(
        Symbol: raw.Coin,
        Rate: raw.FundingRate,
        Time: T(raw.Time),
        MarkPrice: 0m);

    // ─── account / positions ─────────────────────────────────────────────────

    public static Position Map(PositionRaw p, int leverageOverride = 0)
    {
        var size = p.Szi;
        var mode = string.Equals(p.Leverage.Type, "cross", StringComparison.OrdinalIgnoreCase)
            ? MarginMode.Cross
            : MarginMode.Isolated;
        var entry = p.EntryPx ?? 0m;
        var markPrice = size == 0 ? entry : p.PositionValue / Math.Abs(size);
        return new Position(
            Symbol: p.Coin,
            Size: size,
            EntryPrice: entry,
            MarkPrice: markPrice,
            UnrealizedPnl: p.UnrealizedPnl,
            RealizedPnl: 0m,
            Leverage: leverageOverride != 0 ? leverageOverride : p.Leverage.Value,
            MarginMode: mode,
            LiquidationPrice: p.LiquidationPx,
            Margin: p.MarginUsed);
    }

    public static AccountState MapAccountState(
        ClearinghouseStateRaw perp,
        IReadOnlyList<SpotBalanceRaw>? spotBalances)
    {
        var positions = perp.AssetPositions.Select(ap => Map(ap.Position)).ToList();

        var balanceMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (spotBalances is not null)
        {
            foreach (var b in spotBalances)
                balanceMap[b.Coin] = b.Total;
        }
        // Treat perp withdrawable as the effective USDC balance for collateral purposes.
        if (!balanceMap.ContainsKey("USDC"))
            balanceMap["USDC"] = perp.Withdrawable;

        return new AccountState(
            AccountValue: perp.MarginSummary.AccountValue,
            FreeCollateral: perp.Withdrawable,
            MaintenanceMargin: perp.CrossMaintenanceMarginUsed,
            Positions: positions,
            Balances: balanceMap,
            Timestamp: T(perp.Time));
    }

    public static Balance Map(SpotBalanceRaw b) => new(
        Token: b.Coin,
        Total: b.Total,
        Available: b.Total - b.Hold,
        Locked: b.Hold);

    // ─── orders / fills ──────────────────────────────────────────────────────

    public static Order Map(OpenOrderRaw o, OrderStatus status = OrderStatus.Open, long statusTimestamp = 0)
    {
        var side = ParseSide(o.Side);
        var isTrigger = o.IsTrigger ?? false;
        var orderType = ParseOrderType(o.OrderType, isTrigger);
        var tif = ParseTimeInForce(o.Tif);
        var origSize = o.OrigSz ?? o.Sz;
        var remaining = o.Sz;
        var filled = origSize - remaining;
        var createdAt = T(o.Timestamp);
        var updatedAt = statusTimestamp > 0 ? T(statusTimestamp) : createdAt;
        return new Order(
            OrderId: o.Oid,
            ClientOrderId: o.Cloid,
            Symbol: o.Coin,
            Side: side,
            OrderType: orderType,
            Price: o.LimitPx,
            TriggerPrice: isTrigger ? o.TriggerPx : null,
            Size: origSize,
            FilledSize: filled,
            TimeInForce: tif,
            ReduceOnly: o.ReduceOnly ?? false,
            Status: status,
            CreatedAt: createdAt,
            UpdatedAt: updatedAt);
    }

    public static Order Map(HistoricalOrderRaw h) =>
        Map(h.Order, ParseOrderStatus(h.Status), h.StatusTimestamp);

    public static Fill Map(UserFillRaw f) => new(
        TradeId: f.Tid,
        OrderId: f.Oid,
        ClientOrderId: f.Cloid,
        Symbol: f.Coin,
        Side: ParseSide(f.Side),
        Price: f.Px,
        Size: f.Sz,
        Fee: f.Fee,
        FeeAsset: f.FeeToken,
        IsMaker: !f.Crossed,
        Time: T(f.Time));

    public static TwapSliceFill Map(TwapSliceFillRaw raw)
    {
        var f = raw.Fill;
        return new TwapSliceFill(
            TwapId: raw.TwapId,
            Symbol: f.Coin,
            Side: ParseSide(f.Side),
            Price: f.Px,
            Size: f.Sz,
            Time: T(f.Time));
    }

    // ─── fees / rate limit / sub-accounts ────────────────────────────────────

    public static FeeSchedule Map(UserFeesRaw raw) => new(
        MakerRate: raw.UserAddRate,
        TakerRate: raw.UserCrossRate,
        VolumeTier: null,
        VolumeLast30Days: 0m);

    public static RateLimitInfo Map(UserRateLimitRaw raw)
    {
        // HL doesn't expose the window reset time directly; use the current instant as a placeholder.
        return new RateLimitInfo(raw.NRequestsUsed, raw.NRequestsCap, DateTimeOffset.UtcNow);
    }

    public static SubAccount Map(SubAccountRaw raw)
    {
        var spotBalances = raw.SpotState?.Balances;
        var state = MapAccountState(raw.ClearinghouseState, spotBalances);
        return new SubAccount(raw.SubAccountUser, raw.Name, state);
    }

    // ─── vaults / staking ────────────────────────────────────────────────────

    public static VaultDetails Map(VaultDetailsRaw raw)
    {
        var equity = raw.Followers?.Sum(f => f.VaultEquity) ?? 0m;
        var followerCount = raw.Followers?.Count ?? 0;
        return new VaultDetails(
            VaultAddress: raw.VaultAddress,
            Name: raw.Name,
            LeaderAddress: raw.Leader,
            Equity: equity,
            FollowerCount: followerCount,
            MaxDistributable: 0m,
            ApyPercent: raw.Apr.GetValueOrDefault() * 100m);
    }

    public static VaultEquity Map(UserVaultEquityRaw raw) => new(
        VaultAddress: raw.VaultAddress,
        Equity: raw.Equity,
        LockedUntil: raw.LockedUntilTimestamp is > 0 ? T(raw.LockedUntilTimestamp.Value) : null);

    public static Delegation Map(DelegationRaw raw) => new(
        Validator: raw.Validator,
        Amount: raw.Amount,
        LockedUntil: T(raw.LockedUntilTimestamp));

    public static DelegatorSummary Map(DelegatorSummaryRaw raw) => new(
        TotalDelegated: raw.Delegated,
        TotalUndelegating: raw.TotalPendingWithdrawal,
        TotalUndelegated: raw.Undelegated);

    public static Reward Map(DelegatorRewardRaw raw) => new(
        Validator: raw.Source ?? string.Empty,
        Amount: raw.TotalAmount,
        Time: T(raw.Time));

    // ─── portfolio (heterogeneous-tuple shape, parsed manually) ──────────────

    /// <summary>
    /// Parses HyperLiquid's <c>portfolio</c> response, which is
    /// <c>[[period, { accountValueHistory, pnlHistory, vlm }], ...]</c>.
    /// Picks the <c>"allTime"</c> period.
    /// </summary>
    public static Portfolio MapPortfolio(JsonElement raw)
    {
        var equity = new List<PortfolioSample>();
        var pnl = new List<PortfolioSample>();

        foreach (var pair in raw.EnumerateArray())
        {
            var period = pair[0].GetString();
            if (period != "allTime")
                continue;

            var data = pair[1];

            if (data.TryGetProperty("accountValueHistory", out var values))
                ExtractSamples(values, equity);

            if (data.TryGetProperty("pnlHistory", out var pnlSeries))
                ExtractSamples(pnlSeries, pnl);

            break;
        }

        return new Portfolio(equity, pnl);
    }

    private static void ExtractSamples(JsonElement series, List<PortfolioSample> into)
    {
        foreach (var sample in series.EnumerateArray())
        {
            var timeMs = sample[0].GetInt64();
            var raw = sample[1].GetString() ?? "0";
            into.Add(new PortfolioSample(T(timeMs), D(raw)));
        }
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static decimal Pow10Neg(int n)
    {
        if (n <= 0) return 1m;
        decimal result = 1m;
        for (int i = 0; i < n; i++) result /= 10m;
        return result;
    }
}
