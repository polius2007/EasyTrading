using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Dydx.Infrastructure;

namespace EasyTrading.Dydx.Modules;

/// <summary>dYdX implementation of <see cref="IAccount"/>. Read endpoints don't require signing —
/// the user's <c>dydx1…</c> address is passed as a query parameter and the Indexer responds with
/// public-ish subaccount data. <see cref="ApproveAgentAsync"/> doesn't apply (dYdX v4 has no
/// agent-wallet primitive — trade directly with your subaccount key).</summary>
internal sealed class Account(RestClient rest, DydxClientOptions options) : IAccount
{
    public async Task<AccountState> GetStateAsync(CancellationToken ct = default)
    {
        var creds = RequireCreds();
        var raw = await rest.GetAsync<AddressRaw>($"addresses/{creds.Address}", query: null, ct).ConfigureAwait(false);

        SubaccountRaw? sub = null;
        for (var i = 0; i < raw.Subaccounts.Count; i++)
        {
            if (raw.Subaccounts[i].SubaccountNumber == creds.SubaccountNumber)
            {
                sub = raw.Subaccounts[i];
                break;
            }
        }
        sub ??= raw.Subaccounts.Count > 0 ? raw.Subaccounts[0] : null;
        return sub is null
            ? new AccountState(0m, 0m, 0m, Array.Empty<Position>(), new Dictionary<string, decimal>(), DateTimeOffset.UtcNow)
            : Mapper.MapAccount(sub);
    }

    public async Task<decimal> GetBalanceAsync(string token = "USDC", CancellationToken ct = default)
    {
        var balances = await GetBalancesAsync(ct).ConfigureAwait(false);
        return balances.TryGetValue(token, out var v) ? v : 0m;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetBalancesAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct).ConfigureAwait(false);
        return state.Balances;
    }

    public Task<FeeSchedule> GetFeesAsync(CancellationToken ct = default)
        => Task.FromResult(new FeeSchedule(MakerRate: 0m, TakerRate: 0.0005m, VolumeTier: null, VolumeLast30Days: 0m));

    public Task<Portfolio> GetPortfolioAsync(CancellationToken ct = default)
        => Task.FromResult(new Portfolio(Array.Empty<PortfolioSample>(), Array.Empty<PortfolioSample>()));

    public async Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken ct = default)
    {
        var creds = RequireCreds();
        var raw = await rest.GetAsync<AddressRaw>($"addresses/{creds.Address}", query: null, ct).ConfigureAwait(false);
        return raw.Subaccounts
            .Select(s => new SubAccount(
                Address: s.Address,
                Name:    s.SubaccountNumber.ToString(CultureInfo.InvariantCulture),
                State:   Mapper.MapAccount(s)))
            .ToList();
    }

    public Task<RateLimitInfo> GetRateLimitAsync(CancellationToken ct = default)
        => Task.FromResult(new RateLimitInfo(Used: 0, Limit: 175, WindowResetAt: DateTimeOffset.UtcNow.AddSeconds(10)));

    public Task ApproveAgentAsync(string agentAddress, string? name = null, CancellationToken ct = default)
        => throw new NotSupportedException("dYdX v4 doesn't have an agent-wallet equivalent — trade directly with your subaccount key.");

    public Task<IReadOnlyList<AgentInfo>> GetApprovedAgentsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentInfo>>(Array.Empty<AgentInfo>());

    private DydxCredentials RequireCreds() => options.Credentials
        ?? throw new AuthenticationException(
            "DydxClientOptions.Credentials.Address is required for account / subaccount queries.");
}
