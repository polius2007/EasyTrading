using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using Nethereum.Signer;
using Nethereum.Util;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Result of signing a HyperLiquid action — wire-format r/s/v ready to drop into the exchange payload.
/// </summary>
internal readonly record struct HlSignature(string R, string S, int V);

/// <summary>
/// Computes HyperLiquid action hashes and produces EIP-712 ECDSA signatures over them.
/// </summary>
/// <remarks>
/// <para>Two signing flavours are implemented, matching the Python reference SDK
/// (<c>hyperliquid.utils.signing</c>):</para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>L1 (action-signed)</b> — used for <c>order</c>, <c>cancel</c>, <c>modify</c>,
///       <c>updateLeverage</c>, <c>vaultTransfer</c>, <c>scheduleCancel</c>, etc.
///       The action is msgpack-encoded and hashed; a "phantom agent" struct
///       <c>{ source, connectionId }</c> is then signed under the
///       <c>Exchange / chainId = 1337</c> EIP-712 domain.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>User-signed</b> — used for transfers, withdrawals and approvals
///       (<c>usdSend</c>, <c>withdraw3</c>, <c>spotSend</c>, <c>usdClassTransfer</c>,
///       <c>sendAsset</c>, <c>approveAgent</c>, <c>approveBuilderFee</c>).
///       The action itself becomes the EIP-712 message, with domain
///       <c>HyperliquidSignTransaction / chainId = 0x66eee (Arbitrum Sepolia)</c>.
///     </description>
///   </item>
/// </list>
/// </remarks>
internal static class Signer
{
    private static readonly Sha3Keccack Keccak = Sha3Keccack.Current;

    // EIP-712 domain typeHash, shared by both flavours.
    private static readonly byte[] DomainTypeHash =
        Keccak.CalculateHash(Encoding.UTF8.GetBytes(
            "EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)"));

    private static readonly byte[] ZeroAddressPadded = new byte[32];

    // ─── action hash (L1) ────────────────────────────────────────────────────

    /// <summary>
    /// Computes the keccak256 of <c>msgpack(action) ++ nonce(8 BE) ++ vault-byte (++ vault address) ++ (expires-byte ++ expires(8 BE))</c>.
    /// This is the <c>connectionId</c> placed inside the phantom-agent struct.
    /// </summary>
    public static byte[] ActionHash(HlMap action, string? vaultAddress, long nonce, long? expiresAfter)
    {
        using var ms = new MemoryStream();

        var packed = MsgPack.Pack(action);
        ms.Write(packed);

        Span<byte> nonceBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(nonceBytes, nonce);
        ms.Write(nonceBytes);

        if (vaultAddress is null)
        {
            ms.WriteByte(0x00);
        }
        else
        {
            ms.WriteByte(0x01);
            ms.Write(HexToBytes(vaultAddress));
        }

        if (expiresAfter is not null)
        {
            ms.WriteByte(0x00);
            Span<byte> expBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(expBytes, expiresAfter.Value);
            ms.Write(expBytes);
        }

        return Keccak.CalculateHash(ms.ToArray());
    }

    // ─── L1 (action-signed) digest + sign ────────────────────────────────────

    public static HlSignature SignL1Action(
        HlMap action,
        string? vaultAddress,
        long nonce,
        long? expiresAfter,
        bool isMainnet,
        string privateKeyHex)
    {
        var actionHash = ActionHash(action, vaultAddress, nonce, expiresAfter);
        var digest = L1Digest(actionHash, isMainnet);
        return Sign(digest, privateKeyHex);
    }

    private static byte[] L1Digest(byte[] actionHash, bool isMainnet)
    {
        // domain hash: keccak(domainTypeHash ++ keccak("Exchange") ++ keccak("1") ++ uint256(1337) ++ address(0))
        var domainHash = Keccak.CalculateHash(Concat(
            DomainTypeHash,
            Keccak.CalculateHash(Encoding.UTF8.GetBytes("Exchange")),
            Keccak.CalculateHash(Encoding.UTF8.GetBytes("1")),
            Uint256(1337),
            ZeroAddressPadded));

        // Agent struct hash: keccak(typeHash ++ keccak(source) ++ connectionId)
        var agentTypeHash = Keccak.CalculateHash(
            Encoding.UTF8.GetBytes("Agent(string source,bytes32 connectionId)"));

        var sourceHash = Keccak.CalculateHash(
            Encoding.UTF8.GetBytes(isMainnet ? "a" : "b"));

        var structHash = Keccak.CalculateHash(Concat(
            agentTypeHash,
            sourceHash,
            actionHash));

        // final digest: keccak(0x1901 ++ domainHash ++ structHash)
        return Keccak.CalculateHash(Concat(new byte[] { 0x19, 0x01 }, domainHash, structHash));
    }

    // ─── User-signed digest + sign ───────────────────────────────────────────

    /// <summary>
    /// Sign a user-action (transfer, withdrawal, approval). The <paramref name="message"/> is
    /// expected to already contain <c>hyperliquidChain</c> and <c>signatureChainId</c> entries.
    /// </summary>
    public static HlSignature SignUserAction(
        HlMap message,
        string primaryType,
        IReadOnlyList<(string Name, string Type)> typeSchema,
        string privateKeyHex)
    {
        var digest = UserSignedDigest(message, primaryType, typeSchema);
        return Sign(digest, privateKeyHex);
    }

