namespace EasyTrading.Abstractions;

/// <summary>Order side — buy or sell.</summary>
public enum OrderSide
{
    /// <summary>Buy / long order.</summary>
    Buy,

    /// <summary>Sell / short order.</summary>
    Sell,
}

/// <summary>Type of order at submission time.</summary>
public enum OrderType
{
    /// <summary>Resting limit order at a specified price.</summary>
    Limit,

    /// <summary>Immediate execution at the best available price.</summary>
    Market,

    /// <summary>Stop-loss triggered as a limit order at <c>price</c>.</summary>
    StopLimit,

    /// <summary>Stop-loss triggered as a market order.</summary>
    StopMarket,

    /// <summary>Take-profit trigger (limit or market form is venue-specific).</summary>
    TakeProfit,

    /// <summary>Time-weighted-average-price order (sliced execution).</summary>
    Twap,
}

/// <summary>Time-in-force for limit orders.</summary>
public enum TimeInForce
{
    /// <summary>Good-til-cancelled — resting order until filled or cancelled.</summary>
    Gtc,

    /// <summary>Immediate-or-cancel — fill what can be filled immediately, cancel the rest.</summary>
    Ioc,

    /// <summary>Fill-or-kill — fully fill immediately or cancel entirely.</summary>
    Fok,

    /// <summary>Add-liquidity-only / post-only — reject if the order would take liquidity.</summary>
    Alo,
}

/// <summary>Margin mode for perpetual positions.</summary>
public enum MarginMode
{
    /// <summary>Cross margin — equity is shared across all positions.</summary>
    Cross,

    /// <summary>Isolated margin — equity is fenced per position.</summary>
    Isolated,
}

/// <summary>Market kind filter. Use as flags to query both kinds at once.</summary>
[Flags]
public enum MarketKind
{
    /// <summary>No filter.</summary>
    None = 0,

    /// <summary>Perpetual futures markets.</summary>
    Perpetual = 1,

    /// <summary>Spot markets.</summary>
    Spot = 2,

    /// <summary>All market kinds.</summary>
    All = Perpetual | Spot,
}

/// <summary>Candle interval (open-to-open).</summary>
public enum Interval
{
    /// <summary>One minute.</summary>
    OneMinute,

    /// <summary>Three minutes.</summary>
    ThreeMinutes,

    /// <summary>Five minutes.</summary>
    FiveMinutes,

    /// <summary>Fifteen minutes.</summary>
    FifteenMinutes,

    /// <summary>Thirty minutes.</summary>
    ThirtyMinutes,

    /// <summary>One hour.</summary>
    OneHour,

    /// <summary>Two hours.</summary>
    TwoHours,

    /// <summary>Four hours.</summary>
    FourHours,

    /// <summary>Eight hours.</summary>
    EightHours,

    /// <summary>Twelve hours.</summary>
    TwelveHours,

    /// <summary>One day.</summary>
    OneDay,

    /// <summary>Three days.</summary>
    ThreeDays,

    /// <summary>One week.</summary>
    OneWeek,

    /// <summary>One month.</summary>
    OneMonth,
}

/// <summary>Lifecycle status of an order.</summary>
public enum OrderStatus
{
    /// <summary>Submitted but not yet acknowledged by the exchange.</summary>
    Pending,

    /// <summary>Resting on the book.</summary>
    Open,

    /// <summary>Partially filled and still resting on the book.</summary>
    PartiallyFilled,

    /// <summary>Fully filled.</summary>
    Filled,

    /// <summary>Cancelled by the user or by the exchange.</summary>
    Cancelled,

    /// <summary>Rejected by the exchange before resting.</summary>
    Rejected,

    /// <summary>Expired (e.g. IOC with no fill).</summary>
    Expired,

    /// <summary>Trigger condition fired and the order has been submitted to the book.</summary>
    Triggered,
}
