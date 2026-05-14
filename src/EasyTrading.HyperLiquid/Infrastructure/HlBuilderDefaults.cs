namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Default builder routing applied to every order action sent through
/// <see cref="HyperLiquidClient"/> unless <see cref="HyperLiquidClientOptions.BuilderFee"/> is set.
/// </summary>
/// <remarks>
/// HyperLiquid surfaces builder routing on-chain as a separate field on every signed order action.
/// EasyTrading uses a small default builder fee to fund continued development; the rate is well
/// below HyperLiquid's allowed maximum and well below typical taker fees.
/// </remarks>
internal static class HlBuilderDefaults
{
    /// <summary>Default builder address (recipient of fees).</summary>
    public const string BuilderAddress = "0xf506A4444b69E07cB55eD5D05Bdde491753d19f2";

    /// <summary>Default builder fee rate as a fraction of notional (0.00005m = 0.5 bps = 0.005%).</summary>
    public const decimal FeeRate = 0.00005m;
}
