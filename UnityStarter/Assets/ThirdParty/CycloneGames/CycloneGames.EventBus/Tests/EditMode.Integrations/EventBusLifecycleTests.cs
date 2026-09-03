using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime;
using NUnit.Framework;

namespace CycloneGames.EventBus.Tests.Integrations
{
    /// <summary>
    /// Lifecycle and shutdown contracts: the deferred-unsubscribe inbox close protocol, the pump's
    /// structural-mutation semantics, the serialized-budget clamp, context bus ownership, and the
    /// MPSC / global-holder shutdown edges.
    ///
    /// Concurrency cases are made deterministic with <see cref="Barrier"/> and
    /// <see cref="ManualResetEventSlim"/> — never <c>Thread.Sleep</c> — and every worker is joined
    /// with an explicit timeout so a regression fails the test instead of hanging the suite.
    /// </summary>
    [TestFixture]
    public class EventBusLifecycleTests
    {
        private struct Tick
        {
            public int Value;
        }

        private const int JoinTimeoutMs = 15000;

        private static EventBus<Tick> CreateBus()
        {
            return new EventBus<Tick>(new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Swallow));
        }

        // ---------------------------------------------------------------- P0: close protocol

        [Test]
        public void ScheduleRemoval_AfterDisposeCompleted_ReleasesWithoutStrandingTheHandle()
        {
            EventBus<Tick> bus = CreateBus();
            IEventSubscription subscription = bus.Subscribe(_ => { });

            bus.Dispose();

            // The foreign thread arrives strictly after teardown: nothing will drain the inbox
            // again, so ScheduleRemoval must release synchronously.
            Assert.DoesNotThrow(() => bus.ScheduleRemoval(subscription));
            Assert.AreEqual(0, bus.PendingRemovalCount);
        }

        [Test]
        public void DisposeRacingForeignScheduleRemoval_NeverStrandsAHandle()
        {
            for (int round = 0; round < 200; round++)
            {
                EventBus<Tick> bus = CreateBus();
                IEventSubscription subscription = bus.Subscribe(_ => { });

                using (var barrier = new Barrier(2))
                {
                    Exception foreignFailure = null;
                    var foreign = new Thread(() =>
                    {
                        try
                        {
                            barrier.SignalAndWait();
                            bus.ScheduleRemoval(subscription);
                        }
                        catch (Exception exception)
                        {
                            foreignFailure = exception;
                        }
                    });

                    foreign.Start();
                    barrier.SignalAndWait();

                    // Owner disposes concurrently with the foreign enqueue.
                    Assert.DoesNotThrow(() => bus.Dispose());

                    Assert.IsTrue(foreign.Join(JoinTimeoutMs));
                    Assert.IsNull(foreignFailure);
                }

                Assert.AreEqual(0, bus.PendingRemovalCount);
            }
        }

        [Test]
        public void DisposeDuringDispatch_ThenForeignRemoval_StillDrains()
        {
            EventBus<Tick> bus = CreateBus();
            IEventSubscription foreignSubscription = null;

            // A handler that disposes the bus mid-round, exercising the deferred teardown path.
            bus.Subscribe(_ =>
            {
                bus.ScheduleRemoval(foreignSubscription);
                bus.Dispose();
            });

            foreignSubscription = bus.Subscribe(_ => { });

            bus.Publish(new Tick { Value = 1 });

            Assert.IsTrue(bus.IsDisposed);
            Assert.AreEqual(0, bus.PendingRemovalCount);
        }

        [Test]
        public void ForeignRemovalThenOwnerGoesQuiet_IsReleasedByCloseProtocol()
        {
            EventBus<Tick> bus = CreateBus();
            IEventSubscription subscription = bus.Subscribe(_ => { });

            // Enqueue from a foreign thread, then never publish again: the only thing that can
            // release the handle is the close protocol.
            var enqueued = new ManualResetEventSlim(false);
            var foreign = new Thread(() =>
            {
                bus.ScheduleRemoval(subscription);
                enqueued.Set();
            });

            foreign.Start();
            Assert.IsTrue(enqueued.Wait(JoinTimeoutMs));
            Assert.IsTrue(foreign.Join(JoinTimeoutMs));

            Assert.AreEqual(1, bus.PendingRemovalCount);

            bus.Dispose();

            Assert.AreEqual(0, bus.PendingRemovalCount);
        }

        // ---------------------------------------------------------------- P1: pump semantics

        [Test]
        public void PumpClearThenAdd_KeepsTheAddAndRunsItNextTick()
        {
            var pump = new EventBusPump();
            int firstCalls = 0;
            int secondCalls = 0;

            EventPumpFlush second = _ =>
            {
                secondCalls++;
                return 1;
            };

            EventPumpFlush first = _ =>
            {
                firstCalls++;
                // Clear removes everything registered so far; the add that follows it must survive.
                pump.Clear();
                pump.Add(second);
                return 1;
            };

            pump.Add(first);

            pump.Drain(8);

            Assert.AreEqual(1, firstCalls);
            Assert.AreEqual(0, secondCalls);

            pump.Drain(8);

            Assert.AreEqual(1, secondCalls);
            Assert.AreEqual(0, firstCalls > 1 ? 1 : 0, "the cleared source must not run again");
            Assert.AreEqual(1, pump.Count);
        }

        [Test]
        public void PumpDrain_IsNotReentrant()
        {
            var pump = new EventBusPump();
            InvalidOperationException observed = null;

            EventPumpFlush reentrant = _ =>
            {
                try
                {
                    pump.Drain(8);
                }
                catch (InvalidOperationException exception)
                {
                    observed = exception;
                }

                return 1;
            };

            pump.Add(reentrant);

            Assert.DoesNotThrow(() => pump.Drain(8));
            Assert.IsNotNull(observed);
            Assert.IsTrue(observed.Message.Contains("not re-entrant"), "message names the contract");
        }

