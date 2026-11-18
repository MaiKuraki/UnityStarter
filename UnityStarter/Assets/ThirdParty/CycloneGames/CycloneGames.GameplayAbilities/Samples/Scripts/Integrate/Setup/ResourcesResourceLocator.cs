using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Integrate.Setup
{
    /// <summary>
    /// Sample-only IResourceLocator backed by Unity Resources. It has no dependency on
    /// any CycloneGames asset-management package, so the sample stays compilable when
    /// the optional UPM integration is absent.
    /// </summary>
    /// <remarks>
    /// Resources assets have no owned handle lifetime. The wrapper therefore only
    /// clears its reference on dispose; it must never unload or destroy the asset.
    /// The bucket, cacheTag and cacheOwner parameters are intentionally ignored:
    /// Resources has no bucket or cache-ownership model. Production hosts should
    /// supply their own IResourceLocator when they need those capabilities.
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

            T loaded = UnityEngine.Resources.Load<T>(key);
            if (loaded == null)
            {
                return UniTask.FromResult<IResourceHandle<T>>(null);
            }

            return UniTask.FromResult<IResourceHandle<T>>(new OwnedResourceHandle<T>(loaded));
        }
    }
}
