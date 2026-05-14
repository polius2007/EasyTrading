using EasyTrading.Abstractions.Models;

namespace EasyTrading.Abstractions;

/// <summary>Account state — balances, fees, portfolio, sub-accounts, agents, rate limit.</summary>
public interface IAccount
{
    /// <summary>Get the full account state in one call (equity, free collateral, positions, balances).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current account snapshot.</returns>
    Task<AccountState> GetStateAsync(CancellationToken ct = default);

    /// <summary>Get the spot balance of a single token.</summary>
    /// <param name="token">Asset symbol. Defaults to <c>"USDC"</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Total balance.</returns>
    Task<decimal> GetBalanceAsync(string token = "USDC", CancellationToken ct = default);

    /// <summary>Get all spot balances.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Token to total-balance map.</returns>
    Task<IReadOnlyDictionary<string, decimal>> GetBalancesAsync(CancellationToken ct = default);

    /// <summary>Get the account's current fee schedule.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fee schedule.</returns>
    Task<FeeSchedule> GetFeesAsync(CancellationToken ct = default);

    /// <summary>Get the portfolio history (equity / PnL over time).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The portfolio snapshot.</returns>
    Task<Portfolio> GetPortfolioAsync(CancellationToken ct = default);

    /// <summary>Get the account's sub-accounts.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Sub-accounts.</returns>
    Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken ct = default);

    /// <summary>Get the current rate-limit budget.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rate-limit status.</returns>
    Task<RateLimitInfo> GetRateLimitAsync(CancellationToken ct = default);

    /// <summary>
    /// Approve an agent / API wallet to trade on behalf of the master account. Only meaningful on
    /// venues advertising <see cref="ExchangeCapabilities.AgentWallets"/>.
    /// </summary>
    /// <param name="agentAddress">Agent wallet address.</param>
    /// <param name="name">Optional name to attach to the agent.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ApproveAgentAsync(string agentAddress, string? name = null, CancellationToken ct = default);

    /// <summary>List approved agents.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Approved agents.</returns>
    Task<IReadOnlyList<AgentInfo>> GetApprovedAgentsAsync(CancellationToken ct = default);
}
