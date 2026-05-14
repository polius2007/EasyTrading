using EasyTrading.Abstractions.Models;

namespace EasyTrading.Abstractions;

/// <summary>Trade history — your fills (by symbol, by order, by time).</summary>
public interface ITrades
{
    /// <summary>Get the account's fills, optionally filtered by market and / or time range.</summary>
    /// <param name="symbol">If specified, only fills on this market.</param>
    /// <param name="from">Start of the time range (inclusive).</param>
    /// <param name="to">End of the time range (exclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Fills in descending time order (newest first).</returns>
    Task<IReadOnlyList<Fill>> GetMyFillsAsync(
        string? symbol = null, DateTimeOffset? from = null,
        DateTimeOffset? to = null, CancellationToken ct = default);

    /// <summary>Get all fills produced by one order.</summary>
    /// <param name="orderId">Exchange-assigned order id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Fills for the given order.</returns>
    Task<IReadOnlyList<Fill>> GetMyFillsByOrderAsync(long orderId, CancellationToken ct = default);
}
