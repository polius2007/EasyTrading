using EasyTrading.Abstractions;
using EasyTrading.Aster;

namespace EasyTrading.Aster.UnitTests;

/// <summary>
/// Signed-write smoke test against Aster Finance testnet. Skipped unless every required env var
/// is set, because Aster testnet is whitelist-only — there's no programmatic way to register a
/// signer (API wallet) against a master, nor to fund a fresh address; both have to be done by a
/// real user via the Aster testnet UI after their wallet is whitelisted by Aster's team.
/// </summary>
/// <remarks>
/// <para>Once you have a whitelisted master + a registered API wallet on Aster testnet, export:</para>
/// <code>
///   $env:EASYTRADING_INTEGRATION         = "1"
///   $env:ASTER_TESTNET_MASTER_ADDRESS    = "0xYourMasterAddress"
///   $env:ASTER_TESTNET_SIGNER_ADDRESS    = "0xYourSignerAddress"
///   $env:ASTER_TESTNET_PRIVATE_KEY       = "0xYourSignerKey"
///   dotnet test EasyTrading.slnx --filter "FullyQualifiedName~AsterTestnetWriteTests"
/// </code>
/// <para>The test places a far-from-market post-only BTCUSDT buy at half the live mid (guaranteed
/// to rest, never fill) and then cancels by client id.</para>
/// </remarks>
public sealed class AsterTestnetWriteTests
{
    private static bool IntegrationEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_INTEGRATION"), "1", StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Testnet_PlaceLimit_and_Cancel_with_keys_from_env()
    {
        if (!IntegrationEnabled) return;

        var master = Environment.GetEnvironmentVariable("ASTER_TESTNET_MASTER_ADDRESS");
        var signer = Environment.GetEnvironmentVariable("ASTER_TESTNET_SIGNER_ADDRESS");
        var key    = Environment.GetEnvironmentVariable("ASTER_TESTNET_PRIVATE_KEY");

        // Skip silently if the test wallet hasn't been provisioned (matches the dYdX pattern).
        if (string.IsNullOrEmpty(master) || string.IsNullOrEmpty(signer) || string.IsNullOrEmpty(key))
            return;

        await using var client = new AsterClient(new AsterClientOptions
        {
            Network = AsterNetwork.Testnet,
            Credentials = new AsterCredentials(
                MasterAddress: master,
                SignerAddress: signer,
                PrivateKey:    key),
        });

        // Use half the live mid → guaranteed to rest, never fill.
        var mids = await client.Markets.GetAllMidsAsync();
        Assert.True(mids.TryGetValue("BTCUSDT", out var btcMid) && btcMid > 0);
        var safePrice = Math.Floor(btcMid * 0.5m);
        const decimal safeSize = 0.001m;

        var clientOrderId = $"easytrading-test-{Random.Shared.Next(1, 999_999_999)}";

        var placed = await client.Orders.PlaceLimitAsync(
            symbol: "BTCUSDT",
            side:   OrderSide.Buy,
            price:  safePrice,
            size:   safeSize,
            tif:    TimeInForce.Alo,
            reduceOnly:    false,
            clientOrderId: clientOrderId);

        if (placed.Status != OrderStatus.Pending && placed.Status != OrderStatus.Open)
        {
            // Surface the error so the test log is actionable.
            throw new Xunit.Sdk.XunitException(
                $"Aster testnet placement failed — status={placed.Status}, error={placed.ErrorMessage}");
        }

        // Brief delay so the venue has time to ingest the order before we cancel.
        await Task.Delay(TimeSpan.FromSeconds(1));

        var cancelled = await client.Orders.CancelByClientIdAsync("BTCUSDT", clientOrderId);
        Assert.True(cancelled.Success,
            $"cancel failed for client_id={clientOrderId}: {cancelled.ErrorMessage}");
    }
}
