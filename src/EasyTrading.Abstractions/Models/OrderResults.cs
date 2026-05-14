namespace EasyTrading.Abstractions.Models;

/// <summary>Result of placing a single order.</summary>
/// <param name="OrderId">Exchange-assigned order id (0 if the order was rejected before resting).</param>
/// <param name="ClientOrderId">Client-assigned id if one was provided at submission.</param>
/// <param name="Status">Resulting status — <see cref="OrderStatus.Open"/> for resting orders, <see cref="OrderStatus.Filled"/> if filled immediately, <see cref="OrderStatus.Rejected"/> with <see cref="ErrorMessage"/> on failure.</param>
/// <param name="FilledSize">Size filled at submission (for IOC / market / aggressive limit orders).</param>
/// <param name="AverageFillPrice">Average fill price across the immediate fills, if any.</param>
/// <param name="ErrorMessage">Error description if the order was rejected.</param>
public sealed record PlaceOrderResult(
    long OrderId,
    string? ClientOrderId,
    OrderStatus Status,
    decimal FilledSize,
    decimal? AverageFillPrice,
    string? ErrorMessage);

/// <summary>Result of a batch order placement — one entry per submitted order, in submission order.</summary>
/// <param name="Results">Per-order results.</param>
public sealed record BatchOrderResult(IReadOnlyList<PlaceOrderResult> Results);

/// <summary>Result of modifying an order.</summary>
/// <param name="OrderId">Exchange order id (possibly new after modify).</param>
/// <param name="Success">Whether the modification succeeded.</param>
/// <param name="ErrorMessage">Error description on failure.</param>
public sealed record ModifyResult(long OrderId, bool Success, string? ErrorMessage);

/// <summary>Result of a batch order modification.</summary>
/// <param name="Results">Per-order results.</param>
public sealed record BatchModifyResult(IReadOnlyList<ModifyResult> Results);

/// <summary>Result of cancelling an order.</summary>
/// <param name="OrderId">Order id that was cancelled.</param>
/// <param name="Success">Whether the cancellation succeeded.</param>
/// <param name="ErrorMessage">Error description on failure.</param>
public sealed record CancelResult(long OrderId, bool Success, string? ErrorMessage);

/// <summary>Result of a batch cancel.</summary>
/// <param name="Results">Per-order results.</param>
public sealed record BatchCancelResult(IReadOnlyList<CancelResult> Results);
