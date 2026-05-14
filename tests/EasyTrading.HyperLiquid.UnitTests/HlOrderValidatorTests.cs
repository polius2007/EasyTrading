using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Pre-flight validation rules mirror HyperLiquid's published constraints so we surface
/// invalid orders with a precise message before they hit the network.
/// </summary>
public sealed class HlOrderValidatorTests
{
    // BTC perp: szDecimals=5 → max price decimals = 6 - 5 = 1.
    private static readonly HlAssetInfo BtcPerp = new(AssetId: 0, SzDecimals: 5, IsSpot: false);

    // PURR/USDC spot: szDecimals=0 → max price decimals = 8 - 0 = 8.
    private static readonly HlAssetInfo PurrSpot = new(AssetId: 10_000, SzDecimals: 0, IsSpot: true);

    // ─── Happy paths ─────────────────────────────────────────────────────────

    [Fact]
    public void Valid_perp_order_passes()
    {
        HlOrderValidator.Validate("BTC", price: 60_000m, size: 0.01m, BtcPerp, reduceOnly: false);
    }

    [Fact]
    public void Integer_price_always_passes_even_with_many_sig_figs()
    {
        // 12_345_600 has 8 sig figs; integer prices are unconditionally allowed.
        HlOrderValidator.Validate("BTC", price: 12_345_600m, size: 0.001m, BtcPerp, reduceOnly: false);
    }

    [Fact]
    public void Price_with_max_sig_figs_passes()
    {
        // 1234.5 → 5 sig figs, 1 fractional digit; BTC perp allows 6-5=1 fractional digits.
        // Use size=0.01 so notional = 12.345 USDC clears the $10 minimum.
        HlOrderValidator.Validate("BTC", price: 1234.5m, size: 0.01m, BtcPerp, reduceOnly: false);
    }

    [Fact]
    public void Size_at_szDecimals_limit_passes()
    {
        // szDecimals=5 → 0.12345 has exactly 5 fractional digits → OK.
        HlOrderValidator.Validate("BTC", price: 60_000m, size: 0.12345m, BtcPerp, reduceOnly: false);
    }

    [Fact]
    public void Spot_uses_higher_price_decimal_ceiling()
    {
        // PURR spot, szDecimals=0 → max price decimals = 8. 0.12345678 is exactly 8.
        // 5 sig figs cap also limits this — pick a 5-sig-fig number that fits.
        HlOrderValidator.Validate("PURR/USDC", price: 0.12345m, size: 100m, PurrSpot, reduceOnly: false);
    }

    // ─── Size rules ──────────────────────────────────────────────────────────

    [Fact]
    public void Zero_size_throws()
    {
        var ex = Assert.Throws<InvalidOrderException>(() =>
            HlOrderValidator.Validate("BTC", 60_000m, size: 0m, BtcPerp, reduceOnly: false));
        Assert.Contains("size must be > 0", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Negative_size_throws()
    {
        Assert.Throws<InvalidOrderException>(() =>
            HlOrderValidator.Validate("BTC", 60_000m, size: -0.01m, BtcPerp, reduceOnly: false));
    }

    [Fact]
    public void Size_with_too_many_decimals_throws()
    {
        // szDecimals=5; 0.123456 has 6 fractional digits → invalid.
        var ex = Assert.Throws<InvalidOrderException>(() =>
            HlOrderValidator.Validate("BTC", 60_000m, size: 0.123456m, BtcPerp, reduceOnly: false));
        Assert.Contains("decimal places", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at most 5", ex.Message);
    }

    [Fact]
    public void Trailing_zeros_in_size_are_normalised()
    {
        // 0.10 and 0.1 are equal on the wire; both should pass even on szDecimals=1.
        var info = new HlAssetInfo(0, SzDecimals: 1, IsSpot: false);
        HlOrderValidator.Validate("X", price: 1234m, size: 0.10m, info, reduceOnly: false);
        HlOrderValidator.Validate("X", price: 1234m, size: 0.1m, info, reduceOnly: false);
    }

    // ─── Price rules ─────────────────────────────────────────────────────────

    [Fact]
    public void Price_with_too_many_decimals_throws()
    {
        // BTC perp allows 1 fractional digit; 1234.56 has 2 → invalid.
        var ex = Assert.Throws<InvalidOrderException>(() =>
            HlOrderValidator.Validate("BTC", price: 1234.56m, size: 0.01m, BtcPerp, reduceOnly: false));
        Assert.Contains("decimal places", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Price_with_too_many_sig_figs_throws()
    {
        // 1.0001 fits the decimal-place rule (4 ≤ 1)? No — 4 > 1, so this throws on decimals first.
        // Use a perp with szDecimals=0 → max decimals=6, so 1.00001 has 5 decimals → ok,
        // but 6 sig figs (1.00001) > 5 → throws on sig-figs rule.
        var info = new HlAssetInfo(0, SzDecimals: 0, IsSpot: false);
        var ex = Assert.Throws<InvalidOrderException>(() =>
            HlOrderValidator.Validate("X", price: 1.00001m, size: 1m, info, reduceOnly: false));
        Assert.Contains("significant figures", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Zero_price_skips_price_check()
    {
        // Used by TWAP path where HL computes the slice price.
        HlOrderValidator.Validate("BTC", price: 0m, size: 0.01m, BtcPerp, reduceOnly: false);
    }

    // ─── Min notional ────────────────────────────────────────────────────────

    [Fact]
    public void Notional_below_10_usd_throws()
    {
        // 1 * 1 = $1 → below the $10 minimum.
        var info = new HlAssetInfo(0, SzDecimals: 1, IsSpot: false);
        var ex = Assert.Throws<InvalidOrderException>(() =>
            HlOrderValidator.Validate("X", price: 1m, size: 1m, info, reduceOnly: false));
        Assert.Contains("below HyperLiquid's $10 minimum", ex.Message);
    }

    [Fact]
    public void Reduce_only_skips_min_notional_check()
    {
        var info = new HlAssetInfo(0, SzDecimals: 1, IsSpot: false);
        // Same $1 notional — would throw for an opening order, passes for reduce-only.
        HlOrderValidator.Validate("X", price: 1m, size: 1m, info, reduceOnly: true);
    }

    [Fact]
    public void Notional_at_10_usd_passes()
    {
        var info = new HlAssetInfo(0, SzDecimals: 1, IsSpot: false);
        HlOrderValidator.Validate("X", price: 5m, size: 2m, info, reduceOnly: false);
    }
}
