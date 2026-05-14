using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;
using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IVaults"/>.</summary>
internal sealed class HlVaults(
    HlInfoClient info,
    HlExchangeClient exchange,
    HyperLiquidClientOptions options) : IVaults
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
        => VaultTransferAsync(vaultAddress, amount, isDeposit: true, ct);

    public Task<TransferResult> WithdrawAsync(string vaultAddress, decimal amount, CancellationToken ct = default)
        => VaultTransferAsync(vaultAddress, amount, isDeposit: false, ct);

    private async Task<TransferResult> VaultTransferAsync(string vaultAddress, decimal amount, bool isDeposit, CancellationToken ct)
    {
        // `usd` is the amount in 6-decimal USDC (multiples of 1_000_000).
        var usd = (long)decimal.Round(amount * 1_000_000m, MidpointRounding.ToEven);
        var action = new HlMap()
            .Add("type", "vaultTransfer")
            .Add("vaultAddress", vaultAddress.ToLowerInvariant())
            .Add("isDeposit", isDeposit)
            .Add("usd", usd);

        await exchange.SendL1Async(action, expiresAfter: null, ct).ConfigureAwait(false);
        return new TransferResult(TransferId: null, Success: true, ErrorMessage: null);
    }

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for vault-equity queries.");
}
