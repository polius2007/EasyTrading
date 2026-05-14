namespace EasyTrading.Broker;

/// <summary>
/// Options for the EasyTrading builder-fee / rebate decorator.
/// </summary>
/// <remarks>
/// The real implementation (Phase 5) wraps <c>IOrders</c> on the configured
/// <c>IExchangeClient</c> and injects this configuration into every order that doesn't
/// override it explicitly. On HyperLiquid the underlying mechanism is the
/// <c>approveBuilderFee</c> action; analogous routing will be added for other DEXes.
/// </remarks>
public sealed class BrokerOptions
{
    /// <summary>Address of the builder receiving fees on orders routed through EasyTrading.</summary>
    public required string BuilderAddress { get; init; }

    /// <summary>Maximum builder-fee rate as a fraction (e.g. <c>0.0005</c> = 0.05%).</summary>
    public required decimal MaxFeeRate { get; init; }

    /// <summary>
    /// If <c>true</c>, the decorator automatically calls the exchange's builder-approval action
    /// on the first order it intercepts. Defaults to <c>true</c>.
    /// </summary>
    public bool AutoApprove { get; init; } = true;
}
