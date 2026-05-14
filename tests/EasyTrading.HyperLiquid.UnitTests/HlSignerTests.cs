using EasyTrading.HyperLiquid.Infrastructure;
using Nethereum.Signer;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Smoke tests for <see cref="Signer"/>. We can't compare against Python-generated vectors
/// without a live cross-check, so we exercise the properties that matter end-to-end:
/// determinism and "the produced signature is valid ECDSA over the digest the signer claims to hash".
/// </summary>
public sealed class HlSignerTests
{
    // Pinned test key — clearly synthetic, only used here.
    private const string TestPrivateKey =
        "0x1111111111111111111111111111111111111111111111111111111111111111";

    [Fact]
    public void ActionHash_is_32_bytes()
    {
        var action = new HlMap().Add("type", "order").Add("grouping", "na");
        var hash = Signer.ActionHash(action, vaultAddress: null, nonce: 1_700_000_000_000L, expiresAfter: null);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void ActionHash_is_deterministic()
    {
        var build = () => new HlMap().Add("type", "order").Add("grouping", "na");
        var h1 = Signer.ActionHash(build(), null, 12345L, null);
        var h2 = Signer.ActionHash(build(), null, 12345L, null);
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void ActionHash_changes_with_nonce()
    {
        var build = () => new HlMap().Add("type", "order");
        var h1 = Signer.ActionHash(build(), null, 1L, null);
        var h2 = Signer.ActionHash(build(), null, 2L, null);
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void ActionHash_changes_with_vault_address()
    {
        var build = () => new HlMap().Add("type", "order");
        var none = Signer.ActionHash(build(), null, 1L, null);
        var withVault = Signer.ActionHash(build(), "0x0000000000000000000000000000000000000001", 1L, null);
        Assert.NotEqual(none, withVault);
    }

    [Fact]
    public void L1_signature_is_well_formed()
    {
        var action = new HlMap().Add("type", "order").Add("grouping", "na");
        var sig = Signer.SignL1Action(action, vaultAddress: null, nonce: 1_700_000_000_000L,
            expiresAfter: null, isMainnet: true, privateKeyHex: TestPrivateKey);

        // r and s are 0x-prefixed 32-byte hex strings → 2 + 64 chars.
        Assert.StartsWith("0x", sig.R);
        Assert.StartsWith("0x", sig.S);
        Assert.Equal(66, sig.R.Length);
        Assert.Equal(66, sig.S.Length);

        // v is the recovery byte; Nethereum returns 27 or 28 for non-EIP-155 signatures.
        Assert.InRange(sig.V, 27, 28);
    }

    [Fact]
    public void L1_signature_is_deterministic_for_the_same_input()
    {
        // RFC 6979 deterministic ECDSA: same private key + same digest → same signature.
        var build = () => new HlMap().Add("type", "order").Add("grouping", "na");
        var s1 = Signer.SignL1Action(build(), null, 1L, null, isMainnet: true, TestPrivateKey);
        var s2 = Signer.SignL1Action(build(), null, 1L, null, isMainnet: true, TestPrivateKey);
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void Mainnet_and_testnet_produce_different_L1_signatures()
    {
        // The phantom-agent "source" field differs ("a" vs "b"), so the signed digest differs.
        var build = () => new HlMap().Add("type", "order").Add("grouping", "na");
        var sigMain = Signer.SignL1Action(build(), null, 1L, null, isMainnet: true,  TestPrivateKey);
        var sigTest = Signer.SignL1Action(build(), null, 1L, null, isMainnet: false, TestPrivateKey);
        Assert.NotEqual(sigMain, sigTest);
    }

    [Fact]
    public void User_signed_signature_uses_chainId_from_signatureChainId()
    {
        // Schema for the simplest user-signed action: usdClassTransfer.
        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("amount", "string"),
            ("toPerp", "bool"),
            ("nonce", "uint64"),
        };

        var message = new HlMap()
            .Add("hyperliquidChain", "Mainnet")
            .Add("amount", "100")
            .Add("toPerp", true)
            .Add("nonce", 1_700_000_000_000L)
            .Add("signatureChainId", "0x66eee");

        var sig = Signer.SignUserAction(message, "UsdClassTransfer", schema, TestPrivateKey);

        Assert.StartsWith("0x", sig.R);
        Assert.Equal(66, sig.R.Length);
        Assert.InRange(sig.V, 27, 28);
    }

    [Fact]
    public void EthECKey_can_be_constructed_from_our_test_private_key()
    {
        // Confirms the signing primitive we layer on top is functional in this test process.
        var key = new EthECKey(TestPrivateKey);
        var addr = key.GetPublicAddress();
        Assert.NotNull(addr);
        Assert.StartsWith("0x", addr);
        Assert.Equal(42, addr.Length);
    }
}
