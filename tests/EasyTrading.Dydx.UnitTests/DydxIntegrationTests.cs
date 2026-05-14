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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Testnet_validator_returns_account_for_known_test_mnemonic_address()
    {
        // Independent of DYDX_TESTNET_MNEMONIC: derives the dydx1… address for the well-known
        // BIP-39 "abandon × 11 + about" mnemonic (already funded + active on testnet) and queries
        // its account number / sequence via the validator REST gateway. This proves end-to-end
        // that:
        //   1. Our address derivation matches what the chain has on-record.
        //   2. Our CosmosClient.GetAccountAsync correctly parses the BaseAccount JSON.
        //   3. The default validator REST URL for testnet is reachable.
        if (!IntegrationEnabled) return;

        var options = new DydxClientOptions { Network = DydxNetwork.Testnet };
        var signer = new EasyTrading.Dydx.Infrastructure.Signer(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var cosmos = new EasyTrading.Dydx.Infrastructure.CosmosClient(
            http, options.GetEffectiveValidatorRestUrl(), options.RetryPolicy);

        var (accountNumber, sequence) = await cosmos.GetAccountAsync(signer.Address, default);

        // The address has been used historically so both fields will be non-zero.
        Assert.True(accountNumber > 0, $"expected non-zero account_number; got {accountNumber}");
        Assert.True(sequence >= 0,   $"sequence must be ≥ 0; got {sequence}");
    }

    // ─── Phase 7.2 signed-write verification ────────────────────────────────
    //
    // Reads DYDX_TESTNET_MNEMONIC from the environment. NEVER hard-codes it.
    // Set with: $env:DYDX_TESTNET_MNEMONIC = "your 24 testnet words"
    // (use a wallet with a few USDC from the dYdX testnet faucet)
    //
    // The test:
    //   1) Derives the dydx1… address from the mnemonic.
    //   2) Places a far-from-market post-only BTC-USD buy via Orders.PlaceLimitAsync.
    //   3) Asserts the broadcast succeeded (txhash returned, no error).
    //   4) Cancels the order via Orders.CancelByClientIdAsync.
    //
    // If you're seeing this fail with "account does not exist", your testnet wallet has
    // no funds — hit https://faucet.v4testnet.dydx.exchange first.

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Testnet_PlaceLimit_and_Cancel_with_mnemonic_from_env()
    {
        if (!IntegrationEnabled) return;
        var mnemonic = Environment.GetEnvironmentVariable("DYDX_TESTNET_MNEMONIC");
        if (string.IsNullOrEmpty(mnemonic))
        {
            // Skip without failing — letting unit-test runs without a wallet stay green.
            return;
        }

        // Derive the address from the mnemonic so we don't need a second env var.
        var derived = new EasyTrading.Dydx.Infrastructure.Signer(mnemonic);

        await using var client = new DydxClient(new DydxClientOptions
        {
            Network = DydxNetwork.Testnet,
            Credentials = new DydxCredentials(
                Address:          derived.Address,
                Mnemonic:         mnemonic,
                SubaccountNumber: 0),
        });

        // Use a price 50% below the live mid → guaranteed to rest, never fill.
        var mids = await client.Markets.GetAllMidsAsync();
        Assert.True(mids.TryGetValue("BTC-USD", out var btcMid) && btcMid > 0);
        var safePrice = Math.Floor(btcMid * 0.5m);
        const decimal safeSize = 0.001m;

        // Use a sticky client id we can cancel by later.
        var clientId = (uint)Random.Shared.Next(1, 999_999_999);
        var clientIdStr = clientId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var placed = await client.Orders.PlaceLimitAsync(
            symbol: "BTC-USD",
            side:   OrderSide.Buy,
            price:  safePrice,
            size:   safeSize,
            tif:    TimeInForce.Alo,
            reduceOnly:    false,
            clientOrderId: clientIdStr);

        // First-trade self-check: the broadcast either succeeded (Pending), or returned a
        // useful error message we can iterate on. Either way we get a non-null Status.
        Assert.True(placed.Status == OrderStatus.Pending || placed.ErrorMessage is not null,
            $"unexpected placement state — status={placed.Status}, error={placed.ErrorMessage}");

        if (placed.Status != OrderStatus.Pending)
        {
            // Surface the error so the test log is actionable.
            throw new Xunit.Sdk.XunitException(
                $"placement failed against dYdX testnet: {placed.ErrorMessage}");
        }

        // Give the validator a moment to ingest the order.
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Cancel by client id.
        var cancelled = await client.Orders.CancelByClientIdAsync("BTC-USD", clientIdStr);
        Assert.True(cancelled.Success,
            $"cancel failed for client_id={clientIdStr}: {cancelled.ErrorMessage}");
    }
}
