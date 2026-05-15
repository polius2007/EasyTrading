using System.Net.Http.Json;
using EasyTrading.Dydx.Infrastructure;
using NBitcoin;

namespace EasyTrading.Dydx.UnitTests;

/// <summary>
/// One-shot helper that generates a fresh BIP-39 mnemonic, derives the dydx1… address, and
/// requests testnet USDC + a subaccount initialisation from the public dYdX testnet faucet.
/// </summary>
/// <remarks>
/// <para>This fact is gated by <c>EASYTRADING_BOOTSTRAP_FAUCET=1</c> and <c>EASYTRADING_INTEGRATION=1</c>
/// so it never runs in normal CI. To bootstrap a fresh testnet wallet for end-to-end verification:</para>
/// <code>
///   $env:EASYTRADING_INTEGRATION = "1"
///   $env:EASYTRADING_BOOTSTRAP_FAUCET = "1"
///   dotnet test EasyTrading.slnx --filter "FullyQualifiedName~Generate_fresh_testnet_wallet"
/// </code>
/// <para>The mnemonic is written to <c>%TEMP%/easytrading-dydx-testnet.mnemonic</c> on Windows
/// (<c>$TMPDIR</c> on *nix) so the follow-up <c>Testnet_PlaceLimit_and_Cancel</c> run can pick
/// it up without round-tripping through a shell prompt.</para>
/// </remarks>
public sealed class TestnetBootstrap
{
    private const string FaucetUrl    = "https://faucet.v4testnet.dydx.exchange/faucet/tokens";
    private const int    SubaccountNumber = 0;
    // Faucet caps per-call at a low number; 2000 USDC is plenty for a few far-from-market trades.
    private const int    FaucetAmountUsdc = 2000;

    private static bool BootstrapEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_BOOTSTRAP_FAUCET"), "1", StringComparison.Ordinal)
     && string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_INTEGRATION"),       "1", StringComparison.Ordinal);

    /// <summary>Path the fact writes the generated mnemonic + address to (gitignored via .gitignore).</summary>
    public static string TempMnemonicPath =>
        Path.Combine(Path.GetTempPath(), "easytrading-dydx-testnet.mnemonic");

    [Fact]
    [Trait("Category", "Bootstrap")]
    public async Task Generate_fresh_testnet_wallet_and_fund_via_faucet()
    {
        if (!BootstrapEnabled) return;

        // 1. Generate a fresh 24-word BIP-39 mnemonic. NBitcoin uses RandomNumberGenerator under
        //    the hood, so this is cryptographically random.
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        var words = mnemonic.ToString();

        // 2. Derive the dydx1… address with our production Signer so we use exactly the same
        //    derivation path the trading flow does.
        var signer = new Signer(words);
        var addr = signer.Address;

        // 3. Hit the faucet. Body shape mirrors the python/rust SDKs:
        //      POST /faucet/tokens   { address, subaccountNumber, amount }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var payload = new { address = addr, subaccountNumber = SubaccountNumber, amount = FaucetAmountUsdc };

        using var resp = await http.PostAsJsonAsync(FaucetUrl, payload);
        var body = await resp.Content.ReadAsStringAsync();

        Assert.True(resp.IsSuccessStatusCode,
            $"faucet returned {(int)resp.StatusCode} ({resp.StatusCode}): {body}");

        // 4. Persist mnemonic + address to %TEMP% so a follow-up xunit run can pick it up.
        //    Format: line 1 = mnemonic words, line 2 = derived address (for sanity checks).
        await File.WriteAllTextAsync(
            TempMnemonicPath,
            string.Join(Environment.NewLine, new[] { words, addr }) + Environment.NewLine);

        // 5. Address is safe to log; mnemonic is intentionally not echoed in the test output.
        Console.WriteLine($"[bootstrap] funded dYdX testnet wallet: {addr}");
        Console.WriteLine($"[bootstrap] mnemonic written to: {TempMnemonicPath}");
        Console.WriteLine($"[bootstrap] faucet response: {body}");
    }
}
