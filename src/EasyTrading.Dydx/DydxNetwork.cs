namespace EasyTrading.Dydx;

/// <summary>
/// Network selector for the dYdX v4 client. Determines the Indexer REST + WebSocket and validator
/// gRPC base URLs.
/// </summary>
public enum DydxNetwork
{
    /// <summary>dYdX v4 production network (<c>indexer.dydx.trade</c>).</summary>
    Mainnet = 0,

    /// <summary>dYdX v4 public testnet — separate sandbox with its own balances and order book.</summary>
    Testnet = 1,
}
