using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public class AssetManagementResourceLocator : IResourceLocator
    {
        private readonly IAssetPackage assetPackage;
        private readonly Dictionary<object, IAssetHandle<Object>> loadedHandles = new Dictionary<object, IAssetHandle<Object>>();

        public AssetManagementResourceLocator(IAssetPackage assetPackage)
        {
            this.assetPackage = assetPackage;
        }

        public async UniTask<T> LoadAssetAsync<T>(object key) where T : Object
        {
            if (key == null) return null;
            
            if (key is not string stringKey || string.IsNullOrEmpty(stringKey))
            {
                Debug.LogError($"[GAS AssetManagement] Invalid asset key: {key}, key must be a non-empty string.");
                return null;
            }

            if (loadedHandles.TryGetValue(key, out var handle))
            {
                await UniTask.RunOnThreadPool(() => handle.WaitForAsyncComplete());
                return handle.Asset as T;
            }

            var loadHandle = assetPackage.LoadAssetAsync<T>(stringKey);
            loadedHandles[key] = loadHandle;

            await UniTask.RunOnThreadPool(() => loadHandle.WaitForAsyncComplete());

            if (loadHandle.Asset == null)
            {
                Debug.LogError($"[GAS AssetManagement] Failed to load asset with key: {key}");
                loadHandle.Dispose();
                loadedHandles.Remove(key);
                return null;
            }
            return loadHandle.Asset;
        }

        public void ReleaseAsset(object key)
        {
            if (key != null && loadedHandles.TryGetValue(key, out var handle))
            {
                loadedHandles.Remove(key);
                handle.Dispose();
            }
        }

        public void ReleaseAll()
        {
            foreach (var handle in loadedHandles.Values)
            {
                handle.Dispose();
            }
            loadedHandles.Clear();
        }
    }
}
