using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid.Modules;

// ─── Phase-1 stubs ───────────────────────────────────────────────────────────
// Every method throws NotImplementedException. Real implementations land in
// Phase 2 (Info), Phase 3 (Exchange + EIP-712), Phase 4 (WebSocket), and Phase 5
// (Builder). When a phase is implemented, split each module into its own file.

internal static class HlStub
{
    public static Task<T> NotImpl<T>() =>
        Task.FromException<T>(new NotImplementedException(HyperLiquidClient.Phase1NotImplemented));

    public static Task NotImpl() =>
        Task.FromException(new NotImplementedException(HyperLiquidClient.Phase1NotImplemented));

    public static IAsyncEnumerable<T> NotImplStream<T>() =>
        throw new NotImplementedException(HyperLiquidClient.Phase1NotImplemented);
}

internal sealed class HlMarkets : IMarkets
{
    public Task<IReadOnlyList<Symbol>> GetSymbolsAsync(MarketKind kind = MarketKind.All, CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<Symbol>>();
    public Task<Symbol> GetSymbolAsync(string symbol, CancellationToken ct = default) => HlStub.NotImpl<Symbol>();
    public Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default) => HlStub.NotImpl<OrderBook>();
    public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<Candle>>();
    public Task<IReadOnlyDictionary<string, decimal>> GetAllMidsAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyDictionary<string, decimal>>();
    public Task<decimal> GetMidAsync(string symbol, CancellationToken ct = default) => HlStub.NotImpl<decimal>();
    public Task<FundingInfo> GetFundingAsync(string symbol, CancellationToken ct = default) => HlStub.NotImpl<FundingInfo>();
    public Task<IReadOnlyList<FundingRecord>> GetFundingHistoryAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<FundingRecord>>();
    public Task<IReadOnlyList<PublicTrade>> GetRecentTradesAsync(string symbol, int limit = 100, CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<PublicTrade>>();
    public Task<decimal> GetOpenInterestAsync(string symbol, CancellationToken ct = default) => HlStub.NotImpl<decimal>();
}

internal sealed class HlOrders : IOrders
{
    public Task<PlaceOrderResult> PlaceAsync(OrderRequest request, CancellationToken ct = default) => HlStub.NotImpl<PlaceOrderResult>();
    public Task<BatchOrderResult> PlaceBatchAsync(IReadOnlyList<OrderRequest> requests, CancellationToken ct = default) => HlStub.NotImpl<BatchOrderResult>();
    public Task<PlaceOrderResult> PlaceLimitAsync(string symbol, OrderSide side, decimal price, decimal size, TimeInForce tif = TimeInForce.Gtc, bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default) => HlStub.NotImpl<PlaceOrderResult>();
    public Task<PlaceOrderResult> PlaceMarketAsync(string symbol, OrderSide side, decimal size, bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default) => HlStub.NotImpl<PlaceOrderResult>();
    public Task<PlaceOrderResult> PlaceStopAsync(string symbol, OrderSide side, decimal triggerPrice, decimal size, bool isMarket = true, bool reduceOnly = true, CancellationToken ct = default) => HlStub.NotImpl<PlaceOrderResult>();
    public Task<ModifyResult> ModifyAsync(ModifyRequest request, CancellationToken ct = default) => HlStub.NotImpl<ModifyResult>();
    public Task<BatchModifyResult> ModifyBatchAsync(IReadOnlyList<ModifyRequest> requests, CancellationToken ct = default) => HlStub.NotImpl<BatchModifyResult>();
    public Task<CancelResult> CancelAsync(string symbol, long orderId, CancellationToken ct = default) => HlStub.NotImpl<CancelResult>();
    public Task<CancelResult> CancelByClientIdAsync(string symbol, string clientOrderId, CancellationToken ct = default) => HlStub.NotImpl<CancelResult>();
    public Task<BatchCancelResult> CancelBatchAsync(IReadOnlyList<CancelRequest> requests, CancellationToken ct = default) => HlStub.NotImpl<BatchCancelResult>();
    public Task<int> CancelAllAsync(string? symbol = null, CancellationToken ct = default) => HlStub.NotImpl<int>();
    public Task ScheduleCancelAsync(DateTimeOffset? at, CancellationToken ct = default) => HlStub.NotImpl();
    public Task<IReadOnlyList<Order>> GetOpenAsync(string? symbol = null, CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<Order>>();
    public Task<Order?> GetAsync(long orderId, CancellationToken ct = default) => HlStub.NotImpl<Order?>();
    public Task<Order?> GetByClientIdAsync(string clientOrderId, CancellationToken ct = default) => HlStub.NotImpl<Order?>();
    public Task<IReadOnlyList<Order>> GetHistoryAsync(string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<Order>>();
    public Task<TwapResult> PlaceTwapAsync(TwapRequest request, CancellationToken ct = default) => HlStub.NotImpl<TwapResult>();
    public Task<CancelResult> CancelTwapAsync(string symbol, long twapId, CancellationToken ct = default) => HlStub.NotImpl<CancelResult>();
    public Task<IReadOnlyList<TwapSliceFill>> GetTwapFillsAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<TwapSliceFill>>();
}

internal sealed class HlPositions : IPositions
{
    public Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<Position>>();
    public Task<Position?> GetAsync(string symbol, CancellationToken ct = default) => HlStub.NotImpl<Position?>();
    public Task SetLeverageAsync(string symbol, int leverage, MarginMode mode, CancellationToken ct = default) => HlStub.NotImpl();
    public Task AddMarginAsync(string symbol, decimal amount, CancellationToken ct = default) => HlStub.NotImpl();
    public Task ReduceMarginAsync(string symbol, decimal amount, CancellationToken ct = default) => HlStub.NotImpl();
    public Task<PlaceOrderResult> CloseAsync(string symbol, CancellationToken ct = default) => HlStub.NotImpl<PlaceOrderResult>();
}

internal sealed class HlTrades : ITrades
{
    public Task<IReadOnlyList<Fill>> GetMyFillsAsync(string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<Fill>>();
    public Task<IReadOnlyList<Fill>> GetMyFillsByOrderAsync(long orderId, CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<Fill>>();
}

internal sealed class HlAccount : IAccount
{
    public Task<AccountState> GetStateAsync(CancellationToken ct = default) => HlStub.NotImpl<AccountState>();
    public Task<decimal> GetBalanceAsync(string token = "USDC", CancellationToken ct = default) => HlStub.NotImpl<decimal>();
    public Task<IReadOnlyDictionary<string, decimal>> GetBalancesAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyDictionary<string, decimal>>();
    public Task<FeeSchedule> GetFeesAsync(CancellationToken ct = default) => HlStub.NotImpl<FeeSchedule>();
    public Task<Portfolio> GetPortfolioAsync(CancellationToken ct = default) => HlStub.NotImpl<Portfolio>();
    public Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<SubAccount>>();
    public Task<RateLimitInfo> GetRateLimitAsync(CancellationToken ct = default) => HlStub.NotImpl<RateLimitInfo>();
    public Task ApproveAgentAsync(string agentAddress, string? name = null, CancellationToken ct = default) => HlStub.NotImpl();
    public Task<IReadOnlyList<AgentInfo>> GetApprovedAgentsAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<AgentInfo>>();
}

internal sealed class HlTransfers : ITransfers
{
    public Task<TransferResult> WithdrawAsync(string destination, decimal amountUsdc, CancellationToken ct = default) => HlStub.NotImpl<TransferResult>();
    public Task<TransferResult> TransferUsdAsync(string toAddress, decimal amount, CancellationToken ct = default) => HlStub.NotImpl<TransferResult>();
    public Task<TransferResult> TransferTokenAsync(string toAddress, string token, decimal amount, CancellationToken ct = default) => HlStub.NotImpl<TransferResult>();
    public Task<TransferResult> SpotToPerpAsync(decimal amount, CancellationToken ct = default) => HlStub.NotImpl<TransferResult>();
    public Task<TransferResult> PerpToSpotAsync(decimal amount, CancellationToken ct = default) => HlStub.NotImpl<TransferResult>();
    public Task<TransferResult> ToSubAccountAsync(string subAccount, decimal amount, CancellationToken ct = default) => HlStub.NotImpl<TransferResult>();
}

internal sealed class HlStreams : IStreams
{
    public IAsyncEnumerable<TradeUpdate> TradesAsync(string symbol, CancellationToken ct) => HlStub.NotImplStream<TradeUpdate>();
    public IAsyncEnumerable<OrderBookUpdate> OrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default) => HlStub.NotImplStream<OrderBookUpdate>();
    public IAsyncEnumerable<CandleUpdate> CandlesAsync(string symbol, Interval interval, CancellationToken ct = default) => HlStub.NotImplStream<CandleUpdate>();
    public IAsyncEnumerable<MidUpdate> AllMidsAsync(CancellationToken ct) => HlStub.NotImplStream<MidUpdate>();
    public IAsyncEnumerable<BboUpdate> BestBidOfferAsync(string symbol, CancellationToken ct) => HlStub.NotImplStream<BboUpdate>();
    public IAsyncEnumerable<OrderUpdate> MyOrdersAsync(CancellationToken ct) => HlStub.NotImplStream<OrderUpdate>();
    public IAsyncEnumerable<FillUpdate> MyFillsAsync(CancellationToken ct) => HlStub.NotImplStream<FillUpdate>();
    public IAsyncEnumerable<FundingUpdate> MyFundingsAsync(CancellationToken ct) => HlStub.NotImplStream<FundingUpdate>();
    public IAsyncEnumerable<NotificationUpdate> MyNotificationsAsync(CancellationToken ct) => HlStub.NotImplStream<NotificationUpdate>();
}

internal sealed class HlVaults : IVaults
{
    public Task<VaultDetails> GetDetailsAsync(string vaultAddress, CancellationToken ct = default) => HlStub.NotImpl<VaultDetails>();
    public Task<IReadOnlyList<VaultEquity>> GetMyEquitiesAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<VaultEquity>>();
    public Task<TransferResult> DepositAsync(string vaultAddress, decimal amount, CancellationToken ct = default) => HlStub.NotImpl<TransferResult>();
    public Task<TransferResult> WithdrawAsync(string vaultAddress, decimal amount, CancellationToken ct = default) => HlStub.NotImpl<TransferResult>();
}

internal sealed class HlStaking : IStaking
{
    public Task<IReadOnlyList<Delegation>> GetMyDelegationsAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<Delegation>>();
    public Task<DelegatorSummary> GetMySummaryAsync(CancellationToken ct = default) => HlStub.NotImpl<DelegatorSummary>();
    public Task<IReadOnlyList<Reward>> GetMyRewardsAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<Reward>>();
    public Task DepositAsync(decimal amount, CancellationToken ct = default) => HlStub.NotImpl();
    public Task WithdrawAsync(decimal amount, CancellationToken ct = default) => HlStub.NotImpl();
    public Task DelegateAsync(string validator, decimal amount, CancellationToken ct = default) => HlStub.NotImpl();
    public Task UndelegateAsync(string validator, decimal amount, CancellationToken ct = default) => HlStub.NotImpl();
}

internal sealed class HlBuilder : IBuilder
{
    public Task<decimal> GetMaxFeeAsync(string builderAddress, CancellationToken ct = default) => HlStub.NotImpl<decimal>();
    public Task<IReadOnlyList<BuilderApproval>> GetApprovedAsync(CancellationToken ct = default) => HlStub.NotImpl<IReadOnlyList<BuilderApproval>>();
    public Task ApproveAsync(string builderAddress, decimal maxFeeRate, CancellationToken ct = default) => HlStub.NotImpl();
}
