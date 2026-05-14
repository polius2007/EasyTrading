using EasyTrading.Abstractions;

namespace EasyTrading.Aster;

/// <summary>
/// Aster-specific exchange surface. Today this is identical to <see cref="IExchangeClient"/>
/// — Aster doesn't expose features (like HyperLiquid vaults or staking) that need a separate
/// sub-client. The interface exists so future Aster-only methods (e.g. sub-accounts, MMP)
/// can be added without polluting the cross-DEX contract.
/// </summary>
public interface IAsterExchange : IExchangeClient
{
}
