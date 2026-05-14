using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Ordered string-keyed map used to build HyperLiquid action payloads.
/// HyperLiquid's reference implementations rely on insertion-order field encoding
/// when msgpack-packing the action, so we cannot use a plain <see cref="Dictionary{TKey,TValue}"/>
/// (whose ordering is implementation-defined on .NET).
/// </summary>
internal sealed class HlMap : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly List<KeyValuePair<string, object?>> _items = new();

    public int Count => _items.Count;

    public IReadOnlyList<KeyValuePair<string, object?>> Items => _items;

    public HlMap Add(string key, object? value)
    {
        _items.Add(new KeyValuePair<string, object?>(key, value));
        return this;
    }

    public bool TryGetValue(string key, out object? value)
    {
        foreach (var kv in _items)
        {
            if (kv.Key == key) { value = kv.Value; return true; }
        }
        value = null;
        return false;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

/// <summary>
/// Minimal msgpack encoder for the subset of types HyperLiquid action payloads contain:
/// ordered maps (<see cref="HlMap"/>), arrays, signed/unsigned integers, booleans, strings, byte blobs.
/// Matches the byte output of <c>msgpack.packb()</c> from the Python reference SDK for the same input.
/// </summary>
/// <remarks>
/// We deliberately don't depend on a general-purpose msgpack library: HyperLiquid relies on exact byte
/// equality of the encoded action for signature verification, so we keep this encoder small and audited
/// in one place.
/// </remarks>
internal static class HlMsgPack
{
    public static byte[] Pack(object? root)
    {
        using var stream = new MemoryStream();
        WriteValue(stream, root);
        return stream.ToArray();
    }

    private static void WriteValue(MemoryStream s, object? value)
    {
        switch (value)
        {
            case null:                       s.WriteByte(0xc0); break;
            case bool b:                     s.WriteByte(b ? (byte)0xc3 : (byte)0xc2); break;
            case sbyte i8:                   WriteInt(s, i8); break;
            case byte u8:                    WriteInt(s, u8); break;
            case short i16:                  WriteInt(s, i16); break;
            case ushort u16:                 WriteInt(s, u16); break;
            case int i32:                    WriteInt(s, i32); break;
            case uint u32:                   WriteInt(s, u32); break;
            case long i64:                   WriteInt(s, i64); break;
            case ulong u64:                  WriteUInt64(s, u64); break;
            case string str:                 WriteString(s, str); break;
            case byte[] bytes:               WriteBin(s, bytes); break;
            case HlMap map:                  WriteMap(s, map); break;
            case IEnumerable<object?> list:  WriteArray(s, list); break;
            default:
                throw new NotSupportedException(
                    $"HlMsgPack does not support encoding values of type {value.GetType().FullName}.");
        }
    }

    private static void WriteString(MemoryStream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var len = bytes.Length;

        if (len <= 31)
        {
            s.WriteByte((byte)(0xa0 | len));
        }
        else if (len <= byte.MaxValue)
        {
            s.WriteByte(0xd9);
            s.WriteByte((byte)len);
        }
        else if (len <= ushort.MaxValue)
        {
            s.WriteByte(0xda);
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)len);
            s.Write(buf);
        }
        else
        {
            s.WriteByte(0xdb);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)len);
            s.Write(buf);
        }

        s.Write(bytes);
    }

    private static void WriteBin(MemoryStream s, byte[] bytes)
    {
        var len = bytes.Length;
        if (len <= byte.MaxValue)
        {
            s.WriteByte(0xc4);
            s.WriteByte((byte)len);
        }
        else if (len <= ushort.MaxValue)
        {
            s.WriteByte(0xc5);
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)len);
            s.Write(buf);
        }
        else
        {
            s.WriteByte(0xc6);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)len);
            s.Write(buf);
        }
        s.Write(bytes);
    }

    private static void WriteMap(MemoryStream s, HlMap map)
    {
        var n = map.Count;
        if (n <= 15)
        {
            s.WriteByte((byte)(0x80 | n));
        }
        else if (n <= ushort.MaxValue)
        {
            s.WriteByte(0xde);
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)n);
            s.Write(buf);
        }
        else
        {
            s.WriteByte(0xdf);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)n);
            s.Write(buf);
        }

        foreach (var kv in map.Items)
        {
            WriteString(s, kv.Key);
            WriteValue(s, kv.Value);
        }
    }

    private static void WriteArray(MemoryStream s, IEnumerable<object?> items)
    {
        var list = items as IReadOnlyList<object?> ?? items.ToList();
        var n = list.Count;

        if (n <= 15)
        {
            s.WriteByte((byte)(0x90 | n));
        }
        else if (n <= ushort.MaxValue)
        {
            s.WriteByte(0xdc);
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)n);
            s.Write(buf);
        }
        else
        {
            s.WriteByte(0xdd);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)n);
            s.Write(buf);
        }

        foreach (var item in list)
            WriteValue(s, item);
    }

    private static void WriteInt(MemoryStream s, long n)
    {
        if (n >= 0)
        {
            WriteUInt64(s, (ulong)n);
            return;
        }

        if (n >= -32)
        {
            s.WriteByte((byte)((sbyte)n));
            return;
        }

        if (n >= sbyte.MinValue)
        {
            s.WriteByte(0xd0);
            s.WriteByte((byte)(sbyte)n);
            return;
        }

        if (n >= short.MinValue)
        {
            s.WriteByte(0xd1);
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(buf, (short)n);
            s.Write(buf);
            return;
        }

        if (n >= int.MinValue)
        {
            s.WriteByte(0xd2);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buf, (int)n);
            s.Write(buf);
            return;
        }

        s.WriteByte(0xd3);
        Span<byte> bufL = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bufL, n);
        s.Write(bufL);
    }

    private static void WriteUInt64(MemoryStream s, ulong n)
    {
        if (n <= 0x7f)
        {
            s.WriteByte((byte)n);
        }
        else if (n <= byte.MaxValue)
        {
            s.WriteByte(0xcc);
            s.WriteByte((byte)n);
        }
        else if (n <= ushort.MaxValue)
        {
            s.WriteByte(0xcd);
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)n);
            s.Write(buf);
        }
        else if (n <= uint.MaxValue)
        {
            s.WriteByte(0xce);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)n);
            s.Write(buf);
        }
        else
        {
            s.WriteByte(0xcf);
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(buf, n);
            s.Write(buf);
        }
    }
}
