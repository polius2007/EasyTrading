namespace EasyTrading.HyperLiquid.Models;

/// <summary>Detailed info about a HyperLiquid vault.</summary>
/// <param name="VaultAddress">Vault address.</param>
/// <param name="Name">Display name.</param>
/// <param name="LeaderAddress">Address of the vault leader / operator.</param>
/// <param name="Equity">Current vault equity in USDC.</param>
/// <param name="FollowerCount">Number of followers.</param>
/// <param name="MaxDistributable">Maximum amount currently withdrawable.</param>
/// <param name="ApyPercent">Annualised return as a percentage.</param>
public sealed record VaultDetails(
    string VaultAddress,
    string Name,
    string LeaderAddress,
    decimal Equity,
    int FollowerCount,
    decimal MaxDistributable,
    decimal ApyPercent);

/// <summary>The user's equity in a vault.</summary>
/// <param name="VaultAddress">Vault address.</param>
/// <param name="Equity">User's equity (deposit + share of PnL).</param>
/// <param name="LockedUntil">Time at which the deposit becomes withdrawable; <c>null</c> if no lock.</param>
public sealed record VaultEquity(
    string VaultAddress,
    decimal Equity,
    DateTimeOffset? LockedUntil);

/// <summary>An active stake delegation.</summary>
/// <param name="Validator">Validator address.</param>
/// <param name="Amount">Amount staked, in native token.</param>
/// <param name="LockedUntil">Time at which the delegation may be undelegated.</param>
public sealed record Delegation(
    string Validator,
    decimal Amount,
    DateTimeOffset LockedUntil);

/// <summary>Aggregate staking summary for the account.</summary>
/// <param name="TotalDelegated">Total amount currently delegated.</param>
/// <param name="TotalUndelegating">Amount in the unstaking queue.</param>
/// <param name="TotalUndelegated">Amount fully undelegated and back in spot.</param>
public sealed record DelegatorSummary(
    decimal TotalDelegated,
    decimal TotalUndelegating,
    decimal TotalUndelegated);

/// <summary>A single staking-reward payment.</summary>
/// <param name="Validator">Validator address that produced the reward.</param>
/// <param name="Amount">Reward amount in native token.</param>
/// <param name="Time">When the reward was credited.</param>
public sealed record Reward(string Validator, decimal Amount, DateTimeOffset Time);

/// <summary>An approved builder and its maximum fee rate.</summary>
/// <param name="BuilderAddress">Builder address.</param>
/// <param name="MaxFeeRate">Maximum approved fee rate as a fraction (e.g. <c>0.0005</c> = 0.05%).</param>
public sealed record BuilderApproval(string BuilderAddress, decimal MaxFeeRate);
