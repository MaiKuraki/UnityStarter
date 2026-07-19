using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime.Integrations.AssetManagement
{
    public sealed class AssetManagementResourceLocator : IResourceLocator
    {
        private sealed class AssetManagementHandleWrapper<T> : IResourceHandle<T> where T : UnityEngine.Object
        {
            private IAssetHandle<T> underlyingHandle;
            public T Asset => underlyingHandle != null ? underlyingHandle.Asset : null;

            public AssetManagementHandleWrapper(IAssetHandle<T> handle)
            {
                underlyingHandle = handle;
            }

            public void Dispose()
            {
                IAssetHandle<T> handle = underlyingHandle;
                underlyingHandle = null;
                handle?.Dispose();
            }
        }

        private readonly IAssetPackage assetPackage;

        public AssetManagementResourceLocator(IAssetPackage assetPackage)
        {
            this.assetPackage = assetPackage ?? throw new System.ArgumentNullException(nameof(assetPackage));
        }

        public async UniTask<IResourceHandle<T>> LoadAssetAsync<T>(string key, string bucket = null, string cacheTag = null, string cacheOwner = null, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key))
            {
                GASLog.Error($"Invalid asset key: {key}, key must be a non-empty string.");
                return null;
            }

            IAssetHandle<T> loadHandle = assetPackage.LoadAssetAsync<T>(
                key,
                bucket: bucket,
                tag: cacheTag,
                owner: cacheOwner,
                cancellationToken: cancellationToken);
            if (loadHandle == null)
            {
                GASLog.Error($"Asset package returned no load handle for key: {key}");
                return null;
            }

            try
            {
                await loadHandle.Task;
            }
            catch (System.Exception loadFailure)
            {
                try { loadHandle.Dispose(); }
                catch (System.Exception cleanupFailure)
                {
                    GASLog.Error($"Asset load cleanup failed for key '{key}': {cleanupFailure.Message}");
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(loadFailure).Throw();
                throw;
            }

            if (loadHandle.Asset == null)
            {
                GASLog.Error($"Failed to load asset with key: {key}");
                loadHandle.Dispose();
                return null;
            }

            return new AssetManagementHandleWrapper<T>(loadHandle);
        }
    }
}
