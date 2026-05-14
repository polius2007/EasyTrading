namespace EasyTrading.Abstractions.Models;

/// <summary>OHLCV candle.</summary>
/// <param name="Symbol">The market this candle belongs to.</param>
/// <param name="Interval">Candle duration.</param>
/// <param name="OpenTime">Open timestamp.</param>
/// <param name="CloseTime">Close timestamp.</param>
/// <param name="Open">Open price.</param>
/// <param name="High">High price.</param>
/// <param name="Low">Low price.</param>
/// <param name="Close">Close price.</param>
/// <param name="Volume">Traded volume in the base asset over the interval.</param>
/// <param name="TradeCount">Number of trades in the interval.</param>
public sealed record Candle(
    string Symbol,
    Interval Interval,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    int TradeCount);
