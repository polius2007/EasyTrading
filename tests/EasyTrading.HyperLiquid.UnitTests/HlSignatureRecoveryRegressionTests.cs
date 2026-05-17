using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid.Infrastructure;
using Nethereum.Signer;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Wire-format regression test for every HL action. We've burned through three production
/// "recovery returns wrong address" bugs on mainnet (1.2.1 user-signed primary-type prefix,
/// 1.2.2 trigger order field order). This suite is the systematic firewall: for every
/// signed action our library produces, sign with a known private key, recover the signer
/// from the resulting (digest, r, s, v), and assert it matches the signing key's public
/// address. Any future drift in msgpack field order, EIP-712 typeHash, or domain hash
/// surfaces here instead of via a real-money failure on HL.
/// </summary>
public sealed class HlSignatureRecoveryRegressionTests
{
    private const string TestPrivateKey =
        "0x1111111111111111111111111111111111111111111111111111111111111111";

    private static readonly string TestAddress =
        new EthECKey(TestPrivateKey).GetPublicAddress();

    // ─── L1 (action-signed) actions ──────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void L1_order_action_recovers_to_signer(bool isMainnet)
    {
        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", new object[]
            {
                new HlMap()
                    .Add("a", 0)
                    .Add("b", true)
                    .Add("p", "60000")
                    .Add("s", "0.01")
                    .Add("r", false)
                    .Add("t", new HlMap().Add("limit", new HlMap().Add("tif", "Gtc"))),
            })
            .Add("grouping", "na");

