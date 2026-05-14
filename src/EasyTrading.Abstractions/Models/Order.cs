namespace EasyTrading.Abstractions.Models;

/// <summary>A resting / completed order from the exchange's perspective.</summary>
/// <param name="OrderId">Exchange-assigned order id.</param>
/// <param name="ClientOrderId">Client-assigned id if provided at submission.</param>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="OrderType">Order type at submission.</param>
/// <param name="Price">Limit price, if applicable.</param>
/// <param name="TriggerPrice">Trigger price for stop / take-profit orders.</param>
/// <param name="Size">Original order size.</param>
/// <param name="FilledSize">Total filled size so far.</param>
/// <param name="TimeInForce">Time-in-force.</param>
/// <param name="ReduceOnly">Whether the order is reduce-only.</param>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="CreatedAt">Time the order was placed on the exchange.</param>
/// <param name="UpdatedAt">Time of the most recent status change.</param>
public sealed record Order(
    long OrderId,
    string? ClientOrderId,
    string Symbol,
    OrderSide Side,
    OrderType OrderType,
    decimal? Price,
    decimal? TriggerPrice,
    decimal Size,
    decimal FilledSize,
    TimeInForce TimeInForce,
    bool ReduceOnly,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Request to modify an existing order. Identify the target by either <see cref="OrderId"/> or <see cref="ClientOrderId"/>.</summary>
/// <param name="Symbol">Market symbol the order belongs to.</param>
/// <param name="OrderId">Exchange order id. Optional if <see cref="ClientOrderId"/> is set.</param>
/// <param name="ClientOrderId">Client-assigned order id. Optional if <see cref="OrderId"/> is set.</param>
/// <param name="NewPrice">New limit price.</param>
/// <param name="NewSize">New size.</param>
public sealed record ModifyRequest(
    string Symbol,
    long? OrderId = null,
    string? ClientOrderId = null,
    decimal? NewPrice = null,
    decimal? NewSize = null);

/// <summary>Request to cancel a single order.</summary>
/// <param name="Symbol">Market symbol the order belongs to.</param>
/// <param name="OrderId">Exchange order id. Optional if <see cref="ClientOrderId"/> is set.</param>
/// <param name="ClientOrderId">Client-assigned order id. Optional if <see cref="OrderId"/> is set.</param>
public sealed record CancelRequest(
    string Symbol,
    long? OrderId = null,
    string? ClientOrderId = null);
