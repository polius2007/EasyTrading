using System.Globalization;
using EasyTrading.Abstractions;

namespace EasyTrading.Dydx.Infrastructure;

/// <summary>
/// Caches per-market metadata Cosmos transactions need but the cross-DEX
/// <see cref="EasyTrading.Abstractions.Models.Symbol"/> doesn't carry: the integer
/// <see cref="MarketInfo.ClobPairId"/> dYdX wraps in every order, plus the two exponents
/// (<see cref="MarketInfo.AtomicResolution"/>, <see cref="MarketInfo.QuantumConversionExponent"/>)
/// that translate human decimal size + price into the on-chain quantums/subticks integers.
/// </summary>
internal sealed class MarketsCache(RestClient rest) : IDisposable
{
    /// <summary>USDC has 6 decimals — fixed for dYdX v4 today (everything trades against USDC).</summary>
    public const int QuoteAtomicResolution = -6;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public void Dispose() => _lock.Dispose();

    private Dictionary<string, MarketInfo>? _markets;

    /// <summary>Get the cached market info for a ticker. Loads once on first call.</summary>
    public async Task<MarketInfo> GetAsync(string ticker, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (_markets!.TryGetValue(ticker, out var info))
            return info;
        throw new ExchangeApiException($"Unknown dYdX market: '{ticker}'.");
    }

    /// <summary>Force-refresh the cache from the Indexer (e.g. after a new market lists).</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { _markets = null; }
        finally { _lock.Release(); }
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_markets is not null) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_markets is not null) return;

            var raw = await rest.GetAsync<PerpetualMarketsRaw>("perpetualMarkets", query: null, ct).ConfigureAwait(false);
            var map = new Dictionary<string, MarketInfo>(raw.Markets.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var (ticker, m) in raw.Markets)
            {
                if (!uint.TryParse(m.ClobPairId, NumberStyles.None, CultureInfo.InvariantCulture, out var clobPairId))
                    continue; // skip pre-launch markets without a numeric clob_pair_id

                map[ticker] = new MarketInfo(
                    Ticker:                    ticker,
                    ClobPairId:                clobPairId,
                    AtomicResolution:          m.AtomicResolution ?? 0,
                    QuantumConversionExponent: m.QuantumConversionExponent ?? 0,
                    TickSize:                  m.TickSize ?? 0m,
                    StepSize:                  m.StepSize ?? 0m);
            }
            _markets = map;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>
/// Per-market metadata Cosmos transactions need. <see cref="ToQuantums"/> + <see cref="ToSubticks"/>
/// translate human-readable size + price into the integers dYdX expects in <c>Order.quantums</c>
/// and <c>Order.subticks</c>.
/// </summary>
internal sealed record MarketInfo(
    string Ticker,
    uint ClobPairId,
    int AtomicResolution,
    int QuantumConversionExponent,
    decimal TickSize,
    decimal StepSize)
{
    /// <summary><c>quantums = size × 10^(-atomicResolution)</c>. For BTC (atomicResolution=-10), 0.001 BTC → 10,000,000 quantums.</summary>
    public ulong ToQuantums(decimal size)
    {
        var exponent = -AtomicResolution;
        var scaled = size * Pow10(exponent);
        if (scaled < 0m) throw new ArgumentOutOfRangeException(nameof(size), "size must be non-negative");
        if (scaled > ulong.MaxValue)
            throw new OverflowException($"size {size} → {scaled} quantums exceeds ulong.MaxValue for '{Ticker}'.");
        return (ulong)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// <c>subticks = price × 10^(quoteAtomicResolution − atomicResolution − quantumConversionExponent)</c>.
    /// For BTC-USD (atomicResolution=-10, quantumConversionExponent=-9, quoteAtomicResolution=-6),
    /// price 60000 → 6 × 10^17 subticks.
    /// </summary>
    public ulong ToSubticks(decimal price)
    {
        var exponent = MarketsCache.QuoteAtomicResolution - AtomicResolution - QuantumConversionExponent;
        var scaled = price * Pow10(exponent);
        if (scaled < 0m) throw new ArgumentOutOfRangeException(nameof(price), "price must be non-negative");
        if (scaled > ulong.MaxValue)
            throw new OverflowException($"price {price} → {scaled} subticks exceeds ulong.MaxValue for '{Ticker}'.");
        return (ulong)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }

    /// <summary>10^n as a decimal. Supports n ∈ [-20, 20] which is sufficient for every dYdX market.</summary>
    private static decimal Pow10(int n)
    {
        if (n is < -28 or > 28)
            throw new OverflowException($"10^{n} exceeds decimal range.");
        decimal result = 1m;
        if (n >= 0) for (var i = 0; i < n; i++)  result *= 10m;
        else         for (var i = 0; i < -n; i++) result /= 10m;
        return result;
    }
}
