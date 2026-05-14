namespace EasyTrading.Abstractions;

/// <summary>
/// Optional features an exchange may support. Probe with <see cref="Enum.HasFlag"/> before calling
/// venue-specific methods, e.g. <c>if (client.Capabilities.HasFlag(ExchangeCapabilities.Twap)) ...</c>
/// </summary>
[Flags]
public enum ExchangeCapabilities
{
    /// <summary>No optional features advertised.</summary>
    None = 0,

    /// <summary>The exchange supports perpetual futures markets.</summary>
    Perpetuals = 1 << 0,

    /// <summary>The exchange supports spot markets.</summary>
    Spot = 1 << 1,

    /// <summary>The exchange supports time-weighted-average-price (TWAP) orders.</summary>
    Twap = 1 << 2,

    /// <summary>The exchange supports scheduled cancel (dead-man switch).</summary>
    ScheduleCancel = 1 << 3,

    /// <summary>The exchange supports sub-accounts.</summary>
    SubAccounts = 1 << 4,

    /// <summary>The exchange supports vaults.</summary>
    Vaults = 1 << 5,

    /// <summary>The exchange supports native staking.</summary>
    Staking = 1 << 6,

    /// <summary>The exchange supports builder fees / rebate routing.</summary>
    BuilderFees = 1 << 7,

    /// <summary>The exchange supports batch order placement / cancellation / modification.</summary>
    BatchOperations = 1 << 8,

    /// <summary>The exchange supports agent / API wallets that trade on behalf of a master account.</summary>
    AgentWallets = 1 << 9,
}
