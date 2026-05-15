using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Signed-write smoke test against HyperLiquid testnet.
/// </summary>
/// <remarks>
/// <para>Reads <c>HL_TESTNET_PRIVATE_KEY</c> + <c>HL_TESTNET_ADDRESS</c> from the environment.
/// If neither is set, looks for the persisted file at <c>%TEMP%/easytrading-hl-testnet.key</c>
/// (the format <see cref="HyperLiquidTestnetBootstrap"/> writes). Skips silently if no wallet
/// is provisioned, so library users without a testnet wallet keep green tests.</para>
/// <para>To set up a fresh testnet wallet end-to-end:</para>
/// <code>
///   $env:EASYTRADING_INTEGRATION         = "1"
///   $env:EASYTRADING_BOOTSTRAP_FAUCET    = "1"
///   dotnet test EasyTrading.slnx --filter "FullyQualifiedName~HyperLiquidTestnetBootstrap"
///   Remove-Item Env:EASYTRADING_BOOTSTRAP_FAUCET
///   dotnet test EasyTrading.slnx --filter "FullyQualifiedName~HyperLiquidTestnetWriteTests"
/// </code>
/// <para>The test places a far-from-market post-only BTC buy at half the live mid (guaranteed
/// to rest, never fill) and cancels it.</para>
/// </remarks>
public sealed class HyperLiquidTestnetWriteTests
{
    private static bool IntegrationEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_INTEGRATION"), "1", StringComparison.Ordinal);

    private static (string PrivateKey, string Address)? TryLoadWallet()
    {
        var envKey  = Environment.GetEnvironmentVariable("HL_TESTNET_PRIVATE_KEY");
        var envAddr = Environment.GetEnvironmentVariable("HL_TESTNET_ADDRESS");
        if (!string.IsNullOrEmpty(envKey) && !string.IsNullOrEmpty(envAddr))
            return (envKey, envAddr);

        var path = HyperLiquidTestnetBootstrap.TempKeyPath;
        if (!File.Exists(path)) return null;

        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return null;
        var k = lines[0].Trim();
        var a = lines[1].Trim();
        return string.IsNullOrEmpty(k) || string.IsNullOrEmpty(a) ? null : (k, a);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Testnet_PlaceLimit_and_Cancel_with_provisioned_wallet()
    {
        if (!IntegrationEnabled) return;
        var wallet = TryLoadWallet();
        if (wallet is null) return; // no provisioned testnet wallet — skip silently

        await using var client = new HyperLiquidClient(new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Testnet,
            Credentials = new HyperLiquidCredentials(
                MasterAddress: wallet.Value.Address,
                PrivateKey:    wallet.Value.PrivateKey),
        });

        // Half the live mid → far enough not to risk filling, close enough that the order is sane.
        var mids = await client.Markets.GetAllMidsAsync();
        Assert.True(mids.TryGetValue("BTC", out var btcMid) && btcMid > 0);
        var safePrice = Math.Floor(btcMid * 0.5m);
        const decimal safeSize = 0.001m;

        var clientOrderId = $"0x{Guid.NewGuid():N}";

        var placed = await client.Orders.PlaceLimitAsync(
            symbol: "BTC",
            side:   OrderSide.Buy,
            price:  safePrice,
            size:   safeSize,
            tif:    TimeInForce.Alo,
            reduceOnly:    false,
            clientOrderId: clientOrderId);

        if (placed.Status != OrderStatus.Pending && placed.Status != OrderStatus.Open)
        {
            throw new Xunit.Sdk.XunitException(
                $"HL testnet placement failed — status={placed.Status}, error={placed.ErrorMessage}");
        }

        await Task.Delay(TimeSpan.FromSeconds(1));

        var cancelled = await client.Orders.CancelByClientIdAsync("BTC", clientOrderId);
        Assert.True(cancelled.Success,
            $"HL testnet cancel failed for client_id={clientOrderId}: {cancelled.ErrorMessage}");
    }
}