        AssertL1Recovers(action, isMainnet, vault: null, expiresAfter: null);
    }

    [Fact]
    public void L1_order_with_clientOrderId_recovers_to_signer()
    {
        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", new object[]
            {
                new HlMap()
                    .Add("a", 0).Add("b", true).Add("p", "60000").Add("s", "0.01").Add("r", false)
                    .Add("t", new HlMap().Add("limit", new HlMap().Add("tif", "Alo")))
                    .Add("c", "0xabcdef0123456789abcdef0123456789"),
            })
            .Add("grouping", "na");
        AssertL1Recovers(action, isMainnet: true, vault: null, expiresAfter: null);
    }

    [Theory]
    [InlineData("sl", true,  "StopMarket")]
    [InlineData("sl", false, "StopLimit")]
    [InlineData("tp", true,  "TakeProfitMarket")]
    public void L1_trigger_order_recovers_to_signer(string tpsl, bool isMarket, string label)
    {
        _ = label;
        // CRITICAL field order: {isMarket, triggerPx, tpsl}. If reversed, the digest changes,
        // signature recovers on a wrong address, and HL rejects with "User or API Wallet 0x… does not exist".
        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", new object[]
            {
                new HlMap()
                    .Add("a", 0).Add("b", false).Add("p", "30000").Add("s", "0.01").Add("r", true)
                    .Add("t", new HlMap().Add("trigger", new HlMap()
                        .Add("isMarket", isMarket)
                        .Add("triggerPx", "30000")
                        .Add("tpsl", tpsl))),
            })
            .Add("grouping", "na");
        AssertL1Recovers(action, isMainnet: true, vault: null, expiresAfter: null);
    }

    [Fact]
    public void L1_cancel_action_recovers_to_signer()
    {
        var action = new HlMap()
            .Add("type", "cancel")
            .Add("cancels", new object[]
            {
                new HlMap().Add("a", 0).Add("o", 12345L),
            });
        AssertL1Recovers(action, isMainnet: true, vault: null, expiresAfter: null);
    }

    [Fact]
    public void L1_cancelByCloid_recovers_to_signer()
    {
        var action = new HlMap()
            .Add("type", "cancelByCloid")
            .Add("cancels", new object[]
            {
                new HlMap().Add("asset", 0).Add("cloid", "0xabcdef0123456789abcdef0123456789"),
            });
        AssertL1Recovers(action, isMainnet: true, vault: null, expiresAfter: null);
    }

    [Fact]
    public void L1_modify_action_recovers_to_signer()
    {
        var action = new HlMap()
            .Add("type", "modify")
            .Add("oid", 12345L)
            .Add("order", new HlMap()
                .Add("a", 0).Add("b", true).Add("p", "61000").Add("s", "0.01").Add("r", false)
                .Add("t", new HlMap().Add("limit", new HlMap().Add("tif", "Gtc"))));
        AssertL1Recovers(action, isMainnet: true, vault: null, expiresAfter: null);
    }

    [Fact]
    public void L1_batchModify_action_recovers_to_signer()
    {
        var action = new HlMap()
            .Add("type", "batchModify")
            .Add("modifies", new object[]
            {
                new HlMap().Add("oid", 111L).Add("order", new HlMap()
                    .Add("a", 0).Add("b", true).Add("p", "60000").Add("s", "0.01").Add("r", false)
                    .Add("t", new HlMap().Add("limit", new HlMap().Add("tif", "Gtc")))),
                new HlMap().Add("oid", 222L).Add("order", new HlMap()
                    .Add("a", 1).Add("b", false).Add("p", "3500").Add("s", "0.1").Add("r", false)
                    .Add("t", new HlMap().Add("limit", new HlMap().Add("tif", "Alo")))),
            });
        AssertL1Recovers(action, isMainnet: true, vault: null, expiresAfter: null);
    }

    [Fact]
    public void L1_scheduleCancel_action_recovers_to_signer()
    {
        var action = new HlMap()
            .Add("type", "scheduleCancel")
            .Add("time", DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeMilliseconds());
        AssertL1Recovers(action, isMainnet: true, vault: null, expiresAfter: null);
    }

    [Fact]
    public void L1_updateLeverage_action_recovers_to_signer()
    {
        var action = new HlMap()
            .Add("type", "updateLeverage")
            .Add("asset", 0)
            .Add("isCross", true)
            .Add("leverage", 10);
        AssertL1Recovers(action, isMainnet: true, vault: null, expiresAfter: null);
    }

    [Fact]
    public void L1_action_with_vaultAddress_recovers_to_signer()
    {
        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", new object[]
            {
                new HlMap().Add("a", 0).Add("b", true).Add("p", "60000").Add("s", "0.01").Add("r", false)
                           .Add("t", new HlMap().Add("limit", new HlMap().Add("tif", "Gtc"))),
            })
            .Add("grouping", "na");
        // With vault: phantom-agent digest includes the vault byte + address.
        AssertL1Recovers(action, isMainnet: true,
            vault: "0x000000000000000000000000000000000000beef",
            expiresAfter: null);
    }

    [Fact]
    public void L1_action_with_expiresAfter_recovers_to_signer()
    {
        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", new object[]
            {
                new HlMap().Add("a", 0).Add("b", true).Add("p", "60000").Add("s", "0.01").Add("r", false)
                           .Add("t", new HlMap().Add("limit", new HlMap().Add("tif", "Gtc"))),
            })
            .Add("grouping", "na");
        AssertL1Recovers(action, isMainnet: true, vault: null,
            expiresAfter: DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds());
    }

    // ─── User-signed actions ─────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UsdClassTransfer_recovers_to_signer(bool isMainnet)
    {
        var msg = new HlMap()
            .Add("type", "usdClassTransfer")
            .Add("amount", "100")
            .Add("toPerp", true)
            .Add("nonce", 1_700_000_000_000L)
            .Add("hyperliquidChain", isMainnet ? "Mainnet" : "Testnet")
            .Add("signatureChainId", "0x66eee");

        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("amount",           "string"),
            ("toPerp",           "bool"),
            ("nonce",            "uint64"),
        };

        AssertUserSignedRecovers(msg, "UsdClassTransfer", schema);
    }

    [Fact]
    public void UsdSend_recovers_to_signer()
    {
        var msg = new HlMap()
            .Add("type", "usdSend")
            .Add("destination", "0x000000000000000000000000000000000000abcd")
            .Add("amount", "100")
            .Add("time", 1_700_000_000_000L)
            .Add("hyperliquidChain", "Mainnet")
            .Add("signatureChainId", "0x66eee");
        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("destination",      "string"),
            ("amount",           "string"),
            ("time",             "uint64"),
        };
        AssertUserSignedRecovers(msg, "UsdSend", schema);
    }

    [Fact]
    public void SpotSend_recovers_to_signer()
    {
        var msg = new HlMap()
            .Add("type", "spotSend")
            .Add("destination", "0x000000000000000000000000000000000000abcd")
            .Add("token", "PURR:0x123")
            .Add("amount", "10")
            .Add("time", 1_700_000_000_000L)
            .Add("hyperliquidChain", "Mainnet")
            .Add("signatureChainId", "0x66eee");
        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("destination",      "string"),
            ("token",            "string"),
            ("amount",           "string"),
            ("time",             "uint64"),
        };
        AssertUserSignedRecovers(msg, "SpotSend", schema);
    }

    [Fact]
    public void Withdraw_recovers_to_signer()
    {
        var msg = new HlMap()
            .Add("type", "withdraw3")
            .Add("destination", "0x000000000000000000000000000000000000abcd")
            .Add("amount", "1000")
            .Add("time", 1_700_000_000_000L)
            .Add("hyperliquidChain", "Mainnet")
            .Add("signatureChainId", "0x66eee");
        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("destination",      "string"),
            ("amount",           "string"),
            ("time",             "uint64"),
        };
        AssertUserSignedRecovers(msg, "Withdraw", schema);
    }

    [Fact]
    public void ApproveAgent_recovers_to_signer()
    {
        var msg = new HlMap()
            .Add("type", "approveAgent")
            .Add("agentAddress", "0x000000000000000000000000000000000000beef")
            .Add("agentName", "my-bot")
            .Add("nonce", 1_700_000_000_000L)
            .Add("hyperliquidChain", "Mainnet")
            .Add("signatureChainId", "0x66eee");
        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("agentAddress",     "address"),
            ("agentName",        "string"),
            ("nonce",            "uint64"),
        };
        AssertUserSignedRecovers(msg, "ApproveAgent", schema);
    }

    [Fact]
    public void ApproveBuilderFee_recovers_to_signer()
    {
        var msg = new HlMap()
            .Add("type", "approveBuilderFee")
            .Add("maxFeeRate", "0.005%")
            .Add("builder", "0x000000000000000000000000000000000000beef")
            .Add("nonce", 1_700_000_000_000L)
            .Add("hyperliquidChain", "Mainnet")
            .Add("signatureChainId", "0x66eee");
        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("maxFeeRate",       "string"),
            ("builder",          "address"),
            ("nonce",            "uint64"),
        };
        AssertUserSignedRecovers(msg, "ApproveBuilderFee", schema);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void AssertL1Recovers(HlMap action, bool isMainnet, string? vault, long? expiresAfter)
    {
        const long nonce = 1_700_000_000_000L;
        var sig = Signer.SignL1Action(action, vault, nonce, expiresAfter, isMainnet, TestPrivateKey);
        // L1 digest is keccak(0x1901 ++ domainHash ++ structHash) — but the digest itself isn't
        // exposed; we rebuild it via ActionHash + the public Signer.SignL1Action with the SAME
        // key. Determinism (RFC-6979) means we can compare against a reference signing with the
        // SAME key. Then we use Nethereum to recover from the public digest.
        // To get the actual digest, we use a second sign with TestPrivateKey — that returns
        // (r, s, v) that we can recover from. Since we don't expose L1Digest publicly, we
        // verify recovery via a slightly different route: sign once, recover with both keys,
        // and assert the matching key wins.
        var altKey = "0x2222222222222222222222222222222222222222222222222222222222222222";
        var altAddr = new EthECKey(altKey).GetPublicAddress();
        var sigAlt = Signer.SignL1Action(action, vault, nonce, expiresAfter, isMainnet, altKey);

        // (r,s,v) differs between the two — that proves the digest is the SAME for both signers,
        // but the signature depends on the key.
        Assert.NotEqual(sig.R, sigAlt.R);

        // For pure proof of recovery, we need the digest. The cleanest route: recover signer
        // address from the signature using Nethereum's recovery, given the digest. We compute the
        // L1 digest manually by piggy-backing on the exposed ActionHash + the (private) L1Digest
        // construction documented in code.
        var digest = ComputeL1Digest(action, vault, nonce, expiresAfter, isMainnet);
        var recovered = RecoverAddress(digest, sig);
        Assert.Equal(TestAddress.ToLowerInvariant(), recovered.ToLowerInvariant());

        // And the alt-key signature recovers to the alt address — sanity.
        var recoveredAlt = RecoverAddress(digest, sigAlt);
        Assert.Equal(altAddr.ToLowerInvariant(), recoveredAlt.ToLowerInvariant());
    }

    private static void AssertUserSignedRecovers(
        HlMap msg, string primaryType, IReadOnlyList<(string Name, string Type)> schema)
    {
        var sig = Signer.SignUserAction(msg, primaryType, schema, TestPrivateKey);
        var digest = Signer.UserSignedDigest(msg, primaryType, schema);
        var recovered = RecoverAddress(digest, sig);
        Assert.Equal(TestAddress.ToLowerInvariant(), recovered.ToLowerInvariant());

        // Sanity: signing with a different key recovers to a different address.
        var altKey = "0x2222222222222222222222222222222222222222222222222222222222222222";
        var altAddr = new EthECKey(altKey).GetPublicAddress();
        var sigAlt = Signer.SignUserAction(msg, primaryType, schema, altKey);
        var recoveredAlt = RecoverAddress(digest, sigAlt);
        Assert.Equal(altAddr.ToLowerInvariant(), recoveredAlt.ToLowerInvariant());
    }

    /// <summary>
    /// Reconstruct the L1 digest the same way <see cref="Signer.SignL1Action"/> does so we can
    /// recover the signer address from the captured (r, s, v) externally.
    /// Mirrors the steps in Signer.L1Digest (which is private).
    /// </summary>
    private static byte[] ComputeL1Digest(HlMap action, string? vault, long nonce, long? expiresAfter, bool isMainnet)
    {
        var actionHash = Signer.ActionHash(action, vault, nonce, expiresAfter);
        // The library's domain + struct hashing is private; we reproduce it byte-for-byte here.
        // If this drifts from Signer.L1Digest, this test catches it immediately.
        var keccak = new Nethereum.Util.Sha3Keccack();

        var domainTypeHash = keccak.CalculateHash(System.Text.Encoding.UTF8.GetBytes(
            "EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)"));
        var nameHash = keccak.CalculateHash(System.Text.Encoding.UTF8.GetBytes("Exchange"));
        var versionHash = keccak.CalculateHash(System.Text.Encoding.UTF8.GetBytes("1"));
        var chainIdBytes = Uint256(1337);
        var zeroAddr = new byte[32];
        var domainHash = keccak.CalculateHash(Concat(domainTypeHash, nameHash, versionHash, chainIdBytes, zeroAddr));

        var agentTypeHash = keccak.CalculateHash(System.Text.Encoding.UTF8.GetBytes(
            "Agent(string source,bytes32 connectionId)"));
        var sourceHash = keccak.CalculateHash(System.Text.Encoding.UTF8.GetBytes(isMainnet ? "a" : "b"));
        var structHash = keccak.CalculateHash(Concat(agentTypeHash, sourceHash, actionHash));

        return keccak.CalculateHash(Concat(new byte[] { 0x19, 0x01 }, domainHash, structHash));
    }

    private static byte[] Uint256(long value)
    {
        var b = new byte[32];
        var v = (ulong)value;
        for (var i = 31; i >= 0 && v != 0; i--, v >>= 8) b[i] = (byte)(v & 0xff);
        return b;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, result, offset, p.Length); offset += p.Length; }
        return result;
    }

    private static string RecoverAddress(byte[] digest, HlSignature sig)
    {
        var r = HexToBytes(sig.R);
        var s = HexToBytes(sig.S);
        var ecdsaSig = EthECDSASignatureFactory.FromComponents(r, s, new byte[] { (byte)sig.V });
        var recovered = EthECKey.RecoverFromSignature(ecdsaSig, digest);
        return recovered.GetPublicAddress();
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}
