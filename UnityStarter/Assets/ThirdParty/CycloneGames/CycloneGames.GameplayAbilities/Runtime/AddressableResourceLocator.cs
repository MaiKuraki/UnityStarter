using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A concrete implementation of IResourceLocator using Unity's Addressable Assets System.
    /// It handles loading and caching of asset handles.
    /// </summary>
    public class AddressableResourceLocator : IResourceLocator
    {
        private readonly Dictionary<object, AsyncOperationHandle> loadedHandles = new Dictionary<object, AsyncOperationHandle>();

        public async UniTask<T> LoadAssetAsync<T>(object key) where T : Object
        {
            if (key == null) return null;
            if (key is AssetReference assetRef && !assetRef.RuntimeKeyIsValid()) return null;

            if (loadedHandles.TryGetValue(key, out var handle))
            {
                return await handle.Convert<T>().Task;
            }

            var loadHandle = Addressables.LoadAssetAsync<T>(key);
            loadedHandles[key] = loadHandle;

            var asset = await loadHandle.Task;

            if (loadHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[Addressables] Failed to load asset with key: {key}");
                Addressables.Release(loadHandle);
                loadedHandles.Remove(key);
                return null;
            }
            return asset;
        }

        public void ReleaseAsset(object key)
        {
            if (key != null && loadedHandles.TryGetValue(key, out var handle))
            {
                loadedHandles.Remove(key);
                Addressables.Release(handle);
            }
        }

        public void ReleaseAll()
        {
            foreach (var handle in loadedHandles.Values)
            {
                Addressables.Release(handle);
            }
            loadedHandles.Clear();
        }
    }
}
