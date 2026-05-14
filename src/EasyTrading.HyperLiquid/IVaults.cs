using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid;

/// <summary>HyperLiquid vaults — deposit / withdraw and read vault details.</summary>
public interface IVaults
{
    /// <summary>Get a vault's details.</summary>
    /// <param name="vaultAddress">Vault address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Vault details.</returns>
    Task<VaultDetails> GetDetailsAsync(string vaultAddress, CancellationToken ct = default);

    /// <summary>Get the user's equities across all vaults they deposit into.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Vault equities.</returns>
    Task<IReadOnlyList<VaultEquity>> GetMyEquitiesAsync(CancellationToken ct = default);

    /// <summary>Deposit USDC into a vault.</summary>
    /// <param name="vaultAddress">Vault address.</param>
    /// <param name="amount">Amount of USDC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transfer result.</returns>
    Task<TransferResult> DepositAsync(string vaultAddress, decimal amount, CancellationToken ct = default);

    /// <summary>Withdraw USDC from a vault.</summary>
    /// <param name="vaultAddress">Vault address.</param>
    /// <param name="amount">Amount of USDC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transfer result.</returns>
    Task<TransferResult> WithdrawAsync(string vaultAddress, decimal amount, CancellationToken ct = default);
}