        [Test]
        public void PumpBudgetClamp_MapsNegativeToPause()
        {
            Assert.AreEqual(0, EventBusPumpMonoBehaviour.ClampBudget(-1));
            Assert.AreEqual(0, EventBusPumpMonoBehaviour.ClampBudget(0));
            Assert.AreEqual(64, EventBusPumpMonoBehaviour.ClampBudget(64));
            Assert.AreEqual(0, EventBusPumpMonoBehaviour.ClampBudget(int.MinValue));
        }

        // ---------------------------------------------------------------- P1: context ownership

        [Test]
        public void ContextDispose_DoesNotReleaseCallerOwnedBus()
        {
            var configuration = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Swallow);
            var callerOwned = new EventBus<Tick>(configuration);

            var context = new EventBusContext(configuration, new NoopCommandPublisher());
            context.RegisterBus(callerOwned);

            context.Dispose();

            Assert.IsFalse(callerOwned.IsDisposed);

            int received = 0;
            callerOwned.Subscribe(_ => received++);
            callerOwned.Publish(new Tick { Value = 7 });

            Assert.AreEqual(1, received);
            callerOwned.Dispose();
        }

        [Test]
        public void ContextDispose_ReleasesSelfCreatedBus()
        {
            var configuration = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Swallow);
            var context = new EventBusContext(configuration, new NoopCommandPublisher());

            EventBus<Tick> created = context.GetOrCreateBus<Tick>();
            context.Dispose();

            Assert.IsTrue(created.IsDisposed);
        }

        [Test]
        public void ContextIsDisposed_IsObservableByTooling()
        {
            var configuration = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Swallow);
            var context = new EventBusContext(configuration, new NoopCommandPublisher());

            Assert.IsFalse(context.IsDisposed);

            context.Dispose();

            // A debugger window reads this to stop observing a context that has ended; without it
            // the window would keep rendering an all-zero snapshot as if nothing were subscribed.
            Assert.IsTrue(context.IsDisposed);
        }

        [Test]
        public void RegisterOwnedBus_TransfersOwnershipToTheContext()
        {
            var configuration = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Swallow);
            var adopted = new EventBus<Tick>(configuration);

            var context = new EventBusContext(configuration, new NoopCommandPublisher());
            context.RegisterOwnedBus(adopted);
            context.Dispose();

            Assert.IsTrue(adopted.IsDisposed);
        }

        // ---------------------------------------------------------------- P2: shutdown edges

        [Test]
        public void MpscQueue_CloseStopsIngressButKeepsQueuedEventsDrainable()
        {
            var queue = new MpscEventQueue<Tick>(capacity: 16);

            Assert.IsTrue(queue.TryEnqueue(new Tick { Value = 1 }));

            queue.Close();

            Assert.IsTrue(queue.IsClosed);
            Assert.IsFalse(queue.TryEnqueue(new Tick { Value = 2 }));
            Assert.AreEqual(1L, queue.RejectedAfterCloseCount);

            int drained = 0;
            while (queue.TryDequeue(out Tick _))
            {
                drained++;
            }

            Assert.AreEqual(1, drained);
        }

        [Test]
        public void MpscQueue_ProducerRacingClose_NeverSpinsForever()
        {
            for (int round = 0; round < 50; round++)
            {
                var queue = new MpscEventQueue<Tick>(capacity: 8);
                var barrier = new Barrier(2);
                bool lastResult = true;

                var producer = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    for (int index = 0; index < 64; index++)
                    {
                        lastResult = queue.TryEnqueue(new Tick { Value = index });
                    }
                });

                producer.Start();
                barrier.SignalAndWait();
                queue.Close();
                Assert.IsTrue(producer.Join(JoinTimeoutMs));
                Assert.IsFalse(lastResult);
            }
        }

        [Test]
        public void GlobalClearIf_OnlyClearsTheExpectedInstance()
        {
            EventBusGlobal<Tick>.Clear();

            var first = CreateBus();
            EventBusGlobal<Tick>.Instance = first;

            // A second context installs a new bus before the first teardown lands.
            var second = CreateBus();
            EventBusGlobal<Tick>.Instance = second;

            Assert.IsFalse(EventBusGlobal<Tick>.ClearIf(first));
            Assert.AreSame(second, EventBusGlobal<Tick>.Instance);

            Assert.IsTrue(EventBusGlobal<Tick>.ClearIf(second));
            Assert.IsNull(EventBusGlobal<Tick>.Instance);

            first.Dispose();
            second.Dispose();
        }

        [Test]
        public void GlobalPublish_AfterHolderBusDisposed_Throws()
        {
            EventBusGlobal<Tick>.Clear();

            EventBus<Tick> bus = CreateBus();
            EventBusGlobal<Tick>.Instance = bus;
            bus.Dispose();

            Assert.Throws<ObjectDisposedException>(() => EventBusGlobal<Tick>.Publish(new Tick { Value = 1 }));

            EventBusGlobal<Tick>.Clear();
        }

        /// <summary>
        /// Minimal command backend: the context only needs a publisher to exist, and it is not
        /// <see cref="IDisposable"/> here so the ownership tests isolate bus lifetime.
        /// </summary>
        private sealed class NoopCommandPublisher : ICommandPublisher
        {
            public ValueTask PublishAsync<TCommand>(
                in TCommand command,
                CancellationToken cancellationToken = default)
                where TCommand : struct
            {
                return default;
            }
        }
    }
}
