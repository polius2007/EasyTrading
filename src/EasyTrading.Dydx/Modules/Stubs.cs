using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;

namespace EasyTrading.Dydx.Modules;

// ─── Phase-pending stubs ────────────────────────────────────────────────────
//
// Everything that requires a Cosmos SDK signed transaction (writes) lives in Phase 7.2.
// Signed reads (subaccount queries via Indexer w/ optional address) land in Phase 7.1.
// Until then these throw with a precise pointer.

internal static class Phase
{
    public const string Read       = "Pending Phase 7.1 — dYdX signed Indexer reads (Account / Positions / Trades / Orders).";
    public const string Write      = "Pending Phase 7.2 — dYdX Cosmos SDK transaction signing + validator gRPC broadcast.";
    public const string UserStream = "Pending Phase 7.2 — dYdX user WebSocket channels require a signed subaccount subscription.";
}

internal sealed class Orders : IOrders
{
    public Task<IReadOnlyList<Order>> GetOpenAsync(string? symbol = null, CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<Order?> GetAsync(long orderId, CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<Order?> GetByClientIdAsync(string clientOrderId, CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<IReadOnlyList<Order>> GetHistoryAsync(string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<IReadOnlyList<TwapSliceFill>> GetTwapFillsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TwapSliceFill>>(Array.Empty<TwapSliceFill>());

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
}

internal sealed class Positions : IPositions
{
    public Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<Position?> GetAsync(string symbol, CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task SetLeverageAsync(string symbol, int leverage, MarginMode marginMode, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task SetMarginModeAsync(string symbol, MarginMode marginMode, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task AddMarginAsync(string symbol, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task ReduceMarginAsync(string symbol, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<PlaceOrderResult> CloseAsync(string symbol, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
}

internal sealed class Trades : ITrades
{
    public Task<IReadOnlyList<Fill>> GetMyFillsAsync(string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<IReadOnlyList<Fill>> GetMyFillsByOrderAsync(long orderId, CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
}

internal sealed class Account : IAccount
{
    public Task<AccountState> GetStateAsync(CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<decimal> GetBalanceAsync(string token = "USDC", CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<IReadOnlyDictionary<string, decimal>> GetBalancesAsync(CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<FeeSchedule> GetFeesAsync(CancellationToken ct = default) => Task.FromResult(new FeeSchedule(MakerRate: 0m, TakerRate: 0.0005m, VolumeTier: null, VolumeLast30Days: 0m));
    public Task<Portfolio> GetPortfolioAsync(CancellationToken ct = default) => Task.FromResult(new Portfolio(Array.Empty<PortfolioSample>(), Array.Empty<PortfolioSample>()));
    public Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken ct = default) => throw new NotImplementedException(Phase.Read);
    public Task<RateLimitInfo> GetRateLimitAsync(CancellationToken ct = default) => Task.FromResult(new RateLimitInfo(Used: 0, Limit: 175, WindowResetAt: DateTimeOffset.UtcNow.AddSeconds(10)));
    public Task ApproveAgentAsync(string agentAddress, string? name = null, CancellationToken ct = default) => throw new NotSupportedException("dYdX v4 doesn't have an agent-wallet equivalent — trade directly with your subaccount key.");
    public Task<IReadOnlyList<AgentInfo>> GetApprovedAgentsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AgentInfo>>(Array.Empty<AgentInfo>());
}

internal sealed class Transfers : ITransfers
{
    public Task<TransferResult> WithdrawAsync(string destinationAddress, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<TransferResult> TransferUsdAsync(string destinationAddress, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<TransferResult> TransferTokenAsync(string destinationAddress, string token, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<TransferResult> SpotToPerpAsync(decimal amount, CancellationToken ct = default) => throw new NotSupportedException("dYdX v4 has no separate spot account — funds live in the trading subaccount.");
    public Task<TransferResult> PerpToSpotAsync(decimal amount, CancellationToken ct = default) => throw new NotSupportedException("dYdX v4 has no separate spot account — funds live in the trading subaccount.");
    public Task<TransferResult> ToSubAccountAsync(string subAccount, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
}
