namespace EasyTrading.HyperLiquid;

/// <summary>Selects which HyperLiquid environment a client targets.</summary>
public enum HyperLiquidNetwork
{
    /// <summary>Mainnet — <c>https://api.hyperliquid.xyz</c> / <c>wss://api.hyperliquid.xyz/ws</c>.</summary>
    Mainnet,

    /// <summary>Testnet — <c>https://api.hyperliquid-testnet.xyz</c> / <c>wss://api.hyperliquid-testnet.xyz/ws</c>.</summary>
    Testnet,
}
