namespace EasyTrading.HyperLiquid;

/// <summary>Cached state of one HyperLiquid builder-fee approval.</summary>
/// <param name="ApprovedWireFee">Maximum approved fee in HyperLiquid wire units.</param>
/// <param name="CheckedAt">Time when the state was obtained.</param>
/// <param name="RetryAfter">Earliest time when a failed or insufficient state may be refreshed.</param>
public sealed record HyperLiquidBuilderFeeApprovalState(
    int ApprovedWireFee,
    DateTimeOffset CheckedAt,
    DateTimeOffset? RetryAfter = null);
