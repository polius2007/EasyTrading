using EasyTrading.Abstractions.Models;

namespace EasyTrading.Abstractions;

/// <summary>
/// WebSocket subscriptions — public (per-market) and user-scoped (the account is taken from the
/// client's credentials). Each method returns an <see cref="IAsyncEnumerable{T}"/> that yields
/// updates as the server pushes them. Iteration stops when the supplied <see cref="CancellationToken"/>
/// is cancelled or the underlying socket is disposed.
/// </summary>
public interface IStreams
{
    // ─── Public ───────────────────────────────────────────────────────────────

    /// <summary>Subscribe to public trade ticks for one market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="ct">Cancellation token. Cancel to stop the subscription.</param>
    /// <returns>An async stream of trade updates.</returns>
    IAsyncEnumerable<TradeUpdate> TradesAsync(string symbol, CancellationToken ct);

    /// <summary>Subscribe to order-book updates for one market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="depth">Maximum number of levels per side to include in each update.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of order-book updates (snapshot first, then deltas).</returns>
    IAsyncEnumerable<OrderBookUpdate> OrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default);

    /// <summary>Subscribe to candle updates for one market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="interval">Candle interval.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of candle updates.</returns>
    IAsyncEnumerable<CandleUpdate> CandlesAsync(string symbol, Interval interval, CancellationToken ct = default);

    /// <summary>Subscribe to mid-price updates for every market in one stream.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of mid updates.</returns>
    IAsyncEnumerable<MidUpdate> AllMidsAsync(CancellationToken ct);

    /// <summary>Subscribe to best-bid / best-ask updates for one market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of BBO updates.</returns>
    IAsyncEnumerable<BboUpdate> BestBidOfferAsync(string symbol, CancellationToken ct);

    // ─── User-scoped (credentials taken from the client) ──────────────────────

    /// <summary>Subscribe to order status changes on the user's account.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of order updates.</returns>
    IAsyncEnumerable<OrderUpdate> MyOrdersAsync(CancellationToken ct);

    /// <summary>Subscribe to fills on the user's account.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of fill updates.</returns>
    IAsyncEnumerable<FillUpdate> MyFillsAsync(CancellationToken ct);

    /// <summary>Subscribe to funding payments applied to the user's account.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of funding updates.</returns>
    IAsyncEnumerable<FundingUpdate> MyFundingsAsync(CancellationToken ct);

    /// <summary>Subscribe to system notifications (liquidation warnings, etc.).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of notification updates.</returns>
    IAsyncEnumerable<NotificationUpdate> MyNotificationsAsync(CancellationToken ct);
}
