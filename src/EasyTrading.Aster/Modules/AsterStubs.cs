using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;

namespace EasyTrading.Aster.Modules;

// ─── Aster module stubs ─────────────────────────────────────────────────────
//
// Every method throws NotImplementedException with a Phase-6.x pointer. They
// exist so DI resolves and IExchangeClient is satisfied; the venue-specific
// implementations land in subsequent phases:
//
//   Phase 6.1 → Aster read endpoints beyond Markets (Account / Positions / Trades)
//   Phase 6.2 → Aster Exchange (EIP-712 signing + writes)
//   Phase 6.3 → Aster WebSocket streams

internal static class AsterPhase
{
    public const string Write  = "Pending Phase 6.2 — Aster Exchange + EIP-712 signing.";
    public const string Read   = "Pending Phase 6.1 — Aster signed-read endpoints.";
    public const string Stream = "Pending Phase 6.3 — Aster WebSocket streaming.";
}

internal sealed class AsterOrders : IOrders
{
    public Task<IReadOnlyList<Order>> GetOpenAsync(string? symbol = null, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<Order?> GetAsync(long orderId, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<Order?> GetByClientIdAsync(string clientOrderId, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<IReadOnlyList<Order>> GetHistoryAsync(string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<IReadOnlyList<TwapSliceFill>> GetTwapFillsAsync(CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);

    public Task<PlaceOrderResult> PlaceAsync(OrderRequest request, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<PlaceOrderResult> PlaceLimitAsync(string symbol, OrderSide side, decimal price, decimal size, TimeInForce tif = TimeInForce.Gtc, bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<PlaceOrderResult> PlaceMarketAsync(string symbol, OrderSide side, decimal size, bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<PlaceOrderResult> PlaceStopAsync(string symbol, OrderSide side, decimal triggerPrice, decimal size, bool isMarket = true, bool reduceOnly = true, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<BatchOrderResult> PlaceBatchAsync(IReadOnlyList<OrderRequest> requests, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);

    public Task<ModifyResult> ModifyAsync(ModifyRequest request, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<BatchModifyResult> ModifyBatchAsync(IReadOnlyList<ModifyRequest> requests, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);

    public Task<CancelResult> CancelAsync(string symbol, long orderId, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<CancelResult> CancelByClientIdAsync(string symbol, string clientOrderId, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<BatchCancelResult> CancelBatchAsync(IReadOnlyList<CancelRequest> requests, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<int> CancelAllAsync(string? symbol = null, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task ScheduleCancelAsync(DateTimeOffset? at, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);

    public Task<TwapResult> PlaceTwapAsync(TwapRequest request, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<CancelResult> CancelTwapAsync(string symbol, long twapId, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
}

internal sealed class AsterPositions : IPositions
{
    public Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<Position?> GetAsync(string symbol, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task SetLeverageAsync(string symbol, int leverage, MarginMode marginMode, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task SetMarginModeAsync(string symbol, MarginMode marginMode, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task AddMarginAsync(string symbol, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task ReduceMarginAsync(string symbol, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<PlaceOrderResult> CloseAsync(string symbol, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
}

internal sealed class AsterTrades : ITrades
{
    public Task<IReadOnlyList<Fill>> GetMyFillsAsync(string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<IReadOnlyList<Fill>> GetMyFillsByOrderAsync(long orderId, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
}

internal sealed class AsterAccount : IAccount
{
    public Task<AccountState> GetStateAsync(CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<decimal> GetBalanceAsync(string token = "USDC", CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<IReadOnlyDictionary<string, decimal>> GetBalancesAsync(CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<FeeSchedule> GetFeesAsync(CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<Portfolio> GetPortfolioAsync(CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task<RateLimitInfo> GetRateLimitAsync(CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
    public Task ApproveAgentAsync(string agentAddress, string? name = null, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<IReadOnlyList<AgentInfo>> GetApprovedAgentsAsync(CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Read);
}

internal sealed class AsterTransfers : ITransfers
{
    public Task<TransferResult> WithdrawAsync(string destinationAddress, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<TransferResult> TransferUsdAsync(string destinationAddress, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<TransferResult> TransferTokenAsync(string destinationAddress, string token, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<TransferResult> SpotToPerpAsync(decimal amount, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<TransferResult> PerpToSpotAsync(decimal amount, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
    public Task<TransferResult> ToSubAccountAsync(string subAccount, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Write);
}

internal sealed class AsterStreams : IStreams
{
    public IAsyncEnumerable<TradeUpdate> TradesAsync(string symbol, CancellationToken ct) => throw new NotImplementedException(AsterPhase.Stream);
    public IAsyncEnumerable<OrderBookUpdate> OrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Stream);
    public IAsyncEnumerable<CandleUpdate> CandlesAsync(string symbol, Interval interval, CancellationToken ct = default) => throw new NotImplementedException(AsterPhase.Stream);
    public IAsyncEnumerable<MidUpdate> AllMidsAsync(CancellationToken ct) => throw new NotImplementedException(AsterPhase.Stream);
    public IAsyncEnumerable<BboUpdate> BestBidOfferAsync(string symbol, CancellationToken ct) => throw new NotImplementedException(AsterPhase.Stream);
    public IAsyncEnumerable<OrderUpdate> MyOrdersAsync(CancellationToken ct) => throw new NotImplementedException(AsterPhase.Stream);
    public IAsyncEnumerable<FillUpdate> MyFillsAsync(CancellationToken ct) => throw new NotImplementedException(AsterPhase.Stream);
    public IAsyncEnumerable<FundingUpdate> MyFundingsAsync(CancellationToken ct) => throw new NotImplementedException(AsterPhase.Stream);
    public IAsyncEnumerable<NotificationUpdate> MyNotificationsAsync(CancellationToken ct) => throw new NotImplementedException(AsterPhase.Stream);
}
