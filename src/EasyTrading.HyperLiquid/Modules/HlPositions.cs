using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IPositions"/>. Read methods use clearinghouseState; write methods land in Phase 3.</summary>
internal sealed class HlPositions(HlInfoClient info, HyperLiquidClientOptions options) : IPositions
{
    public async Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<ClearinghouseStateRaw>(new { type = "clearinghouseState", user }, ct).ConfigureAwait(false);
        return raw.AssetPositions.Select(ap => HlMapper.Map(ap.Position)).ToList();
    }

    public async Task<Position?> GetAsync(string symbol, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.FirstOrDefault(p => p.Symbol == symbol);
    }

    public Task SetLeverageAsync(string symbol, int leverage, MarginMode mode, CancellationToken ct = default)
        => Task.FromException(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));

    public Task AddMarginAsync(string symbol, decimal amount, CancellationToken ct = default)
        => Task.FromException(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));

    public Task ReduceMarginAsync(string symbol, decimal amount, CancellationToken ct = default)
        => Task.FromException(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));

    public Task<PlaceOrderResult> CloseAsync(string symbol, CancellationToken ct = default)
        => Task.FromException<PlaceOrderResult>(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for position queries.");
}
