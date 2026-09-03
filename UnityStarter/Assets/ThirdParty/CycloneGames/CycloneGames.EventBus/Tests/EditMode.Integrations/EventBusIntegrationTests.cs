using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime.Integrations.R3;
using CycloneGames.EventBus.Runtime.Integrations.UniTask;
using NUnit.Framework;
using R3;

namespace CycloneGames.EventBus.Tests.Integrations
{
    /// <summary>
    /// Coverage for the R3 adapter. Both directions are thin, so what matters here is teardown: a
    /// bridge that leaves a handler on the bus leaks one subscription per binding, and the leak is
    /// invisible until the binding is created every frame.
    /// </summary>
    [TestFixture]
    public class EventBusR3IntegrationTests
    {
        private struct Ping
        {
            public int Value;
        }

        [Test]
        public void ToObservable_ForwardsPublishedEventsInOrder()
        {
            var bus = new EventBus<Ping>();
            var received = new List<int>();

            using (bus.ToObservable().Subscribe(ping => received.Add(ping.Value)))
            {
                bus.Publish(new Ping { Value = 1 });
                bus.Publish(new Ping { Value = 2 });
            }

            CollectionAssert.AreEqual(new[] { 1, 2 }, received);
        }

        [Test]
        public void ToObservable_Disposal_UnsubscribesFromBus()
        {
            var bus = new EventBus<Ping>();
            int count = 0;

            IDisposable subscription = bus.ToObservable().Subscribe(_ => count++);
            Assert.AreEqual(1, bus.SubscriptionCount);

            subscription.Dispose();

            // The whole point of the bridge: disposing the observable must not leave a handler behind.
            Assert.AreEqual(0, bus.SubscriptionCount);
            bus.Publish(new Ping { Value = 1 });
            Assert.AreEqual(0, count);
        }

        [Test]
        public void SubscribeTo_PublishesSourceValuesIntoBus()
        {
            var bus = new EventBus<Ping>();
            var received = new List<int>();
            bus.Subscribe(ping => received.Add(ping.Value));

            var subject = new Subject<Ping>();
            IEventSubscription subscription = bus.SubscribeTo(subject);

            subject.OnNext(new Ping { Value = 7 });
            CollectionAssert.AreEqual(new[] { 7 }, received);

            subscription.Dispose();
            subject.OnNext(new Ping { Value = 8 });

            // Disposing releases the R3 subscription, so the source stops feeding the bus.
            CollectionAssert.AreEqual(new[] { 7 }, received);
        }

        [Test]
        public void SubscribeTo_WrapsAnR3SubscriptionNotABusHandler()
        {
            var bus = new EventBus<Ping>();
            var subject = new Subject<Ping>();

            IEventSubscription subscription = bus.SubscribeTo(subject);

            // The handle wraps an R3 subscription; the bus itself has no handler to remove.
            Assert.AreEqual(0, bus.SubscriptionCount);
            Assert.IsFalse(subscription.IsReleased);

            subscription.Dispose();
            Assert.IsTrue(subscription.IsReleased);
        }
    }

    /// <summary>
    /// Coverage for the UniTask adapter. The one-shot arbitration is the part worth testing: a wait
    /// that ends on cancellation must still release its subscription, or every cancelled await leaves
    /// a handler on the bus that keeps firing into a completion source nobody is reading.
    /// </summary>
    [TestFixture]
    public class EventBusUniTaskIntegrationTests
    {
        private struct Ping
        {
            public int Value;
        }

