namespace EasyTrading.HyperLiquid;

/// <summary>
/// Stores public HyperLiquid asset metadata independently from user credentials and client instances.
/// </summary>
public interface IHyperLiquidMetadataCatalog
{
    /// <summary>Loads metadata for the scope if it has not already been loaded.</summary>
    Task WarmUpAsync(HyperLiquidMetadataScope scope, CancellationToken ct = default);

    /// <summary>Forces a fresh metadata snapshot for the scope.</summary>
    Task RefreshAsync(HyperLiquidMetadataScope scope, CancellationToken ct = default);

    /// <summary>Gets previously loaded metadata without performing a network request.</summary>
    bool TryGetAsset(
        HyperLiquidMetadataScope scope,
        string symbol,
        out HyperLiquidAssetMetadata metadata);

    /// <summary>
    /// Gets previously loaded metadata without performing a network request, or throws when the
    /// catalog has not been warmed up or the symbol is unknown.
    /// </summary>
    HyperLiquidAssetMetadata GetRequiredAsset(
        HyperLiquidMetadataScope scope,
        string symbol);
}
