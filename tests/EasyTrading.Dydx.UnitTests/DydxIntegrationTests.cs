using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Dydx;

namespace EasyTrading.Dydx.UnitTests;

/// <summary>
/// Read-only integration tests that hit dYdX v4 Indexer live mainnet. Skipped by default;
/// set <c>EASYTRADING_INTEGRATION=1</c> to run.
/// </summary>
public sealed class DydxIntegrationTests
{
    private static bool IntegrationEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_INTEGRATION"), "1", StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSymbols_returns_active_perpetuals()
    {
        if (!IntegrationEnabled) return;

        await using var client = new DydxClient(new DydxClientOptions { Network = DydxNetwork.Mainnet });
        var symbols = await client.Markets.GetSymbolsAsync();

        Assert.NotEmpty(symbols);
        Assert.Contains(symbols, s => s.Name == "BTC-USD");
        Assert.All(symbols, s => Assert.Equal(MarketKind.Perpetual, s.Kind));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetOrderBook_for_BTC_USD_returns_valid_spread()
    {
        if (!IntegrationEnabled) return;

        await using var client = new DydxClient(new DydxClientOptions { Network = DydxNetwork.Mainnet });
        var book = await client.Markets.GetOrderBookAsync("BTC-USD", depth: 10);

        Assert.NotEmpty(book.Bids);
        Assert.NotEmpty(book.Asks);
        Assert.True(book.Bids[0].Price < book.Asks[0].Price,
            $"crossed book: bid {book.Bids[0].Price} > ask {book.Asks[0].Price}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAllMids_includes_BTC_USD_with_positive_oracle_price()
    {
        if (!IntegrationEnabled) return;

        await using var client = new DydxClient(new DydxClientOptions { Network = DydxNetwork.Mainnet });
        var mids = await client.Markets.GetAllMidsAsync();

        Assert.NotEmpty(mids);
        Assert.True(mids.TryGetValue("BTC-USD", out var px) && px > 0,
            $"Expected BTC-USD oracle price > 0; got {(mids.TryGetValue("BTC-USD", out var v) ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "<missing>")}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WebSocket_TradesAsync_for_BTC_USD_yields_at_least_one_trade()
    {
        if (!IntegrationEnabled) return;

        await using var client = new DydxClient(new DydxClientOptions { Network = DydxNetwork.Mainnet });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var seen = 0;
        try
        {
            await foreach (var t in client.Streams.TradesAsync("BTC-USD", cts.Token))
            {
                Assert.Equal("BTC-USD", t.Trade.Symbol);
                Assert.True(t.Trade.Price > 0);
                Assert.True(t.Trade.Size > 0);
                seen++;
                if (seen >= 1) break;
            }
        }
        catch (OperationCanceledException) { /* timeout */ }

        Assert.True(seen >= 1, "expected at least one BTC-USD trade within 30s on dYdX mainnet");
    }
}
