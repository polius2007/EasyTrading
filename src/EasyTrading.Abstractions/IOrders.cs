using EasyTrading.Abstractions.Models;

namespace EasyTrading.Abstractions;

/// <summary>
/// All order operations — placement, modification, cancellation, queries, and TWAP. Methods are
/// grouped here (not split between <c>Trading</c> and <c>Account</c>) so that everything you can do
/// with an order lives in one place.
/// </summary>
public interface IOrders
{
    // ─── Place ────────────────────────────────────────────────────────────────

    /// <summary>Place an order with full control over every field.</summary>
    /// <param name="request">The order parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The placement result, including the exchange-assigned order id.</returns>
    /// <exception cref="InvalidOrderException">The order parameters violate market constraints.</exception>
    /// <exception cref="InsufficientFundsException">The account has insufficient collateral.</exception>
    Task<PlaceOrderResult> PlaceAsync(OrderRequest request, CancellationToken ct = default);

    /// <summary>Place several orders in a single round-trip.</summary>
    /// <param name="requests">Orders to submit, in the desired order of submission.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One result per submitted order, in the same order.</returns>
    Task<BatchOrderResult> PlaceBatchAsync(IReadOnlyList<OrderRequest> requests, CancellationToken ct = default);

    /// <summary>Convenience: place a limit order.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="side">Buy or sell.</param>
    /// <param name="price">Limit price.</param>
    /// <param name="size">Order size in base units.</param>
    /// <param name="tif">Time-in-force. Defaults to <see cref="TimeInForce.Gtc"/>; use <see cref="TimeInForce.Alo"/> for post-only.</param>
    /// <param name="reduceOnly">If <c>true</c>, the order may only reduce an existing position.</param>
    /// <param name="clientOrderId">Optional client order id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The placement result.</returns>
    Task<PlaceOrderResult> PlaceLimitAsync(
        string symbol, OrderSide side, decimal price, decimal size,
        TimeInForce tif = TimeInForce.Gtc, bool reduceOnly = false,
        string? clientOrderId = null, CancellationToken ct = default);

    /// <summary>Convenience: place a market order.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="side">Buy or sell.</param>
    /// <param name="size">Order size in base units.</param>
    /// <param name="reduceOnly">If <c>true</c>, the order may only reduce an existing position.</param>
    /// <param name="clientOrderId">Optional client order id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The placement result.</returns>
    Task<PlaceOrderResult> PlaceMarketAsync(
        string symbol, OrderSide side, decimal size,
        bool reduceOnly = false, string? clientOrderId = null,
        CancellationToken ct = default);

    /// <summary>Convenience: place a stop order (stop-loss).</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="side">Order side (a stop-loss on a long position is a sell stop).</param>
    /// <param name="triggerPrice">Price at which the order is triggered.</param>
    /// <param name="size">Order size in base units.</param>
    /// <param name="isMarket">If <c>true</c>, the triggered order is a market order; otherwise it is a limit at <paramref name="triggerPrice"/>.</param>
    /// <param name="reduceOnly">If <c>true</c>, the order may only reduce an existing position. Stops are usually reduce-only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The placement result.</returns>
    Task<PlaceOrderResult> PlaceStopAsync(
        string symbol, OrderSide side, decimal triggerPrice, decimal size,
        bool isMarket = true, bool reduceOnly = true,
        CancellationToken ct = default);

    // ─── Modify ───────────────────────────────────────────────────────────────

    /// <summary>Modify an existing order's price and / or size.</summary>
    /// <param name="request">The modification request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The modification result.</returns>
    Task<ModifyResult> ModifyAsync(ModifyRequest request, CancellationToken ct = default);

    /// <summary>Modify several orders in a single round-trip.</summary>
    /// <param name="requests">Modifications to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One result per requested modification, in the same order.</returns>
    Task<BatchModifyResult> ModifyBatchAsync(IReadOnlyList<ModifyRequest> requests, CancellationToken ct = default);

    // ─── Cancel ───────────────────────────────────────────────────────────────

