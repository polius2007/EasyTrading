namespace EasyTrading.Abstractions.Models;

/// <summary>Current perpetual funding-rate snapshot.</summary>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Rate">Current funding rate as a fraction (e.g. <c>0.0001</c> = 0.01%).</param>
/// <param name="NextFundingTime">Time at which this rate is next applied.</param>
/// <param name="MarkPrice">Current mark price used for funding.</param>
/// <param name="IndexPrice">Current index price.</param>
public sealed record FundingInfo(
    string Symbol,
    decimal Rate,
    DateTimeOffset NextFundingTime,
    decimal MarkPrice,
    decimal IndexPrice);

/// <summary>Historical funding-payment record.</summary>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Rate">Funding rate that was applied.</param>
/// <param name="Time">Time the funding was applied.</param>
/// <param name="MarkPrice">Mark price at funding time.</param>
public sealed record FundingRecord(
    string Symbol,
    decimal Rate,
    DateTimeOffset Time,
    decimal MarkPrice);

/// <summary>A public trade tick (one of many on the tape).</summary>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Price">Trade price.</param>
/// <param name="Size">Trade size in base asset.</param>
/// <param name="Side">Aggressor side.</param>
/// <param name="Time">Server timestamp.</param>
/// <param name="TradeId">Exchange-assigned trade id.</param>
public sealed record PublicTrade(
    string Symbol,
    decimal Price,
    decimal Size,
    OrderSide Side,
    DateTimeOffset Time,
    long TradeId);
