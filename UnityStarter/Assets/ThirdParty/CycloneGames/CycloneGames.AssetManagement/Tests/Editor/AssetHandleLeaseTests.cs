using System;
using System.Reflection;
using System.Threading;

using UnityEngine;

using Cysharp.Threading.Tasks;
using NUnit.Framework;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetHandleLeaseTests
    {
        private const string RECOVERABLE_BROADCAST_FAILURE = "recoverable broadcast failure";

        [Test]
        public void Dispose_Is_Idempotent_And_Invalidates_Access()
        {
            var backend = new RecordingHandle();
            IAssetHandle<Texture2D> lease = AssetHandleLeases.Create(backend);

            lease.Dispose();
            lease.Dispose();

            Assert.AreEqual(1, backend.DisposeCallCount);
            Assert.Throws<ObjectDisposedException>(() => _ = lease.Asset);
        }

        [Test]
        public void Caller_Cancellation_Does_Not_Release_Shared_Backend()
        {
            var backend = new RecordingHandle(completed: false);
            using var cancellation = new CancellationTokenSource();
            IAssetHandle<Texture2D> lease = AssetHandleLeases.Create(backend, cancellation.Token);

            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await lease.Task.AsTask());
            Assert.AreEqual(0, backend.DisposeCallCount);

            lease.Dispose();
            Assert.AreEqual(1, backend.DisposeCallCount);
        }

        [Test]
        public void Backend_Can_Be_Unwrapped_Only_Through_Internal_Lease_Boundary()
        {
            var backend = new RecordingHandle();
            IAssetHandle<Texture2D> lease = AssetHandleLeases.Create(backend);

            bool found = ((IAssetHandleLease)lease).TryGetBackend(out RecordingHandle resolved);

            Assert.IsTrue(found);
            Assert.AreSame(backend, resolved);
            lease.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task Lease_Task_Supports_Repeated_Awaits()
        {
            var backend = new RecordingHandle();
            IAssetHandle<Texture2D> lease = AssetHandleLeases.Create(backend);

            await lease.Task;
            await lease.Task;

            lease.Dispose();
            Assert.AreEqual(1, backend.DisposeCallCount);
        }

        [Test]
        public async System.Threading.Tasks.Task Two_Leases_Can_Concurrently_Await_One_Pending_Backend()
        {
            var backend = new RecordingHandle(completed: false);
            IAssetHandle<Texture2D> firstLease = AssetHandleLeases.Create(backend);
            IAssetHandle<Texture2D> secondLease = AssetHandleLeases.Create(backend);

            System.Threading.Tasks.Task firstWait = AwaitSuccessAsync(firstLease.Task);
            System.Threading.Tasks.Task repeatedFirstWait = AwaitSuccessAsync(firstLease.Task);
            System.Threading.Tasks.Task secondWait = AwaitSuccessAsync(secondLease.Task);
            backend.Succeed();

            await firstWait;
            await repeatedFirstWait;
            await secondWait;
            Assert.IsTrue(firstLease.IsDone);
            Assert.IsTrue(secondLease.IsDone);

            firstLease.Dispose();
            secondLease.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task Two_Leases_Receive_The_Same_Pending_Backend_Failure()
        {
            var backend = new RecordingHandle(completed: false);
            IAssetHandle<Texture2D> firstLease = AssetHandleLeases.Create(backend);
            IAssetHandle<Texture2D> secondLease = AssetHandleLeases.Create(backend);

            System.Threading.Tasks.Task<Exception> firstWait = CaptureFailureAsync(firstLease.Task);
            System.Threading.Tasks.Task<Exception> secondWait = CaptureFailureAsync(secondLease.Task);
            backend.Fail(new InvalidOperationException("provider broadcast failed"));

            Exception firstFailure = await firstWait;
            Exception secondFailure = await secondWait;
            Assert.IsInstanceOf<InvalidOperationException>(firstFailure);
            Assert.IsInstanceOf<InvalidOperationException>(secondFailure);
            Assert.AreEqual("provider broadcast failed", firstFailure.Message);
            Assert.AreEqual(firstFailure.Message, secondFailure.Message);

            firstLease.Dispose();
            secondLease.Dispose();
        }

        [Test]
        public async System.Threading.Tasks.Task Generic_Broadcast_Supports_Concurrent_Awaiters()
        {
            var provider = new SingleContinuationSource<int>();
            UniTask<int> broadcast = AssetOperationBroadcast.Create(provider.Task);

            System.Threading.Tasks.Task<int> firstWait = AwaitResultAsync(broadcast);
            System.Threading.Tasks.Task<int> secondWait = AwaitResultAsync(broadcast);
            provider.Succeed(42);

            Assert.AreEqual(42, await firstWait);
            Assert.AreEqual(42, await secondWait);
        }

        [Test]
        public async System.Threading.Tasks.Task Broadcast_Forwards_Fatal_Exception_Without_Remaining_Pending()
        {
            var fatal = new OutOfMemoryException("fatal provider failure");
            UniTask broadcast = AssetOperationBroadcast.Create(UniTask.FromException(fatal));

            Exception observed = await CaptureFailureAsync(broadcast);

            Assert.AreSame(fatal, observed);
            Assert.AreEqual(UniTaskStatus.Faulted, broadcast.Status);
        }

        [Test]
        public async System.Threading.Tasks.Task Generic_Broadcast_Forwards_Fatal_Exception_Without_Remaining_Pending()
        {
            var fatal = new OutOfMemoryException("fatal generic provider failure");
            UniTask<int> broadcast = AssetOperationBroadcast.Create(UniTask.FromException<int>(fatal));

            Exception observed = await CaptureFailureAsync(broadcast);

            Assert.AreSame(fatal, observed);
            Assert.AreEqual(UniTaskStatus.Faulted, broadcast.Status);
        }

        [Test]
        public async System.Threading.Tasks.Task Caller_View_Cancellation_Does_Not_Cancel_Shared_Broadcast()
        {
            var provider = new SingleContinuationSource();
            UniTask broadcast = AssetOperationBroadcast.Create(provider.Task);
            using var cancellation = new CancellationTokenSource();

            System.Threading.Tasks.Task<Exception> cancelledWait = CaptureFailureAsync(
                AssetOperationBroadcast.CreateCallerView(broadcast, cancellation.Token));
            System.Threading.Tasks.Task survivorWait = AwaitSuccessAsync(broadcast);
            cancellation.Cancel();

            Exception cancellationFailure = await cancelledWait;
            Assert.IsInstanceOf<OperationCanceledException>(cancellationFailure);
            Assert.AreEqual(UniTaskStatus.Pending, broadcast.Status);

            System.Threading.Tasks.Task lateWait = AwaitSuccessAsync(
                AssetOperationBroadcast.CreateCallerView(broadcast, default));
            provider.Succeed();

            await survivorWait;
            await lateWait;
        }

        [Test]
        public async System.Threading.Tasks.Task Recoverable_Fault_Is_Marked_Observed_Before_Caller_Await_And_Remains_Memoized()
        {
            var nonGenericFailure = new InvalidOperationException(RECOVERABLE_BROADCAST_FAILURE);
            var genericFailure = new InvalidOperationException(RECOVERABLE_BROADCAST_FAILURE);
            UniTask broadcast = AssetOperationBroadcast.Create(UniTask.FromException(nonGenericFailure));
            UniTask<int> genericBroadcast = AssetOperationBroadcast.Create(
                UniTask.FromException<int>(genericFailure));

            Assert.AreEqual(UniTaskStatus.Faulted, broadcast.Status);
            Assert.AreEqual(UniTaskStatus.Faulted, genericBroadcast.Status);
            AssertUniTaskExceptionMarkedObserved(broadcast);
            AssertUniTaskExceptionMarkedObserved(genericBroadcast);

            Exception firstFailure = await CaptureFailureAsync(broadcast);
            Exception repeatedFailure = await CaptureFailureAsync(broadcast);
            Exception firstGenericFailure = await CaptureFailureAsync(genericBroadcast);
            Exception repeatedGenericFailure = await CaptureFailureAsync(genericBroadcast);

            Assert.AreSame(nonGenericFailure, firstFailure);
            Assert.AreSame(firstFailure, repeatedFailure);
            Assert.AreSame(genericFailure, firstGenericFailure);
            Assert.AreSame(firstGenericFailure, repeatedGenericFailure);
        }

        private static void AssertUniTaskExceptionMarkedObserved<TUniTask>(TUniTask task)
        {
            const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo sourceField = typeof(TUniTask).GetField("source", PRIVATE_INSTANCE);
            Assert.NotNull(
                sourceField,
                "The pinned UniTask layout changed: the task source field was not found.");

            object source = sourceField.GetValue(task);
            Assert.NotNull(source, "The broadcast must use a UniTask completion source.");

            FieldInfo exceptionField = source.GetType().GetField("exception", PRIVATE_INSTANCE);
            Assert.NotNull(
                exceptionField,
                "The pinned UniTask completion-source layout changed: the exception holder was not found.");

            object exceptionHolder = exceptionField.GetValue(source);
            Assert.NotNull(exceptionHolder, "The faulted broadcast must retain its exception holder.");

            FieldInfo calledGetField = exceptionHolder.GetType().GetField("calledGet", PRIVATE_INSTANCE);
            Assert.NotNull(
                calledGetField,
                "The pinned UniTask exception-holder layout changed: the observation flag was not found.");
            Assert.IsTrue(
                (bool)calledGetField.GetValue(exceptionHolder),
                "AssetOperationBroadcast must mark a recoverable fault observed before any caller awaits it.");
        }

        private static async System.Threading.Tasks.Task AwaitSuccessAsync(UniTask task)
        {
            await task;
        }

        private static async System.Threading.Tasks.Task<Exception> CaptureFailureAsync(UniTask task)
        {
            try
            {
                await task;
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static async System.Threading.Tasks.Task<Exception> CaptureFailureAsync<T>(UniTask<T> task)
        {
            try
            {
                await task;
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static async System.Threading.Tasks.Task<T> AwaitResultAsync<T>(UniTask<T> task)
        {
            return await task;
        }

        private sealed class RecordingHandle : IAssetHandle<Texture2D>
        {
            private readonly SingleContinuationSource _providerCompletion;
            private readonly UniTask _task;

            public RecordingHandle(bool completed = true)
            {
                _providerCompletion = new SingleContinuationSource();
                _task = AssetOperationBroadcast.Create(_providerCompletion.Task);
                if (completed)
                {
                    _providerCompletion.Succeed();
                }
            }

            public int DisposeCallCount { get; private set; }
            public Texture2D Asset => null;
            public UnityEngine.Object AssetObject => null;
            public bool IsDone => Task.Status != UniTaskStatus.Pending;
            public float Progress => IsDone ? 1f : 0f;
            public string Error => null;
            public UniTask Task => _task;

            public void Succeed()
            {
                _providerCompletion.Succeed();
            }

            public void Fail(Exception exception)
            {
                _providerCompletion.Fail(exception);
            }

            public void WaitForAsyncComplete()
            {
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }

        private sealed class SingleContinuationSource : IUniTaskSource
        {
            private UniTaskCompletionSourceCore<AsyncUnit> _core;

            public UniTask Task => new UniTask(this, _core.Version);

            public void Succeed()
            {
                _core.TrySetResult(AsyncUnit.Default);
            }

            public void Fail(Exception exception)
            {
                _core.TrySetException(exception);
            }

            public void GetResult(short token)
            {
                _core.GetResult(token);
            }

            public UniTaskStatus GetStatus(short token)
            {
                return _core.GetStatus(token);
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                return _core.UnsafeGetStatus();
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                _core.OnCompleted(continuation, state, token);
            }
        }

        private sealed class SingleContinuationSource<T> : IUniTaskSource<T>
        {
            private UniTaskCompletionSourceCore<T> _core;

            public UniTask<T> Task => new UniTask<T>(this, _core.Version);

            public void Succeed(T result)
            {
                _core.TrySetResult(result);
            }

            public T GetResult(short token)
            {
                return _core.GetResult(token);
            }

            void IUniTaskSource.GetResult(short token)
            {
                _core.GetResult(token);
            }

            public UniTaskStatus GetStatus(short token)
            {
                return _core.GetStatus(token);
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                return _core.UnsafeGetStatus();
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                _core.OnCompleted(continuation, state, token);
            }
        }
    }
}
