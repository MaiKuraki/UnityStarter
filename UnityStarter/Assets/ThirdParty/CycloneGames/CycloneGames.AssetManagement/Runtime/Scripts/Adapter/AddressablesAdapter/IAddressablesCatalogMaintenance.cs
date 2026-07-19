#if CYCLONEGAMES_HAS_ADDRESSABLES
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Addressables-specific maintenance that deliberately does not model a catalog as a version-addressable,
    /// isolated, or atomic candidate. The composition root must quiesce loads and validate its release policy
    /// before activating the latest catalogs reported by Addressables. Catalog and cache mutations are
    /// main-thread-affine and fail fast when another mutation is already in progress on the same package.
    /// </summary>
    public interface IAddressablesCatalogMaintenance
    {
        /// <summary>
        /// Reads product-owned release metadata. This value is not an Addressables catalog identity, authenticated
        /// release authorization, anti-rollback evidence, or a download-size estimate.
        /// </summary>
        UniTask<string> ReadReleaseMetadataVersionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks for and activates the latest catalogs. Cancellation is observed before activation; after catalog
        /// mutation begins the operation completes deterministically because Addressables cannot roll it back.
        /// </summary>
        UniTask<bool> UpdateLatestCatalogsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes cached bundles that are not referenced by the currently loaded Addressables catalogs.
        /// Once cleanup starts, it completes deterministically because Unity cache mutation cannot be rolled back.
        /// </summary>
        UniTask<bool> CleanUnusedBundleCacheAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears Unity's process-wide AssetBundle cache. This destructive operation is not scoped to this package
        /// and can affect Addressables, raw AssetBundle users, and third-party systems sharing <c>Caching</c>.
        /// </summary>
        UniTask<bool> ClearAllCacheFilesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a provider-managed dependency downloader. Addressables does not expose per-request concurrency
        /// or retry controls.
        /// </summary>
        IDownloader CreateDownloaderForTags(string[] tags);

        /// <summary>
        /// Creates a provider-managed dependency downloader. Location dependency closure is always recursive.
        /// </summary>
        IDownloader CreateDownloaderForLocations(string[] locations);
    }
}
#endif
