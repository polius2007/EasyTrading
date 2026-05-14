namespace EasyTrading.Aster;

/// <summary>
/// Network selector for the Aster client. Determines the REST and WebSocket base URLs.
/// </summary>
public enum AsterNetwork
{
    /// <summary>Aster Finance production network (<c>fapi.asterdex.com</c>).</summary>
    Mainnet = 0,

    /// <summary>Aster Finance testnet — separate sandbox with its own balances and order book.</summary>
    Testnet = 1,
}
