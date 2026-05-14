using EasyTrading.Abstractions.Models;

namespace EasyTrading.Abstractions;

/// <summary>Position operations — read positions, set leverage, add / reduce margin, close.</summary>
public interface IPositions
{
    /// <summary>Get all open positions for the account.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Open positions.</returns>
    Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get the open position for one market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The position, or <c>null</c> if the account has no position on this market.</returns>
    Task<Position?> GetAsync(string symbol, CancellationToken ct = default);

    /// <summary>Set leverage for one market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="leverage">New leverage (integer multiple, e.g. 1, 2, 5, 10, 20, 50).</param>
    /// <param name="mode">Cross or isolated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOrderException">Leverage exceeds the market's maximum.</exception>
    Task SetLeverageAsync(string symbol, int leverage, MarginMode mode, CancellationToken ct = default);

    /// <summary>Add isolated margin to a position.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="amount">Margin to add, in quote asset.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddMarginAsync(string symbol, decimal amount, CancellationToken ct = default);

    /// <summary>Reduce isolated margin on a position.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="amount">Margin to remove, in quote asset.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReduceMarginAsync(string symbol, decimal amount, CancellationToken ct = default);

    /// <summary>Close a position with a reduce-only market order.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The placement result of the closing order.</returns>
    Task<PlaceOrderResult> CloseAsync(string symbol, CancellationToken ct = default);
}
