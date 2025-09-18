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
        private readonly TAsset syncAsset;

        public override bool IsDone => request?.isDone ?? true;
        public override float Progress => request?.progress ?? 1f;
        public override UniTask Task => request?.ToUniTask() ?? UniTask.CompletedTask;
        
        public TAsset Asset => syncAsset != null ? syncAsset : request?.asset as TAsset;
        public UnityEngine.Object AssetObject => Asset;

        public ResourcesAssetHandle(Action<int> unregister, int id, ResourceRequest request) : base(unregister, id)
        {
            this.request = request;
        }
        
        public ResourcesAssetHandle(Action<int> unregister, int id, TAsset asset) : base(unregister, id)
        {
            this.syncAsset = asset;
        }

        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            Unregister(Id);
            // Individual assets loaded from Resources cannot be unloaded.
        }
    }

    internal sealed class ResourcesAllAssetsHandle<TAsset> : ResourcesOperationHandle, IAllAssetsHandle<TAsset> where TAsset : UnityEngine.Object
    {
        public override bool IsDone => _task.Status.IsCompleted();
        public override float Progress => _task.Status.IsCompleted() ? 1f : 0f;
        public override UniTask Task => _task;
        private readonly UniTask _task;
        
        public IReadOnlyList<TAsset> Assets { get; }

        public ResourcesAllAssetsHandle(Action<int> unregister, int id, TAsset[] assets) : base(unregister, id)
        {
            Assets = assets;
            _task = SimulateAsync();
        }

        private async UniTask SimulateAsync()
        {
            await UniTask.Yield();
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