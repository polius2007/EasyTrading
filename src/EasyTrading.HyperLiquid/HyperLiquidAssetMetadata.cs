namespace EasyTrading.HyperLiquid;

/// <summary>Public HyperLiquid asset metadata required to build and validate trading actions.</summary>
/// <param name="Symbol">Exchange symbol, for example <c>BTC</c> or <c>PURR/USDC</c>.</param>
/// <param name="AssetId">Integer asset id used in HyperLiquid action payloads.</param>
/// <param name="SzDecimals">Maximum decimal places allowed for order quantity.</param>
/// <param name="IsSpot">Whether the asset belongs to the spot market.</param>
public sealed record HyperLiquidAssetMetadata(
    string Symbol,
    int AssetId,
    int SzDecimals,
    bool IsSpot);
