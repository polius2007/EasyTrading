using EasyTrading.Dydx.Infrastructure;

namespace EasyTrading.Dydx.UnitTests;

/// <summary>
/// Verifies the quantum / subtick conversion formulas dYdX uses on-chain. The exact reference
/// values come from cross-checking with the dYdX v4 Python client
/// (<c>v4-client-py</c>'s <c>OrderSizeQuantums</c> / <c>OrderPriceSubticks</c> helpers).
/// </summary>
public sealed class MarketsCacheTests
{
    // BTC-USD shape on dYdX testnet:
    //   atomicResolution          = -10  (size has 10 decimals on-chain)
    //   quantumConversionExponent = -9
    // → quantums = humanSize × 10^10
    // → subticks = humanPrice × 10^(-6 - (-10) - (-9)) = humanPrice × 10^13
    private static readonly MarketInfo BtcUsd = new(
        Ticker:                    "BTC-USD",
        ClobPairId:                0,
        AtomicResolution:          -10,
        QuantumConversionExponent: -9,
        TickSize:                  1m,
        StepSize:                  0.0001m);

    [Fact]
    public void Quantums_for_BTC_uses_10_pow_10()
    {
        Assert.Equal(10_000_000UL, BtcUsd.ToQuantums(0.001m));   // 0.001 BTC
        Assert.Equal(1UL,          BtcUsd.ToQuantums(0.0000000001m)); // 1 quantum
        Assert.Equal(10_000_000_000UL, BtcUsd.ToQuantums(1m));   // 1 BTC
    }

    [Fact]
    public void Subticks_for_BTC_uses_10_pow_13()
    {
        // 1 USD → 10^13 subticks
        Assert.Equal(10_000_000_000_000UL, BtcUsd.ToSubticks(1m));
        // 60000 USD → 6 × 10^17 subticks
        Assert.Equal(600_000_000_000_000_000UL, BtcUsd.ToSubticks(60_000m));
    }

    [Fact]
    public void Quantums_rejects_negative_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BtcUsd.ToQuantums(-0.1m));
    }

    [Fact]
    public void Subticks_rejects_negative_price()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BtcUsd.ToSubticks(-100m));
    }

    [Fact]
    public void Different_exponents_yield_different_scale()
    {
        // ETH-USD shape: atomicResolution=-9, quantumConversionExponent=-9
        var ethUsd = BtcUsd with { Ticker = "ETH-USD", AtomicResolution = -9 };
        Assert.Equal(1_000_000UL, ethUsd.ToQuantums(0.001m)); // ETH: 10^9 scale, not 10^10
    }
}
