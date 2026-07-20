namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>Resolves the official HyperLiquid endpoints for a selected network.</summary>
internal static class HyperLiquidEndpoints
{
    private const string MainnetRestBaseUrl = "https://api.hyperliquid.xyz";
    private const string TestnetRestBaseUrl = "https://api.hyperliquid-testnet.xyz";

    public static Uri GetRestBaseUrl(HyperLiquidNetwork network) => network switch
    {
        HyperLiquidNetwork.Mainnet => new Uri(MainnetRestBaseUrl),
        HyperLiquidNetwork.Testnet => new Uri(TestnetRestBaseUrl),
        _ => throw new ArgumentOutOfRangeException(
            nameof(network),
            network,
            "Unknown HyperLiquid network."),
    };
}
