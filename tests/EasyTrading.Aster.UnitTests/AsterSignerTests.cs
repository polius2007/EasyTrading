using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.UnitTests;

/// <summary>
/// Tests for Aster's EIP-712 signer. Without a third-party verifier we focus on:
/// (1) deterministic output — same key + same msg → same signature
/// (2) well-formed shape — 0x + 130 hex chars, with v ∈ {27,28}
/// (3) different message → different signature
/// (4) different key   → different signature
/// </summary>
public sealed class AsterSignerTests
{
    // Demo key from Aster's published Python example (DO NOT use for real trading).
    private const string DemoPrivateKey =
        "0x4fd0a42218f3eae43a6ce26d22544e986139a01e5b34a62db53757ffca81bae1";

    private const string SampleMsg =
        "symbol=ASTERUSDT&type=LIMIT&side=BUY&timeInForce=GTC&quantity=20&price=0.5&nonce=1748310859508867&signer=0x21cF8Ae13Bb72632562c6Fff438652Ba1a151bb0";

    [Fact]
    public void Signature_is_deterministic_for_the_same_inputs()
    {
        var sig1 = AsterSigner.Sign(SampleMsg, DemoPrivateKey);
        var sig2 = AsterSigner.Sign(SampleMsg, DemoPrivateKey);
        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void Signature_has_expected_shape()
    {
        var sig = AsterSigner.Sign(SampleMsg, DemoPrivateKey);

        Assert.StartsWith("0x", sig, StringComparison.Ordinal);
        Assert.Equal(2 + 130, sig.Length); // 0x + 65 bytes * 2 hex chars

        // Final byte (v) must be 27 or 28 — 0x1b or 0x1c.
        var vHex = sig[^2..];
        Assert.True(vHex is "1b" or "1c", $"expected v ∈ {{1b, 1c}}; got {vHex}");
    }

    [Fact]
    public void Different_message_produces_different_signature()
    {
        var sig1 = AsterSigner.Sign(SampleMsg, DemoPrivateKey);
        var sig2 = AsterSigner.Sign(SampleMsg.Replace("BUY", "SELL", StringComparison.Ordinal), DemoPrivateKey);
        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Different_key_produces_different_signature()
    {
        var sig1 = AsterSigner.Sign(SampleMsg, DemoPrivateKey);
        // A different (but valid) private key — flip a byte.
        var altKey = DemoPrivateKey[..^2] + (DemoPrivateKey[^2] == 'f' ? "ee" : "ff");
        var sig2 = AsterSigner.Sign(SampleMsg, altKey);
        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Empty_message_signs_without_throwing()
    {
        // Edge case — Aster's API will reject this but the signer shouldn't.
        var sig = AsterSigner.Sign(string.Empty, DemoPrivateKey);
        Assert.StartsWith("0x", sig, StringComparison.Ordinal);
        Assert.Equal(2 + 130, sig.Length);
    }
}
