using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Service
{
    public interface IAssetLoader
    {
        /// <summary>
        /// Loads an asset asynchronously and retains the handle in memory after loading is complete.
        /// To prevent memory leaks, ReleaseAssetHandle(key) must be called when the asset is no longer needed.
        /// <param name="key">The key of the asset to be loaded.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <typeparam name="TResultObject">The type of the asset to be loaded.</typeparam>
        /// <returns>A UniTask that completes with the loaded asset.</returns>
        UniTask<TResultObject> LoadAssetAsync<TResultObject>(string key,
            CancellationToken cancellationToken = default) where TResultObject : UnityEngine.Object;

        /// <summary>
        /// Releases the handle associated with a previously loaded asset.
        /// </summary>
        /// <param name="key">The key of the asset whose handle is to be released.</param>
        void ReleaseAssetHandle(string key);
    }
}