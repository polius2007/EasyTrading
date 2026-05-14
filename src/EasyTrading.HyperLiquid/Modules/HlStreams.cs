using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IStreams"/>. WebSocket streaming lands in Phase 4.</summary>
internal sealed class HlStreams : IStreams
{
    public IAsyncEnumerable<TradeUpdate> TradesAsync(string symbol, CancellationToken ct) => Fail<TradeUpdate>();
    public IAsyncEnumerable<OrderBookUpdate> OrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default) => Fail<OrderBookUpdate>();
    public IAsyncEnumerable<CandleUpdate> CandlesAsync(string symbol, Interval interval, CancellationToken ct = default) => Fail<CandleUpdate>();
    public IAsyncEnumerable<MidUpdate> AllMidsAsync(CancellationToken ct) => Fail<MidUpdate>();
    public IAsyncEnumerable<BboUpdate> BestBidOfferAsync(string symbol, CancellationToken ct) => Fail<BboUpdate>();
    public IAsyncEnumerable<OrderUpdate> MyOrdersAsync(CancellationToken ct) => Fail<OrderUpdate>();
    public IAsyncEnumerable<FillUpdate> MyFillsAsync(CancellationToken ct) => Fail<FillUpdate>();
    public IAsyncEnumerable<FundingUpdate> MyFundingsAsync(CancellationToken ct) => Fail<FundingUpdate>();
    public IAsyncEnumerable<NotificationUpdate> MyNotificationsAsync(CancellationToken ct) => Fail<NotificationUpdate>();

    private static IAsyncEnumerable<T> Fail<T>() => throw new NotImplementedException(HyperLiquidClient.StreamPhase4Message);
}