        [Test]
        public async Task WaitAsync_CompletesWithTheNextEvent()
        {
            var bus = new EventBus<Ping>();

            var wait = bus.WaitAsync();
            bus.Publish(new Ping { Value = 42 });

            Ping result = await wait;
            Assert.AreEqual(42, result.Value);
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public async Task WaitAsync_Predicate_SkipsNonMatchingEvents()
        {
            var bus = new EventBus<Ping>();

            var wait = bus.WaitAsync(ping => ping.Value > 10);
            bus.Publish(new Ping { Value = 1 });
            bus.Publish(new Ping { Value = 2 });
            bus.Publish(new Ping { Value = 11 });

            Ping result = await wait;
            Assert.AreEqual(11, result.Value);
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public async Task WaitAsync_Cancellation_ReleasesTheSubscription()
        {
            var bus = new EventBus<Ping>();
            var cancellation = new CancellationTokenSource();

            var wait = bus.WaitAsync(cancellation.Token);
            Assert.AreEqual(1, bus.SubscriptionCount);

            cancellation.Cancel();

            try
            {
                await wait;
                Assert.Fail("A cancelled wait should not complete successfully.");
            }
            catch (OperationCanceledException)
            {
            }

            // The leak this test exists to catch.
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public async Task WaitAsync_AlreadyCancelledToken_ReleasesTheSubscription()
        {
            var bus = new EventBus<Ping>();
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var wait = bus.WaitAsync(cancellation.Token);

            try
            {
                await wait;
                Assert.Fail("An already-cancelled wait should not complete successfully.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public async Task WaitAsync_EventWinsOverLateCancellation_CompletesWithTheEvent()
        {
            var bus = new EventBus<Ping>();
            var cancellation = new CancellationTokenSource();

            var wait = bus.WaitAsync(cancellation.Token);
            bus.Publish(new Ping { Value = 5 });
            cancellation.Cancel();

            // One-shot arbitration: the event settled first, the late cancellation must not
            // corrupt or replace that result.
            Ping result = await wait;
            Assert.AreEqual(5, result.Value);
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public async Task WaitAsync_PredicateThrows_CompletesFaultedAndReleasesSubscription()
        {
            var bus = new EventBus<Ping>();

            // AttachExternalCancellation keeps the red phase of this test observable: before the
            // fix a throwing predicate left the waiter parked forever, and the await would hang.
            var wait = bus.WaitAsync(_ => throw new InvalidOperationException("predicate"))
                .AttachExternalCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);

            bus.Publish(new Ping { Value = 1 });

            try
            {
                await wait;
                Assert.Fail("A throwing predicate must fault the wait.");
            }
            catch (InvalidOperationException)
            {
            }

            // The leak this test exists to catch: the failed wait must not leave a handler behind.
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        [Timeout(60000)]
        public async Task WaitAsync_CancellationFromBackgroundThread_DuringPublishStorm_BusStaysConsistent()
        {
            for (int iteration = 0; iteration < 100; iteration++)
            {
                var bus = new EventBus<Ping>();
                int churn = 0;
                bus.Subscribe(_ => churn++);

                using (var cancellation = new CancellationTokenSource())
                {
                    UniTask<Ping> wait = bus.WaitAsync(_ => false, cancellation.Token);

                    // The cancellation callback fires on a thread-pool thread. The bus is
                    // single-thread-confined, so the release must be deferred to the owner thread
                    // instead of mutating the handler array from here.
                    Task cancel = Task.Run(async () =>
                    {
                        await Task.Yield();
                        cancellation.Cancel();
                    });

                    // No yield inside the loop. The cancellation callback runs concurrently on a
                    // thread-pool thread, and that concurrency IS the interleaving under test.
                    // Yielding per publish instead serializes the storm against the main-thread
                    // player loop: thousands of yields per run in the Editor, which is both far
                    // slower and a weaker race, because the cancellation then almost always lands
                    // at the same point in the sequence.
                    for (int index = 0; index < 50; index++)
                    {
                        bus.Publish(new Ping { Value = index });
                    }

                    await cancel;

                    // One yield so the deferred removal is applied on the owner thread before the
                    // assertions below observe the bus.
                    await Task.Yield();

                    try
                    {
                        await wait;
                        Assert.Fail($"Iteration {iteration}: a cancelled wait must not complete successfully.");
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    // The foreign-thread cancellation deferred the unsubscribe into the bus's
                    // removal inbox; one owner-thread operation applies it. Without this publish
                    // the stale subscription legitimately lives until the next one.
                    bus.Publish(new Ping { Value = -1 });

                    Assert.AreEqual(1, bus.SubscriptionCount, $"Iteration {iteration} left a subscription behind.");
                    Assert.AreEqual(51, churn, $"Iteration {iteration} lost churn deliveries.");
                }
            }
        }

        [Test]
        public async Task WaitAsync_BusDisposedWhileWaiting_ThenCancelled_CompletesSafely()
        {
            var bus = new EventBus<Ping>();
            var cancellation = new CancellationTokenSource();

            var wait = bus.WaitAsync(cancellation.Token);
            bus.Dispose();

            // The parked task can never complete by event now; the token is the escape hatch.
            cancellation.Cancel();

            try
            {
                await wait;
                Assert.Fail("A cancelled wait must not complete successfully.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.IsTrue(bus.IsDisposed);
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public void WaitAsync_NullBus_Throws()
        {
            try
            {
                ((EventBus<Ping>)null).WaitAsync();
                Assert.Fail("WaitAsync should reject a null bus.");
            }
            catch (ArgumentNullException)
            {
            }
        }
    }
}
