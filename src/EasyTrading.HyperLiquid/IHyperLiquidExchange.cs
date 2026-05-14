using EasyTrading.Abstractions;

namespace EasyTrading.HyperLiquid;

/// <summary>
/// HyperLiquid-specific extension of <see cref="IExchangeClient"/>. Adds the HL-only surface
/// (vaults, staking) on top of the cross-DEX contract.
/// </summary>
/// <remarks>
/// Register a <see cref="HyperLiquidClient"/> as both <see cref="IExchangeClient"/> and
/// <see cref="IHyperLiquidExchange"/> so that cross-DEX code can depend on the former while
/// HL-aware code can request the latter.
/// </remarks>
public interface IHyperLiquidExchange : IExchangeClient
{
    /// <summary>HyperLiquid vaults — deposit / withdraw / read details.</summary>
    IVaults Vaults { get; }

    /// <summary>HyperLiquid native staking — delegate / undelegate / rewards.</summary>
    IStaking Staking { get; }
}
