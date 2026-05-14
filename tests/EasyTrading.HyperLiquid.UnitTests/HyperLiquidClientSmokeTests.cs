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
    public async Task Phase_3_1_writes_without_credentials_throw_AuthenticationException()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        // All write methods (user-signed transfers, vaults, staking, agent approval) now reach
        // the Exchange-endpoint signing path. Without credentials they raise AuthenticationException
        // synchronously, before any network call.
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Transfers.SpotToPerpAsync(100m));
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Transfers.TransferUsdAsync("0xdest", 100m));
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Transfers.WithdrawAsync("0xdest", 100m));
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Account.ApproveAgentAsync("0xagent"));
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Vaults.DepositAsync("0xvault", 1m));
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Staking.DepositAsync(1m));
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Staking.DelegateAsync("0xvalidator", 1m));
    }

    [Fact]
    public async Task Phase_3_writes_without_credentials_throw_AuthenticationException()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        // CancelAllAsync hits GetOpenAsync first, which requires a user address before any network call.
        await Assert.ThrowsAsync<AuthenticationException>(() => client.Orders.CancelAllAsync());
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
    public void Phase_4_public_streams_return_enumerable_without_throwing()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        // Public WS streams are async iterators: returning the enumerable is side-effect-free.
        // The WebSocket connect only happens once the caller starts `await foreach`.
        Assert.NotNull(client.Streams.TradesAsync("BTC", default));
        Assert.NotNull(client.Streams.AllMidsAsync(default));
        Assert.NotNull(client.Streams.OrderBookAsync("BTC", 20, default));
        Assert.NotNull(client.Streams.BestBidOfferAsync("BTC", default));
        Assert.NotNull(client.Streams.CandlesAsync("BTC", Interval.OneMinute, default));
    }

    [Fact]
    public void Phase_4_user_streams_without_credentials_throw_AuthenticationException()
    {
        var client = new HyperLiquidClient(new HyperLiquidClientOptions());

        // User-scoped streams check creds synchronously before returning the enumerable.
        Assert.Throws<AuthenticationException>(() => client.Streams.MyOrdersAsync(default));
        Assert.Throws<AuthenticationException>(() => client.Streams.MyFillsAsync(default));
        Assert.Throws<AuthenticationException>(() => client.Streams.MyFundingsAsync(default));
        Assert.Throws<AuthenticationException>(() => client.Streams.MyNotificationsAsync(default));
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
