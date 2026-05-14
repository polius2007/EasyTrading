using EasyTrading.Dydx.Infrastructure;

namespace EasyTrading.Dydx.UnitTests;

/// <summary>
/// Tests for the Cosmos SDK signer. Address derivation is the critical correctness check —
/// every other signing flow flows through it. The expected address for the test mnemonic
/// matches the value computed by Cosmos's reference Python SDK (<c>cosmpy</c>) and dYdX's
/// JavaScript v4 client when configured with HRP="dydx" and the standard Cosmos derivation
/// path m/44'/118'/0'/0/0.
/// </summary>
public sealed class SignerTests
{
    // Well-known BIP-39 test mnemonic (do NOT use for real funds). Generates the same
    // address across every Cosmos-family wallet at the default path m/44'/118'/0'/0/0.
    private const string TestMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Fact]
    public void Address_has_expected_shape_and_is_deterministic()
    {
        var s1 = new Signer(TestMnemonic);
        var s2 = new Signer(TestMnemonic);

        // bech32 dydx addresses are exactly 43 chars (4-char HRP + "1" + 32 data + 6 checksum).
        Assert.Equal(43, s1.Address.Length);
        Assert.StartsWith("dydx1", s1.Address, StringComparison.Ordinal);
        // Determinism: identical input → identical address (no random state).
        Assert.Equal(s1.Address, s2.Address);
    }

    [Fact]
    public void Address_matches_known_value_for_test_mnemonic()
    {
        // Address derived from the BIP-39 "abandon × 11 + about" mnemonic at the standard
        // Cosmos path m/44'/118'/0'/0/0 with HRP "dydx". Cross-checked against the Cosmos
        // pure-Python reference (cosmpy.crypto.keypairs + bech32 encoding). If you change the
        // derivation algorithm, regenerate this value via:
        //   python -c "from cosmpy.crypto.keypairs import PrivateKey
        //              from cosmpy.crypto.address import Address
        //              from mnemonic import Mnemonic
        //              ..."
        // and double-check in Keplr / Leap by importing the same mnemonic.
        const string Expected = "dydx19rl4cm2hmr8afy4kldpxz3fka4jguq0a4erelz";

        var signer = new Signer(TestMnemonic);
        Assert.Equal(Expected, signer.Address);
    }

    [Fact]
    public void CompressedPublicKey_is_33_bytes_and_starts_with_02_or_03()
    {
        var signer = new Signer(TestMnemonic);
        var pk = signer.CompressedPublicKey;
        Assert.Equal(33, pk.Length);
        Assert.True(pk[0] is 0x02 or 0x03,
            $"compressed pubkey must start with 0x02 or 0x03; got 0x{pk[0]:x2}");
    }

    [Fact]
    public void Signature_is_64_bytes_with_low_S()
    {
        var signer = new Signer(TestMnemonic);
        var digest = new byte[32];
        for (var i = 0; i < 32; i++) digest[i] = (byte)i;

        var sig = signer.SignDigest(digest);

        Assert.Equal(64, sig.Length);

        // Low-S: the second half of the signature (s) must be < N/2 (Cosmos canonical).
        // Curve order N for secp256k1 = 0xFFFFFFFF…BAAEDCE6AF48A03BBFD25E8CD0364141
        // Half-order  N/2 = 0x7FFFFFFF…5D576E7357A4501DDFE92F46681B20A0
        var sBytes = sig.AsSpan(32, 32);
        // The top bit of `s` must be 0 in low-S form (since N is roughly 2^256, N/2 ≈ 2^255).
        Assert.True((sBytes[0] & 0x80) == 0,
            "expected low-S signature (top bit of s must be 0)");
    }

    [Fact]
    public void Signature_is_deterministic_RFC6979()
    {
        var signer = new Signer(TestMnemonic);
        var digest = new byte[32];
        for (var i = 0; i < 32; i++) digest[i] = 0x42;

        var sig1 = signer.SignDigest(digest);
        var sig2 = signer.SignDigest(digest);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void Different_mnemonic_yields_different_address()
    {
        var s1 = new Signer(TestMnemonic);
        var s2 = new Signer("legal winner thank year wave sausage worth useful legal winner thank yellow");
        Assert.NotEqual(s1.Address, s2.Address);
    }

    [Fact]
    public void SignDigest_rejects_non_32_byte_input()
    {
        var signer = new Signer(TestMnemonic);
        Assert.Throws<ArgumentException>(() => signer.SignDigest(new byte[31]));
        Assert.Throws<ArgumentException>(() => signer.SignDigest(new byte[33]));
    }
}
