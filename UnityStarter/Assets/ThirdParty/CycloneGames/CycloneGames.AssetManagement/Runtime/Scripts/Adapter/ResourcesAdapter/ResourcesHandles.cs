using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    internal abstract class ResourcesOperationHandle : IOperation
    {
        protected readonly Action<int> Unregister;
        protected readonly int Id;
        public virtual bool IsDone => true;
        public virtual float Progress => 1f;
        public virtual string Error => string.Empty;
        public virtual Task Task => Task.CompletedTask;
        public virtual void WaitForAsyncComplete() { }

        protected ResourcesOperationHandle(Action<int> unregister, int id)
        {
            Unregister = unregister;
            Id = id;
        }
    }

    internal sealed class ResourcesAssetHandle<TAsset> : ResourcesOperationHandle, IAssetHandle<TAsset> where TAsset : UnityEngine.Object
    {
        public TAsset Asset { get; }
        public UnityEngine.Object AssetObject => Asset;

        public ResourcesAssetHandle(Action<int> unregister, int id, TAsset asset) : base(unregister, id)
        {
            Asset = asset;
        }

        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            Unregister(Id);
            // For Resources.Load, we don't unload individual assets. Let UnloadUnusedAssets handle it.
        }
    }

    internal sealed class ResourcesAllAssetsHandle<TAsset> : ResourcesOperationHandle, IAllAssetsHandle<TAsset> where TAsset : UnityEngine.Object
    {
        public IReadOnlyList<TAsset> Assets { get; }

        public ResourcesAllAssetsHandle(Action<int> unregister, int id, TAsset[] assets) : base(unregister, id)
        {
            Assets = assets;
        }

        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            Unregister(Id);
        }
    }

    internal sealed class ResourcesInstantiateHandle : ResourcesOperationHandle, IInstantiateHandle
    {
        public GameObject Instance { get; }

        public ResourcesInstantiateHandle(Action<int> unregister, int id, GameObject instance) : base(unregister, id)
        {
            Instance = instance;
        }

        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            Unregister(Id);
            // The user is responsible for destroying the instantiated GameObject.
        }
    }
}
