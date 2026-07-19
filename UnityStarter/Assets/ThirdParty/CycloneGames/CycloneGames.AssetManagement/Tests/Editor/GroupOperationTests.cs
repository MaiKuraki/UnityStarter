using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine.TestTools;

using Cysharp.Threading.Tasks;
using NUnit.Framework;

using CycloneGames.AssetManagement.Runtime.Batch;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class GroupOperationTests
    {
        [Test]
        public void Items_Returns_Stable_Snapshot_Until_Add()
        {
            var group = new GroupOperation();
            var first = new TestOperation(1f);
            var second = new TestOperation(1f);

            group.Add(first);
            var items = group.Items;
            Assert.AreEqual(1, items.Count);
            Assert.AreSame(first, items[0]);

            group.Add(second);
            Assert.AreEqual(2, group.Items.Count);
            Assert.AreSame(second, group.Items[1]);
        }

        [Test]
        public async Task StartAsync_Completes_Items_In_Order_And_Updates_Progress()
        {
            var group = new GroupOperation();
            group.Add(new TestOperation(1f), 1f);
            group.Add(new TestOperation(1f), 3f);

            await group.StartAsync();

            Assert.IsTrue(group.IsDone);
            Assert.AreEqual(1f, group.Progress);
            Assert.IsNull(group.Error);
        }

        [Test]
        public void StartAsync_Captures_First_Item_Error()
        {
            var group = new GroupOperation();
            group.Add(new TestOperation(1f, "missing asset"));
            group.Add(new TestOperation(1f, "second error"));

            var exception = Assert.CatchAsync<Exception>(async () => await group.StartAsync());
            var taskException = Assert.CatchAsync<Exception>(async () => await group.Task);

            Assert.IsTrue(group.IsDone);
            Assert.AreEqual("missing asset", group.Error);
            Assert.AreEqual("missing asset", exception.Message);
            Assert.AreEqual("missing asset", taskException.Message);
        }

        [Test]
        public void StartAsync_Faults_When_Child_Task_Faults_Without_Error_Text()
        {
            var group = new GroupOperation();
            group.Add(new TestOperation(
                1f,
                task: UniTask.FromException(new InvalidOperationException("provider task failed")),
                isDoneOverride: false));

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await group.StartAsync());
            InvalidOperationException taskException = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await group.Task);

            Assert.IsTrue(group.IsDone);
            Assert.AreEqual("provider task failed", group.Error);
            Assert.AreEqual("provider task failed", exception.Message);
            Assert.AreEqual("provider task failed", taskException.Message);
            Assert.That(group.Items[0].Error, Is.Null.Or.Empty);
        }

        [Test]
        public void StartAsync_Cancels_When_Child_Task_Is_Canceled_Without_Error_Text()
        {
            var group = new GroupOperation();
            group.Add(new TestOperation(
                1f,
                task: UniTask.FromCanceled(new CancellationToken(canceled: true))));

            Assert.CatchAsync<OperationCanceledException>(async () => await group.StartAsync());
            Assert.CatchAsync<OperationCanceledException>(async () => await group.Task);

            Assert.IsTrue(group.IsDone);
            Assert.That(group.Items[0].Error, Is.Null.Or.Empty);
        }

        [Test]
        public async Task Add_Throws_After_StartAsync_Completes()
        {
            var group = new GroupOperation();
            group.Add(new TestOperation(1f));

            await group.StartAsync();

            Assert.Throws<InvalidOperationException>(() => group.Add(new TestOperation(1f)));
        }

        [Test]
        public async Task StartAsync_From_Worker_Thread_Fails_Fast()
        {
            var group = new GroupOperation();
            group.Add(new TestOperation(1f));

            Exception failure = await Task.Run(async () =>
            {
                try
                {
                    await group.StartAsync();
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            Assert.IsInstanceOf<InvalidOperationException>(failure);
        }

        [UnityTest]
        public IEnumerator StartAsync_Caller_Cancellation_Does_Not_Cancel_Shared_Execution()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var childCompletion = new UniTaskCompletionSource();
                var group = new GroupOperation();
                group.Add(new TestOperation(0f, task: childCompletion.Task));
                using var cancellation = new CancellationTokenSource();

                UniTask cancelledWait = group.StartAsync(cancellation.Token);
                UniTask survivorWait = group.StartAsync();
                cancellation.Cancel();

                Exception cancellationFailure = await CaptureFailureAsync(cancelledWait);
                Assert.IsInstanceOf<OperationCanceledException>(cancellationFailure);
                Assert.IsFalse(group.IsDone);

                childCompletion.TrySetResult();
                await survivorWait;
                await group.Task;

                Assert.IsTrue(group.IsDone);
                Assert.AreEqual(1f, group.Progress);
            });
        }

        [UnityTest]
        public IEnumerator Cancel_Cancels_All_Shared_Waiters()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var childCompletion = new UniTaskCompletionSource();
                var group = new GroupOperation();
                group.Add(new TestOperation(0f, task: childCompletion.Task));

                UniTask firstWait = group.StartAsync();
                UniTask secondWait = group.StartAsync();
                group.Cancel();

                Exception firstFailure = await CaptureFailureAsync(firstWait);
                Exception secondFailure = await CaptureFailureAsync(secondWait);

                Assert.IsInstanceOf<OperationCanceledException>(firstFailure);
                Assert.IsInstanceOf<OperationCanceledException>(secondFailure);
                Assert.IsTrue(group.IsDone);

                childCompletion.TrySetException(new InvalidOperationException("late child failure"));
                await UniTask.Yield(PlayerLoopTiming.Update);
            });
        }

        private static async UniTask<Exception> CaptureFailureAsync(UniTask task)
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
    }
}
