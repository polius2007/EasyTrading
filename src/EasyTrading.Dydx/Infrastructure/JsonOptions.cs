using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyTrading.Dydx.Infrastructure;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for dYdX — case-insensitive (the Indexer API uses
/// camelCase, but some fields drift between revisions) and tolerant of string-encoded numerics
/// (dYdX returns prices and sizes as JSON strings to preserve precision).
/// </summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
