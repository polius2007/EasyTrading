using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid.Infrastructure;
using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IStaking"/>. Reads via Info; writes signed L1 actions through the Exchange endpoint.</summary>
internal sealed class HlStaking(
    HlInfoClient info,
    HlExchangeClient exchange,
    HyperLiquidClientOptions options) : IStaking
{
    /// <summary>HYPE (HyperLiquid native token) uses 8-decimal "wei" units in staking actions.</summary>
    private const decimal HypeWeiMultiplier = 100_000_000m;

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

    public async Task DepositAsync(decimal amount, CancellationToken ct = default)
    {
        var wei = (long)decimal.Round(amount * HypeWeiMultiplier, MidpointRounding.ToEven);
        var action = new HlMap().Add("type", "cDeposit").Add("wei", wei);
        await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
    }

    public async Task WithdrawAsync(decimal amount, CancellationToken ct = default)
    {
        var wei = (long)decimal.Round(amount * HypeWeiMultiplier, MidpointRounding.ToEven);
        var action = new HlMap().Add("type", "cWithdraw").Add("wei", wei);
        await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
    }

    public Task DelegateAsync(string validator, decimal amount, CancellationToken ct = default)
        => TokenDelegateAsync(validator, amount, isUndelegate: false, ct);

    public Task UndelegateAsync(string validator, decimal amount, CancellationToken ct = default)
        => TokenDelegateAsync(validator, amount, isUndelegate: true, ct);

    private async Task TokenDelegateAsync(string validator, decimal amount, bool isUndelegate, CancellationToken ct)
    {
        var wei = (long)decimal.Round(amount * HypeWeiMultiplier, MidpointRounding.ToEven);
        var action = new HlMap()
            .Add("type", "tokenDelegate")
            .Add("validator", validator.ToLowerInvariant())
            .Add("isUndelegate", isUndelegate)
            .Add("wei", wei);

        await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
    }

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for staking queries.");
}
