using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Integrate.Setup
{
    /// <summary>
    /// Sample-only IResourceLocator backed by Unity Resources.
    /// </summary>
    /// <remarks>
    /// Resources assets have no owned handle lifetime. The wrapper therefore only
    /// clears its reference on dispose; it must never unload or destroy the asset.
    /// The bucket, cacheTag and cacheOwner parameters are ignored: Resources has no
    /// bucket or cache-ownership model.
    /// </remarks>
    public sealed class ResourcesResourceLocator : IResourceLocator
    {
        private const int MaxKeyLength = 1024;

        private sealed class OwnedResourceHandle<T> : IResourceHandle<T>
            where T : UnityEngine.Object
        {
            private T asset;

            public T Asset => asset;

            public OwnedResourceHandle(T asset)
            {
                this.asset = asset;
            }

            public void Dispose()
            {
                asset = null;
            }
        }

        public UniTask<IResourceHandle<T>> LoadAssetAsync<T>(
            string key,
            string bucket = null,
            string cacheTag = null,
            string cacheOwner = null,
            CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            if (cancellationToken.IsCancellationRequested ||
                string.IsNullOrEmpty(key) ||
                key.Length > MaxKeyLength ||
                key.IndexOf('\0') >= 0)
            {
                return UniTask.FromResult<IResourceHandle<T>>(null);
            }

            // CG0014: this sample locator uses Resources as its backend.
#pragma warning disable CG0014
            T loaded = UnityEngine.Resources.Load<T>(key);
#pragma warning restore CG0014
            if (loaded == null)
            {
                return UniTask.FromResult<IResourceHandle<T>>(null);
            }

            return UniTask.FromResult<IResourceHandle<T>>(new OwnedResourceHandle<T>(loaded));
        }
    }
}
