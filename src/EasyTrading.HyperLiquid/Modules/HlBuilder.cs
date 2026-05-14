using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid.Infrastructure;
using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IBuilder"/>. <c>GetMaxFeeAsync</c> uses the Info endpoint; approval is Phase 3.</summary>
internal sealed class HlBuilder(HlInfoClient info, HyperLiquidClientOptions options) : IBuilder
{
    public async Task<decimal> GetMaxFeeAsync(string builderAddress, CancellationToken ct = default)
    {
        var user = RequireUser();
        // HyperLiquid returns a bare numeric value (possibly string-wrapped) — NumberHandling.AllowReadingFromString handles both.
        var raw = await info.PostAsync<decimal>(new
        {
            type = "maxBuilderFee",
            user,
            builder = builderAddress,
        }, ct).ConfigureAwait(false);
        return raw;
    }

    /// <summary>HyperLiquid does not currently expose a read endpoint listing all approved builders.</summary>
    public Task<IReadOnlyList<BuilderApproval>> GetApprovedAsync(CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<BuilderApproval>>(new NotSupportedException(
            "HyperLiquid does not expose a read endpoint listing approved builders. "
            + "Track them client-side after calling ApproveAsync (Phase 3), "
            + "or call GetMaxFeeAsync(builderAddress) for a specific builder."));

    public Task ApproveAsync(string builderAddress, decimal maxFeeRate, CancellationToken ct = default)
        => Task.FromException(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for builder-fee queries.");
}
