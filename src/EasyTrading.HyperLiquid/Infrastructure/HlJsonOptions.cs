using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for talking to HyperLiquid.
/// </summary>
/// <remarks>
/// <para>HyperLiquid returns numeric values (prices, sizes, fees) as JSON strings to avoid
/// floating-point precision loss. <see cref="JsonNumberHandling.AllowReadingFromString"/> lets
/// us deserialize them straight into <c>decimal</c> properties.</para>
/// <para>Case-insensitive matching is disabled because the candle endpoint returns both
/// <c>"t"</c> (open time) and <c>"T"</c> (close time) — these must be distinguished exactly.</para>
/// <para>An <see cref="HlMapJsonConverter"/> is registered so we can pass <see cref="HlMap"/>
/// instances directly to <see cref="JsonSerializer.Serialize{TValue}(TValue, JsonSerializerOptions?)"/>
/// when building Exchange-endpoint payloads.</para>
/// </remarks>
internal static class HlJsonOptions
{
    public static readonly JsonSerializerOptions Default = Build();

    private static JsonSerializerOptions Build()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        opts.Converters.Add(new HlMapJsonConverter());
        return opts;
    }
}

/// <summary>
/// Serialises <see cref="HlMap"/> preserving insertion order. Deserialisation is not supported —
/// HlMap is only used to build outgoing payloads.
/// </summary>
internal sealed class HlMapJsonConverter : JsonConverter<HlMap>
{
    public override HlMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("HlMap is serialize-only.");

    public override void Write(Utf8JsonWriter writer, HlMap value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kv in value.Items)
        {
            writer.WritePropertyName(kv.Key);
            WriteValue(writer, kv.Value, options);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case uint ui:
                writer.WriteNumberValue(ui);
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            case HlMap nested:
                JsonSerializer.Serialize(writer, nested, options);
                break;
            case IEnumerable<object?> list:
                writer.WriteStartArray();
                foreach (var item in list)
                    WriteValue(writer, item, options);
                writer.WriteEndArray();
                break;
            default:
                // Last-resort: delegate to the system serializer (handles records, anonymous types, etc.)
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
                break;
        }
    }
}
