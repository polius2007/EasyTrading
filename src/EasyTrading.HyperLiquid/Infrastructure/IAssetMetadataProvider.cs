namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>Resolves the metadata required to build HyperLiquid trading actions.</summary>
internal interface IAssetMetadataProvider
{
    Task<int> GetAssetIdAsync(string symbol, CancellationToken ct = default);

    Task<AssetInfo> GetAssetInfoAsync(string symbol, CancellationToken ct = default);
}
