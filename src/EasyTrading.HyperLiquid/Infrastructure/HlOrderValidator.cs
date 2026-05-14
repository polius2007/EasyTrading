using System.Globalization;
using EasyTrading.Abstractions;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Client-side pre-flight checks that reject obviously-invalid orders before they go on
/// the wire. The rules mirror HyperLiquid's published validation so a rejected order
/// surfaces as a typed <see cref="InvalidOrderException"/> with a clear message instead
/// of a generic "invalid order" string from the exchange.
/// </summary>
/// <remarks>
/// <para>HyperLiquid's tick / lot rules:</para>
/// <list type="bullet">
///   <item><description>Size must have at most <c>szDecimals</c> decimal places.</description></item>
///   <item><description>Price must have at most 5 significant figures <b>and</b> at most
///       <c>MAX_DECIMALS - szDecimals</c> decimal places, where MAX_DECIMALS is 6 for perps
///       and 8 for spot. Integer prices are always allowed regardless of significant figures.</description></item>
///   <item><description>Order notional (price × size) must be ≥ $10 USDC, with the exception of
///       reduce-only orders, which the exchange lets through at any size.</description></item>
/// </list>
/// </remarks>
internal static class HlOrderValidator
{
    /// <summary>HyperLiquid's published minimum order value in USDC.</summary>
    public const decimal MinOrderValueUsd = 10m;

    /// <summary>Max significant figures allowed in a non-integer price.</summary>
    public const int MaxPriceSignificantFigures = 5;

    /// <summary>
    /// Validate price + size against HL tick / lot / min-notional rules.
    /// Throws <see cref="InvalidOrderException"/> with a precise message on the first violation.
    /// </summary>
    /// <param name="symbol">Market symbol — included in error messages for debuggability.</param>
    /// <param name="price">Limit / trigger price. Pass <c>0</c> to skip price validation
    /// (e.g. for size-only TWAP orders where the exchange computes the slice price).</param>
    /// <param name="size">Order size in base units.</param>
    /// <param name="info">Asset metadata from <see cref="HlMetaCache.GetAssetInfoAsync"/>.</param>
    /// <param name="reduceOnly">Whether this order is reduce-only — the min-notional check is skipped if so.</param>
    public static void Validate(string symbol, decimal price, decimal size, HlAssetInfo info, bool reduceOnly)
    {
        if (size <= 0m)
            throw new InvalidOrderException($"Order size must be > 0 (got {Format(size)} for '{symbol}').");

        var sizeDecimals = CountFractionalDigits(size);
        if (sizeDecimals > info.SzDecimals)
            throw new InvalidOrderException(
                $"Order size {Format(size)} on '{symbol}' has {sizeDecimals} decimal places; the market allows at most {info.SzDecimals}.");

        // price == 0 is a sentinel for "skip price validation" — used for actions
        // that omit a price field (size-only TWAPs).
        if (price > 0m)
            ValidatePrice(symbol, price, info);

        if (!reduceOnly && price > 0m)
        {
            var notional = price * size;
            if (notional < MinOrderValueUsd)
                throw new InvalidOrderException(
                    $"Order notional {Format(notional)} USDC on '{symbol}' is below HyperLiquid's $10 minimum (price {Format(price)} × size {Format(size)}).");
        }
    }

    private static void ValidatePrice(string symbol, decimal price, HlAssetInfo info)
    {
        if (price <= 0m)
            throw new InvalidOrderException($"Order price must be > 0 (got {Format(price)} for '{symbol}').");

        // Integer prices are always allowed, regardless of significant-figure count.
        if (decimal.Truncate(price) == price)
            return;

        var maxDecimals = (info.IsSpot ? 8 : 6) - info.SzDecimals;
        if (maxDecimals < 0) maxDecimals = 0;

        var priceDecimals = CountFractionalDigits(price);
        if (priceDecimals > maxDecimals)
            throw new InvalidOrderException(
                $"Order price {Format(price)} on '{symbol}' has {priceDecimals} decimal places; this market allows at most {maxDecimals} ({(info.IsSpot ? "spot" : "perp")}, szDecimals={info.SzDecimals}).");

        var sigFigs = CountSignificantFigures(price);
        if (sigFigs > MaxPriceSignificantFigures)
            throw new InvalidOrderException(
                $"Order price {Format(price)} on '{symbol}' has {sigFigs} significant figures; HyperLiquid allows at most {MaxPriceSignificantFigures} for non-integer prices.");
    }

    /// <summary>
    /// Count the number of decimal places after the decimal point, treating trailing zeros
    /// as significant (so <c>1.50</c> has 2 fractional digits, not 1). This matches how
    /// HL's wire format ("0.########") truncates trailing zeros — see <c>FloatToWire</c>.
    /// </summary>
    /// <remarks>
    /// We normalise the decimal via <c>decimal/1m</c> so trailing zeros are dropped before
    /// inspecting the scale, otherwise <c>1.5m</c> and <c>1.50m</c> would report different
    /// fractional-digit counts even though they represent the same value on the wire.
    /// </remarks>
    private static int CountFractionalDigits(decimal value)
    {
        var normalised = value / 1.000000000000000000000000000000m;
        var bits = decimal.GetBits(normalised);
        return (bits[3] >> 16) & 0x7F;
    }

    /// <summary>
    /// Count significant figures in a non-integer decimal. Leading zeros (e.g. in
    /// <c>0.001234</c>) don't count; trailing zeros that survive normalisation do.
    /// </summary>
    private static int CountSignificantFigures(decimal value)
    {
        if (value == 0m) return 0;
        var absValue = Math.Abs(value);

        // Normalise to drop trailing fractional zeros — same reasoning as in CountFractionalDigits.
        var normalised = absValue / 1.000000000000000000000000000000m;

        // Render via "G29" to get a canonical decimal string with no leading zeros after
        // the leading non-zero digit, then count digits ignoring '.', '-', and leading zeros.
        var s = normalised.ToString("G29", CultureInfo.InvariantCulture);

        var count = 0;
        var seenNonZero = false;
        foreach (var ch in s)
        {
            if (ch == '.' || ch == '-') continue;
            if (ch == '0' && !seenNonZero) continue;
            seenNonZero = true;
            count++;
        }
        return count;
    }

    private static string Format(decimal value) =>
        value.ToString("0.##########", CultureInfo.InvariantCulture);
}
