#if CYCLONEGAMES_HAS_NAVIGATHENA
using System;
using System.Threading;

using Cysharp.Threading.Tasks;
using MackySoft.Navigathena;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Runtime.Integrations.Navigathena
{
    /// <summary>
    /// An interrupt operation for Navigathena that clears the unused cache 
    /// for a specific IAssetPackage from CycloneGames.AssetManagement.
    /// </summary>
    /// <remarks>
    /// To ensure proper memory management, an instance of this class should be passed as the
    /// `interruptOperation` parameter to Navigathena's scene transition methods (e.g., `Push`, `Pop`, `Change`).
    /// This allows the asset system to perform a cleanup cycle at the precise moment between
    /// the old scene being unloaded and the new scene being loaded.
    /// </remarks>
    public sealed class UnloadPackageAssetsOperation : IAsyncOperation
    {
        private readonly IAssetPackage _assetPackage;

        /// <summary>
        /// Creates a new operation to clean the asset cache for the given package.
        /// </summary>
        /// <param name="assetPackage">The asset package whose cache should be cleared.</param>
        public UnloadPackageAssetsOperation(IAssetPackage assetPackage)
        {
            _assetPackage = assetPackage ?? throw new ArgumentNullException(nameof(assetPackage));
        }

        public UniTask ExecuteAsync(IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _assetPackage.TrimIdleCache(AssetCacheRetentionPolicy.EvictAllIdle);
            return UniTask.CompletedTask;
        }
    }
}
#endif
