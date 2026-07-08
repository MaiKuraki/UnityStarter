using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public interface IAssetRepairTarget
    {
        string PackageName { get; }

        UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.All, object clearParam = null, CancellationToken cancellationToken = default);
        IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain);
        UniTask UnloadUnusedAssetsAsync();
    }
}
