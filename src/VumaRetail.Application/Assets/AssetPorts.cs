#pragma warning disable CS1591
using VumaRetail.Domain.Assets;

namespace VumaRetail.Application.Assets;

public interface IAssetRepository
{
    Task<FixedAsset?> FindAssetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AssetBook?> FindBookAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AssetBook?> FindBookAsync(Guid assetId, string bookName, CancellationToken cancellationToken = default);
    void Add(FixedAsset asset);
    void Add(AssetBook book);
}
