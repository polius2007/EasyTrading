using EasyTrading.Abstractions;
using EasyTrading.Dydx;

namespace EasyTrading.Dydx.UnitTests;

/// <summary>
/// Smoke tests that don't require network access. Verifies the surface compiles, DI shape is
/// sane, options resolve URLs correctly, and Phase-pending modules report the expected
/// <c>NotImplementedException</c> pointing at the right phase.
/// </summary>
public sealed class DydxClientSmokeTests
{
    [Fact]
    public async Task Construct_with_defaults_succeeds_and_reports_perpetuals_capability()
    {
        var options = new DydxClientOptions { Network = DydxNetwork.Mainnet };
        await using var client = new DydxClient(options);

        Assert.Equal("dydx", client.ExchangeId);
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.Perpetuals));
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.SubAccounts));
    }

    [Fact]
    public void Options_resolve_to_documented_mainnet_url()
    {
        var options = new DydxClientOptions { Network = DydxNetwork.Mainnet };
        Assert.Equal("https://indexer.dydx.trade/v4/", options.GetEffectiveRestBaseUrl().ToString());
        Assert.Equal("wss://indexer.dydx.trade/v4/ws", options.GetEffectiveWebSocketUrl().ToString());
    }

    [Fact]
    public void Options_resolve_to_documented_testnet_url()
    {
        var options = new DydxClientOptions { Network = DydxNetwork.Testnet };
        Assert.Equal("https://indexer.v4testnet.dydx.exchange/v4/", options.GetEffectiveRestBaseUrl().ToString());
    }

    [Fact]
    public void Custom_rest_base_url_overrides_network_default()
    {
        var options = new DydxClientOptions
        {
            Network = DydxNetwork.Mainnet,
            IndexerRestUrl = new Uri("https://my-indexer.example.com/v4/"),
        };
        Assert.Equal("https://my-indexer.example.com/v4/", options.GetEffectiveRestBaseUrl().ToString());
    }

    [Fact]
    public async Task Pending_write_methods_throw_NotImplementedException()
    {
        var options = new DydxClientOptions { Network = DydxNetwork.Mainnet };
        await using var client = new DydxClient(options);

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            client.Orders.PlaceLimitAsync("BTC-USD", OrderSide.Buy, 60_000m, 0.001m));
    }

    [Fact]
    public async Task User_streams_without_credentials_raise_AuthenticationException()
    {
        var options = new DydxClientOptions { Network = DydxNetwork.Mainnet };
        await using var client = new DydxClient(options);

        await Assert.ThrowsAsync<AuthenticationException>(async () =>
        {
            await foreach (var _ in client.Streams.MyFillsAsync(default)) { /* never reached */ }
        });
    }

    [Fact]
    public async Task Signed_reads_without_credentials_raise_AuthenticationException()
    {
        var options = new DydxClientOptions { Network = DydxNetwork.Mainnet };
        await using var client = new DydxClient(options);

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Account.GetStateAsync());
    }

    [Fact]
    public async Task TWAP_methods_throw_NotSupportedException_dYdX_has_no_TWAP()
    {
        var options = new DydxClientOptions { Network = DydxNetwork.Mainnet };
        await using var client = new DydxClient(options);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            client.Orders.PlaceTwapAsync(new(Symbol: "BTC-USD", Side: OrderSide.Buy, Size: 0.1m, DurationMinutes: 10)));
    }
}
