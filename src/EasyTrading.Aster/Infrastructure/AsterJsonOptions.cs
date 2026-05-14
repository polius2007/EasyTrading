using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyTrading.Aster.Infrastructure;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for Aster — case-sensitive (Aster's JSON uses lower
/// camelCase consistently) and tolerant of string-encoded numerics (Aster returns prices and sizes
/// as JSON strings to preserve precision, same as Binance and HyperLiquid).
/// </summary>
internal static class AsterJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = null, // explicit JsonPropertyName attributes on every DTO
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
