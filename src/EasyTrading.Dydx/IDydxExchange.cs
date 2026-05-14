using EasyTrading.Abstractions;

namespace EasyTrading.Dydx;

/// <summary>
/// dYdX v4 exchange surface. Today this is identical to <see cref="IExchangeClient"/> — dYdX's
/// venue-specific features (subaccounts, isolated-margin pools, governance staking) can fit
/// inside the cross-DEX shape (subaccounts via <see cref="IAccount.GetSubAccountsAsync"/>;
/// transfers via <see cref="ITransfers"/>) without needing a separate sub-client. The interface
/// exists so future dYdX-only methods can be added without polluting the cross-DEX contract.
/// </summary>
public interface IDydxExchange : IExchangeClient
{
}
