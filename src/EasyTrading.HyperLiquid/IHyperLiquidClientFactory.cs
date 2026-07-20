namespace EasyTrading.HyperLiquid;

/// <summary>Creates isolated HyperLiquid clients that share only public metadata.</summary>
public interface IHyperLiquidClientFactory
{
    /// <summary>Creates a client with an internally owned HTTP transport.</summary>
    HyperLiquidClient Create(
        HyperLiquidCredentials credentials,
        HyperLiquidClientOptions? options = null);

    /// <summary>
    /// Creates a client with an internally owned HTTP transport and an explicit shared
    /// builder-fee approval store.
    /// </summary>
    HyperLiquidClient Create(
        HyperLiquidCredentials credentials,
        IHyperLiquidBuilderFeeApprovalStore builderFeeApprovalStore,
        HyperLiquidClientOptions? options = null);

    /// <summary>Creates a client using a caller-owned HTTP transport.</summary>
    HyperLiquidClient Create(
        HttpClient httpClient,
        HyperLiquidCredentials credentials,
        HyperLiquidClientOptions? options = null);

    /// <summary>
    /// Creates a client using a caller-owned HTTP transport and an explicit shared builder-fee
    /// approval store.
    /// </summary>
    HyperLiquidClient Create(
        HttpClient httpClient,
        HyperLiquidCredentials credentials,
        IHyperLiquidBuilderFeeApprovalStore builderFeeApprovalStore,
        HyperLiquidClientOptions? options = null);
}
