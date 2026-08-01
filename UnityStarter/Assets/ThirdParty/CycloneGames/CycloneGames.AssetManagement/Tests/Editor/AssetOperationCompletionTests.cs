using System;
using System.Threading.Tasks;

using Cysharp.Threading.Tasks;
using NUnit.Framework;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetOperationCompletionTests
    {
        [Test]
        public void ProviderSuccess_WinsBeforeLateOwnerRetirement()
        {
            var provider = new UniTaskCompletionSource();
            var tails = new AssetOperationTailTracker();
            AssetOperationCompletion completion = AssetOperationCompletion.Start(provider.Task, tails);

            provider.TrySetResult();

            Assert.That(completion.Task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(tails.PendingCount, Is.Zero);
            Assert.That(completion.TryCancelByOwner(), Is.False);
            Assert.DoesNotThrowAsync(async () => await completion.Task);
        }

        [Test]
        public async Task ProviderSuccess_RetiresTailBeforeAwaitContinuation()
        {
            var provider = new UniTaskCompletionSource();
            var tails = new AssetOperationTailTracker();
            AssetOperationCompletion completion = AssetOperationCompletion.Start(provider.Task, tails);
            UniTask<int> pendingCountAfterAwait = CapturePendingCountAfterAwaitAsync(completion.Task, tails);

            provider.TrySetResult();

            Assert.That(await pendingCountAfterAwait, Is.Zero);
        }

        [Test]
        public void ProviderFailure_WinsBeforeLateOwnerRetirementAndRemainsMemoized()
        {
            var provider = new UniTaskCompletionSource();
            var tails = new AssetOperationTailTracker();
            var expected = new InvalidOperationException("Synthetic provider failure.");
            AssetOperationCompletion completion = AssetOperationCompletion.Start(provider.Task, tails);

            provider.TrySetException(expected);

            Assert.That(completion.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(tails.PendingCount, Is.Zero);
            Assert.That(completion.TryCancelByOwner(), Is.False);
            Assert.That(Assert.CatchAsync<InvalidOperationException>(async () => await completion.Task), Is.SameAs(expected));
            Assert.That(Assert.CatchAsync<InvalidOperationException>(async () => await completion.Task), Is.SameAs(expected));
        }

        [Test]
        public void ProviderCancellation_CancelsPublicTaskAndDrainsTail()
        {
            var tails = new AssetOperationTailTracker();
            AssetOperationCompletion completion = AssetOperationCompletion.Start(
                UniTask.FromCanceled(new System.Threading.CancellationToken(canceled: true)),
                tails);

            Assert.That(completion.Task.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(tails.PendingCount, Is.Zero);
            Assert.CatchAsync<OperationCanceledException>(async () => await completion.Task);
        }

        [Test]
        public void OwnerRetirement_CancelsPublicTaskButKeepsProviderTailPending()
        {
            var provider = new UniTaskCompletionSource();
            var tails = new AssetOperationTailTracker();
            AssetOperationCompletion completion = AssetOperationCompletion.Start(provider.Task, tails);

            Assert.That(completion.TryCancelByOwner(), Is.True);

            Assert.That(completion.Task.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(tails.PendingCount, Is.EqualTo(1));
            Assert.CatchAsync<OperationCanceledException>(async () => await completion.Task);
            Assert.CatchAsync<OperationCanceledException>(async () => await completion.Task);

            provider.TrySetResult();

            Assert.That(tails.PendingCount, Is.Zero);
            Assert.That(completion.Task.Status, Is.EqualTo(UniTaskStatus.Canceled));
        }

        [Test]
        public void LateProviderFailureAfterOwnerRetirement_IsObservedAndDrainsTail()
        {
            var provider = new UniTaskCompletionSource();
            var tails = new AssetOperationTailTracker();
            AssetOperationCompletion completion = AssetOperationCompletion.Start(provider.Task, tails);

            completion.TryCancelByOwner();
            provider.TrySetException(new InvalidOperationException("Late provider failure."));

            Assert.That(tails.PendingCount, Is.Zero);
            Assert.That(completion.Task.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.CatchAsync<OperationCanceledException>(async () => await completion.Task);
        }

        [Test]
        public async Task OwnerRetirement_CancelsConcurrentPublicAwaiters()
        {
            var provider = new UniTaskCompletionSource();
            var tails = new AssetOperationTailTracker();
            AssetOperationCompletion completion = AssetOperationCompletion.Start(provider.Task, tails);

            Task<Exception> firstWait = CaptureFailureAsync(completion.Task);
            Task<Exception> secondWait = CaptureFailureAsync(completion.Task);
            completion.TryCancelByOwner();

            Assert.That(await firstWait, Is.InstanceOf<OperationCanceledException>());
            Assert.That(await secondWait, Is.InstanceOf<OperationCanceledException>());
            Assert.That(tails.PendingCount, Is.EqualTo(1));

            provider.TrySetResult();
            Assert.That(tails.PendingCount, Is.Zero);
        }

        [Test]
        public void TailTracker_ReusesACompletedDrainAcrossOperationEpochs()
        {
            var tails = new AssetOperationTailTracker();
            tails.RegisterTail();
            tails.RegisterTail();
            UniTask firstDrain = tails.WaitForAllAsync();

            tails.CompleteTail();
            Assert.That(firstDrain.Status, Is.EqualTo(UniTaskStatus.Pending));
            tails.CompleteTail();
            Assert.That(firstDrain.Status, Is.EqualTo(UniTaskStatus.Succeeded));

            tails.RegisterTail();
            UniTask secondDrain = tails.WaitForAllAsync();
            Assert.That(secondDrain.Status, Is.EqualTo(UniTaskStatus.Pending));
            tails.CompleteTail();

            Assert.That(secondDrain.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(tails.PendingCount, Is.Zero);
        }

        private static async Task<Exception> CaptureFailureAsync(UniTask task)
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

        private static async UniTask<int> CapturePendingCountAfterAwaitAsync(
            UniTask task,
            AssetOperationTailTracker tails)
        {
            await task;
            return tails.PendingCount;
        }
    }
}
