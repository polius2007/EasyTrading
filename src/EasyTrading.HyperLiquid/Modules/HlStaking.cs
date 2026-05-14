using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid.Infrastructure;
using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IStaking"/>. Reads are wired up; writes land in Phase 3.</summary>
internal sealed class HlStaking(HlInfoClient info, HyperLiquidClientOptions options) : IStaking
{
    public async Task<IReadOnlyList<Delegation>> GetMyDelegationsAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<List<DelegationRaw>>(new { type = "delegations", user }, ct).ConfigureAwait(false);
        return raw.Select(HlMapper.Map).ToList();
    }

    public async Task<DelegatorSummary> GetMySummaryAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<DelegatorSummaryRaw>(new { type = "delegatorSummary", user }, ct).ConfigureAwait(false);
        return HlMapper.Map(raw);
    }

    public async Task<IReadOnlyList<Reward>> GetMyRewardsAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<List<DelegatorRewardRaw>>(new { type = "delegatorRewards", user }, ct).ConfigureAwait(false);
        return raw.Select(HlMapper.Map).ToList();
    }

    public Task DepositAsync(decimal amount, CancellationToken ct = default) => WriteFail();
    public Task WithdrawAsync(decimal amount, CancellationToken ct = default) => WriteFail();
    public Task DelegateAsync(string validator, decimal amount, CancellationToken ct = default) => WriteFail();
    public Task UndelegateAsync(string validator, decimal amount, CancellationToken ct = default) => WriteFail();

    private static Task WriteFail() => Task.FromException(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for staking queries.");
}
