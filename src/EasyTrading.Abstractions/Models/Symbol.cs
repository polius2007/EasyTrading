namespace EasyTrading.Abstractions.Models;

/// <summary>
/// Metadata describing a single tradeable market (perpetual or spot pair).
/// </summary>
/// <param name="Name">Canonical identifier used in API calls (e.g. <c>"BTC"</c> for a HyperLiquid perp, <c>"PURR/USDC"</c> for a spot pair).</param>
/// <param name="Kind">Whether the instrument is a perpetual or spot market.</param>
/// <param name="BaseAsset">Base asset symbol (e.g. <c>"BTC"</c>).</param>
/// <param name="QuoteAsset">Quote asset symbol (e.g. <c>"USDC"</c>).</param>
/// <param name="PriceTick">Minimum price increment. Order prices must be a multiple of this value.</param>
/// <param name="SizeStep">Minimum size increment. Order sizes must be a multiple of this value.</param>
/// <param name="MinSize">Minimum allowed order size.</param>
/// <param name="MaxLeverage">Maximum allowed leverage for perpetuals; <c>null</c> for spot markets.</param>
public sealed record Symbol(
    string Name,
    MarketKind Kind,
    string BaseAsset,
    string QuoteAsset,
    decimal PriceTick,
    decimal SizeStep,
    decimal MinSize,
    int? MaxLeverage);
