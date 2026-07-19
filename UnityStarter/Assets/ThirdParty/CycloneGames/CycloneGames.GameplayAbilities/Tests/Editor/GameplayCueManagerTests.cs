using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GameplayCueManagerTests
    {
        private const string CueTagName = "Test.GAS.Cue.Lifecycle";
        private const string CueAssetAddress = "Cue.Asset";
        private const string CuePrefabAddress = "Cue.Prefab";

        private GameplayTag cueTag;
        private GameObject prefab;
        private GameplayCueProbeAsset cueAsset;
        private CueTestResourceLocator resourceLocator;
        private GameplayCueManager manager;
        private AbilitySystemComponent target;

        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.RegisterDynamicTag(CueTagName, "Gameplay Cue lifecycle test tag");
            GameplayTagManager.InitializeIfNeeded();
            cueTag = GameplayTagManager.RequestTag(CueTagName);

            prefab = new GameObject("GameplayCueManagerTestPrefab");
            cueAsset = ScriptableObject.CreateInstance<GameplayCueProbeAsset>();
            cueAsset.PrefabAddress = CuePrefabAddress;
            resourceLocator = new CueTestResourceLocator(cueAsset, prefab);
            manager = new GameplayCueManager(new GameObjectPoolManager.PoolConfig(
                maxAssetPools: 4,
                maxActiveLeases: 8,
                maxActiveLeasesPerPool: 4,
                maxRetainedInstancesPerPool: 2,
                minRetainedInstancesPerPool: 0,
                idleExpirationTime: 30f));
            manager.RegisterStaticCue(cueTag, CueAssetAddress);
            manager.Initialize(resourceLocator);
            target = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
        }

        [TearDown]
        public void TearDown()
        {
            target?.Dispose();
            manager?.Shutdown();
            if (cueAsset != null) UnityEngine.Object.DestroyImmediate(cueAsset);
            if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab);
        }

        [Test]
        public async Task OnActive_UsesOneLeaseAndDispatchesActivePhasesInOrder()
        {
            await manager.HandleCueAsync(cueTag, EGameplayCueEvent.OnActive, CreateParameters());

            Assert.AreEqual(1, cueAsset.CreateCount);
            Assert.AreEqual(1, cueAsset.OnActiveCount);
            Assert.AreEqual(1, cueAsset.WhileActiveCount);
            CollectionAssert.AreEqual(new[] { "Create", "OnActive", "WhileActive" }, cueAsset.EventOrder);
            Assert.AreEqual(1, resourceLocator.GameObjectLoadCount);
        }

        [Test]
        public async Task SameTagReferences_ActivateOnFirstAndRemoveOnLast()
        {
            GameplayCueParameters parameters = CreateParameters();
            await manager.HandleCueAsync(cueTag, EGameplayCueEvent.OnActive, parameters);
            await manager.HandleCueAsync(cueTag, EGameplayCueEvent.OnActive, parameters);

            Assert.AreEqual(1, cueAsset.CreateCount);
            await manager.HandleCueAsync(cueTag, EGameplayCueEvent.Removed, parameters);
            Assert.AreEqual(0, cueAsset.RemovedCount);

            await manager.HandleCueAsync(cueTag, EGameplayCueEvent.Removed, parameters);
            Assert.AreEqual(1, cueAsset.RemovedCount);
        }

        [Test]
        public async Task RemovedDuringColdLoad_InvalidatesLateActivation()
        {
            resourceLocator.DelayCueAssetLoads();
            UniTask activation = manager.HandleCueAsync(
                cueTag,
                EGameplayCueEvent.OnActive,
                CreateParameters());

            Assert.AreEqual(1, resourceLocator.CueAssetLoadCount);
            await manager.HandleCueAsync(cueTag, EGameplayCueEvent.Removed, CreateParameters());
            resourceLocator.CompleteCueAssetLoads();
            await activation;
            await UniTask.Yield();

            Assert.AreEqual(0, cueAsset.CreateCount);
            Assert.AreEqual(0, resourceLocator.GameObjectLoadCount);
        }

        [Test]
        public async Task ColdAddressLoad_IsSingleFlightForConcurrentDispatches()
        {
            resourceLocator.DelayCueAssetLoads();
            UniTask first = manager.HandleCueAsync(cueTag, EGameplayCueEvent.Executed, CreateParameters());
            UniTask second = manager.HandleCueAsync(cueTag, EGameplayCueEvent.Executed, CreateParameters());

            Assert.AreEqual(1, resourceLocator.CueAssetLoadCount);
            resourceLocator.CompleteCueAssetLoads();
            await first;
            await second;

            Assert.AreEqual(2, cueAsset.ExecutedCount);
        }

        [Test]
        public async Task PredictionRollback_RemovesOnlyMatchingTagReference()
        {
            GASPredictionKey predictionKey = new GASPredictionKey(101);
            await manager.HandleCueAsync(
                cueTag,
                EGameplayCueEvent.OnActive,
                CreateParameters(predictionKey));
            await manager.HandleCueAsync(
                cueTag,
                EGameplayCueEvent.OnActive,
                CreateParameters());

            await manager.RollbackPredictedCuesAsync(target, predictionKey);
            Assert.AreEqual(0, cueAsset.RemovedCount);

            await manager.HandleCueAsync(cueTag, EGameplayCueEvent.Removed, CreateParameters());
            Assert.AreEqual(1, cueAsset.RemovedCount);
        }

        [Test]
        public async Task PersistentHandlerWorkerFault_ReleasesAcquiredLease()
        {
            cueAsset.FailOnWorker = true;

            await manager.HandleCueAsync(cueTag, EGameplayCueEvent.OnActive, CreateParameters());

            AssertLeaseWasReturnedAfterHandlerFailure();
        }

        [Test]
        public async Task PersistentHandlerWorkerCancellation_ReleasesAcquiredLease()
        {
            cueAsset.CancelOnWorker = true;

            await manager.HandleCueAsync(cueTag, EGameplayCueEvent.OnActive, CreateParameters());

            AssertLeaseWasReturnedAfterHandlerFailure();
        }

        private void AssertLeaseWasReturnedAfterHandlerFailure()
        {
            Assert.IsTrue(cueAsset.LastLease.IsValid, "The failure must occur after acquiring a lease.");
            Assert.IsFalse(cueAsset.WorkerFailureWasMainThread, "The injected failure must originate on a worker thread.");
            Assert.IsNotNull(cueAsset.LastPoolManager);
            Assert.IsFalse(cueAsset.LastPoolManager.IsLeaseOutstanding(cueAsset.LastLease));
            Assert.AreEqual(
                0,
                ((GameObjectPoolManager)cueAsset.LastPoolManager).GetStatistics(CuePrefabAddress).ActiveCount);
        }

        private GameplayCueParameters CreateParameters(GASPredictionKey predictionKey = default)
        {
            return new GameplayCueParameters(new GameplayCueEventParams(
                source: null,
                target: target,
                effectDefinition: null,
                sourceObject: null,
                targetObject: null,
                effectLevel: 1,
                effectDurationRaw: GASFixedValue.One.RawValue,
                predictionKey: predictionKey));
        }
    }

    public sealed class GameplayCueProbeAsset : GameplayCueSO, IPersistentGameplayCue
    {
        public string PrefabAddress;
        public int CreateCount;
        public int OnActiveCount;
        public int WhileActiveCount;
        public int RemovedCount;
        public int ExecutedCount;
        public bool FailOnWorker;
        public bool CancelOnWorker;
        public bool WorkerFailureWasMainThread;
        public GameObjectLease LastLease;
        public IGameObjectPoolManager LastPoolManager;
        public readonly List<string> EventOrder = new List<string>();

        public async UniTask<GameObjectLease> CreateInstanceAsync(
            GameplayCueParameters parameters,
            IGameObjectPoolManager poolManager,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            EventOrder.Add("Create");
            GameObjectLease lease = await poolManager.GetAsync(
                PrefabAddress,
                Vector3.zero,
                Quaternion.identity,
                cancellationToken: cancellationToken);
            LastLease = lease;
            LastPoolManager = poolManager;
            return lease;
        }

        public async UniTask OnActiveAsync(
            GameObject instance,
            GameplayCueParameters parameters,
            CancellationToken cancellationToken = default)
        {
            OnActiveCount++;
            EventOrder.Add("OnActive");
            if (!FailOnWorker) return;

            await UniTask.SwitchToThreadPool();
            WorkerFailureWasMainThread = PlayerLoopHelper.IsMainThread;
            throw new InvalidOperationException("Injected worker failure.");
        }

        public async UniTask OnWhileActiveAsync(
            GameObject instance,
            GameplayCueParameters parameters,
            CancellationToken cancellationToken = default)
        {
            WhileActiveCount++;
            EventOrder.Add("WhileActive");
            if (!CancelOnWorker) return;

            await UniTask.SwitchToThreadPool();
            WorkerFailureWasMainThread = PlayerLoopHelper.IsMainThread;
            throw new OperationCanceledException("Injected worker cancellation.", cancellationToken);
        }

        public UniTask OnRemovedAsync(
            GameObject instance,
            GameplayCueParameters parameters,
            CancellationToken cancellationToken = default)
        {
            RemovedCount++;
            EventOrder.Add("Removed");
            return UniTask.CompletedTask;
        }

        public override UniTask OnExecutedAsync(
            GameplayCueParameters parameters,
            IGameObjectPoolManager poolManager,
            CancellationToken cancellationToken = default)
        {
            ExecutedCount++;
            return UniTask.CompletedTask;
        }
    }

    internal sealed class CueTestResourceLocator : IResourceLocator
    {
        private readonly GameplayCueSO cueAsset;
        private readonly GameObject prefab;
        private UniTaskCompletionSource<bool> cueLoadGate;

        public int CueAssetLoadCount { get; private set; }
        public int GameObjectLoadCount { get; private set; }

        public CueTestResourceLocator(GameplayCueSO cueAsset, GameObject prefab)
        {
            this.cueAsset = cueAsset;
            this.prefab = prefab;
        }

        public void DelayCueAssetLoads()
        {
            cueLoadGate = new UniTaskCompletionSource<bool>();
        }

        public void CompleteCueAssetLoads()
        {
            cueLoadGate?.TrySetResult(true);
        }

        public async UniTask<IResourceHandle<T>> LoadAssetAsync<T>(
            string key,
            string bucket = null,
            string cacheTag = null,
            string cacheOwner = null,
            CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            if (typeof(T) == typeof(GameplayCueSO))
            {
                CueAssetLoadCount++;
                if (cueLoadGate != null)
                {
                    await cueLoadGate.Task;
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new CueTestResourceHandle<T>(cueAsset as T);
            }

            if (typeof(T) == typeof(GameObject))
            {
                GameObjectLoadCount++;
                cancellationToken.ThrowIfCancellationRequested();
                return new CueTestResourceHandle<T>(prefab as T);
            }

            return null;
        }

        private sealed class CueTestResourceHandle<T> : IResourceHandle<T>
            where T : UnityEngine.Object
        {
            public T Asset { get; }

            public CueTestResourceHandle(T asset)
            {
                Asset = asset;
            }

            public void Dispose() { }
        }
    }
}
