namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Adapts the shared public catalog to the internal order-building contract without triggering
/// metadata requests from a user-specific client.
/// </summary>
internal sealed class CatalogMetadataProvider(
    IHyperLiquidMetadataCatalog catalog,
    HyperLiquidMetadataScope scope) : IAssetMetadataProvider
{
    public Task<int> GetAssetIdAsync(string symbol, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(catalog.GetRequiredAsset(scope, symbol).AssetId);
    }

    public Task<AssetInfo> GetAssetInfoAsync(string symbol, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var metadata = catalog.GetRequiredAsset(scope, symbol);
        return Task.FromResult(new AssetInfo(
            metadata.AssetId,
            metadata.SzDecimals,
            metadata.IsSpot));
    }
}
