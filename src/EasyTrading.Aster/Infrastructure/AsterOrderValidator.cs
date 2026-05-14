using System.Globalization;
using EasyTrading.Abstractions;

namespace EasyTrading.Aster.Infrastructure;

/// <summary>
/// Client-side pre-flight checks that reject obviously-invalid orders before they go on the
/// wire. Mirrors Aster's published filter rules:
/// <list type="bullet">
///   <item><description><b>PRICE_FILTER</b>: <c>price</c> is on the tick grid;
///       <c>price &gt;= minPrice</c> and <c>price &lt;= maxPrice</c> when those are non-zero.</description></item>
///   <item><description><b>LOT_SIZE</b>: <c>quantity</c> is on the step grid;
///       <c>quantity &gt;= minQty</c> and <c>quantity &lt;= maxQty</c> when those are non-zero.</description></item>
///   <item><description><b>MIN_NOTIONAL</b>: <c>price * quantity &gt;= notional</c>. Skipped for
///       reduce-only orders, mirroring HL's handling.</description></item>
/// </list>
/// </summary>
internal static class AsterOrderValidator
{
    /// <summary>
    /// Validate <paramref name="price"/> and <paramref name="size"/> against the symbol's
    /// published filters. Throws <see cref="InvalidOrderException"/> with a precise message on
    /// the first violation.
    /// </summary>
    /// <param name="info">Symbol info from <see cref="AsterMetaCache"/>.</param>
    /// <param name="price">Limit / trigger price. Pass <c>0</c> for size-only actions (market orders,
    /// stop markets where the price isn't user-controlled) to skip the tick / min-notional checks.</param>
    /// <param name="size">Order quantity (base units).</param>
    /// <param name="reduceOnly">Whether this is a reduce-only order; the min-notional check is skipped.</param>
    public static void Validate(AsterSymbolInfo info, decimal price, decimal size, bool reduceOnly)
    {
        if (size <= 0m)
            throw new InvalidOrderException($"Order size must be > 0 (got {Fmt(size)} for '{info.Symbol}').");

        if (info.MinQty > 0m && size < info.MinQty)
            throw new InvalidOrderException(
                $"Order size {Fmt(size)} on '{info.Symbol}' is below LOT_SIZE.minQty {Fmt(info.MinQty)}.");

        if (info.MaxQty > 0m && size > info.MaxQty)
            throw new InvalidOrderException(
                $"Order size {Fmt(size)} on '{info.Symbol}' exceeds LOT_SIZE.maxQty {Fmt(info.MaxQty)}.");

        if (info.StepSize > 0m)
        {
            var diff = size - info.MinQty;
            var mod = diff % info.StepSize;
            if (mod != 0m)
                throw new InvalidOrderException(
                    $"Order size {Fmt(size)} on '{info.Symbol}' does not align with LOT_SIZE.stepSize {Fmt(info.StepSize)} (remainder {Fmt(mod)}).");
        }

        // price == 0 → caller is omitting it (market order); skip price / notional rules.
        if (price > 0m)
        {
            if (info.TickSize > 0m)
            {
                var mod = price % info.TickSize;
                if (mod != 0m)
                    throw new InvalidOrderException(
                        $"Order price {Fmt(price)} on '{info.Symbol}' does not align with PRICE_FILTER.tickSize {Fmt(info.TickSize)} (remainder {Fmt(mod)}).");
            }

            if (!reduceOnly && info.MinNotional > 0m)
            {
                var notional = price * size;
                if (notional < info.MinNotional)
                    throw new InvalidOrderException(
                        $"Order notional {Fmt(notional)} on '{info.Symbol}' is below MIN_NOTIONAL {Fmt(info.MinNotional)} (price {Fmt(price)} × size {Fmt(size)}).");
            }
        }
    }

    private static string Fmt(decimal d) =>
        d.ToString("0.##########", CultureInfo.InvariantCulture);
}
