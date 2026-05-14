using EasyTrading.HyperLiquid;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// End-to-end smoke tests that hit the live HyperLiquid mainnet REST endpoint. Read-only — no
/// credentials needed. Skipped by default; set the env var <c>EASYTRADING_INTEGRATION=1</c> to run.
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
}
