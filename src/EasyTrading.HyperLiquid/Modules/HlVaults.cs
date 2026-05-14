using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;
using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IVaults"/>. Reads are wired up; deposits / withdrawals land in Phase 3.</summary>
internal sealed class HlVaults(HlInfoClient info, HyperLiquidClientOptions options) : IVaults
{
    public async Task<VaultDetails> GetDetailsAsync(string vaultAddress, CancellationToken ct = default)
    {
        var user = options.Credentials?.MasterAddress;
        var raw = user is null
            ? await info.PostAsync<VaultDetailsRaw>(new { type = "vaultDetails", vaultAddress }, ct).ConfigureAwait(false)
            : await info.PostAsync<VaultDetailsRaw>(new { type = "vaultDetails", vaultAddress, user }, ct).ConfigureAwait(false);
        return HlMapper.Map(raw);
    }

    public async Task<IReadOnlyList<VaultEquity>> GetMyEquitiesAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<List<UserVaultEquityRaw>>(new { type = "userVaultEquities", user }, ct).ConfigureAwait(false);
        return raw.Select(HlMapper.Map).ToList();
    }

    public Task<TransferResult> DepositAsync(string vaultAddress, decimal amount, CancellationToken ct = default)
        => Task.FromException<TransferResult>(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));

    public Task<TransferResult> WithdrawAsync(string vaultAddress, decimal amount, CancellationToken ct = default)
        => Task.FromException<TransferResult>(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for vault-equity queries.");
}
