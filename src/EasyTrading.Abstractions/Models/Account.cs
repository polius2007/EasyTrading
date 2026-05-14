namespace EasyTrading.Abstractions.Models;

/// <summary>Snapshot of the account's overall state.</summary>
/// <param name="AccountValue">Total equity in quote asset (USDC for HyperLiquid).</param>
/// <param name="FreeCollateral">Collateral available to open new positions.</param>
/// <param name="MaintenanceMargin">Currently used maintenance margin.</param>
/// <param name="Positions">Currently open positions.</param>
/// <param name="Balances">Per-token spot balances (token symbol -> total balance).</param>
/// <param name="Timestamp">Server timestamp.</param>
public sealed record AccountState(
    decimal AccountValue,
    decimal FreeCollateral,
    decimal MaintenanceMargin,
    IReadOnlyList<Position> Positions,
    IReadOnlyDictionary<string, decimal> Balances,
    DateTimeOffset Timestamp);

/// <summary>A single spot balance.</summary>
/// <param name="Token">Asset symbol.</param>
/// <param name="Total">Total balance.</param>
/// <param name="Available">Balance available for trading / withdrawal.</param>
/// <param name="Locked">Balance locked in open orders or withdrawals.</param>
public sealed record Balance(
    string Token,
    decimal Total,
    decimal Available,
    decimal Locked);

/// <summary>The account's current fee schedule.</summary>
/// <param name="MakerRate">Maker fee rate as a fraction.</param>
/// <param name="TakerRate">Taker fee rate as a fraction.</param>
/// <param name="VolumeTier">Current volume-tier name, if applicable.</param>
/// <param name="VolumeLast30Days">Trading volume over the last 30 days in quote asset.</param>
public sealed record FeeSchedule(
    decimal MakerRate,
    decimal TakerRate,
    string? VolumeTier,
    decimal VolumeLast30Days);

/// <summary>Portfolio history snapshot (equity / PnL over time).</summary>
/// <param name="AccountValueHistory">Equity samples over time.</param>
/// <param name="PnlHistory">Cumulative PnL samples over time.</param>
public sealed record Portfolio(
    IReadOnlyList<PortfolioSample> AccountValueHistory,
    IReadOnlyList<PortfolioSample> PnlHistory);

/// <summary>A single time-series sample.</summary>
/// <param name="Time">Sample timestamp.</param>
/// <param name="Value">Sample value.</param>
public readonly record struct PortfolioSample(DateTimeOffset Time, decimal Value);

/// <summary>A sub-account.</summary>
/// <param name="Address">Sub-account address.</param>
/// <param name="Name">Sub-account name.</param>
/// <param name="State">Current account state of the sub-account.</param>
public sealed record SubAccount(string Address, string Name, AccountState State);

/// <summary>An approved agent / API wallet.</summary>
/// <param name="Address">Agent wallet address.</param>
/// <param name="Name">Optional name.</param>
/// <param name="ApprovedAt">When the agent was approved.</param>
public sealed record AgentInfo(string Address, string? Name, DateTimeOffset ApprovedAt);

/// <summary>Current rate-limit budget for the account.</summary>
/// <param name="Used">Requests used in the current window.</param>
/// <param name="Limit">Total budget in the current window.</param>
/// <param name="WindowResetAt">When the current window resets.</param>
public sealed record RateLimitInfo(int Used, int Limit, DateTimeOffset WindowResetAt);
