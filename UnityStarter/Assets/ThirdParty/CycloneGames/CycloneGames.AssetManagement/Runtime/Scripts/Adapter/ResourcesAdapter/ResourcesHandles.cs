using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    internal abstract class ResourcesOperationHandle : IOperation
    {
        protected readonly int Id;
        public virtual bool IsDone => true;
        public virtual float Progress => 1f;
        public virtual string Error => string.Empty;
        public virtual UniTask Task => UniTask.CompletedTask;
        public virtual void WaitForAsyncComplete() { }

        protected ResourcesOperationHandle(int id)
        {
            Id = id;
        }
    }

    internal sealed class ResourcesAssetHandle<TAsset> : ResourcesOperationHandle, IAssetHandle<TAsset> where TAsset : UnityEngine.Object
    {
        private readonly ResourceRequest request;
        private readonly TAsset syncAsset;

        public override bool IsDone => request?.isDone ?? true;
        public override float Progress => request?.progress ?? 1f;
        public override UniTask Task { get; }
        
        public TAsset Asset => syncAsset != null ? syncAsset : request?.asset as TAsset;
        public UnityEngine.Object AssetObject => Asset;

        // Async handle
        public ResourcesAssetHandle(int id, ResourceRequest request, System.Threading.CancellationToken cancellationToken) : base(id)
        {
            this.request = request;
            this.Task = request.ToUniTask(cancellationToken: cancellationToken);
        }
        
        // Sync handle
        public ResourcesAssetHandle(int id, TAsset asset) : base(id)
        {
            this.syncAsset = asset;
            this.Task = UniTask.CompletedTask;
        }

        public void Dispose()
        {
            HandleTracker.Unregister(Id);
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

        public ResourcesAllAssetsHandle(int id, TAsset[] assets) : base(id)
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
        }
    }

    internal sealed class ResourcesInstantiateHandle : ResourcesOperationHandle, IInstantiateHandle
    {
        public GameObject Instance { get; }

        public ResourcesInstantiateHandle(int id, GameObject instance) : base(id)
        {
            Instance = instance;
        }

        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            // The user is responsible for destroying the instantiated GameObject.
        }
    }
}
