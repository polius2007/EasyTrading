using EasyTrading.HyperLiquid.Models;

namespace EasyTrading.HyperLiquid;

/// <summary>
/// HyperLiquid builder-fee management. A builder is an address that receives a small fee on every
/// order an account routes through it. <c>EasyTrading.Broker</c> uses this surface to inject its
/// configured builder address into the order flow automatically.
/// </summary>
public interface IBuilder
{
    /// <summary>Get the maximum approved fee rate for one builder.</summary>
    /// <param name="builderAddress">Builder address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The maximum fee rate, or <c>0</c> if the builder is not approved.</returns>
    Task<decimal> GetMaxFeeAsync(string builderAddress, CancellationToken ct = default);

    /// <summary>List all builders currently approved by the account.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Approved builders.</returns>
    Task<IReadOnlyList<BuilderApproval>> GetApprovedAsync(CancellationToken ct = default);

    /// <summary>
    /// Approve a builder address up to a maximum fee rate. The builder may then attach a fee up to
    /// this rate to orders routed through it.
    /// </summary>
    /// <param name="builderAddress">Builder address.</param>
    /// <param name="maxFeeRate">Maximum fee rate as a fraction (e.g. <c>0.0005</c> = 0.05%).</param>
    /// <param name="ct">Cancellation token.</param>
    Task ApproveAsync(string builderAddress, decimal maxFeeRate, CancellationToken ct = default);
}
