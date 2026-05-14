using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid;

/// <summary>HyperLiquid native staking — delegate / undelegate / rewards.</summary>
public interface IStaking
{
    /// <summary>Get the account's active delegations.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active delegations.</returns>
    Task<IReadOnlyList<Delegation>> GetMyDelegationsAsync(CancellationToken ct = default);

    /// <summary>Get the account's aggregate staking summary.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Staking summary.</returns>
    Task<DelegatorSummary> GetMySummaryAsync(CancellationToken ct = default);

    /// <summary>Get the account's staking-reward history.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rewards in descending time order.</returns>
    Task<IReadOnlyList<Reward>> GetMyRewardsAsync(CancellationToken ct = default);

    /// <summary>Move native tokens from spot into the staking pool.</summary>
    /// <param name="amount">Amount of native token.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DepositAsync(decimal amount, CancellationToken ct = default);

    /// <summary>Move native tokens from the staking pool back to spot (subject to the 7-day unstaking queue).</summary>
    /// <param name="amount">Amount of native token.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WithdrawAsync(decimal amount, CancellationToken ct = default);

    /// <summary>Delegate staked tokens to a validator.</summary>
    /// <param name="validator">Validator address.</param>
    /// <param name="amount">Amount of native token.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DelegateAsync(string validator, decimal amount, CancellationToken ct = default);

    /// <summary>Undelegate staked tokens from a validator.</summary>
    /// <param name="validator">Validator address.</param>
    /// <param name="amount">Amount of native token.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UndelegateAsync(string validator, decimal amount, CancellationToken ct = default);
}
