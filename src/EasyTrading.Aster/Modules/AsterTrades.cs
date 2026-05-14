using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.Modules;

/// <summary>Aster implementation of <see cref="ITrades"/> backed by <c>/fapi/v3/userTrades</c>.</summary>
internal sealed class AsterTrades(AsterRestClient rest) : ITrades
{
    public async Task<IReadOnlyList<Fill>> GetMyFillsAsync(
        string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        // Aster requires `symbol` on /fapi/v3/userTrades; without one we can't fetch.
        if (symbol is null)
            throw new InvalidOrderException(
                "Aster's /fapi/v3/userTrades requires a symbol parameter. Pass `symbol` to GetMyFillsAsync.");

        var p = new Dictionary<string, string>
        {
            ["symbol"] = symbol,
            ["limit"]  = "500",
        };
        if (from is not null) p["startTime"] = from.Value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        if (to   is not null) p["endTime"]   = to.Value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        var raw = await rest.SendSignedAsync<List<UserTradeRaw>>(HttpMethod.Get, "/fapi/v3/userTrades", p, ct).ConfigureAwait(false);
        return raw.Select(AsterMapper.MapFill).ToList();
    }

    public async Task<IReadOnlyList<Fill>> GetMyFillsByOrderAsync(long orderId, CancellationToken ct = default)
    {
        // Aster's userTrades supports `orderId` filter but still requires `symbol`. Without a
        // symbol hint we'd need to scan every market — keep this an explicit two-call flow for
        // callers who have the symbol.
        throw new NotSupportedException(
            "Aster requires the symbol when querying fills by order. Use GetMyFillsAsync(symbol) and filter by OrderId, or call /fapi/v3/userTrades directly with both `symbol` and `orderId`.");
    }
}
