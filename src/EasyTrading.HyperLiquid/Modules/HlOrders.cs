using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IOrders"/>. Read methods use the Info endpoint; write methods land in Phase 3.</summary>
internal sealed class HlOrders(HlInfoClient info, HyperLiquidClientOptions options) : IOrders
{
    // ─── Read methods (Phase 2) ──────────────────────────────────────────────

    public async Task<IReadOnlyList<Order>> GetOpenAsync(string? symbol = null, CancellationToken ct = default)
    {
        var user = RequireUser();
        // frontendOpenOrders carries the richer payload (orderType, tif, reduceOnly, …)
        var raw = await info.PostAsync<List<OpenOrderRaw>>(new { type = "frontendOpenOrders", user }, ct).ConfigureAwait(false);
        IEnumerable<Order> orders = raw.Select(o => HlMapper.Map(o));
        if (symbol is not null) orders = orders.Where(o => o.Symbol == symbol);
        return orders.ToList();
    }

    public async Task<Order?> GetAsync(long orderId, CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<OrderStatusResponseRaw>(new { type = "orderStatus", user, oid = orderId }, ct).ConfigureAwait(false);
        return MapStatus(raw);
    }

    public async Task<Order?> GetByClientIdAsync(string clientOrderId, CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<OrderStatusResponseRaw>(new { type = "orderStatus", user, oid = clientOrderId }, ct).ConfigureAwait(false);
        return MapStatus(raw);
    }

    public async Task<IReadOnlyList<Order>> GetHistoryAsync(string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<List<HistoricalOrderRaw>>(new { type = "historicalOrders", user }, ct).ConfigureAwait(false);
        IEnumerable<Order> orders = raw.Select(HlMapper.Map);
        if (symbol is not null) orders = orders.Where(o => o.Symbol == symbol);
        if (from is not null) orders = orders.Where(o => o.CreatedAt >= from.Value);
        if (to is not null) orders = orders.Where(o => o.CreatedAt < to.Value);
        return orders.ToList();
    }

    public async Task<IReadOnlyList<TwapSliceFill>> GetTwapFillsAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<List<TwapSliceFillRaw>>(new { type = "userTwapSliceFills", user }, ct).ConfigureAwait(false);
        return raw.Select(HlMapper.Map).ToList();
    }

    private static Order? MapStatus(OrderStatusResponseRaw raw)
    {
        if (raw.Status != "order" || raw.Order is null)
            return null;
        return HlMapper.Map(raw.Order.Order, HlMapper.ParseOrderStatus(raw.Order.Status), raw.Order.StatusTimestamp);
    }

    // ─── Write methods (Phase 3) ─────────────────────────────────────────────

    public Task<PlaceOrderResult> PlaceAsync(OrderRequest request, CancellationToken ct = default) => WriteFail<PlaceOrderResult>();
    public Task<BatchOrderResult> PlaceBatchAsync(IReadOnlyList<OrderRequest> requests, CancellationToken ct = default) => WriteFail<BatchOrderResult>();
    public Task<PlaceOrderResult> PlaceLimitAsync(string symbol, OrderSide side, decimal price, decimal size, TimeInForce tif = TimeInForce.Gtc, bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default) => WriteFail<PlaceOrderResult>();
    public Task<PlaceOrderResult> PlaceMarketAsync(string symbol, OrderSide side, decimal size, bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default) => WriteFail<PlaceOrderResult>();
    public Task<PlaceOrderResult> PlaceStopAsync(string symbol, OrderSide side, decimal triggerPrice, decimal size, bool isMarket = true, bool reduceOnly = true, CancellationToken ct = default) => WriteFail<PlaceOrderResult>();
    public Task<ModifyResult> ModifyAsync(ModifyRequest request, CancellationToken ct = default) => WriteFail<ModifyResult>();
    public Task<BatchModifyResult> ModifyBatchAsync(IReadOnlyList<ModifyRequest> requests, CancellationToken ct = default) => WriteFail<BatchModifyResult>();
    public Task<CancelResult> CancelAsync(string symbol, long orderId, CancellationToken ct = default) => WriteFail<CancelResult>();
    public Task<CancelResult> CancelByClientIdAsync(string symbol, string clientOrderId, CancellationToken ct = default) => WriteFail<CancelResult>();
    public Task<BatchCancelResult> CancelBatchAsync(IReadOnlyList<CancelRequest> requests, CancellationToken ct = default) => WriteFail<BatchCancelResult>();
    public Task<int> CancelAllAsync(string? symbol = null, CancellationToken ct = default) => WriteFail<int>();
    public Task ScheduleCancelAsync(DateTimeOffset? at, CancellationToken ct = default) => WriteFail();
    public Task<TwapResult> PlaceTwapAsync(TwapRequest request, CancellationToken ct = default) => WriteFail<TwapResult>();
    public Task<CancelResult> CancelTwapAsync(string symbol, long twapId, CancellationToken ct = default) => WriteFail<CancelResult>();

    private static Task<T> WriteFail<T>() => Task.FromException<T>(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));
    private static Task WriteFail() => Task.FromException(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for order queries.");
}
