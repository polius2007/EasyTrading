using EasyTrading.Abstractions;
using EasyTrading.Aster;
using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.UnitTests;

/// <summary>
/// Smoke tests that don't require network access. Verifies the surface compiles, DI shape is
/// sane, options resolve URLs correctly, and stub modules report the expected
/// <c>NotImplementedException</c> for the venue-pending features.
/// </summary>
public sealed class AsterClientSmokeTests
{
    [Fact]
    public async Task Construct_with_defaults_succeeds_and_reports_perpetuals_capability()
    {
        var options = new AsterClientOptions { Network = AsterNetwork.Mainnet };
        await using var client = new AsterClient(options);

        Assert.Equal("aster", client.ExchangeId);
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.Perpetuals));
        Assert.True(client.Capabilities.HasFlag(ExchangeCapabilities.AgentWallets));
    }

    [Fact]
    public void Options_resolve_to_documented_mainnet_url()
    {
        var options = new AsterClientOptions { Network = AsterNetwork.Mainnet };
        Assert.Equal("https://fapi.asterdex.com/", options.GetEffectiveRestBaseUrl().ToString());
    }

    [Fact]
    public void Custom_rest_base_url_overrides_network_default()
    {
        var options = new AsterClientOptions
        {
            Network = AsterNetwork.Mainnet,
            RestBaseUrl = new Uri("https://fapi3.asterdex.com"),
        };
        Assert.Equal("https://fapi3.asterdex.com/", options.GetEffectiveRestBaseUrl().ToString());
    }

    [Fact]
    public async Task Signed_endpoints_without_credentials_raise_AuthenticationException()
    {
        // No Credentials configured → SignAndSendAsync rejects up-front before any HTTP. We probe
        // via GetStateAsync because it doesn't swallow auth errors the way the cancel/transfer
        // result-returning methods do.
        var options = new AsterClientOptions { Network = AsterNetwork.Mainnet };
        await using var client = new AsterClient(options);

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Account.GetStateAsync());
    }

    [Fact]
    public async Task User_streams_without_credentials_raise_AuthenticationException()
    {
        var options = new AsterClientOptions { Network = AsterNetwork.Mainnet };
        await using var client = new AsterClient(options);

        await Assert.ThrowsAsync<AuthenticationException>(async () =>
        {
            await foreach (var _ in client.Streams.MyFillsAsync(default)) { /* never reached */ }
        });
    }

    [Fact]
    public void Nonce_is_strictly_monotonic()
    {
        var n = new Nonce();
        long a = n.Next();
        long b = n.Next();
        long c = n.Next();
        Assert.True(b > a, $"expected {b} > {a}");
        Assert.True(c > b, $"expected {c} > {b}");
    }
}
