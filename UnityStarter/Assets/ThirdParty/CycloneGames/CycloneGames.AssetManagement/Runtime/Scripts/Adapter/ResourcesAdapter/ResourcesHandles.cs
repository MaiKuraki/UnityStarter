using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
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
        public virtual UniTask Task => UniTask.CompletedTask;
        public virtual void WaitForAsyncComplete() { }

        protected ResourcesOperationHandle(Action<int> unregister, int id)
        {
            Unregister = unregister;
            Id = id;
        }
    }

    internal sealed class ResourcesAssetHandle<TAsset> : ResourcesOperationHandle, IAssetHandle<TAsset> where TAsset : UnityEngine.Object
    {
        private readonly ResourceRequest request;
        private readonly TAsset syncAsset; // Used for sync loads

        public override bool IsDone => request?.isDone ?? true;
        public override float Progress => request?.progress ?? 1f;
        public override UniTask Task => request?.ToUniTask() ?? UniTask.CompletedTask;
        
        public TAsset Asset => syncAsset ? syncAsset : request?.asset as TAsset;
        public UnityEngine.Object AssetObject => Asset;

        // Constructor for async loads
        public ResourcesAssetHandle(Action<int> unregister, int id, ResourceRequest request) : base(unregister, id)
        {
            this.request = request;
        }
        
        // Constructor for sync loads
        public ResourcesAssetHandle(Action<int> unregister, int id, TAsset asset) : base(unregister, id)
        {
            this.syncAsset = asset;
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
