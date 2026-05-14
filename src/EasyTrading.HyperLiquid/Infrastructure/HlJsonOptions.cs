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
/// </remarks>
internal static class HlJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
