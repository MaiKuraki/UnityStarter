#if NAVIGATHENA_PRESENT
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
    public class UnloadPackageAssetsOperation : IAsyncOperation
    {
        private readonly IAssetPackage assetPackage;

        /// <summary>
        /// Creates a new operation to clean the asset cache for the given package.
        /// </summary>
        /// <param name="assetPackage">The asset package whose cache should be cleared.</param>
        public UnloadPackageAssetsOperation(IAssetPackage assetPackage)
        {
            // Ensure the package is not null.
            this.assetPackage = assetPackage ?? throw new ArgumentNullException(nameof(assetPackage));
        }

        public async UniTask ExecuteAsync(IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {
            if (assetPackage != null)
            {
                // This is the recommended approach when using a comprehensive asset management framework.
                // It tells the specific provider (e.g., YooAsset) to intelligently unload any of its
                // managed assets and bundles that are no longer in use. It is generally more
                // performant and precise than Unity's global UnloadUnusedAssets.
                await assetPackage.ClearCacheFilesAsync(ClearCacheMode.Unused);
            }

            // The call below is Unity's global, generic asset cleanup function.
            // You generally DO NOT need to call this if all your assets are managed by CycloneGames.AssetManagement.
            // However, if your project loads assets from other sources (e.g., legacy 'Resources' folders,
            // direct AssetBundle.LoadFromFile calls), you might need to uncomment this line to clean up those as well.
            // Be aware that this call can cause performance hitches.
            //
            // var unloadOperation = Resources.UnloadUnusedAssets();
            // await unloadOperation.ToUniTask(cancellationToken: cancellationToken);
        }
    }
}
#endif