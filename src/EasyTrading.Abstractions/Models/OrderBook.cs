namespace EasyTrading.Abstractions.Models;

/// <summary>Order book snapshot for a single market.</summary>
/// <param name="Symbol">The market this snapshot describes.</param>
/// <param name="Timestamp">Server timestamp of the snapshot.</param>
/// <param name="Bids">Bid levels in descending price order.</param>
/// <param name="Asks">Ask levels in ascending price order.</param>
public sealed record OrderBook(
    string Symbol,
    DateTimeOffset Timestamp,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks);

/// <summary>A single price level in an order book.</summary>
/// <param name="Price">Price at this level.</param>
/// <param name="Size">Aggregate size resting at this price.</param>
/// <param name="OrderCount">Number of distinct orders at this price, or 0 if the exchange does not provide it.</param>
public readonly record struct OrderBookLevel(decimal Price, decimal Size, int OrderCount);
