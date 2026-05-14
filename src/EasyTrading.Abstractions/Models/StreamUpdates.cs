namespace EasyTrading.Abstractions.Models;

/// <summary>A new public trade pushed by the exchange.</summary>
/// <param name="Trade">The trade payload.</param>
public sealed record TradeUpdate(PublicTrade Trade);

/// <summary>An order-book delta or snapshot pushed by the exchange.</summary>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Timestamp">Server timestamp.</param>
/// <param name="Bids">Bid levels (full snapshot when <paramref name="IsSnapshot"/> is <c>true</c>, otherwise levels that changed).</param>
/// <param name="Asks">Ask levels.</param>
/// <param name="IsSnapshot">Whether this update is a full snapshot or a delta.</param>
public sealed record OrderBookUpdate(
    string Symbol,
    DateTimeOffset Timestamp,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks,
    bool IsSnapshot);

/// <summary>A candle update — a new or updated candle for the subscribed interval.</summary>
/// <param name="Candle">The candle payload.</param>
public sealed record CandleUpdate(Candle Candle);

/// <summary>An updated mid price for one market.</summary>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Mid">New mid price.</param>
/// <param name="Timestamp">Server timestamp.</param>
public sealed record MidUpdate(string Symbol, decimal Mid, DateTimeOffset Timestamp);

/// <summary>Best bid / ask update.</summary>
/// <param name="Symbol">Market symbol.</param>
/// <param name="BidPrice">Best bid price.</param>
/// <param name="BidSize">Size at the best bid.</param>
/// <param name="AskPrice">Best ask price.</param>
/// <param name="AskSize">Size at the best ask.</param>
/// <param name="Timestamp">Server timestamp.</param>
public sealed record BboUpdate(
    string Symbol,
    decimal BidPrice,
    decimal BidSize,
    decimal AskPrice,
    decimal AskSize,
    DateTimeOffset Timestamp);

/// <summary>An order status change pushed to the user.</summary>
/// <param name="Order">Current order state.</param>
public sealed record OrderUpdate(Order Order);

/// <summary>A fill on the user's account pushed by the exchange.</summary>
/// <param name="Fill">The fill payload.</param>
public sealed record FillUpdate(Fill Fill);

/// <summary>A funding payment applied to the user's account.</summary>
/// <param name="Symbol">Market symbol the payment relates to.</param>
/// <param name="Amount">Funding amount in quote asset (positive = received, negative = paid).</param>
/// <param name="Rate">Funding rate that was applied.</param>
/// <param name="Time">Time the payment was applied.</param>
public sealed record FundingUpdate(string Symbol, decimal Amount, decimal Rate, DateTimeOffset Time);

/// <summary>A free-form notification (liquidation warning, system messages, etc.).</summary>
/// <param name="Message">Notification text.</param>
/// <param name="Time">Server timestamp.</param>
public sealed record NotificationUpdate(string Message, DateTimeOffset Time);
