using System.Numerics;
using System.Text;
using Nethereum.Signer;
using Nethereum.Util;

namespace EasyTrading.Aster.Infrastructure;

/// <summary>
/// EIP-712 signer for Aster's V3 API. Every signed request (TRADE / USER_DATA / USER_STREAM)
/// produces an EIP-712 ECDSA signature over a single-field <c>Message(string msg)</c> struct,
/// where <c>msg</c> is the URL-encoded form of the request parameters (including <c>nonce</c>
/// and <c>signer</c>). The signature is then appended to the request as a <c>signature</c>
/// parameter.
/// </summary>
/// <remarks>
/// <para>Domain (constant, taken from Aster's published Python demo):</para>
/// <list type="bullet">
///   <item><description><c>name</c> = <c>AsterSignTransaction</c></description></item>
///   <item><description><c>version</c> = <c>1</c></description></item>
///   <item><description><c>chainId</c> = <c>1666</c></description></item>
///   <item><description><c>verifyingContract</c> = <c>0x0000000000000000000000000000000000000000</c></description></item>
/// </list>
/// <para>The <c>msg</c> field is the URL-encoded representation of the request's form body /
/// query string, including the <c>nonce</c> (microsecond timestamp) and <c>signer</c> (API
/// wallet address) but excluding the <c>signature</c> field itself.</para>
/// </remarks>
internal static class AsterSigner
{
    private static readonly Sha3Keccack Keccak = Sha3Keccack.Current;

    // EIP-712 type hash for the domain.
    private static readonly byte[] DomainTypeHash =
        Keccak.CalculateHash(Encoding.UTF8.GetBytes(
            "EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)"));

    // Type hash for the message body.
    private static readonly byte[] MessageTypeHash =
        Keccak.CalculateHash(Encoding.UTF8.GetBytes("Message(string msg)"));

    // Cached domain separator (constant for Aster).
    private static readonly byte[] DomainSeparator = BuildDomainSeparator();

    /// <summary>
    /// Sign the supplied <paramref name="msg"/> with the API wallet's private key and return
    /// a 0x-prefixed 65-byte hex signature (r ‖ s ‖ v) ready to drop into the request as the
    /// <c>signature</c> parameter.
    /// </summary>
    /// <param name="msg">URL-encoded request parameters (including <c>nonce</c> + <c>signer</c>).</param>
    /// <param name="privateKeyHex">Hex-encoded private key of the API wallet (<c>signer</c>).</param>
    public static string Sign(string msg, string privateKeyHex)
    {
        ArgumentNullException.ThrowIfNull(msg);
        ArgumentNullException.ThrowIfNull(privateKeyHex);

        // EIP-712 message struct hash: keccak256(MessageTypeHash || keccak256(msg))
        var msgBytes = Encoding.UTF8.GetBytes(msg);
        var msgHash = Keccak.CalculateHash(msgBytes);
        var structHash = Keccak.CalculateHash(Concat(MessageTypeHash, msgHash));

        // Final digest: keccak256("\x19\x01" || domainSeparator || structHash)
        var digest = Keccak.CalculateHash(Concat(new byte[] { 0x19, 0x01 }, DomainSeparator, structHash));

        var key = new EthECKey(privateKeyHex);
        var ecdsa = key.SignAndCalculateV(digest);

        // r, s come back as variable-length big-endian; pad-left to 32 bytes.
        var r = LeftPad(ecdsa.R, 32);
        var s = LeftPad(ecdsa.S, 32);

        // Aster expects 65-byte concatenation (r ‖ s ‖ v). v is the recovery byte 27 or 28
        // (EIP-155 form is rejected by Aster's verifier — keep the raw 27/28 value).
        var v = ecdsa.V is { Length: > 0 } vArr ? vArr[0] : (byte)27;
        if (v < 27) v = (byte)(v + 27);

        var sig = new byte[65];
        Buffer.BlockCopy(r, 0, sig, 0, 32);
        Buffer.BlockCopy(s, 0, sig, 32, 32);
        sig[64] = v;

        return "0x" + Convert.ToHexString(sig).ToLowerInvariant();
    }

    // ─── domain separator (constant for Aster) ───────────────────────────────

    private static byte[] BuildDomainSeparator()
    {
        var nameHash = Keccak.CalculateHash(Encoding.UTF8.GetBytes("AsterSignTransaction"));
        var versionHash = Keccak.CalculateHash(Encoding.UTF8.GetBytes("1"));
        var chainId = Uint256(1666);
        var verifyingContract = new byte[32]; // zero address, padded left to 32 bytes
        return Keccak.CalculateHash(Concat(DomainTypeHash, nameHash, versionHash, chainId, verifyingContract));
    }

    // ─── ABI helpers ─────────────────────────────────────────────────────────

    private static byte[] Uint256(long value)
    {
        var bi = new BigInteger(value);
        var raw = bi.ToByteArray(isUnsigned: true, isBigEndian: true);
        return LeftPad(raw, 32);
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

    private static byte[] Concat(params byte[][] arrays)
    {
        var total = arrays.Sum(a => a.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }
        return result;
    }
}
