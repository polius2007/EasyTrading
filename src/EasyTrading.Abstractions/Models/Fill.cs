namespace EasyTrading.Abstractions.Models;

/// <summary>A single trade execution (fill) on the user's account.</summary>
/// <param name="TradeId">Exchange-assigned trade id.</param>
/// <param name="OrderId">Order id that produced this fill.</param>
/// <param name="ClientOrderId">Client order id if one was set.</param>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Side">Side of the order that produced the fill.</param>
/// <param name="Price">Fill price.</param>
/// <param name="Size">Fill size in base asset.</param>
/// <param name="Fee">Fee charged for this fill (positive = paid, negative = rebate).</param>
/// <param name="FeeAsset">Asset the fee was charged in.</param>
/// <param name="IsMaker">Whether the fill was a maker (passive) or taker (aggressive).</param>
/// <param name="Time">Server timestamp.</param>
public sealed record Fill(
    long TradeId,
    long OrderId,
    string? ClientOrderId,
    string Symbol,
    OrderSide Side,
    decimal Price,
    decimal Size,
    decimal Fee,
    string FeeAsset,
    bool IsMaker,
    DateTimeOffset Time);
