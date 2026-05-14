using EasyTrading.Abstractions.Models;

namespace EasyTrading.Abstractions;

/// <summary>Transfers — withdraw, internal transfers, spot ↔ perp, sub-account moves.</summary>
public interface ITransfers
{
    /// <summary>Withdraw USDC from the exchange to an external L1 address.</summary>
    /// <param name="destination">Destination L1 address.</param>
    /// <param name="amountUsdc">Amount of USDC to withdraw.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transfer result.</returns>
    Task<TransferResult> WithdrawAsync(string destination, decimal amountUsdc, CancellationToken ct = default);

    /// <summary>Send USDC to another address on the exchange's core ledger (no L1 bridge).</summary>
    /// <param name="toAddress">Destination address.</param>
    /// <param name="amount">Amount of USDC to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transfer result.</returns>
    Task<TransferResult> TransferUsdAsync(string toAddress, decimal amount, CancellationToken ct = default);

    /// <summary>Send a spot token to another address.</summary>
    /// <param name="toAddress">Destination address.</param>
    /// <param name="token">Token symbol.</param>
    /// <param name="amount">Amount to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transfer result.</returns>
    Task<TransferResult> TransferTokenAsync(string toAddress, string token, decimal amount, CancellationToken ct = default);

    /// <summary>Move USDC from the spot wallet to the perpetual margin account.</summary>
    /// <param name="amount">Amount of USDC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transfer result.</returns>
    Task<TransferResult> SpotToPerpAsync(decimal amount, CancellationToken ct = default);

    /// <summary>Move USDC from the perpetual margin account to the spot wallet.</summary>
    /// <param name="amount">Amount of USDC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transfer result.</returns>
    Task<TransferResult> PerpToSpotAsync(decimal amount, CancellationToken ct = default);

    /// <summary>Transfer USDC to a sub-account owned by the master account.</summary>
    /// <param name="subAccount">Sub-account address.</param>
    /// <param name="amount">Amount of USDC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transfer result.</returns>
    Task<TransferResult> ToSubAccountAsync(string subAccount, decimal amount, CancellationToken ct = default);
}
