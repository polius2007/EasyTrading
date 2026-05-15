using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Dydx.Infrastructure;

namespace EasyTrading.Dydx.Modules;

/// <summary>dYdX implementation of <see cref="IPositions"/>. Reads come from
/// <c>/perpetualPositions?address=…&amp;subaccountNumber=N</c>. Cross-margin leverage on dYdX v4
/// is account-wide (no per-symbol setting); explicit margin add / remove writes are on the
/// follow-up roadmap and currently throw <see cref="NotSupportedException"/> with guidance.</summary>
internal sealed class Positions(RestClient rest, DydxClientOptions options) : IPositions
{
    public async Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default)
    {
        var creds = RequireCreds();
        var query = new Dictionary<string, string>
        {
            ["address"]          = creds.Address,
            ["subaccountNumber"] = creds.SubaccountNumber.ToString(CultureInfo.InvariantCulture),
            ["status"]           = "OPEN",
        };
        var raw = await rest.GetAsync<PerpetualPositionsRaw>("perpetualPositions", query, ct).ConfigureAwait(false);
        return raw.Positions.Select(Mapper.MapPosition).ToList();
    }

    public async Task<Position?> GetAsync(string symbol, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.FirstOrDefault(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
    }

    public Task SetLeverageAsync(string symbol, int leverage, MarginMode marginMode, CancellationToken ct = default) => throw new NotImplementedException(Phase.PositionWrite);
    public Task SetMarginModeAsync(string symbol, MarginMode marginMode, CancellationToken ct = default) => throw new NotImplementedException(Phase.PositionWrite);
    public Task AddMarginAsync(string symbol, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.PositionWrite);
    public Task ReduceMarginAsync(string symbol, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.PositionWrite);
    public Task<PlaceOrderResult> CloseAsync(string symbol, CancellationToken ct = default) => throw new NotImplementedException(Phase.PositionWrite);

    private DydxCredentials RequireCreds() => options.Credentials
        ?? throw new AuthenticationException(
            "DydxClientOptions.Credentials.Address is required for position queries.");
}
