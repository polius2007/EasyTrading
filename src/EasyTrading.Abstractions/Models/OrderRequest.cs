namespace EasyTrading.Abstractions.Models;

/// <summary>
/// Request to place a new order. Use the convenience overloads on <c>IOrders</c>
/// (e.g. <c>PlaceLimitAsync</c>, <c>PlaceMarketAsync</c>) for the common cases —
/// this record is for full control.
/// </summary>
/// <param name="Symbol">Market symbol. Perpetuals use the coin name (e.g. <c>"BTC"</c>); spot uses <c>"BASE/QUOTE"</c> or a venue-specific index (e.g. <c>"@107"</c>).</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="OrderType">Limit, Market, StopLimit, StopMarket, TakeProfit, or Twap.</param>
/// <param name="Size">Order size in base units. Must be a multiple of the market's <c>SizeStep</c> and at least <c>MinSize</c>.</param>
/// <param name="Price">Limit price. Must be a multiple of the market's <c>PriceTick</c>. Required for limit / trigger orders; ignored for market orders.</param>
/// <param name="TriggerPrice">Trigger price for stop / take-profit orders.</param>
/// <param name="TimeInForce">Time-in-force for limit orders. Defaults to <see cref="Abstractions.TimeInForce.Gtc"/>.</param>
/// <param name="ReduceOnly">If <c>true</c>, the order may only reduce an existing position.</param>
/// <param name="ClientOrderId">Optional 128-bit hex client order id. Use to dedupe and to cancel by CLOID later.</param>
/// <param name="BuilderFeeOverride">Optional explicit builder-fee config (only honoured by venues that support builder fees).</param>
public sealed record OrderRequest(
    string Symbol,
    OrderSide Side,
    OrderType OrderType,
    decimal Size,
    decimal? Price = null,
    decimal? TriggerPrice = null,
    TimeInForce TimeInForce = TimeInForce.Gtc,
    bool ReduceOnly = false,
    string? ClientOrderId = null,
    BuilderFee? BuilderFeeOverride = null);

/// <summary>Optional builder-fee routing applied to a single order.</summary>
/// <param name="BuilderAddress">Address of the builder receiving the fee.</param>
/// <param name="FeeRate">Fee rate as a fraction (e.g. <c>0.0005</c> = 0.05%).</param>
public readonly record struct BuilderFee(string BuilderAddress, decimal FeeRate);
