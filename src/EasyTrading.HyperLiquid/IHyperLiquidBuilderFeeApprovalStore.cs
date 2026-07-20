namespace EasyTrading.HyperLiquid;

/// <summary>
/// Shares builder-fee approval state between credential-isolated HyperLiquid clients.
/// </summary>
public interface IHyperLiquidBuilderFeeApprovalStore
{
    /// <summary>
    /// Returns a usable cached state or executes one coordinated refresh for the supplied key.
    /// </summary>
    Task<HyperLiquidBuilderFeeApprovalState> GetOrRefreshAsync(
        HyperLiquidBuilderFeeApprovalKey key,
        int requiredWireFee,
        Func<CancellationToken, Task<HyperLiquidBuilderFeeApprovalState>> refresh,
        CancellationToken ct = default);

    /// <summary>Gets a cached state without performing a refresh.</summary>
    bool TryGet(
        HyperLiquidBuilderFeeApprovalKey key,
        out HyperLiquidBuilderFeeApprovalState state);

    /// <summary>Invalidates a cached state so the next request performs a refresh.</summary>
    void Invalidate(HyperLiquidBuilderFeeApprovalKey key);
}
