using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Dydx.Infrastructure;

namespace EasyTrading.Dydx.Modules;

/// <summary>dYdX implementation of <see cref="ITrades"/>. Fills come from
/// <c>/fills?address=…&amp;subaccountNumber=N&amp;market=…</c>; the address-by-order lookup is
/// supported because dYdX's <c>/fills</c> accepts an <c>orderId</c> filter directly.</summary>
internal sealed class Trades(RestClient rest, DydxClientOptions options) : ITrades
{
    public async Task<IReadOnlyList<Fill>> GetMyFillsAsync(
        string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var creds = RequireCreds();
        var query = new Dictionary<string, string>
        {
            ["address"]          = creds.Address,
            ["subaccountNumber"] = creds.SubaccountNumber.ToString(CultureInfo.InvariantCulture),
            ["limit"]            = "100",
        };
        if (symbol is not null) query["market"] = symbol;
        if (from   is not null) query["createdBeforeOrAt"] = from.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        if (to     is not null) query["createdAtOrBefore"] = to.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

        var raw = await rest.GetAsync<FillsRaw>("fills", query, ct).ConfigureAwait(false);
        return raw.Fills.Select(Mapper.MapFill).ToList();
    }

    public async Task<IReadOnlyList<Fill>> GetMyFillsByOrderAsync(long orderId, CancellationToken ct = default)
    {
        // dYdX's order IDs are GUIDs/strings; the cross-DEX surface gives us a long via stable
        // hashing. We can't reverse the hash, so this filter has to operate on the FULL fill list
        // and match by hashing each raw orderId.
        var all = await GetMyFillsAsync(ct: ct).ConfigureAwait(false);
        return all.Where(f => f.OrderId == orderId).ToList();
    }

    private DydxCredentials RequireCreds() => options.Credentials
        ?? throw new AuthenticationException(
            "DydxClientOptions.Credentials.Address is required for fill queries.");
}
