using EasyTrading.Abstractions;
using EasyTrading.Aster;

namespace EasyTrading.Aster.UnitTests;

/// <summary>
/// Read-only integration tests that hit Aster Finance live mainnet. Skipped by default;
/// set <c>EASYTRADING_INTEGRATION=1</c> to run.
/// </summary>
public sealed class AsterIntegrationTests
{
    private static bool IntegrationEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_INTEGRATION"), "1", StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSymbols_returns_non_empty_perpetual_universe()
    {
        if (!IntegrationEnabled) return;

        await using var client = new AsterClient(new AsterClientOptions { Network = AsterNetwork.Mainnet });
        var symbols = await client.Markets.GetSymbolsAsync();

        Assert.NotEmpty(symbols);
        Assert.Contains(symbols, s => s.QuoteAsset.Equals("USDT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetOrderBook_for_BTCUSDT_returns_valid_levels()
    {
        if (!IntegrationEnabled) return;

        await using var client = new AsterClient(new AsterClientOptions { Network = AsterNetwork.Mainnet });
        var book = await client.Markets.GetOrderBookAsync("BTCUSDT", depth: 10);

        Assert.NotEmpty(book.Bids);
        Assert.NotEmpty(book.Asks);
        Assert.True(book.Bids[0].Price < book.Asks[0].Price,
            $"top-of-book sanity: bid {book.Bids[0].Price} should be < ask {book.Asks[0].Price}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAllMids_includes_BTCUSDT()
    {
        if (!IntegrationEnabled) return;

        await using var client = new AsterClient(new AsterClientOptions { Network = AsterNetwork.Mainnet });
        var mids = await client.Markets.GetAllMidsAsync();

        Assert.NotEmpty(mids);
        Assert.True(mids.TryGetValue("BTCUSDT", out var btc) && btc > 0,
            $"Expected BTCUSDT mid > 0, got {(mids.TryGetValue("BTCUSDT", out var v) ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "<missing>")}.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WebSocket_TradesAsync_for_BTCUSDT_yields_at_least_one_trade()
    {
        if (!IntegrationEnabled) return;

        await using var client = new AsterClient(new AsterClientOptions { Network = AsterNetwork.Mainnet });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var seen = 0;
        try
        {
            await foreach (var t in client.Streams.TradesAsync("BTCUSDT", cts.Token))
            {
                Assert.Equal("BTCUSDT", t.Trade.Symbol, ignoreCase: true);
                Assert.True(t.Trade.Price > 0);
                Assert.True(t.Trade.Size > 0);
                seen++;
                if (seen >= 1) break;
            }
        }
        catch (OperationCanceledException) { /* timeout */ }

        Assert.True(seen >= 1, "expected at least one BTCUSDT trade within 30s on Aster mainnet");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WebSocket_BookTicker_for_BTCUSDT_yields_at_least_one_update()
    {
        if (!IntegrationEnabled) return;

        await using var client = new AsterClient(new AsterClientOptions { Network = AsterNetwork.Mainnet });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var seen = 0;
        try
        {
            await foreach (var bbo in client.Streams.BestBidOfferAsync("BTCUSDT", cts.Token))
            {
                Assert.Equal("BTCUSDT", bbo.Symbol, ignoreCase: true);
                Assert.True(bbo.BidPrice > 0);
                Assert.True(bbo.AskPrice > 0);
                Assert.True(bbo.BidPrice <= bbo.AskPrice, $"crossed book: bid {bbo.BidPrice} > ask {bbo.AskPrice}");
                seen++;
                if (seen >= 1) break;
            }
        }
        catch (OperationCanceledException) { /* timeout */ }

        Assert.True(seen >= 1, "expected at least one BTCUSDT BBO update within 30s on Aster mainnet");
    }
}
