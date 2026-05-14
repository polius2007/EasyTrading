using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="ITrades"/> backed by the Info endpoint.</summary>
internal sealed class HlTrades(HlInfoClient info, HyperLiquidClientOptions options) : ITrades
{
    public async Task<IReadOnlyList<Fill>> GetMyFillsAsync(string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var user = RequireUser();

        List<UserFillRaw> raw;
        if (from is not null)
        {
            raw = await info.PostAsync<List<UserFillRaw>>(new
            {
                type = "userFillsByTime",
                user,
                startTime = from.Value.ToUnixTimeMilliseconds(),
                endTime = (to ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds(),
            }, ct).ConfigureAwait(false);
        }
        else
        {
            raw = await info.PostAsync<List<UserFillRaw>>(new { type = "userFills", user }, ct).ConfigureAwait(false);
        }

        IEnumerable<Fill> fills = raw.Select(HlMapper.Map);
        if (symbol is not null) fills = fills.Where(f => f.Symbol == symbol);
        return fills.ToList();
    }

    public async Task<IReadOnlyList<Fill>> GetMyFillsByOrderAsync(long orderId, CancellationToken ct = default)
    {
        // HL doesn't have a "fills-by-order" Info request — filter from userFills.
        var all = await GetMyFillsAsync(ct: ct).ConfigureAwait(false);
        return all.Where(f => f.OrderId == orderId).ToList();
    }

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for fill queries.");
}
