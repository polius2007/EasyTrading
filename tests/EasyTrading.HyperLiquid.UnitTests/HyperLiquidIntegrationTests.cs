using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// End-to-end smoke tests that hit live HyperLiquid mainnet. Read-only — no credentials needed.
/// Skipped by default; set <c>EASYTRADING_INTEGRATION=1</c> to run.
/// </summary>
public sealed class HyperLiquidIntegrationTests
{
    private static bool IntegrationEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_INTEGRATION"), "1", StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAllMids_returns_real_data_from_mainnet()
    {
        if (!IntegrationEnabled)
            return; // silently skip — kept green in CI without the env var.

        await using var client = new HyperLiquidClient(new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Mainnet,
        });

        var mids = await client.Markets.GetAllMidsAsync();

        Assert.NotEmpty(mids);
        Assert.True(mids.ContainsKey("BTC"), "Expected 'BTC' mid in the response.");
        Assert.True(mids["BTC"] > 0, $"Expected BTC mid > 0, got {mids["BTC"]}.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSymbols_returns_perpetuals_and_spot()
    {
        if (!IntegrationEnabled)
            return;

        await using var client = new HyperLiquidClient(new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Mainnet,
        });

        var symbols = await client.Markets.GetSymbolsAsync();

        Assert.NotEmpty(symbols);
        Assert.Contains(symbols, s => s.Name == "BTC" && s.Kind == EasyTrading.Abstractions.MarketKind.Perpetual);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetOrderBook_for_BTC_returns_levels()
    {
        if (!IntegrationEnabled)
            return;

        await using var client = new HyperLiquidClient(new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Mainnet,
        });

        var book = await client.Markets.GetOrderBookAsync("BTC", depth: 5);

        Assert.Equal("BTC", book.Symbol);
        Assert.NotEmpty(book.Bids);
        Assert.NotEmpty(book.Asks);
        Assert.True(book.Asks[0].Price > book.Bids[0].Price, "Best ask should be above best bid.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WebSocket_AllMids_receives_live_updates()
    {
        if (!IntegrationEnabled)
            return;

        await using var client = new HyperLiquidClient(new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Mainnet,
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var seen = new List<MidUpdate>();

        try
        {
            await foreach (var mid in client.Streams.AllMidsAsync(cts.Token))
            {
                seen.Add(mid);
                if (seen.Count >= 10) break; // got enough, exit early
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // expected timeout — what we got in `seen` is what we assert on
        }

        Assert.True(seen.Count > 0, "Expected at least one MidUpdate from the WebSocket stream.");
        Assert.Contains(seen, m => !string.IsNullOrEmpty(m.Symbol) && m.Mid > 0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WebSocket_Trades_for_BTC_receives_live_updates()
    {
        if (!IntegrationEnabled)
            return;

        await using var client = new HyperLiquidClient(new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Mainnet,
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var seen = new List<TradeUpdate>();

        try
        {
            await foreach (var trade in client.Streams.TradesAsync("BTC", cts.Token))
            {
                seen.Add(trade);
                if (seen.Count >= 3) break;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // expected
        }

        Assert.True(seen.Count > 0,
            $"Expected at least one trade for BTC within 15s; got {seen.Count}. "
            + "BTC trades on HyperLiquid are very frequent — failure usually means WS isn't subscribed correctly.");
        Assert.All(seen, t => Assert.Equal("BTC", t.Trade.Symbol));
        Assert.All(seen, t => Assert.True(t.Trade.Price > 0));
    }
}
