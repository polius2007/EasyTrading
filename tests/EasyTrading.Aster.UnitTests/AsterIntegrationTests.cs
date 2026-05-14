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
}
