namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Metadata about a HyperLiquid market needed both for action payloads (the integer
/// <see cref="AssetId"/>) and for pre-flight order validation (<see cref="SzDecimals"/>
/// + <see cref="IsSpot"/>, which together determine the legal tick / lot grid).
/// </summary>
/// <param name="AssetId">The integer asset id HyperLiquid expects in action payloads
/// (perp = zero-based index in <c>meta.universe</c>; spot = <c>10000 + spot.universe[i].index</c>).</param>
/// <param name="SzDecimals">Max decimal places allowed for the order size on this market.
/// For perps this is read directly from <c>meta.universe[i].szDecimals</c>; for spot pairs
/// it comes from the base token in <c>spotMeta.tokens</c>.</param>
/// <param name="IsSpot">True for spot pairs; false for perpetuals. Determines the price
/// decimal ceiling: <c>(IsSpot ? 8 : 6) - SzDecimals</c>.</param>
internal readonly record struct HlAssetInfo(int AssetId, int SzDecimals, bool IsSpot);
