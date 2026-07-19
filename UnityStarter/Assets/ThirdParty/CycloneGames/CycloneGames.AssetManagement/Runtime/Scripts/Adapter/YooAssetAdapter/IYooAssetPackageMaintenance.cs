#if CYCLONEGAMES_HAS_YOOASSET
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// YooAsset-specific versioned-manifest, cache-file, and downloader maintenance.
    /// Product code must quiesce dependent loads before manifest or cache mutation. Manifest and cache mutations
    /// are main-thread-affine and fail fast when another mutation is already in progress on the same package.
    /// </summary>
    public interface IYooAssetPackageMaintenance
    {
        /// <summary>
        /// Activates a manifest version obtained by product code through an authenticated, response-size-bounded
        /// version service. Provider-native version requests are intentionally not exposed because their response
        /// buffering cannot enforce the framework's authentication and response-size boundaries.
        /// </summary>
        UniTask<bool> UpdatePackageManifestAsync(
            string packageVersion,
            int timeoutSeconds = 60,
            CancellationToken cancellationToken = default);

        UniTask<bool> ClearAllCacheFilesAsync(CancellationToken cancellationToken = default);

        UniTask<bool> ClearUnusedCacheFilesAsync(CancellationToken cancellationToken = default);

        UniTask<bool> ClearCacheFilesByTagsAsync(
            string[] tags,
            CancellationToken cancellationToken = default);

        IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain);

        /// <summary>
        /// Creates a YooAsset tag downloader for bundles selected by the active manifest.
        /// </summary>
        IDownloader CreateDownloaderForTags(
            string[] tags,
            int downloadingMaxNumber,
            int failedTryAgain);

        IDownloader CreateDownloaderForLocations(
            string[] locations,
            bool recursiveDownload,
            int downloadingMaxNumber,
            int failedTryAgain);
    }
}
#endif
