using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Byte-level checks for our msgpack encoder. The encoded bytes feed straight into the
/// keccak256 that produces the L1 action hash, so any deviation from the canonical msgpack
/// format would silently corrupt signatures.
/// </summary>
public sealed class HlMsgPackTests
{
    [Fact]
    public void Encodes_fixstr()
    {
        var bytes = HlMsgPack.Pack("type");
        // 0xa4 = fixstr, length 4; followed by the UTF-8 bytes of "type".
        Assert.Equal(new byte[] { 0xa4, 0x74, 0x79, 0x70, 0x65 }, bytes);
    }

    [Fact]
    public void Encodes_fixmap_with_string_value()
    {
        var map = new HlMap().Add("type", "order");
        var bytes = HlMsgPack.Pack(map);
        // 0x81 = fixmap with 1 entry; "type" key (fixstr 4); "order" value (fixstr 5).
        Assert.Equal(
            new byte[] { 0x81, 0xa4, 0x74, 0x79, 0x70, 0x65, 0xa5, 0x6f, 0x72, 0x64, 0x65, 0x72 },
            bytes);
    }

    [Fact]
    public void Preserves_map_insertion_order()
    {
        // HyperLiquid's signing depends on field order, so the encoder must respect the
        // order keys were added — not sort or hash them.
        var a = HlMsgPack.Pack(new HlMap().Add("a", 1).Add("b", 2));
        var b = HlMsgPack.Pack(new HlMap().Add("b", 2).Add("a", 1));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Encodes_booleans_and_nil()
    {
        Assert.Equal(new byte[] { 0xc3 }, HlMsgPack.Pack(true));
        Assert.Equal(new byte[] { 0xc2 }, HlMsgPack.Pack(false));
        Assert.Equal(new byte[] { 0xc0 }, HlMsgPack.Pack(null));
    }

    [Fact]
    public void Encodes_positive_fixint_boundary()
    {
        Assert.Equal(new byte[] { 0x00 }, HlMsgPack.Pack(0));
        Assert.Equal(new byte[] { 0x7f }, HlMsgPack.Pack(127));
    }

    [Fact]
    public void Encodes_uint8_above_fixint_range()
    {
        // 128 doesn't fit in positive fixint → uint8 (0xcc) prefix.
        Assert.Equal(new byte[] { 0xcc, 0x80 }, HlMsgPack.Pack(128));
        Assert.Equal(new byte[] { 0xcc, 0xff }, HlMsgPack.Pack(255));
    }

    [Fact]
    public void Encodes_uint16_and_uint32_at_boundaries()
    {
        // 256 → uint16 (0xcd) + 0x0100
        Assert.Equal(new byte[] { 0xcd, 0x01, 0x00 }, HlMsgPack.Pack(256));
        // 65536 → uint32 (0xce) + 0x00010000
        Assert.Equal(new byte[] { 0xce, 0x00, 0x01, 0x00, 0x00 }, HlMsgPack.Pack(65536));
    }

    [Fact]
    public void Encodes_negative_fixint_and_int8()
    {
        // -1 fits in negative fixint (5-bit) → 0xff
        Assert.Equal(new byte[] { 0xff }, HlMsgPack.Pack(-1));
        // -32 is the lower bound of negative fixint → 0xe0
        Assert.Equal(new byte[] { 0xe0 }, HlMsgPack.Pack(-32));
        // -33 needs int8 → 0xd0 0xdf
        Assert.Equal(new byte[] { 0xd0, 0xdf }, HlMsgPack.Pack(-33));
    }

    [Fact]
    public void Encodes_fixarray()
    {
        var bytes = HlMsgPack.Pack(new object?[] { 1, true, "x" });
        // 0x93 = fixarray with 3 items; 0x01 = int 1; 0xc3 = true; 0xa1 "x" = fixstr len 1
        Assert.Equal(new byte[] { 0x93, 0x01, 0xc3, 0xa1, 0x78 }, bytes);
    }

    [Fact]
    public void Round_trips_an_order_action_shape()
    {
        // Shape of a real HL order action — exercises maps inside maps, arrays of maps, booleans, ints, strings.
        var orderWire = new HlMap()
            .Add("a", 0)
            .Add("b", true)
            .Add("p", "60000")
            .Add("s", "0.01")
            .Add("r", false)
            .Add("t", new HlMap().Add("limit", new HlMap().Add("tif", "Gtc")));

        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", new object[] { orderWire })
            .Add("grouping", "na");

        var bytes = HlMsgPack.Pack(action);

        // We don't pin every byte — the deeper assertion is that this encodes without throwing and
        // produces a non-trivial blob. Determinism is tested separately.
        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length > 30, $"Encoded action looks too short ({bytes.Length} bytes).");
    }
}
