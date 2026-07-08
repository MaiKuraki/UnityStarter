using System;
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class AssetPackageRepairTarget : IAssetRepairTarget
    {
        private readonly IAssetPackage _package;

        public AssetPackageRepairTarget(IAssetPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public string PackageName => _package.Name;

        public UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.All, object clearParam = null, CancellationToken cancellationToken = default)
        {
            return _package.ClearCacheFilesAsync(clearMode, clearParam, cancellationToken);
        }

        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            return _package.CreateDownloaderForLocations(locations, recursiveDownload, downloadingMaxNumber, failedTryAgain);
        }

        public UniTask UnloadUnusedAssetsAsync()
        {
            return _package.UnloadUnusedAssetsAsync();
        }
    }
}
