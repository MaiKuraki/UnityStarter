using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public class AssetManagementResourceLocator : IResourceLocator
    {
        private class AssetManagementHandleWrapper<T> : IResourceHandle<T> where T : Object
        {
            private IAssetHandle<T> underlyingHandle;
            public T Asset => underlyingHandle.Asset;

            private static readonly Stack<AssetManagementHandleWrapper<T>> _pool = new Stack<AssetManagementHandleWrapper<T>>(32);
            private static readonly object _poolLock = new object();

            public static AssetManagementHandleWrapper<T> Get(IAssetHandle<T> handle)
            {
                lock (_poolLock)
                {
                    if (_pool.Count > 0)
                    {
                        var wrapper = _pool.Pop();
                        wrapper.underlyingHandle = handle;
                        return wrapper;
                    }
                }
                return new AssetManagementHandleWrapper<T>(handle);
            }

            private AssetManagementHandleWrapper(IAssetHandle<T> handle)
            {
                underlyingHandle = handle;
            }

            public void Dispose()
            {
                underlyingHandle?.Dispose();
                underlyingHandle = null;

                lock (_poolLock)
                {
                    if (_pool.Count < 256)
                    {
                        _pool.Push(this);
                    }
                }
            }
        }

        private readonly IAssetPackage assetPackage;

        public AssetManagementResourceLocator(IAssetPackage assetPackage)
        {
            this.assetPackage = assetPackage;
        }

        public async UniTask<IResourceHandle<T>> LoadAssetAsync<T>(object key, string bucket = null, string cacheTag = null, string cacheOwner = null, CancellationToken cancellationToken = default) where T : Object
        {
            if (key == null) return null;

            if (key is not string stringKey || string.IsNullOrEmpty(stringKey))
            {
                GASLog.Error($"Invalid asset key: {key}, key must be a non-empty string.");
                return null;
            }

            var loadHandle = assetPackage.LoadAssetAsync<T>(stringKey, bucket: bucket, tag: cacheTag, owner: cacheOwner, cancellationToken: cancellationToken);

            await loadHandle.Task;

            if (loadHandle.Asset == null)
            {
                GASLog.Error($"Failed to load asset with key: {key}");
                loadHandle.Dispose();
                return null;
            }

            return AssetManagementHandleWrapper<T>.Get(loadHandle);
        }
    }
}