    /// <summary>Cancel a single order by exchange order id.</summary>
    /// <param name="symbol">Market symbol the order belongs to.</param>
    /// <param name="orderId">Exchange-assigned order id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cancellation result.</returns>
    Task<CancelResult> CancelAsync(string symbol, long orderId, CancellationToken ct = default);

    /// <summary>Cancel a single order by client order id.</summary>
    /// <param name="symbol">Market symbol the order belongs to.</param>
    /// <param name="clientOrderId">Client-assigned order id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cancellation result.</returns>
    Task<CancelResult> CancelByClientIdAsync(string symbol, string clientOrderId, CancellationToken ct = default);

    /// <summary>Cancel several orders in a single round-trip.</summary>
    /// <param name="requests">Cancellations to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One result per requested cancellation, in the same order.</returns>
    Task<BatchCancelResult> CancelBatchAsync(IReadOnlyList<CancelRequest> requests, CancellationToken ct = default);

    /// <summary>Cancel all open orders, optionally filtered to one market.</summary>
    /// <param name="symbol">If specified, cancel only orders on this market; otherwise cancel everything.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of orders cancelled.</returns>
    Task<int> CancelAllAsync(string? symbol = null, CancellationToken ct = default);

    /// <summary>
    /// Schedule a cancel-all (dead-man switch). After <paramref name="at"/> elapses with no further
    /// schedule update, the exchange cancels all open orders for this account. Pass <c>null</c> to
    /// remove a previously scheduled cancel. Only available on venues advertising
    /// <see cref="ExchangeCapabilities.ScheduleCancel"/>.
    /// </summary>
    /// <param name="at">Time at which to cancel-all; <c>null</c> to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ScheduleCancelAsync(DateTimeOffset? at, CancellationToken ct = default);

    // ─── Read ─────────────────────────────────────────────────────────────────

    /// <summary>Get currently open (resting) orders, optionally filtered to one market.</summary>
    /// <param name="symbol">If specified, only orders on this market; otherwise all open orders.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Open orders.</returns>
    Task<IReadOnlyList<Order>> GetOpenAsync(string? symbol = null, CancellationToken ct = default);

    /// <summary>Get an order by exchange order id.</summary>
    /// <param name="orderId">Exchange-assigned order id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The order, or <c>null</c> if not found.</returns>
    Task<Order?> GetAsync(long orderId, CancellationToken ct = default);

    /// <summary>Get an order by client order id.</summary>
    /// <param name="clientOrderId">Client-assigned order id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The order, or <c>null</c> if not found.</returns>
    Task<Order?> GetByClientIdAsync(string clientOrderId, CancellationToken ct = default);

    /// <summary>Get historical orders (filled, cancelled, rejected) over a time range.</summary>
    /// <param name="symbol">If specified, filter to one market.</param>
    /// <param name="from">Start of the time range (inclusive).</param>
    /// <param name="to">End of the time range (exclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Orders in descending time order (newest first).</returns>
    Task<IReadOnlyList<Order>> GetHistoryAsync(
        string? symbol = null, DateTimeOffset? from = null,
        DateTimeOffset? to = null, CancellationToken ct = default);

    // ─── TWAP ─────────────────────────────────────────────────────────────────

    /// <summary>Place a TWAP order. Only available on venues advertising <see cref="ExchangeCapabilities.Twap"/>.</summary>
    /// <param name="request">The TWAP parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The TWAP placement result.</returns>
    Task<TwapResult> PlaceTwapAsync(TwapRequest request, CancellationToken ct = default);

    /// <summary>Cancel an active TWAP order.</summary>
    /// <param name="symbol">Market symbol of the TWAP.</param>
    /// <param name="twapId">TWAP id returned by <see cref="PlaceTwapAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cancellation result.</returns>
    Task<CancelResult> CancelTwapAsync(string symbol, long twapId, CancellationToken ct = default);

    /// <summary>Get the fills produced by TWAP orders.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>TWAP slice fills.</returns>
    Task<IReadOnlyList<TwapSliceFill>> GetTwapFillsAsync(CancellationToken ct = default);
}
