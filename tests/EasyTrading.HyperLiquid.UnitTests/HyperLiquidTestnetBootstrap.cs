using System.Net.Http.Json;
using System.Text.Json;
using Nethereum.Signer;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// One-shot helper that generates a fresh Ethereum keypair, derives the EVM address that the
/// HyperLiquid testnet recognises, and requests testnet USDC from the public faucet endpoint
/// <c>POST /info</c> with <c>type=claimDrip</c>. Persists the private key + address to
/// <c>%TEMP%/easytrading-hl-testnet.key</c> so a follow-up write test can pick them up via
/// the <c>HL_TESTNET_PRIVATE_KEY</c> + <c>HL_TESTNET_ADDRESS</c> env vars.
/// </summary>
/// <remarks>
/// Gated by <c>EASYTRADING_BOOTSTRAP_FAUCET=1</c> + <c>EASYTRADING_INTEGRATION=1</c> so it
/// never runs in normal CI.
/// <code>
///   $env:EASYTRADING_INTEGRATION = "1"
///   $env:EASYTRADING_BOOTSTRAP_FAUCET = "1"
///   dotnet test EasyTrading.slnx --filter "FullyQualifiedName~HyperLiquid.UnitTests.HyperLiquidTestnetBootstrap"
/// </code>
/// </remarks>
public sealed class HyperLiquidTestnetBootstrap
{
    private const string FaucetUrl = "https://api.hyperliquid-testnet.xyz/info";

    private static bool BootstrapEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_BOOTSTRAP_FAUCET"), "1", StringComparison.Ordinal)
     && string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_INTEGRATION"),       "1", StringComparison.Ordinal);

    /// <summary>Path the fact writes the generated key + address to.</summary>
    public static string TempKeyPath =>
        Path.Combine(Path.GetTempPath(), "easytrading-hl-testnet.key");

    [Fact]
    [Trait("Category", "Bootstrap")]
    public async Task Generate_fresh_testnet_wallet_and_fund_via_faucet()
    {
        if (!BootstrapEnabled) return;

        // 1. Fresh secp256k1 keypair via Nethereum (already a transitive dependency of EasyTrading.HyperLiquid).
        var key = EthECKey.GenerateKey();
        var privKey = key.GetPrivateKey();         // 0x-prefixed 32-byte hex
        var addr    = key.GetPublicAddress();      // EIP-55 / mixed-case EVM address

        // 2. POST /info { type: "claimDrip", user: addr }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var payload = new { type = "claimDrip", user = addr };

        using var resp = await http.PostAsJsonAsync(FaucetUrl, payload);
        var body = await resp.Content.ReadAsStringAsync();

        // The endpoint returns either:
        //   - 200 + JSON success body (drip credited)
        //   - 200 + "Drip already claimed" (per-address rate limit)
        //   - 200 + "User must be initialized" or similar (account hasn't been seen on mainnet/testnet)
        //   - 4xx + error
        Assert.True(resp.IsSuccessStatusCode,
            $"HL faucet returned {(int)resp.StatusCode} ({resp.StatusCode}): {body}");

        // 3. Persist the key + address so the place-cancel test can pick them up.
        await File.WriteAllTextAsync(
            TempKeyPath,
            string.Join(Environment.NewLine, new[] { privKey, addr }) + Environment.NewLine);

        Console.WriteLine($"[hl-bootstrap] generated testnet wallet: {addr}");
        Console.WriteLine($"[hl-bootstrap] private key written to: {TempKeyPath}");
        Console.WriteLine($"[hl-bootstrap] faucet response: {body}");
    }
}
