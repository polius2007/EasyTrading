using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.Modules;

/// <summary>Aster implementation of <see cref="IAccount"/>.</summary>
internal sealed class AsterAccount(AsterRestClient rest) : IAccount
{
    public async Task<AccountState> GetStateAsync(CancellationToken ct = default)
    {
        var raw = await rest.SendSignedAsync<AccountInfoRaw>(HttpMethod.Get, "/fapi/v3/account", new Dictionary<string, string>(), ct).ConfigureAwait(false);
        return AsterMapper.MapAccount(raw);
    }

    public async Task<decimal> GetBalanceAsync(string token = "USDC", CancellationToken ct = default)
    {
        // Aster's futures wallet is USDT-denominated by default; the contract param is named `asset`
        // in the response but the user-facing default is USDC for cross-DEX consistency. We look up
        // by case-insensitive match.
        var balances = await GetBalancesAsync(ct).ConfigureAwait(false);
        return balances.TryGetValue(token, out var v) ? v : 0m;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetBalancesAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct).ConfigureAwait(false);
        return state.Balances;
    }

    public Task<FeeSchedule> GetFeesAsync(CancellationToken ct = default)
    {
        // Aster doesn't expose a per-account commission-rate endpoint in V3; the fee schedule is a
        // public table. Return a sentinel until/unless that endpoint shows up. Callers can pass
        // their negotiated rate through their own config.
        return Task.FromResult(new FeeSchedule(MakerRate: 0.0002m, TakerRate: 0.0005m, VolumeTier: null, VolumeLast30Days: 0m));
    }

    public Task<Portfolio> GetPortfolioAsync(CancellationToken ct = default)
        => Task.FromResult(new Portfolio(Array.Empty<PortfolioSample>(), Array.Empty<PortfolioSample>()));

    public Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SubAccount>>(Array.Empty<SubAccount>());

    public async Task<RateLimitInfo> GetRateLimitAsync(CancellationToken ct = default)
    {
        // Aster carries the IP rate-limit budget in response headers (X-MBX-USED-WEIGHT-*) rather
        // than a dedicated endpoint. We expose a synthetic snapshot — accurate Used count would
        // require plumbing header capture through the REST client, which is a Phase 6.4 polish.
        await Task.CompletedTask.ConfigureAwait(false);
        return new RateLimitInfo(Used: 0, Limit: 2400, WindowResetAt: DateTimeOffset.UtcNow.AddMinutes(1));
    }

    public async Task ApproveAgentAsync(string agentAddress, string? name = null, CancellationToken ct = default)
    {
        // Aster's agent / signer registration happens via the web UI (/api-wallet), then
        // POST /fapi/v3/registerSignerAndApprove confirms on-chain. The exact param set may
        // diverge between testnet builds — we send the documented shape.
        var p = new Dictionary<string, string>
        {
            ["signerAddress"] = agentAddress,
        };
        if (name is not null) p["name"] = name;

        await rest.SendSignedRawAsync(HttpMethod.Post, "/fapi/v3/registerSignerAndApprove", p, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AgentInfo>> GetApprovedAgentsAsync(CancellationToken ct = default)
    {
        // No public endpoint to list approved agents per master account. Phase 6.4 polish if Aster
        // adds one.
        return Task.FromResult<IReadOnlyList<AgentInfo>>(Array.Empty<AgentInfo>());
    }
}
