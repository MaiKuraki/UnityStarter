using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using CycloneGames.GameplayAbilities.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GameObjectPoolManagerTests
    {
        private readonly List<GameObjectPoolManager> managers = new List<GameObjectPoolManager>();
        private GameObject prefab;
        private TestResourceLocator resourceLocator;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("GameplayCuePoolTestPrefab");
            resourceLocator = new TestResourceLocator(prefab);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < managers.Count; i++)
            {
                managers[i].Shutdown();
            }

            managers.Clear();
            if (prefab != null)
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void Constructor_RejectsUnboundedDefaultConfiguration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameObjectPoolManager(resourceLocator, default));
        }

        [Test]
        public void Release_RejectsForeignAndAlreadyReturnedLeases()
        {
            GameObjectPoolManager manager = CreateManager(CreateConfig());
            GameObjectPoolManager foreignManager = CreateManager(CreateConfig());
            GameObjectLease lease = Get(manager, "Cue.Vfx");
            GameObjectLease foreignLease = Get(foreignManager, "Cue.Vfx");

            Assert.IsNotNull(lease.Instance);
            Assert.IsTrue(manager.IsLeaseOutstanding(lease));
            Assert.Throws<ArgumentException>(() => manager.Release(default));
            Assert.Throws<ArgumentException>(() => manager.Release(foreignLease));
            manager.Release(lease);
            Assert.Throws<InvalidOperationException>(() => _ = lease.Instance);
            Assert.IsFalse(manager.IsLeaseOutstanding(lease));
            Assert.Throws<InvalidOperationException>(() => manager.Release(lease));
            foreignManager.Release(foreignLease);
        }

        [Test]
        public void Get_EnforcesGlobalPerPoolAndAssetPoolCapacities()
        {
            GameObjectPoolManager perPoolManager = CreateManager(new GameObjectPoolManager.PoolConfig(
                maxAssetPools: 2,
                maxActiveLeases: 2,
                maxActiveLeasesPerPool: 1,
                maxRetainedInstancesPerPool: 1,
                minRetainedInstancesPerPool: 0,
                idleExpirationTime: 30f));

            GameObjectLease first = Get(perPoolManager, "Cue.A");
            Assert.Throws<InvalidOperationException>(() => Get(perPoolManager, "Cue.A"));

            GameObjectPoolManager assetPoolManager = CreateManager(new GameObjectPoolManager.PoolConfig(
                maxAssetPools: 1,
                maxActiveLeases: 2,
                maxActiveLeasesPerPool: 2,
                maxRetainedInstancesPerPool: 1,
                minRetainedInstancesPerPool: 0,
                idleExpirationTime: 30f));

            GameObjectLease active = Get(assetPoolManager, "Cue.A");
            Assert.Throws<InvalidOperationException>(() => Get(assetPoolManager, "Cue.B"));

            perPoolManager.Release(first);
            assetPoolManager.Release(active);
        }

        [Test]
        public void Prewarm_IsBoundedCancelableAndReusable()
        {
            GameObjectPoolManager manager = CreateManager(CreateConfig(maxRetainedInstancesPerPool: 2));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                manager.PrewarmPoolAsync("Cue.Vfx", 3).GetAwaiter().GetResult());

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    manager.PrewarmPoolAsync("Cue.Vfx", 1, cancellationToken: cancellation.Token)
                        .GetAwaiter()
                        .GetResult());
            }

            manager.PrewarmPoolAsync("Cue.Vfx", 2).GetAwaiter().GetResult();
            PoolStatistics prewarmed = manager.GetStatistics("Cue.Vfx");
            Assert.AreEqual(2, prewarmed.CurrentPoolSize);

            GameObjectLease lease = Get(manager, "Cue.Vfx");
            manager.Release(lease);

            PoolStatistics reused = manager.GetStatistics("Cue.Vfx");
            Assert.AreEqual(2, reused.CurrentPoolSize);
            Assert.AreEqual(1, reused.TotalHits);
            Assert.AreEqual(1, reused.TotalGets);
        }

        [Test]
        public void Release_EnforcesGlobalRetainedCapacityAcrossAssetPools()
        {
            GameObjectPoolManager manager = CreateManager(new GameObjectPoolManager.PoolConfig(
                maxAssetPools: 2,
                maxActiveLeases: 2,
                maxActiveLeasesPerPool: 1,
                maxRetainedInstancesPerPool: 2,
                minRetainedInstancesPerPool: 0,
                idleExpirationTime: 30f,
                maxTotalRetainedInstances: 1));

            GameObjectLease first = Get(manager, "Cue.A");
            GameObjectLease second = Get(manager, "Cue.B");

            manager.Release(first);
            manager.Release(second);

            PoolStatistics firstPool = manager.GetStatistics("Cue.A");
            PoolStatistics secondPool = manager.GetStatistics("Cue.B");
            Assert.AreEqual(1, firstPool.CurrentPoolSize);
            Assert.AreEqual(0, secondPool.CurrentPoolSize);
            Assert.AreEqual(1, secondPool.TotalDestroyed);
        }

        [Test]
        public void Prewarm_ReservesGlobalCapacityAcrossPendingLoadsAndReleasesItOnCancellation()
        {
            var deferredLocator = new DeferredResourceLocator();
            GameObjectPoolManager manager = CreateManager(
                deferredLocator,
                new GameObjectPoolManager.PoolConfig(
                    maxAssetPools: 2,
                    maxActiveLeases: 2,
                    maxActiveLeasesPerPool: 1,
                    maxRetainedInstancesPerPool: 1,
                    minRetainedInstancesPerPool: 0,
                    idleExpirationTime: 30f,
                    maxTotalRetainedInstances: 1));

            using (var firstCancellation = new CancellationTokenSource())
            {
                UniTask firstPrewarm = manager.PrewarmPoolAsync(
                    "Cue.A",
                    1,
                    cancellationToken: firstCancellation.Token);

                Assert.Throws<InvalidOperationException>(() =>
                    manager.PrewarmPoolAsync("Cue.B", 1).GetAwaiter().GetResult());

                firstCancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    firstPrewarm.GetAwaiter().GetResult());
            }

            using (var secondCancellation = new CancellationTokenSource())
            {
                UniTask secondPrewarm = manager.PrewarmPoolAsync(
                    "Cue.B",
                    1,
                    cancellationToken: secondCancellation.Token);

                secondCancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    secondPrewarm.GetAwaiter().GetResult());
            }
        }

        [Test]
        public void Prewarm_InFlightPoolCannotBeDuplicatedClearedOrEvicted()
        {
            var deferredLocator = new DeferredResourceLocator();
            GameObjectPoolManager manager = CreateManager(
                deferredLocator,
                new GameObjectPoolManager.PoolConfig(
                    maxAssetPools: 1,
                    maxActiveLeases: 2,
                    maxActiveLeasesPerPool: 1,
                    maxRetainedInstancesPerPool: 1,
                    minRetainedInstancesPerPool: 0,
                    idleExpirationTime: 30f,
                    maxTotalRetainedInstances: 1));

            using (var firstCancellation = new CancellationTokenSource())
            {
                UniTask firstPrewarm = manager.PrewarmPoolAsync(
                    "Cue.A",
                    1,
                    cancellationToken: firstCancellation.Token);

                Assert.Throws<InvalidOperationException>(() =>
                    manager.PrewarmPoolAsync("Cue.A", 1).GetAwaiter().GetResult());
                Assert.Throws<InvalidOperationException>(() => manager.ClearPool("Cue.A"));
                Assert.Throws<InvalidOperationException>(() => manager.AggressiveShrink("Cue.A"));
                Assert.DoesNotThrow(() => manager.AggressiveShrink());
                Assert.Throws<InvalidOperationException>(() =>
                    manager.GetAsync("Cue.B", Vector3.zero, Quaternion.identity).GetAwaiter().GetResult());

                firstCancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => firstPrewarm.GetAwaiter().GetResult());
            }

            using (var secondCancellation = new CancellationTokenSource())
            {
                UniTask secondPrewarm = manager.PrewarmPoolAsync(
                    "Cue.B",
                    1,
                    cancellationToken: secondCancellation.Token);
                secondCancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => secondPrewarm.GetAwaiter().GetResult());
            }
        }

        [Test]
        public async Task WorkerCancellation_ReleasesReservationsWithoutCancelingSharedLoad()
        {
            var controlledLocator = new ControlledResourceLocator(prefab);
            GameObjectPoolManager manager = CreateManager(
                controlledLocator,
                new GameObjectPoolManager.PoolConfig(
                    maxAssetPools: 2,
                    maxActiveLeases: 2,
                    maxActiveLeasesPerPool: 2,
                    maxRetainedInstancesPerPool: 1,
                    minRetainedInstancesPerPool: 0,
                    idleExpirationTime: 30f,
                    maxTotalRetainedInstances: 1));

            using (var firstCancellation = new CancellationTokenSource())
            using (var getCancellation = new CancellationTokenSource())
            using (var secondCancellation = new CancellationTokenSource())
            {
                UniTask firstPrewarm = manager.PrewarmPoolAsync(
                    "Cue.Shared",
                    1,
                    cancellationToken: firstCancellation.Token);
                UniTask<GameObjectLease> canceledGet = manager.GetAsync(
                    "Cue.Shared",
                    Vector3.zero,
                    Quaternion.identity,
                    cancellationToken: getCancellation.Token);
                UniTask<GameObjectLease> sharedGet = manager.GetAsync(
                    "Cue.Shared",
                    Vector3.zero,
                    Quaternion.identity);

                Assert.AreEqual(1, controlledLocator.LoadCount);
                Assert.AreEqual(2, manager.GetStatistics("Cue.Shared").PendingCount);

                await Task.Run(() =>
                {
                    firstCancellation.Cancel();
                    getCancellation.Cancel();
                });
                await AssertCanceledAsync(firstPrewarm);
                await AssertCanceledAsync(canceledGet);

                Assert.IsFalse(
                    controlledLocator.UnderlyingCancellationRequested,
                    "Canceling one waiter must not cancel a load that still has another waiter.");
                Assert.AreEqual(1, manager.GetStatistics("Cue.Shared").PendingCount);

                UniTask secondPrewarm = manager.PrewarmPoolAsync(
                    "Cue.Shared",
                    1,
                    cancellationToken: secondCancellation.Token);

                controlledLocator.CompleteLoad();
                GameObjectLease lease = await sharedGet;
                await secondPrewarm;

                PoolStatistics completed = manager.GetStatistics("Cue.Shared");
                Assert.AreEqual(1, controlledLocator.LoadCount, "The shared load must remain single-flight.");
                Assert.IsFalse(controlledLocator.UnderlyingCancellationRequested);
                Assert.AreEqual(0, completed.PendingCount);
                Assert.AreEqual(1, completed.ActiveCount);
                Assert.AreEqual(1, completed.CurrentPoolSize);

                GameObjectLease globalCapacityProbe = await manager.GetAsync(
                    "Cue.Other",
                    Vector3.zero,
                    Quaternion.identity);
                Assert.AreEqual(
                    1,
                    manager.GetStatistics("Cue.Other").ActiveCount,
                    "A canceled Get must return its global pending-lease reservation.");

                manager.Release(lease);
                manager.Release(globalCapacityProbe);
                Assert.AreEqual(0, manager.GetStatistics("Cue.Shared").ActiveCount);
                Assert.AreEqual(0, manager.GetStatistics("Cue.Other").ActiveCount);
            }
        }

        [Test]
        public void Shutdown_ReleasesHandlesAndRejectsFurtherOperations()
        {
            GameObjectPoolManager manager = CreateManager(CreateConfig());
            GameObjectLease activeLease = Get(manager, "Cue.Vfx");
            GameObject outstandingInstance = activeLease.Instance;

            manager.Shutdown();

            Assert.AreEqual(1, resourceLocator.DisposedHandleCount);
            Assert.IsTrue(outstandingInstance == null, "Shutdown must destroy outstanding leased instances.");
            Assert.Throws<ObjectDisposedException>(() => _ = activeLease.Instance);
            Assert.IsFalse(manager.IsLeaseOutstanding(activeLease));
            Assert.Throws<ObjectDisposedException>(() => Get(manager, "Cue.Vfx"));
            Assert.Throws<ObjectDisposedException>(() => manager.Release(activeLease));
            Assert.Throws<ObjectDisposedException>(() => _ = manager.ResourceLocator);
        }

        private GameObjectPoolManager CreateManager(GameObjectPoolManager.PoolConfig config)
        {
            return CreateManager(resourceLocator, config);
        }

        private GameObjectPoolManager CreateManager(
            IResourceLocator locator,
            GameObjectPoolManager.PoolConfig config)
        {
            var manager = new GameObjectPoolManager(locator, config);
            managers.Add(manager);
            return manager;
        }

        [Test]
        public void Release_RejectsStaleGenerationAfterSameInstanceIsRentedAgain()
        {
            GameObjectPoolManager manager = CreateManager(CreateConfig(maxRetainedInstancesPerPool: 1));
            GameObjectLease first = Get(manager, "Cue.Vfx");
            GameObject instance = first.Instance;
            manager.Release(first);

            GameObjectLease second = Get(manager, "Cue.Vfx");
            Assert.AreSame(instance, second.Instance);
            Assert.Greater(second.Generation, first.Generation);
            Assert.Throws<InvalidOperationException>(() => _ = first.Instance);
            Assert.IsFalse(manager.IsLeaseOutstanding(first));
            Assert.IsTrue(manager.IsLeaseOutstanding(second));
            Assert.Throws<InvalidOperationException>(() => manager.Release(first));
            Assert.AreEqual(1, manager.GetStatistics("Cue.Vfx").ActiveCount);

            manager.Release(second);
            Assert.AreEqual(0, manager.GetStatistics("Cue.Vfx").ActiveCount);
        }

        [Test]
        public void Release_InvokesLifecycleAndRestoresPrefabScaleBeforeReuse()
        {
            prefab.transform.localScale = new Vector3(2f, 3f, 4f);
            prefab.AddComponent<PoolLifecycleProbe>();
            GameObjectPoolManager manager = CreateManager(CreateConfig(maxRetainedInstancesPerPool: 1));

            GameObjectLease first = Get(manager, "Cue.Vfx");
            GameObject firstInstance = first.Instance;
            PoolLifecycleProbe firstProbe = firstInstance.GetComponent<PoolLifecycleProbe>();
            firstProbe.State = 42;
            firstInstance.transform.localScale = Vector3.one * 9f;

            manager.Release(first);
            Assert.AreEqual(0, firstProbe.State);
            Assert.AreEqual(1, firstProbe.ReturnCount);
            Assert.Throws<InvalidOperationException>(() => _ = first.Instance);

            GameObjectLease second = Get(manager, "Cue.Vfx");
            PoolLifecycleProbe secondProbe = second.Instance.GetComponent<PoolLifecycleProbe>();
            Assert.AreSame(firstInstance, second.Instance);
            Assert.AreEqual(new Vector3(2f, 3f, 4f), second.Instance.transform.localScale);
            Assert.AreEqual(2, secondProbe.RentCount);
            Assert.AreEqual(0, secondProbe.State);

            manager.Release(second);
        }

        [Test]
        public void Release_QuarantinesInstanceWhenReturnLifecycleFails()
        {
            prefab.AddComponent<PoolLifecycleProbe>();
            GameObjectPoolManager manager = CreateManager(CreateConfig(maxRetainedInstancesPerPool: 1));
            GameObjectLease lease = Get(manager, "Cue.Vfx");
            GameObject failedInstance = lease.Instance;
            failedInstance.GetComponent<PoolLifecycleProbe>().ThrowOnReturn = true;

            Assert.Throws<InvalidOperationException>(() => manager.Release(lease));
            Assert.IsTrue(failedInstance == null);

            PoolStatistics afterFailure = manager.GetStatistics("Cue.Vfx");
            Assert.AreEqual(0, afterFailure.ActiveCount);
            Assert.AreEqual(0, afterFailure.CurrentPoolSize);
            Assert.AreEqual(1, afterFailure.TotalDestroyed);

            GameObjectLease replacement = Get(manager, "Cue.Vfx");
            Assert.IsNotNull(replacement.Instance);
            manager.Release(replacement);
        }

        [Test]
        public void IsLeaseOutstanding_RejectsForeignReturnedAndStaleLeases()
        {
            GameObjectPoolManager manager = CreateManager(CreateConfig(maxRetainedInstancesPerPool: 1));
            GameObjectPoolManager foreign = CreateManager(CreateConfig(maxRetainedInstancesPerPool: 1));
            GameObjectLease first = Get(manager, "Cue.Vfx");
            GameObjectLease foreignLease = Get(foreign, "Cue.Vfx");

            Assert.IsTrue(manager.IsLeaseOutstanding(first));
            Assert.IsFalse(manager.IsLeaseOutstanding(foreignLease));
            manager.Release(first);
            Assert.IsFalse(manager.IsLeaseOutstanding(first));

            GameObjectLease second = Get(manager, "Cue.Vfx");
            Assert.IsFalse(manager.IsLeaseOutstanding(first));
            Assert.IsTrue(manager.IsLeaseOutstanding(second));
            Assert.IsNotNull(second.Instance);

            manager.Release(second);
            foreign.Release(foreignLease);
        }

        [Test]
        public void Instance_RejectsWorkerThreadWithoutInvalidatingActiveLease()
        {
            GameObjectPoolManager manager = CreateManager(CreateConfig());
            GameObjectLease lease = Get(manager, "Cue.Vfx");
            GameObject expected = lease.Instance;

            Exception workerFailure = RunOnWorkerThread(() => _ = lease.Instance);

            Assert.That(workerFailure, Is.TypeOf<InvalidOperationException>());
            Assert.IsTrue(manager.IsLeaseOutstanding(lease));
            Assert.AreSame(expected, lease.Instance);

            manager.Release(lease);
            Assert.Throws<InvalidOperationException>(() => _ = lease.Instance);
        }

        private sealed class PoolLifecycleProbe : MonoBehaviour, IGameObjectPoolLifecycle
        {
            public int State;
            public int RentCount;
            public int ReturnCount;
            public bool ThrowOnReturn;

            public void OnRentFromPool()
            {
                RentCount++;
            }

            public void OnReturnToPool()
            {
                ReturnCount++;
                State = 0;
                if (ThrowOnReturn)
                {
                    throw new InvalidOperationException("Return reset failed.");
                }
            }
        }

        private static GameObjectLease Get(GameObjectPoolManager manager, string assetKey)
        {
            return manager.GetAsync(assetKey, Vector3.zero, Quaternion.identity)
                .GetAwaiter()
                .GetResult();
        }

        private static Exception RunOnWorkerThread(Action action)
        {
            Exception exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception caught)
                {
                    exception = caught;
                }
            })
            {
                IsBackground = true
            };

            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The lease validation worker did not finish within the timeout.");
            }

            return exception;
        }

        private static async Task AssertCanceledAsync(UniTask task)
        {
            try
            {
                await task;
                Assert.Fail("Expected the operation to be canceled.");
            }
            catch (OperationCanceledException)
            {
                Assert.IsTrue(
                    PlayerLoopHelper.IsMainThread,
                    "Cancellation cleanup must complete on the Unity main thread.");
            }
        }

        private static async Task AssertCanceledAsync<T>(UniTask<T> task)
        {
            try
            {
                await task;
                Assert.Fail("Expected the operation to be canceled.");
            }
            catch (OperationCanceledException)
            {
                Assert.IsTrue(
                    PlayerLoopHelper.IsMainThread,
                    "Cancellation cleanup must complete on the Unity main thread.");
            }
        }

        private static GameObjectPoolManager.PoolConfig CreateConfig(int maxRetainedInstancesPerPool = 4)
        {
            return new GameObjectPoolManager.PoolConfig(
                maxAssetPools: 4,
                maxActiveLeases: 8,
                maxActiveLeasesPerPool: 4,
                maxRetainedInstancesPerPool: maxRetainedInstancesPerPool,
                minRetainedInstancesPerPool: 0,
                idleExpirationTime: 30f);
        }

        private sealed class TestResourceLocator : IResourceLocator
        {
            private readonly GameObject asset;

            public int DisposedHandleCount { get; private set; }

            public TestResourceLocator(GameObject asset)
            {
                this.asset = asset;
            }

            public UniTask<IResourceHandle<T>> LoadAssetAsync<T>(
                string key,
                string bucket = null,
                string cacheTag = null,
                string cacheOwner = null,
                CancellationToken cancellationToken = default)
                where T : UnityEngine.Object
            {
                cancellationToken.ThrowIfCancellationRequested();
                var handle = new TestResourceHandle<T>(asset as T, this);
                return UniTask.FromResult<IResourceHandle<T>>(handle);
            }

            private void NotifyDisposed()
            {
                DisposedHandleCount++;
            }

            private sealed class TestResourceHandle<T> : IResourceHandle<T>
                where T : UnityEngine.Object
            {
                private readonly TestResourceLocator owner;
                private bool disposed;

                public T Asset { get; }

                public TestResourceHandle(T asset, TestResourceLocator owner)
                {
                    Asset = asset;
                    this.owner = owner;
                }

                public void Dispose()
                {
                    if (disposed) return;
                    disposed = true;
                    owner.NotifyDisposed();
                }
            }
        }

        private sealed class DeferredResourceLocator : IResourceLocator
        {
            public UniTask<IResourceHandle<T>> LoadAssetAsync<T>(
                string key,
                string bucket = null,
                string cacheTag = null,
                string cacheOwner = null,
                CancellationToken cancellationToken = default)
                where T : UnityEngine.Object
            {
                var completion = new UniTaskCompletionSource<IResourceHandle<T>>();
                return completion.Task.AttachExternalCancellation(cancellationToken);
            }
        }

        private sealed class ControlledResourceLocator : IResourceLocator
        {
            private readonly GameObject asset;
            private readonly UniTaskCompletionSource<bool> loadGate = new UniTaskCompletionSource<bool>();
            private int underlyingCancellationRequested;

            public int LoadCount { get; private set; }
            public bool UnderlyingCancellationRequested => Volatile.Read(ref underlyingCancellationRequested) != 0;

            public ControlledResourceLocator(GameObject asset)
            {
                this.asset = asset;
            }

            public void CompleteLoad()
            {
                loadGate.TrySetResult(true);
            }

            public async UniTask<IResourceHandle<T>> LoadAssetAsync<T>(
                string key,
                string bucket = null,
                string cacheTag = null,
                string cacheOwner = null,
                CancellationToken cancellationToken = default)
                where T : UnityEngine.Object
            {
                LoadCount++;
                using (cancellationToken.Register(() =>
                           Interlocked.Exchange(ref underlyingCancellationRequested, 1)))
                {
                    await loadGate.Task.AttachExternalCancellation(cancellationToken);
                }

                return new ControlledResourceHandle<T>(asset as T);
            }

            private sealed class ControlledResourceHandle<T> : IResourceHandle<T>
                where T : UnityEngine.Object
            {
                public T Asset { get; }

                public ControlledResourceHandle(T asset)
                {
                    Asset = asset;
                }

                public void Dispose() { }
            }
        }
    }
}
