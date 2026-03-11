using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public class AssetManagementResourceLocator : IResourceLocator
    {
        private class AssetManagementHandleWrapper<T> : IResourceHandle<T> where T : Object
        {
            private readonly IAssetHandle<T> underlyingHandle;
            public T Asset => underlyingHandle.Asset;

            public AssetManagementHandleWrapper(IAssetHandle<T> handle)
            {
                underlyingHandle = handle;
            }

            public void Dispose()
            {
                underlyingHandle?.Dispose();
            }
        }

        private readonly IAssetPackage assetPackage;

        public AssetManagementResourceLocator(IAssetPackage assetPackage)
        {
            this.assetPackage = assetPackage;
        }

        public async UniTask<IResourceHandle<T>> LoadAssetAsync<T>(object key, string cacheTag = null, string cacheOwner = null) where T : Object
        {
            if (key == null) return null;

            if (key is not string stringKey || string.IsNullOrEmpty(stringKey))
            {
                GASLog.Error($"Invalid asset key: {key}, key must be a non-empty string.");
                return null;
            }

            var loadHandle = assetPackage.LoadAssetAsync<T>(stringKey, tag: cacheTag, owner: cacheOwner);
            await UniTask.RunOnThreadPool(() => loadHandle.WaitForAsyncComplete());

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
