namespace EasyTrading.HyperLiquid;

/// <summary>Identifies one user-to-builder approval in one HyperLiquid environment.</summary>
/// <param name="Network">HyperLiquid network.</param>
/// <param name="RestBaseUrl">Effective REST endpoint used by the client.</param>
/// <param name="MasterAddress">User master address whose approval is being checked.</param>
/// <param name="BuilderAddress">Builder address receiving the fee.</param>
public sealed record HyperLiquidBuilderFeeApprovalKey(
    HyperLiquidNetwork Network,
    Uri RestBaseUrl,
    string MasterAddress,
    string BuilderAddress);
