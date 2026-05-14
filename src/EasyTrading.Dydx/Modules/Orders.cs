using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Dydx.Infrastructure;

namespace EasyTrading.Dydx.Modules;

/// <summary>dYdX implementation of <see cref="IOrders"/>. Reads come from <c>/orders</c>
/// (filtered by address + subaccount). Writes require Cosmos SDK transaction signing and land
/// in Phase 7.2 — they throw <see cref="NotImplementedException"/> until then.</summary>
internal sealed class Orders(RestClient rest, DydxClientOptions options) : IOrders
{
    // ─── Reads ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Order>> GetOpenAsync(string? symbol = null, CancellationToken ct = default)
    {
        var creds = RequireCreds();
        var query = new Dictionary<string, string>
        {
            ["address"]          = creds.Address,
            ["subaccountNumber"] = creds.SubaccountNumber.ToString(CultureInfo.InvariantCulture),
            ["status"]           = "OPEN",
            ["limit"]            = "1000",
        };
        if (symbol is not null) query["ticker"] = symbol;

        var raw = await rest.GetAsync<List<OrderRaw>>("orders", query, ct).ConfigureAwait(false);
        return raw.Select(Mapper.MapOrder).ToList();
    }

    public async Task<Order?> GetAsync(long orderId, CancellationToken ct = default)
    {
        // Cross-DEX orderId is a stable-hashed long — we can't reverse it to dYdX's GUID. Walk
        // the open + history lists and match by hash.
        var open = await GetOpenAsync(ct: ct).ConfigureAwait(false);
        var hit = open.FirstOrDefault(o => o.OrderId == orderId);
        if (hit is not null) return hit;

        var hist = await GetHistoryAsync(ct: ct).ConfigureAwait(false);
        return hist.FirstOrDefault(o => o.OrderId == orderId);
    }

    public async Task<Order?> GetByClientIdAsync(string clientOrderId, CancellationToken ct = default)
    {
        var open = await GetOpenAsync(ct: ct).ConfigureAwait(false);
        var hit = open.FirstOrDefault(o => string.Equals(o.ClientOrderId, clientOrderId, StringComparison.Ordinal));
        if (hit is not null) return hit;

        var hist = await GetHistoryAsync(ct: ct).ConfigureAwait(false);
        return hist.FirstOrDefault(o => string.Equals(o.ClientOrderId, clientOrderId, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<Order>> GetHistoryAsync(
        string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var creds = RequireCreds();
        var query = new Dictionary<string, string>
        {
            ["address"]          = creds.Address,
            ["subaccountNumber"] = creds.SubaccountNumber.ToString(CultureInfo.InvariantCulture),
            ["limit"]            = "1000",
        };
        if (symbol is not null) query["ticker"] = symbol;

        var raw = await rest.GetAsync<List<OrderRaw>>("orders", query, ct).ConfigureAwait(false);
        IEnumerable<Order> mapped = raw.Select(Mapper.MapOrder);
        if (from is not null) mapped = mapped.Where(o => o.UpdatedAt >= from.Value);
        if (to   is not null) mapped = mapped.Where(o => o.UpdatedAt < to.Value);
        return mapped.ToList();
    }

    public Task<IReadOnlyList<TwapSliceFill>> GetTwapFillsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TwapSliceFill>>(Array.Empty<TwapSliceFill>());

    // ─── Writes (pending Phase 7.2) ──────────────────────────────────────────

    public Task<PlaceOrderResult> PlaceAsync(OrderRequest request, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<PlaceOrderResult> PlaceLimitAsync(string symbol, OrderSide side, decimal price, decimal size, TimeInForce tif = TimeInForce.Gtc, bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<PlaceOrderResult> PlaceMarketAsync(string symbol, OrderSide side, decimal size, bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<PlaceOrderResult> PlaceStopAsync(string symbol, OrderSide side, decimal triggerPrice, decimal size, bool isMarket = true, bool reduceOnly = true, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<BatchOrderResult> PlaceBatchAsync(IReadOnlyList<OrderRequest> requests, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);

    public Task<ModifyResult> ModifyAsync(ModifyRequest request, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<BatchModifyResult> ModifyBatchAsync(IReadOnlyList<ModifyRequest> requests, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);

    public Task<CancelResult> CancelAsync(string symbol, long orderId, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<CancelResult> CancelByClientIdAsync(string symbol, string clientOrderId, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<BatchCancelResult> CancelBatchAsync(IReadOnlyList<CancelRequest> requests, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<int> CancelAllAsync(string? symbol = null, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task ScheduleCancelAsync(DateTimeOffset? at, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);

    public Task<TwapResult> PlaceTwapAsync(TwapRequest request, CancellationToken ct = default) => throw new NotSupportedException("dYdX v4 does not offer a native TWAP order type.");
    public Task<CancelResult> CancelTwapAsync(string symbol, long twapId, CancellationToken ct = default) => throw new NotSupportedException("dYdX v4 does not offer a native TWAP order type.");

    private DydxCredentials RequireCreds() => options.Credentials
        ?? throw new AuthenticationException(
            "DydxClientOptions.Credentials.Address is required for order queries.");
}