    private static byte[] UserSignedDigest(
        HlMap message,
        string primaryType,
        IReadOnlyList<(string Name, string Type)> typeSchema)
    {
        // chainId is the integer value of message["signatureChainId"] (a 0x-hex string).
        if (!message.TryGetValue("signatureChainId", out var sigChainObj) || sigChainObj is not string sigChain)
            throw new InvalidOperationException("User-signed message is missing 'signatureChainId'.");

        var chainId = HexStringToBigInteger(sigChain);

        // domain hash: keccak(domainTypeHash ++ keccak("HyperliquidSignTransaction") ++ keccak("1") ++ uint256(chainId) ++ address(0))
        var domainHash = Keccak.CalculateHash(Concat(
            DomainTypeHash,
            Keccak.CalculateHash(Encoding.UTF8.GetBytes("HyperliquidSignTransaction")),
            Keccak.CalculateHash(Encoding.UTF8.GetBytes("1")),
            Uint256(chainId),
            ZeroAddressPadded));

        // typeHash from the schema: "PrimaryType(type1 name1,type2 name2,...)"
        var typeString = primaryType + "("
            + string.Join(",", typeSchema.Select(f => $"{f.Type} {f.Name}"))
            + ")";
        var typeHash = Keccak.CalculateHash(Encoding.UTF8.GetBytes(typeString));

        // struct hash: keccak(typeHash ++ ABI-encoded field values)
        using var fieldStream = new MemoryStream();
        fieldStream.Write(typeHash);
        foreach (var (name, type) in typeSchema)
        {
            if (!message.TryGetValue(name, out var rawValue))
                throw new InvalidOperationException($"User-signed message is missing field '{name}'.");
            fieldStream.Write(EncodeAbiField(type, rawValue));
        }
        var structHash = Keccak.CalculateHash(fieldStream.ToArray());

        return Keccak.CalculateHash(Concat(new byte[] { 0x19, 0x01 }, domainHash, structHash));
    }

    private static byte[] EncodeAbiField(string solType, object? value)
    {
        if (solType == "string")
        {
            var s = value as string ?? throw new InvalidOperationException("Expected string value.");
            return Keccak.CalculateHash(Encoding.UTF8.GetBytes(s));
        }
        if (solType == "address")
        {
            var addr = value as string ?? throw new InvalidOperationException("Expected address string.");
            return AddressTo32(addr);
        }
        if (solType == "bytes32")
        {
            if (value is byte[] b32 && b32.Length == 32) return b32;
            throw new InvalidOperationException("Expected 32-byte array for bytes32.");
        }
        if (solType == "bool")
        {
            var b = value is bool bb ? bb : throw new InvalidOperationException("Expected bool value.");
            return Uint256(b ? 1 : 0);
        }
        if (solType.StartsWith("uint", StringComparison.Ordinal))
        {
            return Uint256(ToBigInteger(value));
        }
        if (solType.StartsWith("int", StringComparison.Ordinal))
        {
            // ABI-encodes signed ints as two's-complement 32 bytes; HL doesn't currently use signed ints
            // in user-signed payloads, so we keep this simple.
            return Uint256(ToBigInteger(value));
        }
        throw new NotSupportedException($"EIP-712 type '{solType}' is not supported by Signer.");
    }

    // ─── ECDSA sign ──────────────────────────────────────────────────────────

    private static HlSignature Sign(byte[] digest, string privateKeyHex)
    {
        var key = new EthECKey(privateKeyHex);
        var ecdsa = key.SignAndCalculateV(digest);

        // r, s come back as variable-length big-endian arrays; pad-left to 32 bytes.
        var r = LeftPad(ecdsa.R, 32);
        var s = LeftPad(ecdsa.S, 32);
        var v = ecdsa.V is { Length: > 0 } vArr ? (int)vArr[0] : 27;

        return new HlSignature(
            R: "0x" + Convert.ToHexString(r).ToLowerInvariant(),
            S: "0x" + Convert.ToHexString(s).ToLowerInvariant(),
            V: v);
    }

    // ─── ABI primitives ──────────────────────────────────────────────────────

    private static byte[] Uint256(long value) => Uint256(new BigInteger(value));

    private static byte[] Uint256(BigInteger value)
    {
        if (value.Sign < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "uint256 cannot encode negative values.");

        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        return LeftPad(raw, 32);
    }

    private static byte[] AddressTo32(string hexAddress)
    {
        var raw = HexToBytes(hexAddress);
        if (raw.Length != 20)
            throw new ArgumentException($"Expected 20-byte EVM address, got {raw.Length} bytes.", nameof(hexAddress));
        return LeftPad(raw, 32);
    }

    private static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            throw new ArgumentException("Hex string is empty.", nameof(hex));
        var trimmed = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (trimmed.Length % 2 != 0)
            throw new ArgumentException($"Hex string has odd length: '{hex}'.", nameof(hex));
        return Convert.FromHexString(trimmed);
    }

    private static byte[] LeftPad(byte[] source, int totalLength)
    {
        if (source.Length == totalLength)
            return source;
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

    private static BigInteger ToBigInteger(object? value) => value switch
    {
        null => BigInteger.Zero,
        BigInteger bi => bi,
        long l => new BigInteger(l),
        ulong ul => new BigInteger(ul),
        int i => new BigInteger(i),
        uint ui => new BigInteger(ui),
        short sh => new BigInteger(sh),
        ushort ush => new BigInteger(ush),
        byte b => new BigInteger(b),
        sbyte sb => new BigInteger(sb),
        string s => BigInteger.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"Cannot convert {value.GetType()} to BigInteger.")
    };

    private static BigInteger HexStringToBigInteger(string hex)
    {
        var trimmed = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        // BigInteger.Parse expects a leading 0 for unsigned positive parsing.
        return BigInteger.Parse("0" + trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
