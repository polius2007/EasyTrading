using Cosmos.Tx.V1Beta1;
using Cosmos.Base.V1Beta1;
using Cosmos.Tx.Signing.V1Beta1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Cryptoxsecp = Cosmos.Crypto.Secp256K1;

namespace EasyTrading.Dydx.Infrastructure;

/// <summary>
/// Builds and signs Cosmos SDK transactions for the dYdX v4 chain. Wraps the <c>Signer</c>
/// with the boilerplate of producing a <c>TxRaw</c>: assemble <see cref="TxBody"/> + <see cref="AuthInfo"/>,
/// sign the <see cref="SignDoc"/>, and pack everything into the broadcast envelope.
/// </summary>
/// <remarks>
/// <para>Flow per Cosmos SDK reference (SIGN_MODE_DIRECT):</para>
/// <list type="number">
///   <item><description>Wrap each application message in <see cref="Any"/> (typeUrl + serialized bytes).</description></item>
///   <item><description>Build <see cref="TxBody"/> from the messages + memo + optional timeout-height.</description></item>
///   <item><description>Build <see cref="AuthInfo"/> with a single <see cref="SignerInfo"/> (pubkey + sequence + SIGN_MODE_DIRECT) + a <see cref="Fee"/>.</description></item>
///   <item><description>Build <see cref="SignDoc"/> with the serialized body + auth-info bytes + chain id + account number.</description></item>
///   <item><description>Sign SHA-256(SignDoc.ToByteArray()) with the signer; signature is 64 bytes <c>r ‖ s</c>.</description></item>
///   <item><description>Assemble <see cref="TxRaw"/> = body_bytes + auth_info_bytes + [signature].</description></item>
/// </list>
/// </remarks>
internal sealed class TransactionBuilder
{
    private readonly Signer _signer;
    private readonly string _chainId;

    public TransactionBuilder(Signer signer, string chainId)
    {
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentException.ThrowIfNullOrEmpty(chainId);
        _signer = signer;
        _chainId = chainId;
    }

    /// <summary>
    /// Build, sign, and return the raw <c>TxRaw</c> bytes ready for <c>POST /cosmos/tx/v1beta1/txs</c>.
    /// </summary>
    /// <param name="messages">Application messages to include. Each must be a generated protobuf
    /// message — caller wraps with <c>Any.Pack(msg)</c> via <see cref="PackAny"/>.</param>
    /// <param name="accountNumber">Account number from <c>/cosmos/auth/v1beta1/accounts/{address}</c>.</param>
    /// <param name="sequence">Current sequence (a.k.a. nonce) for the account.</param>
    /// <param name="fee">Transaction fee.</param>
    /// <param name="gasLimit">Gas limit.</param>
    /// <param name="memo">Optional memo string (default empty).</param>
    /// <param name="timeoutHeight">Optional block-height after which the tx is invalid (default 0 = no timeout).</param>
    public byte[] BuildAndSign(
        IEnumerable<Any> messages,
        ulong accountNumber,
        ulong sequence,
        IEnumerable<Coin> fee,
        ulong gasLimit,
        string memo = "",
        ulong timeoutHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // 1. TxBody
        var body = new TxBody
        {
            Memo = memo,
            TimeoutHeight = timeoutHeight,
        };
        foreach (var msg in messages)
            body.Messages.Add(msg);

        // 2. AuthInfo (one signer)
        var pubKeyMsg = new Cryptoxsecp.PubKey { Key = ByteString.CopyFrom(_signer.CompressedPublicKey) };
        var pubKeyAny = Any.Pack(pubKeyMsg, "/");
        // Any.Pack(msg, "/") produces type_url = "/<full_proto_name>"; default would prepend
        // "type.googleapis.com/" which Cosmos rejects.

        var signerInfo = new SignerInfo
        {
            PublicKey = pubKeyAny,
            ModeInfo  = new ModeInfo { Single = new ModeInfo.Types.Single { Mode = SignMode.Direct } },
            Sequence  = sequence,
        };

        var feeMsg = new Fee { GasLimit = gasLimit };
        foreach (var c in fee) feeMsg.Amount.Add(c);

        var authInfo = new AuthInfo { Fee = feeMsg };
        authInfo.SignerInfos.Add(signerInfo);

        // 3. SignDoc
        var bodyBytes = body.ToByteArray();
        var authBytes = authInfo.ToByteArray();
        var signDoc = new SignDoc
        {
            BodyBytes     = ByteString.CopyFrom(bodyBytes),
            AuthInfoBytes = ByteString.CopyFrom(authBytes),
            ChainId       = _chainId,
            AccountNumber = accountNumber,
        };

        // 4. Sign SHA-256(SignDoc)
        var signature = _signer.SignMessage(signDoc.ToByteArray());

        // 5. TxRaw
        var txRaw = new TxRaw
        {
            BodyBytes     = ByteString.CopyFrom(bodyBytes),
            AuthInfoBytes = ByteString.CopyFrom(authBytes),
        };
        txRaw.Signatures.Add(ByteString.CopyFrom(signature));

        return txRaw.ToByteArray();
    }

    /// <summary>
    /// Convenience: pack an application message into <see cref="Any"/> with the Cosmos-correct
    /// <c>/&lt;full_type_name&gt;</c> type URL prefix.
    /// </summary>
    public static Any PackAny(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Any.Pack(message, "/");
    }
}
