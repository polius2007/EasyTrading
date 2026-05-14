using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IAccount"/> backed by the Info endpoint and (for writes) the Exchange endpoint.</summary>
internal sealed class HlAccount(
    HlInfoClient info,
    HlExchangeClient exchange,
    HyperLiquidClientOptions options) : IAccount
{
    public async Task<AccountState> GetStateAsync(CancellationToken ct = default)
    {
        var user = RequireUser();

        var perpTask = info.PostAsync<ClearinghouseStateRaw>(new { type = "clearinghouseState", user }, ct);
        var spotTask = info.PostAsync<SpotClearinghouseStateRaw>(new { type = "spotClearinghouseState", user }, ct);

        await Task.WhenAll(perpTask, spotTask).ConfigureAwait(false);

        return HlMapper.MapAccountState(perpTask.Result, spotTask.Result.Balances);
    }

    public async Task<decimal> GetBalanceAsync(string token = "USDC", CancellationToken ct = default)
    {
        var balances = await GetBalancesAsync(ct).ConfigureAwait(false);
        return balances.TryGetValue(token, out var b) ? b : 0m;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetBalancesAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var spot = await info.PostAsync<SpotClearinghouseStateRaw>(new { type = "spotClearinghouseState", user }, ct).ConfigureAwait(false);
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in spot.Balances)
            result[b.Coin] = b.Total;
        return result;
    }

    public async Task<FeeSchedule> GetFeesAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<UserFeesRaw>(new { type = "userFees", user }, ct).ConfigureAwait(false);
        return HlMapper.Map(raw);
    }

    public async Task<Portfolio> GetPortfolioAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostRawAsync(new { type = "portfolio", user }, ct).ConfigureAwait(false);
        return HlMapper.MapPortfolio(raw);
    }

    public async Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<List<SubAccountRaw>>(new { type = "subAccounts", user }, ct).ConfigureAwait(false);
        return raw.Select(HlMapper.Map).ToList();
    }

    public async Task<RateLimitInfo> GetRateLimitAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<UserRateLimitRaw>(new { type = "userRateLimit", user }, ct).ConfigureAwait(false);
        return HlMapper.Map(raw);
    }

    /// <summary>
    /// Approves an agent / API wallet on the master account. User-signed EIP-712 action
    /// (<c>ApproveAgent</c>, domain <c>HyperliquidSignTransaction</c>).
    /// </summary>
    public async Task ApproveAgentAsync(string agentAddress, string? name = null, CancellationToken ct = default)
    {
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var action = new HlMap()
            .Add("type", "approveAgent")
            .Add("agentAddress", agentAddress.ToLowerInvariant())
            .Add("agentName", name ?? string.Empty)
            .Add("nonce", nonce);

        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("agentAddress",     "address"),
            ("agentName",        "string"),
            ("nonce",            "uint64"),
        };

        await exchange.SendUserAsync(action, "ApproveAgent", schema, ct).ConfigureAwait(false);
    }

    /// <summary>HyperLiquid does not expose a read endpoint to enumerate approved agents.</summary>
    public Task<IReadOnlyList<AgentInfo>> GetApprovedAgentsAsync(CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<AgentInfo>>(new NotSupportedException(
            "HyperLiquid does not provide a read endpoint to enumerate approved agents. "
            + "Track them client-side after calling ApproveAgentAsync."));

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for user-state queries.");
}
