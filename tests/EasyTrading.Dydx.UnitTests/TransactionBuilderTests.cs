using Cosmos.Base.V1Beta1;
using Cosmos.Tx.V1Beta1;
using EasyTrading.Dydx.Infrastructure;
using Google.Protobuf;
using ProtoOrder    = Dydxprotocol.Clob.Order;
using ProtoOrderId  = Dydxprotocol.Clob.OrderId;
using MsgPlaceOrder = Dydxprotocol.Clob.MsgPlaceOrder;
using SubaccountId  = Dydxprotocol.Subaccounts.SubaccountId;

namespace EasyTrading.Dydx.UnitTests;

/// <summary>
/// Verifies the Cosmos transaction-assembly pipeline. We can't test broadcast end-to-end
/// without a funded testnet wallet, so these tests focus on the byte-shape:
/// determinism, signature inclusion, correct envelope structure, and round-trip parsing of
/// the generated <c>TxRaw</c>.
/// </summary>
public sealed class TransactionBuilderTests
{
    private const string TestMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private const string TestChainId = "dydx-testnet-4";

    private static MsgPlaceOrder SamplePlaceOrder(string address) => new()
    {
        Order = new ProtoOrder
        {
            OrderId = new ProtoOrderId
            {
                SubaccountId = new SubaccountId { Owner = address, Number = 0 },
                ClientId     = 42,
                OrderFlags   = 64, // LONG_TERM
                ClobPairId   = 0,  // BTC-USD
            },
            Side             = ProtoOrder.Types.Side.Buy,
            Quantums         = 1_000_000UL,
            Subticks         = 100_000_000UL,
            GoodTilBlockTime = 1_750_000_000U,
            TimeInForce      = ProtoOrder.Types.TimeInForce.Unspecified,
            ReduceOnly       = false,
        },
    };

    [Fact]
    public void BuildAndSign_produces_deterministic_bytes_for_same_inputs()
    {
        var signer = new Signer(TestMnemonic);
        var builder = new TransactionBuilder(signer, TestChainId);
        var msg = SamplePlaceOrder(signer.Address);
        var any = TransactionBuilder.PackAny(msg);

        var bytes1 = builder.BuildAndSign(
            messages: new[] { any },
            accountNumber: 1,
            sequence: 0,
            fee: Array.Empty<Coin>(),
            gasLimit: 1_000_000UL);

        var bytes2 = builder.BuildAndSign(
            messages: new[] { any },
            accountNumber: 1,
            sequence: 0,
            fee: Array.Empty<Coin>(),
            gasLimit: 1_000_000UL);

        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void BuildAndSign_yields_parseable_TxRaw_with_one_signature()
    {
        var signer = new Signer(TestMnemonic);
        var builder = new TransactionBuilder(signer, TestChainId);
        var msg = SamplePlaceOrder(signer.Address);

        var bytes = builder.BuildAndSign(
            messages: new[] { TransactionBuilder.PackAny(msg) },
            accountNumber: 42,
            sequence: 7,
            fee: Array.Empty<Coin>(),
            gasLimit: 1_000_000UL);

        // Round-trip through Google.Protobuf parser to assert wire-format correctness.
        var parsed = TxRaw.Parser.ParseFrom(bytes);
        Assert.NotEmpty(parsed.BodyBytes);
        Assert.NotEmpty(parsed.AuthInfoBytes);
        Assert.Single(parsed.Signatures);
        Assert.Equal(64, parsed.Signatures[0].Length); // raw r∥s Cosmos signature

        // Re-parse body + auth-info to confirm structural integrity.
        var body = TxBody.Parser.ParseFrom(parsed.BodyBytes);
        Assert.Single(body.Messages);
        Assert.Equal("/dydxprotocol.clob.MsgPlaceOrder", body.Messages[0].TypeUrl);

        var authInfo = AuthInfo.Parser.ParseFrom(parsed.AuthInfoBytes);
        Assert.Single(authInfo.SignerInfos);
        Assert.NotNull(authInfo.SignerInfos[0].PublicKey);
        Assert.StartsWith("/cosmos.crypto.secp256k1.PubKey", authInfo.SignerInfos[0].PublicKey!.TypeUrl, StringComparison.Ordinal);
        Assert.Equal(7UL, authInfo.SignerInfos[0].Sequence);
    }

    [Fact]
    public void Different_sequence_produces_different_signature()
    {
        var signer = new Signer(TestMnemonic);
        var builder = new TransactionBuilder(signer, TestChainId);
        var msg = SamplePlaceOrder(signer.Address);

        var s0 = builder.BuildAndSign(new[] { TransactionBuilder.PackAny(msg) }, 1, 0, Array.Empty<Coin>(), 1_000_000UL);
        var s1 = builder.BuildAndSign(new[] { TransactionBuilder.PackAny(msg) }, 1, 1, Array.Empty<Coin>(), 1_000_000UL);

        var p0 = TxRaw.Parser.ParseFrom(s0);
        var p1 = TxRaw.Parser.ParseFrom(s1);
        Assert.NotEqual(p0.Signatures[0].ToByteArray(), p1.Signatures[0].ToByteArray());
    }

    [Fact]
    public void Different_chain_id_produces_different_signature()
    {
        var signer = new Signer(TestMnemonic);
        var b1 = new TransactionBuilder(signer, "dydx-testnet-4");
        var b2 = new TransactionBuilder(signer, "dydx-mainnet-1");
        var msg = SamplePlaceOrder(signer.Address);
        var any = TransactionBuilder.PackAny(msg);

        var s1 = b1.BuildAndSign(new[] { any }, 1, 0, Array.Empty<Coin>(), 1_000_000UL);
        var s2 = b2.BuildAndSign(new[] { any }, 1, 0, Array.Empty<Coin>(), 1_000_000UL);

        var p1 = TxRaw.Parser.ParseFrom(s1);
        var p2 = TxRaw.Parser.ParseFrom(s2);
        Assert.NotEqual(p1.Signatures[0].ToByteArray(), p2.Signatures[0].ToByteArray());
    }

    [Fact]
    public void PackAny_uses_slash_prefix_for_cosmos_type_url()
    {
        var signer = new Signer(TestMnemonic);
        var msg = SamplePlaceOrder(signer.Address);
        var any = TransactionBuilder.PackAny(msg);
        // Cosmos rejects "type.googleapis.com/…" — must be "/<full_type_name>".
        Assert.Equal("/dydxprotocol.clob.MsgPlaceOrder", any.TypeUrl);
    }
}
