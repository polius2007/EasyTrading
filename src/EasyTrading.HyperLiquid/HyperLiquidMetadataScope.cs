namespace EasyTrading.HyperLiquid;

/// <summary>Identifies an isolated HyperLiquid metadata universe.</summary>
/// <param name="Network">HyperLiquid network.</param>
/// <param name="RestBaseUrl">Optional REST endpoint override.</param>
public sealed record HyperLiquidMetadataScope(
    HyperLiquidNetwork Network,
    Uri? RestBaseUrl = null)
{
    internal static HyperLiquidMetadataScope FromOptions(HyperLiquidClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new HyperLiquidMetadataScope(options.Network, options.RestBaseUrl);
    }

    internal Uri GetEffectiveRestBaseUrl() => RestBaseUrl ?? Network switch
    {
        HyperLiquidNetwork.Mainnet => new Uri("https://api.hyperliquid.xyz"),
        HyperLiquidNetwork.Testnet => new Uri("https://api.hyperliquid-testnet.xyz"),
        _ => throw new ArgumentOutOfRangeException(nameof(Network), Network, "Unknown HyperLiquid network.")
    };
}
