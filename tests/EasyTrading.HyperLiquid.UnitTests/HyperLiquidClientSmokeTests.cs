using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;

namespace EasyTrading.HyperLiquid.UnitTests;

public sealed class HyperLiquidClientSmokeTests
{
    [Fact]
    public async Task Client_constructs_and_disposes_with_default_options()
    {
        await using var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        Assert.NotNull(client);
    }

    [Fact]
    public void Client_exposes_canonical_exchange_id()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());
        Assert.Equal("hyperliquid", client.ExchangeId);
    }

    [Fact]
    public void Client_advertises_HyperLiquid_capabilities()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.Perpetuals));
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.Spot));
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.Twap));
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.Vaults));
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.Staking));
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.BuilderFees));
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.AgentWallets));
    }

    [Fact]
    public void Mainnet_options_resolve_to_official_urls()
    {
        var opts = new HyperLiquidClientOptions { Network = HyperLiquidNetwork.Mainnet };

        Assert.Equal("https://api.hyperliquid.xyz/", opts.GetEffectiveRestBaseUrl().ToString());
        Assert.Equal("wss://api.hyperliquid.xyz/ws", opts.GetEffectiveWebSocketUrl().ToString());
    }

    [Fact]
    public void Testnet_options_resolve_to_testnet_urls()
    {
        var opts = new HyperLiquidClientOptions { Network = HyperLiquidNetwork.Testnet };

        Assert.Equal("https://api.hyperliquid-testnet.xyz/", opts.GetEffectiveRestBaseUrl().ToString());
        Assert.Equal("wss://api.hyperliquid-testnet.xyz/ws", opts.GetEffectiveWebSocketUrl().ToString());
    }

    [Fact]
    public void Custom_url_overrides_network_default()
    {
        var opts = new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Mainnet,
            RestBaseUrl = new Uri("https://my-proxy.example/hl"),
        };

        Assert.Equal("https://my-proxy.example/hl", opts.GetEffectiveRestBaseUrl().ToString());
    }

    [Fact]
    public async Task Write_operations_still_throw_NotImplementedException_in_Phase_2()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        await Assert.ThrowsAsync<NotImplementedException>(() => client.Orders.CancelAllAsync());
        await Assert.ThrowsAsync<NotImplementedException>(() => client.Positions.SetLeverageAsync("BTC", 10, MarginMode.Cross));
        await Assert.ThrowsAsync<NotImplementedException>(() => client.Transfers.SpotToPerpAsync(100m));
    }

    [Fact]
    public async Task User_state_queries_without_credentials_throw_AuthenticationException()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Account.GetStateAsync());
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Positions.GetAllAsync());
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Orders.GetOpenAsync());
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Trades.GetMyFillsAsync());
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Staking.GetMyDelegationsAsync());
    }

    [Fact]
    public void Streaming_methods_throw_NotImplementedException_until_Phase_4()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        Assert.Throws<NotImplementedException>(() => client.Streams.TradesAsync("BTC", default));
        Assert.Throws<NotImplementedException>(() => client.Streams.AllMidsAsync(default));
        Assert.Throws<NotImplementedException>(() => client.Streams.MyFillsAsync(default));
    }

    [Fact]
    public async Task GetRecentTrades_is_unsupported_on_HL_REST()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        await Assert.ThrowsAsync<NotSupportedException>(() => client.Markets.GetRecentTradesAsync("BTC"));
    }

    [Fact]
    public async Task DI_registration_wires_up_IHyperLiquidExchange_and_IExchangeClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEasyTrading().AddHyperLiquid(o =>
        {
            o.Network = HyperLiquidNetwork.Testnet;
        });

        await using var sp = services.BuildServiceProvider();

        var hl       = sp.GetRequiredService<IHyperLiquidExchange>();
        var cross    = sp.GetRequiredService<IExchangeClient>();
        var concrete = sp.GetRequiredService<HyperLiquidClient>();

        // All three resolve to the same singleton.
        Assert.Same(concrete, hl);
        Assert.Same(concrete, cross);
        Assert.Equal(HyperLiquidNetwork.Testnet, concrete.Options.Network);
    }
}
