using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Logging;

namespace CycloneGames.GameplayAbilities.Runtime.Integrations.AssetManagement
{
    public sealed class AssetManagementResourceLocator : IResourceLocator
    {
        private static readonly LogChannel Log = GameplayAbilitiesAssetManagementLog.Channel;

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
                Log.Error(key, static (assetKey, sb) => sb.Append("Invalid asset key: ").Append(assetKey)
                    .Append(", key must be a non-empty string."));
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
                Log.Error(key, static (assetKey, sb) => sb.Append("Asset package returned no load handle for key: ")
                    .Append(assetKey));
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
                    Log.Error(cleanupFailure, $"Asset load cleanup failed for key '{key}'.");
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(loadFailure).Throw();
                throw;
            }

            if (loadHandle.Asset == null)
            {
                Log.Error(key, static (assetKey, sb) => sb.Append("Failed to load asset with key: ").Append(assetKey));
                loadHandle.Dispose();
                return null;
            }

            return new AssetManagementHandleWrapper<T>(loadHandle);
        }
    }
}
