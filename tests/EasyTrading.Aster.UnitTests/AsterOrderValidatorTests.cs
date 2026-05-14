using EasyTrading.Abstractions;
using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.UnitTests;

/// <summary>
/// Aster pre-flight validator — driven by symbol filters from /fapi/v3/exchangeInfo.
/// </summary>
public sealed class AsterOrderValidatorTests
{
    // BTCUSDT-shaped: tick=0.1 USDT, step=0.001 BTC, min size=0.001, min notional=5 USDT.
    private static readonly AsterSymbolInfo BtcUsdt = new(
        Symbol:            "BTCUSDT",
        Status:            "TRADING",
        PricePrecision:    1,
        QuantityPrecision: 3,
        TickSize:          0.1m,
        StepSize:          0.001m,
        MinQty:            0.001m,
        MaxQty:            1000m,
        MinNotional:       5m);

    // ─── happy paths ─────────────────────────────────────────────────────────

    [Fact]
    public void Valid_limit_order_passes()
    {
        AsterOrderValidator.Validate(BtcUsdt, price: 60_000.1m, size: 0.001m, reduceOnly: false);
    }

    [Fact]
    public void Zero_price_skips_price_and_notional_rules()
    {
        // Market orders pass `price: 0` to short-circuit the price-grid + min-notional checks.
        AsterOrderValidator.Validate(BtcUsdt, price: 0m, size: 0.001m, reduceOnly: false);
    }

    // ─── size rules ──────────────────────────────────────────────────────────

    [Fact]
    public void Zero_size_throws()
    {
        var ex = Assert.Throws<InvalidOrderException>(() =>
            AsterOrderValidator.Validate(BtcUsdt, price: 60_000m, size: 0m, reduceOnly: false));
        Assert.Contains("size must be > 0", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Below_min_qty_throws()
    {
        var ex = Assert.Throws<InvalidOrderException>(() =>
            AsterOrderValidator.Validate(BtcUsdt, price: 60_000m, size: 0.0005m, reduceOnly: false));
        Assert.Contains("below LOT_SIZE.minQty", ex.Message);
    }

    [Fact]
    public void Above_max_qty_throws()
    {
        var ex = Assert.Throws<InvalidOrderException>(() =>
            AsterOrderValidator.Validate(BtcUsdt, price: 60_000m, size: 1001m, reduceOnly: false));
        Assert.Contains("exceeds LOT_SIZE.maxQty", ex.Message);
    }

    [Fact]
    public void Size_not_aligned_to_step_throws()
    {
        // step=0.001; 0.0015 is mid-step.
        var ex = Assert.Throws<InvalidOrderException>(() =>
            AsterOrderValidator.Validate(BtcUsdt, price: 60_000m, size: 0.0015m, reduceOnly: false));
        Assert.Contains("LOT_SIZE.stepSize", ex.Message);
    }

    // ─── price rules ─────────────────────────────────────────────────────────

    [Fact]
    public void Price_not_aligned_to_tick_throws()
    {
        // tick=0.1; 60000.05 is mid-tick.
        var ex = Assert.Throws<InvalidOrderException>(() =>
            AsterOrderValidator.Validate(BtcUsdt, price: 60_000.05m, size: 0.001m, reduceOnly: false));
        Assert.Contains("PRICE_FILTER.tickSize", ex.Message);
    }

    // ─── min notional ────────────────────────────────────────────────────────

    [Fact]
    public void Below_min_notional_throws()
    {
        // price=1, size=1 → notional 1 < min notional 5
        var info = BtcUsdt with { TickSize = 1m, StepSize = 1m, MinQty = 1m };
        var ex = Assert.Throws<InvalidOrderException>(() =>
            AsterOrderValidator.Validate(info, price: 1m, size: 1m, reduceOnly: false));
        Assert.Contains("MIN_NOTIONAL", ex.Message);
    }

    [Fact]
    public void Reduce_only_skips_min_notional()
    {
        var info = BtcUsdt with { TickSize = 1m, StepSize = 1m, MinQty = 1m };
        // Same parameters that would fail without reduceOnly — pass when reduceOnly = true.
        AsterOrderValidator.Validate(info, price: 1m, size: 1m, reduceOnly: true);
    }
}
