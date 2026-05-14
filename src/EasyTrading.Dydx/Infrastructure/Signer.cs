using System.Security.Cryptography;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;

namespace EasyTrading.Dydx.Infrastructure;

/// <summary>
/// Cosmos SDK signer for dYdX v4. Owns the BIP-39 mnemonic, derives a secp256k1 keypair via
/// BIP-32 at Cosmos's default path (<c>m/44'/118'/0'/0/0</c>), and exposes the <c>dydx1…</c>
/// bech32 address, the 33-byte compressed public key, and a raw secp256k1 ECDSA sign that
/// produces the 64-byte <c>r ‖ s</c> Cosmos signature (with <c>s</c> in the lower half).
/// </summary>
/// <remarks>
/// <para>Cosmos's signing flavour differs from Ethereum's in three ways:</para>
/// <list type="number">
///   <item><description>Digest is sha256, not keccak256.</description></item>
///   <item><description>Signature is raw <c>r‖s</c> (64 B), no recovery byte v.</description></item>
///   <item><description>The pubkey on the wire is the 33-byte compressed form.</description></item>
/// </list>
/// <para>NBitcoin provides BIP-39 + BIP-32 + bech32; BouncyCastle (transitive) provides the
/// secp256k1 ECDSA signer with RFC-6979 deterministic <c>k</c>.</para>
/// </remarks>
internal sealed class Signer
{
    /// <summary>BIP-44 coin type 118 = Cosmos. dYdX uses this same path despite being its own chain.</summary>
    private const string DerivationPath = "m/44'/118'/0'/0/0";

    /// <summary>Bech32 HRP (human-readable prefix) for dYdX addresses.</summary>
    public const string AddressHrp = "dydx";

    // secp256k1 curve parameters (cached once).
    private static readonly X9ECParameters Curve = SecNamedCurves.GetByName("secp256k1");
    // Note: SecNamedCurves is the SEC2 curve table — it has secp256k1.
    private static readonly ECDomainParameters Domain = new(Curve.Curve, Curve.G, Curve.N, Curve.H);

    private readonly byte[] _privateKeyBytes;
    private readonly byte[] _compressedPubKey;

    /// <summary>The <c>dydx1…</c> bech32 address derived from the mnemonic + path.</summary>
    public string Address { get; }

    /// <summary>The 33-byte compressed secp256k1 public key (<c>0x02</c>/<c>0x03</c> + X).</summary>
    public byte[] CompressedPublicKey => (byte[])_compressedPubKey.Clone();

    public Signer(string mnemonic, string passphrase = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(mnemonic);

        // BIP-39 → seed → BIP-32 child at m/44'/118'/0'/0/0
        var m = new Mnemonic(mnemonic, Wordlist.English);
        var seed = m.DeriveSeed(passphrase);
        var master = ExtKey.CreateFromSeed(seed);
        var child = master.Derive(new KeyPath(DerivationPath));

        _privateKeyBytes = child.PrivateKey.ToBytes();
        _compressedPubKey = child.PrivateKey.PubKey.Compress().ToBytes();
        Address = DeriveAddress(_compressedPubKey);
    }

    /// <summary>
    /// Sign a 32-byte digest with the loaded key. Returns the 64-byte canonical Cosmos signature
    /// (<c>r ‖ s</c>, with <c>s</c> in the lower half of the curve order). Uses RFC-6979
    /// deterministic <c>k</c> via BouncyCastle's <see cref="HMacDsaKCalculator"/>.
    /// </summary>
    public byte[] SignDigest(byte[] digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 32)
            throw new ArgumentException($"Expected 32-byte digest, got {digest.Length}.", nameof(digest));

        var privKey = new ECPrivateKeyParameters(new BigInteger(1, _privateKeyBytes), Domain);
        var ecdsa = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));
        ecdsa.Init(forSigning: true, privKey);

        var rs = ecdsa.GenerateSignature(digest);
        var r = rs[0];
        var s = rs[1];

        // Enforce low-S canonicalisation (Cosmos requires it).
        var halfOrder = Domain.N.ShiftRight(1);
        if (s.CompareTo(halfOrder) > 0)
            s = Domain.N.Subtract(s);

        var result = new byte[64];
        Buffer.BlockCopy(LeftPad(r.ToByteArrayUnsigned(), 32), 0, result, 0,  32);
        Buffer.BlockCopy(LeftPad(s.ToByteArrayUnsigned(), 32), 0, result, 32, 32);
        return result;
    }

    /// <summary>Convenience: sha256(message) + sign.</summary>
    public byte[] SignMessage(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var digest = SHA256.HashData(message);
        return SignDigest(digest);
    }

    // ─── address derivation ────────────────────────────────────────────────

    /// <summary>Compute <c>dydx1…</c> bech32 address: <c>bech32("dydx", ripemd160(sha256(pubKey)))</c>.</summary>
    private static string DeriveAddress(byte[] compressedPubKey)
    {
        var sha = SHA256.HashData(compressedPubKey);
        var addressBytes = Hashes.RIPEMD160(sha, sha.Length);

        // Bech32 operates on 5-bit groups internally. NBitcoin's `EncodeData` takes pre-converted
        // data (each byte ∈ [0, 31]); the SegWit-flavour `Encode` prepends a witness version, which
        // Cosmos doesn't use. So we hand-do the 8→5 conversion and feed `EncodeData`.
        var squeezed = ConvertBits(addressBytes, fromBits: 8, toBits: 5, pad: true);
        var bech = Encoders.Bech32(AddressHrp);
        return bech.EncodeData(squeezed, Bech32EncodingType.BECH32);
    }

    /// <summary>Standard bech32 bit-packing conversion (BIP-173).</summary>
    private static byte[] ConvertBits(byte[] data, int fromBits, int toBits, bool pad)
    {
        var acc = 0;
        var bits = 0;
        var maxv = (1 << toBits) - 1;
        var result = new List<byte>(data.Length * fromBits / toBits + 2);

        foreach (var value in data)
        {
            if (value < 0 || (value >> fromBits) != 0)
                throw new ArgumentException("ConvertBits: input value out of range.", nameof(data));
            acc = (acc << fromBits) | value;
            bits += fromBits;
            while (bits >= toBits)
            {
                bits -= toBits;
                result.Add((byte)((acc >> bits) & maxv));
            }
        }
        if (pad && bits > 0)
            result.Add((byte)((acc << (toBits - bits)) & maxv));
        else if (!pad && (bits >= fromBits || ((acc << (toBits - bits)) & maxv) != 0))
            throw new InvalidOperationException("ConvertBits: invalid padding.");

        return result.ToArray();
    }

    private static byte[] LeftPad(byte[] source, int totalLength)
    {
        if (source.Length == totalLength) return source;
        if (source.Length > totalLength)
            throw new ArgumentException($"Value of {source.Length} bytes does not fit into {totalLength} bytes.");
        var result = new byte[totalLength];
        Array.Copy(source, 0, result, totalLength - source.Length, source.Length);
        return result;
    }
}
