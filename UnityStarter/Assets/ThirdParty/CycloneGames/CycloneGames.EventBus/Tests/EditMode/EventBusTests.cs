using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Tests
{
    /// <summary>
    /// Core dispatch, lifecycle and memory-governance behaviour of <see cref="EventBus{T}"/>.
    ///
    /// Every test runs on one thread and every bus is single-thread-confined, which is the contract
    /// under test here as much as the numbers are.
    /// </summary>
    public sealed class EventBusTests
    {
        [Test]
        public void Subscribe_Publish_DeliversToAllHandlersInOrder()
        {
            var bus = new EventBus<ScoreChanged>();
            var order = new List<int>();
            bus.Subscribe(_ => order.Add(1));
            bus.Subscribe(_ => order.Add(2));
            bus.Subscribe(_ => order.Add(3));

            bus.Publish(new ScoreChanged { Score = 42 });

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var bus = new EventBus<ScoreChanged>();
            int delivered = 0;
            Action<ScoreChanged> handler = _ => delivered++;
            IEventSubscription subscription = bus.Subscribe(handler);
            subscription.Dispose();

            bus.Publish(new ScoreChanged());

            Assert.AreEqual(0, delivered);
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public void Unsubscribe_ReturnsTrueWhenFound_AndFalseOtherwise()
        {
            var bus = new EventBus<ScoreChanged>();
            Action<ScoreChanged> handler = _ => { };
            bus.Subscribe(handler);

            Assert.IsTrue(bus.Unsubscribe(handler));
            Assert.IsFalse(bus.Unsubscribe(handler));
        }

        [Test]
        public void Unsubscribe_AfterBusDisposed_IsSafeNoOp()
        {
            var bus = new EventBus<ScoreChanged>();
            Action<ScoreChanged> handler = _ => { };
            IEventSubscription subscription = bus.Subscribe(handler);

            bus.Dispose();

            // Deferred scope disposal (a MonoBehaviour OnDestroy running after the context disposed
            // the bus) must not throw, and must not resurrect anything.
            Assert.DoesNotThrow(() => subscription.Dispose());
            Assert.IsFalse(bus.Unsubscribe(handler));
        }

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            var bus = new EventBus<ScoreChanged>();
            Assert.Throws<ArgumentNullException>(() => bus.Subscribe(null));
        }

        [Test]
        public void Publish_AfterDispose_Throws()
        {
            var bus = new EventBus<ScoreChanged>();
            bus.Subscribe(_ => { });
            bus.Dispose();

            Assert.Throws<ObjectDisposedException>(() => bus.Publish(new ScoreChanged()));
        }

        [Test]
        public void Subscribe_AfterDispose_Throws()
        {
            var bus = new EventBus<ScoreChanged>();
            bus.Dispose();

            Assert.Throws<ObjectDisposedException>(() => bus.Subscribe(_ => { }));
        }

        [Test]
        public void Subscribe_ReusesTombstoneSlot_BeforeGrowing()
        {
            var bus = new EventBus<ScoreChanged>(null, 4);
            Action<ScoreChanged> first = _ => { };
            Action<ScoreChanged> second = _ => { };
            bus.Subscribe(first);
            bus.Subscribe(second);

            bus.Unsubscribe(first);
            int capacityBefore = bus.Capacity;
            bus.Subscribe(first);

            Assert.AreEqual(capacityBefore, bus.Capacity, "Tombstone reuse must not grow the array.");
        }

        [Test]
        public void PeakSubscriptionCount_TracksHighWaterMark()
        {
            var bus = new EventBus<ScoreChanged>();
            bus.Subscribe(_ => { });
            bus.Subscribe(_ => { });
            bus.Subscribe(_ => { });

            Assert.AreEqual(3, bus.PeakSubscriptionCount);
        }

        // ---------------------------------------------------------------- error policy

        [Test]
        public void Publish_StopPolicy_PropagatesFirstHandlerException_AndSkipsRest()
        {
            var bus = new EventBus<ScoreChanged>(
                new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Stop));
            int after = 0;
            bus.Subscribe(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe(_ => after++);

            Assert.Throws<InvalidOperationException>(() => bus.Publish(new ScoreChanged()));

            Assert.AreEqual(0, after);
            Assert.AreEqual(1, bus.SubscriberErrorCount);
        }

        [Test]
        public void Publish_SwallowPolicy_ContinuesToRemainingHandlers()
        {
            var bus = new EventBus<ScoreChanged>(
                new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Swallow));
            int after = 0;
            bus.Subscribe(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe(_ => throw new InvalidOperationException("boom again"));
            bus.Subscribe(_ => after++);

            Assert.DoesNotThrow(() => bus.Publish(new ScoreChanged()));

            Assert.AreEqual(1, after);
            Assert.AreEqual(2, bus.SubscriberErrorCount);
        }

        [Test]
        public void Publish_ContinueOnError_RunsEverySubscriber_ThenRethrowsFirst()
        {
            var bus = new EventBus<ScoreChanged>(
                new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.ContinueOnError));
            int after = 0;
            bus.Subscribe(_ => throw new InvalidOperationException("first"));
            bus.Subscribe(_ => throw new InvalidOperationException("second"));
            bus.Subscribe(_ => after++);

            var thrown = Assert.Throws<InvalidOperationException>(
                () => bus.Publish(new ScoreChanged()));

            // No subscriber is skipped...
            Assert.AreEqual(1, after);
            // ...the first fault is the one that surfaces...
            Assert.AreEqual("first", thrown.Message);
            // ...and the fallout is still counted.
            Assert.AreEqual(2, bus.SubscriberErrorCount);
        }

        [Test]
        public void Publish_CapturedException_PreservesOriginalThrowSite()
        {
            var bus = new EventBus<ScoreChanged>(
                new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.ContinueOnError));
            bus.Subscribe(_ => ThrowFromSubscriber());

            var thrown = Assert.Throws<InvalidOperationException>(
                () => bus.Publish(new ScoreChanged()));

            // A plain `throw exception` would rewrite the stack and drop this frame. Seeing the
            // subscriber's own method here is the whole point of ExceptionDispatchInfo.
            Assert.IsTrue(
                thrown.StackTrace != null && thrown.StackTrace.Contains(nameof(ThrowFromSubscriber)),
                "Stack trace lost the original throw site: " + thrown.StackTrace);
        }

        [Test]
        public void Publish_AfterSubscriberException_DispatchDepthIsRestored()
        {
            var bus = new EventBus<ScoreChanged>(
                new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Swallow));
            bus.Subscribe(_ => throw new InvalidOperationException("boom"));

            bus.Publish(new ScoreChanged());

            Assert.AreEqual(0, bus.DispatchDepth);
        }

        [Test]
        public void Publish_NestedDispatch_CanCatchInnerFaultAndContinue()
        {
            var config = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Swallow);
            var outer = new EventBus<OuterEvent>(config);
            var inner = new EventBus<InnerEvent>(config);
            int outerReceived = 0;

            // A separate event type for the inner round: publishing the same type from inside its
            // own handler would just recurse until the depth ceiling drops it.
            inner.Subscribe(_ => throw new InvalidOperationException("inner boom"));
            outer.Subscribe(_ =>
            {
                try
                {
                    inner.Publish(new InnerEvent());
                }
                catch (InvalidOperationException)
                {
                    // Expected: the inner bus faults, the outer round must survive it.
                }
            });
            outer.Subscribe(evt => outerReceived += evt.Score);

            outer.Publish(new OuterEvent { Score = 1 });

            Assert.AreEqual(1, outerReceived);
            Assert.AreEqual(0, outer.DispatchDepth);
            Assert.AreEqual(0, inner.DispatchDepth);
        }

        [Test]
        public void Publish_ReentrancyDepthLimit_DropsAndCounts()
        {
            var bus = new EventBus<ScoreChanged>(new EventBusConfiguration(maxDispatchDepth: 4));
            int entered = 0;
            Action<ScoreChanged> handler = null;
            handler = _ =>
            {
                entered++;
                bus.Publish(new ScoreChanged());
            };
            bus.Subscribe(handler);

            bus.Publish(new ScoreChanged());

            Assert.AreEqual(4, entered);
            Assert.AreEqual(1, bus.DroppedReentrantCount);
            Assert.AreEqual(0, bus.DispatchDepth);
        }

        // ------------------------------------------------- structural change during dispatch

        [Test]
        public void SubscribeDuringPublish_DoesNotFireThisRound()
        {
            var bus = new EventBus<ScoreChanged>();
            bool secondFired = false;
            bus.Subscribe(_ => bus.Subscribe(__ => secondFired = true));

            bus.Publish(new ScoreChanged());
            Assert.IsFalse(secondFired);

            bus.Publish(new ScoreChanged());
            Assert.IsTrue(secondFired);
        }

        [Test]
        public void SubscribeDuringPublish_ThatGrowsBackingArray_DoesNotFireThisRound()
        {
            // Capacity 1 forces the mid-dispatch subscribe to replace the array, which is the case
            // where a loop holding a stale local array reference would read freed/old handlers.
            var bus = new EventBus<ScoreChanged>(null, 1);
            int secondFired = 0;
            bus.Subscribe(_ => bus.Subscribe(__ => secondFired++));

            bus.Publish(new ScoreChanged());
            Assert.AreEqual(0, secondFired);
            Assert.Greater(bus.Capacity, 1, "The backing array should have grown.");

            bus.Publish(new ScoreChanged());
            Assert.AreEqual(1, secondFired);
        }

        [Test]
        public void UnsubscribeDuringPublish_SkipsNulledSlot()
        {
            var bus = new EventBus<ScoreChanged>();
            int first = 0;
            int second = 0;
            Action<ScoreChanged> secondHandler = _ => second++;

            bus.Subscribe(_ =>
            {
                first++;
                bus.Unsubscribe(secondHandler);
            });
            bus.Subscribe(secondHandler);

            bus.Publish(new ScoreChanged());

            Assert.AreEqual(1, first);
            Assert.AreEqual(0, second);
        }

        [Test]
        public void UnsubscribeDuringPublish_DefersCompactionToOutermostFrame()
        {
            var bus = new EventBus<ScoreChanged>(null, 32);
            Action<ScoreChanged> victim = _ => { };
            for (int index = 0; index < 20; index++)
            {
                bus.Subscribe(_ => { });
            }

            bus.Subscribe(victim);

            int tombstonesDuringDispatch = -1;
            bus.Subscribe(_ =>
            {
                bus.Unsubscribe(victim);

                // Compaction must not run here: shifting slot indices mid-iteration would skip or
                // double-fire a subscriber.
                tombstonesDuringDispatch = bus.TombstoneCount;
            });

            bus.Publish(new ScoreChanged());

            Assert.AreEqual(1, tombstonesDuringDispatch, "Compaction must be deferred, not immediate.");
            Assert.AreEqual(0, bus.TombstoneCount, "The outermost frame compacts on exit.");
            Assert.AreEqual(21, bus.SubscriptionCount);
        }

        [Test]
        public void Compact_DuringDispatch_Throws()
        {
            var bus = new EventBus<ScoreChanged>();
            bus.Subscribe(_ => bus.Compact());

            Assert.Throws<InvalidOperationException>(() => bus.Publish(new ScoreChanged()));
        }

        [Test]
        public void Clear_DuringDispatch_Throws()
        {
            var bus = new EventBus<ScoreChanged>();
            bus.Subscribe(_ => bus.Clear());

            Assert.Throws<InvalidOperationException>(() => bus.Publish(new ScoreChanged()));
        }

        // ------------------------------------------------------------ memory governance

        [Test]
        public void Churn_SubscribeDispose_ReclaimsTombstonesAutomatically()
        {
            var bus = new EventBus<ScoreChanged>(null, 64);

            for (int round = 0; round < 200; round++)
            {
                bus.Subscribe(_ => { }).Dispose();
            }

            Assert.AreEqual(0, bus.SubscriptionCount);
            Assert.AreEqual(0, bus.TombstoneCount);
            Assert.AreEqual(64, bus.Capacity, "Compaction must not shrink reserved capacity.");
        }

        [Test]
        public void BulkUnsubscribe_CompactsAndStaysBounded()
        {
            var bus = new EventBus<ScoreChanged>(null, 64);
            var handles = new List<IEventSubscription>();
            for (int index = 0; index < 32; index++)
            {
                handles.Add(bus.Subscribe(_ => { }));
            }

            for (int index = 0; index < 24; index++)
            {
                handles[index].Dispose();
            }

            Assert.AreEqual(8, bus.SubscriptionCount);
            // Automatic compaction keeps tombstones far below the 24 that were created.
            Assert.LessOrEqual(bus.TombstoneCount, 8);
            Assert.AreEqual(64, bus.Capacity);
        }

        [Test]
        public void Compact_RemovesTombstones_AndRetainsCapacity()
        {
            var bus = new EventBus<ScoreChanged>(null, 16);
            Action<ScoreChanged> middle = _ => { };
            bus.Subscribe(_ => { });
            bus.Subscribe(middle);
            bus.Subscribe(_ => { });

            bus.Unsubscribe(middle);
            bus.Compact();

            Assert.AreEqual(2, bus.SubscriptionCount);
            Assert.AreEqual(0, bus.TombstoneCount);
            Assert.AreEqual(16, bus.Capacity);
        }

        [Test]
        public void Clear_RemovesAllSubscriptions_AndAllowsReuse()
        {
            var bus = new EventBus<ScoreChanged>();
            int delivered = 0;
            bus.Subscribe(_ => delivered++);
            bus.Subscribe(_ => delivered++);

            bus.Clear();

            Assert.AreEqual(0, bus.SubscriptionCount);
            Assert.AreEqual(0, bus.TombstoneCount);

            bus.Publish(new ScoreChanged());
            Assert.AreEqual(0, delivered);

            bus.Subscribe(_ => delivered++);
            bus.Publish(new ScoreChanged());
            Assert.AreEqual(1, delivered);
        }

        [Test]
        public void Clear_PreservesDiagnosticCounters()
        {
            var bus = new EventBus<ScoreChanged>();
            bus.Subscribe(_ => { });
            bus.Publish(new ScoreChanged());

            long before = bus.PublishCount;
            bus.Clear();

            Assert.AreEqual(before, bus.PublishCount);
        }

        [Test]
        public void Publish_ZeroAllocation_AfterWarmup()
        {
            var bus = new EventBus<ScoreChanged>();
            bus.Subscribe(_ => { });
            bus.Subscribe(_ => { });

            var evt = new ScoreChanged { Score = 1 };
            for (int index = 0; index < 200; index++)
            {
                bus.Publish(in evt);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 5000; index++)
            {
                bus.Publish(in evt);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // Proxy assertion: managed allocation only. It does not replace a Profiler GC.Alloc
            // measurement of a real frame.
            Assert.AreEqual(0, allocated, $"Publish allocated {allocated} bytes.");
        }

        [Test]
        public void SubscribeDisposeChurn_ZeroAllocation_AfterWarmup()
        {
            var bus = new EventBus<ScoreChanged>(null, 16);
            Action<ScoreChanged> handler = _ => { };

            // Warm up the pool so every later subscribe rents instead of allocating.
            for (int index = 0; index < 200; index++)
            {
                bus.Subscribe(handler).Dispose();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 5000; index++)
            {
                bus.Subscribe(handler).Dispose();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // The handle holds (bus, handler) directly instead of an unsubscribe closure, so the
            // steady state rents from the pool and allocates nothing.
            Assert.AreEqual(0, allocated, $"Subscribe/Dispose churn allocated {allocated} bytes.");
        }

        [Test]
        public void Snapshot_ReflectsCounters()
        {
            var bus = new EventBus<ScoreChanged>(null, 8);
            bus.Subscribe(_ => { });
            bus.Publish(new ScoreChanged());

            EventBusSnapshot snapshot = bus.GetSnapshot();

            Assert.AreEqual(1, snapshot.SubscriptionCount);
            Assert.AreEqual(0, snapshot.TombstoneCount);
            Assert.AreEqual(1, snapshot.PublishCount);
            Assert.AreEqual(1, snapshot.PeakSubscriptionCount);
            Assert.AreEqual(0, snapshot.DispatchDepth);
            Assert.AreEqual(8, snapshot.Capacity);
            Assert.IsFalse(snapshot.IsDisposed);
        }

        private static void ThrowFromSubscriber()
        {
            throw new InvalidOperationException("thrown from a named subscriber");
        }

        private struct ScoreChanged
        {
            public int Score;
        }

        private struct OuterEvent
        {
            public int Score;
        }

        private struct InnerEvent
        {
            public int Value;
        }
    }

    /// <summary>
    /// <see cref="EventBusGlobal{T}"/>: the non-DI access path. It resolves through a per-type
    /// static field instead of a dictionary, so publishing through it costs the same as publishing
    /// through a directly held bus reference.
    /// </summary>
    public sealed class EventBusGlobalTests
    {
        [Test]
        public void Publish_ThroughHolder_Delivers()
        {
            var bus = new EventBus<Ping>();
            int received = 0;
            bus.Subscribe(_ => received++);
            EventBusGlobal<Ping>.Instance = bus;

            try
            {
                EventBusGlobal<Ping>.Publish(new Ping());
                Assert.AreEqual(1, received);
                Assert.IsTrue(EventBusGlobal<Ping>.HasInstance);
            }
            finally
            {
                EventBusGlobal<Ping>.Clear();
            }
        }

        [Test]
        public void Publish_WithoutAssignedBus_Throws()
        {
            EventBusGlobal<Ping>.Clear();

            Assert.Throws<InvalidOperationException>(() => EventBusGlobal<Ping>.Publish(new Ping()));
        }

        private struct Ping
        {
            public int Value;
        }
    }

    /// <summary>
    /// <see cref="EventStream{T}"/>: bounded batching with an explicit flush point.
    /// </summary>
    public sealed class EventStreamTests
    {
        [Test]
        public void Flush_DeliversInWriteOrder()
        {
            var stream = new EventStream<int>(8);
            var bus = new EventBus<int>();
            var received = new List<int>();
            bus.Subscribe(received.Add);

            stream.TryWrite(1);
            stream.TryWrite(2);
            stream.TryWrite(3);
            int flushed = stream.FlushTo(bus);

            Assert.AreEqual(3, flushed);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, received);
            Assert.IsTrue(stream.IsEmpty);
        }

        [Test]
        public void Write_FullStream_RejectsAndCountsRejections()
        {
            var stream = new EventStream<int>(4);

            int accepted = 0;
            for (int index = 0; index < 10; index++)
            {
                if (stream.TryWrite(index))
                {
                    accepted++;
                }
            }

            Assert.AreEqual(4, accepted);
            Assert.AreEqual(6, stream.RejectedCount);

            // Refused writes are not losses: nothing entered the stream, so nothing was lost.
            Assert.AreEqual(0, stream.DroppedCount);
            Assert.IsTrue(stream.IsFull);
        }

        [Test]
        public void Clear_CountsPendingEventsAsDropped()
        {
            var stream = new EventStream<int>(4);
            stream.TryWrite(1);
            stream.TryWrite(2);
            stream.TryWrite(3);

            Assert.AreEqual(0, stream.DroppedCount);

            stream.Clear();

            // Entered the stream and never read: the only events the stream can call lost.
            Assert.AreEqual(3, stream.DroppedCount);
            Assert.IsTrue(stream.IsEmpty);

            // Clearing an empty stream discards nothing.
            stream.Clear();
            Assert.AreEqual(3, stream.DroppedCount);
        }

        [Test]
        public void Flush_BudgetLeavesRemainderForNextRound()
        {
            var stream = new EventStream<int>(16);
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ => received++);

            for (int index = 0; index < 10; index++)
            {
                Assert.IsTrue(stream.TryWrite(index));
            }

            int first = stream.FlushTo(bus, 3);
            Assert.AreEqual(3, first);
            Assert.AreEqual(3, received);
            Assert.AreEqual(7, stream.Count);

            int second = stream.FlushTo(bus);
            Assert.AreEqual(7, second);
            Assert.AreEqual(10, received);
            Assert.AreEqual(0, stream.DroppedCount);
        }

        [Test]
        public void Flush_WritesDuringFlush_DeferToNextRound()
        {
            var stream = new EventStream<int>(8);
            var bus = new EventBus<int>();
            var received = new List<int>();
            bus.Subscribe(value =>
            {
                received.Add(value);
                if (value < 100)
                {
                    // Written during the flush; it must extend the queue, not the round that
                    // produced it. Otherwise a self-feeding handler would never let flush return.
                    stream.TryWrite(value + 100);
                }
            });

            stream.TryWrite(1);
            stream.TryWrite(2);

            Assert.AreEqual(2, stream.FlushTo(bus));
            CollectionAssert.AreEqual(new[] { 1, 2 }, received);

            // The two deferred events arrive on the next round and stop feeding (101 >= 100).
            Assert.AreEqual(2, stream.FlushTo(bus));
            CollectionAssert.AreEqual(new[] { 1, 2, 101, 102 }, received);
            Assert.AreEqual(0, stream.DroppedCount);
        }

        [Test]
        public void Clear_DiscardsPendingEvents()
        {
            var stream = new EventStream<int>(8);
            stream.TryWrite(1);
            stream.TryWrite(2);

            stream.Clear();

            Assert.AreEqual(0, stream.Count);
            Assert.IsTrue(stream.IsEmpty);
        }

        [Test]
        public void Flush_HandlerClearsMidFlush_RemainingEventsAreNotPublished()
        {
            var stream = new EventStream<int>(8);
            var bus = new EventBus<int>();
            var received = new List<int>();
            bus.Subscribe(value =>
            {
                received.Add(value);
                if (value == 1)
                {
                    stream.Clear();
                }
            });

            stream.TryWrite(1);
            stream.TryWrite(2);
            stream.TryWrite(3);

            // Regression: the loop used to keep reading the zeroed buffer and publish default
            // phantom events, and the trailing RemoveFront then destroyed events written after
            // the clear.
            Assert.AreEqual(1, stream.FlushTo(bus));
            CollectionAssert.AreEqual(new[] { 1 }, received);
            Assert.AreEqual(0, stream.Count);

            // Clear ran while event 1 was already published but not yet removed from the buffer,
            // so the drop count covers all three buffered entries. The guarantees that matter:
            // no phantom default events, and nothing published twice.
            Assert.AreEqual(3, stream.DroppedCount);
        }

        [Test]
        public void Flush_HandlerClearsAndWrites_NewEventSurvivesToNextRound()
        {
            var stream = new EventStream<int>(8);
            var bus = new EventBus<int>();
            var received = new List<int>();
            bus.Subscribe(value =>
            {
                received.Add(value);
                if (value == 1)
                {
                    stream.Clear();
                    stream.TryWrite(9);
                }
            });

            stream.TryWrite(1);
            stream.TryWrite(2);

            Assert.AreEqual(1, stream.FlushTo(bus));
            CollectionAssert.AreEqual(new[] { 1 }, received);
            Assert.AreEqual(1, stream.Count);

            Assert.AreEqual(1, stream.FlushTo(bus));
            CollectionAssert.AreEqual(new[] { 1, 9 }, received);
        }

        [Test]
        public void Flush_BudgetedFlushHandlerClears_NewWritesStayQueued()
        {
            var stream = new EventStream<int>(8);
            var bus = new EventBus<int>();
            var received = new List<int>();
            bus.Subscribe(value =>
            {
                received.Add(value);
                if (value == 1)
                {
                    stream.Clear();
                    stream.TryWrite(7);
                    stream.TryWrite(8);
                }
            });

            stream.TryWrite(1);
            stream.TryWrite(2);

            Assert.AreEqual(1, stream.FlushTo(bus, 4));
            CollectionAssert.AreEqual(new[] { 1 }, received);

            Assert.AreEqual(2, stream.FlushTo(bus));
            CollectionAssert.AreEqual(new[] { 1, 7, 8 }, received);
        }

        [Test]
        public void Flush_HandlerThrowsUnderStopPolicy_PublishedEventsLeaveTheStream()
        {
            var config = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Stop);
            var bus = new EventBus<int>(config);
            var stream = new EventStream<int>(8);
            var received = new List<int>();
            bool faulted = false;
            bus.Subscribe(value =>
            {
                received.Add(value);
                if (value == 2 && !faulted)
                {
                    // Fault exactly once: the next flush re-publishes event 2, and a second
                    // throw there would fault the verification flush itself.
                    faulted = true;
                    throw new InvalidOperationException("stop");
                }
            });

            stream.TryWrite(1);
            stream.TryWrite(2);
            stream.TryWrite(3);

            Assert.Throws<InvalidOperationException>(() => stream.FlushTo(bus));

            // The handler records the value before throwing, so event 2 shows up here even though
            // the round aborted with it.
            CollectionAssert.AreEqual(new[] { 1, 2 }, received);

            // Event 1 was published, so the next flush must not deliver it a second time.
            Assert.AreEqual(2, stream.Count);
            Assert.AreEqual(2, stream.FlushTo(bus));
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 3 }, received);
        }

        [Test]
        public void Flush_ReentrantFlushFromHandler_FailsLoud()
        {
            var stream = new EventStream<int>(8);
            var bus = new EventBus<int>();
            Exception observed = null;
            bus.Subscribe(value =>
            {
                try
                {
                    // An inner flush would publish entries the outer round still owns and shift
                    // the buffer under its loop. Before the guard this recursed to a stack
                    // overflow, which no test could catch.
                    stream.FlushTo(bus);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }
            });

            stream.TryWrite(1);
            stream.FlushTo(bus);

            Assert.IsInstanceOf<InvalidOperationException>(observed);
        }

        [Test]
        public void WriteFlush_ZeroAllocation_AfterWarmup()
        {
            var stream = new EventStream<int>(64);
            var bus = new EventBus<int>();
            bus.Subscribe(_ => { });

            for (int index = 0; index < 200; index++)
            {
                stream.TryWrite(index);
                stream.FlushTo(bus);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 5000; index++)
            {
                stream.TryWrite(index);
                stream.FlushTo(bus);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, $"EventStream write/flush allocated {allocated} bytes.");
        }

        [Test]
        public void Constructor_NonPositiveCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EventStream<int>(0));
        }
    }

    /// <summary>
    /// <see cref="MpscEventQueue{T}"/>: the supported way to cross a thread boundary into a
    /// main-thread-confined bus.
    /// </summary>
    public sealed class MpscEventQueueTests
    {
        [Test]
        public void Enqueue_Drain_PreservesProducerOrder()
        {
            var queue = new MpscEventQueue<int>(16);
            var received = new List<int>();

            for (int index = 0; index < 8; index++)
            {
                Assert.IsTrue(queue.TryEnqueue(index));
            }

            while (queue.TryDequeue(out int value))
            {
                received.Add(value);
            }

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, received);
        }

        [Test]
        public void Enqueue_FullQueue_RejectsAndCountsRejections()
        {
            var queue = new MpscEventQueue<int>(4);
            int accepted = 0;
            for (int index = 0; index < 10; index++)
            {
                if (queue.TryEnqueue(index))
                {
                    accepted++;
                }
            }

            Assert.AreEqual(4, accepted);
            Assert.AreEqual(6, queue.RejectedCount);

            // Refused writes are not losses: nothing entered the ring, so nothing was lost.
            Assert.AreEqual(0, queue.DroppedCount);
            Assert.AreEqual(4, queue.PendingCount);
        }

        [Test]
        public void Enqueue_CapacityIsRoundedUpToPowerOfTwo()
        {
            Assert.AreEqual(8, new MpscEventQueue<int>(5).Capacity);
            Assert.AreEqual(8, new MpscEventQueue<int>(8).Capacity);
            Assert.AreEqual(16, new MpscEventQueue<int>(9).Capacity);
        }

        [Test]
        public void Ring_WrapsAroundWithoutLosingEvents()
        {
            var queue = new MpscEventQueue<int>(4);
            var received = new List<int>();

            // Three laps through a 4-slot ring: exercises the sequence-number wrap logic.
            for (int lap = 0; lap < 3; lap++)
            {
                for (int index = 0; index < 4; index++)
                {
                    Assert.IsTrue(queue.TryEnqueue(lap * 10 + index));
                }

                for (int index = 0; index < 4; index++)
                {
                    Assert.IsTrue(queue.TryDequeue(out int value));
                    received.Add(value);
                }
            }

            CollectionAssert.AreEqual(
                new[] { 0, 1, 2, 3, 10, 11, 12, 13, 20, 21, 22, 23 },
                received);
        }

        [Test]
        public void FlushTo_BudgetSnapshot_PreventsSelfFeedingLoop()
        {
            var queue = new MpscEventQueue<int>(64);
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ =>
            {
                received++;
                queue.TryEnqueue(received + 1000);
            });

            queue.TryEnqueue(1);

            // The handler enqueues on every delivery. Draining "until empty" would never return;
            // a budget snapshotted at entry keeps this to one round.
            Assert.AreEqual(1, queue.FlushTo(bus));
            Assert.AreEqual(1, received);
            Assert.AreEqual(1, queue.PendingCount);
        }

        [Test]
        public void FlushTo_BoundedBudget_LeavesRemainder()
        {
            var queue = new MpscEventQueue<int>(16);
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ => received++);

            for (int index = 0; index < 10; index++)
            {
                queue.TryEnqueue(index);
            }

            Assert.AreEqual(3, queue.FlushTo(bus, 3));
            Assert.AreEqual(3, received);

            Assert.AreEqual(7, queue.FlushTo(bus));
            Assert.AreEqual(10, received);
            Assert.IsTrue(queue.IsEmpty);
        }

        [Test]
        public void ConcurrentEnqueue_FromMultipleThreads_DeliversAllEvents()
        {
            const int producerCount = 4;
            const int eventsPerProducer = 5000;
            const int total = producerCount * eventsPerProducer;

            // Capacity covers the whole burst, so nothing is dropped and the assertion is exact.
            var queue = new MpscEventQueue<int>(32768);

            var producers = new Task[producerCount];
            for (int producer = 0; producer < producerCount; producer++)
            {
                int offset = producer * eventsPerProducer;
                producers[producer] = Task.Run(() =>
                {
                    for (int index = 0; index < eventsPerProducer; index++)
                    {
                        queue.TryEnqueue(offset + index);
                    }
                });
            }

            Task.WaitAll(producers);

            int drained = 0;
            while (queue.TryDequeue(out _))
            {
                drained++;
            }

            Assert.AreEqual(total, drained);
            Assert.AreEqual(0, queue.DroppedCount);
        }

        [Test]
        public void ConcurrentEnqueue_ConcurrentConsumer_DeliversWithoutLoss()
        {
            const int producerCount = 4;
            const int eventsPerProducer = 5000;
            const int total = producerCount * eventsPerProducer;

            var queue = new MpscEventQueue<int>(4096);
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ => received++);

            int stop = 0;
            var consumer = new Thread(() =>
            {
                while (Volatile.Read(ref stop) == 0 || !queue.IsEmpty)
                {
                    queue.FlushTo(bus, 256);
                    Thread.SpinWait(20);
                }
            })
            {
                IsBackground = true,
                Name = "MpscDrainThread",
            };
            consumer.Start();

            var producers = new Task[producerCount];
            for (int producer = 0; producer < producerCount; producer++)
            {
                int offset = producer * eventsPerProducer;
                producers[producer] = Task.Run(() =>
                {
                    for (int index = 0; index < eventsPerProducer; index++)
                    {
                        // Backpressure, not loss: retry until the consumer makes room.
                        while (!queue.TryEnqueue(offset + index))
                        {
                            Thread.Yield();
                        }
                    }
                });
            }

            Task.WaitAll(producers);
            Volatile.Write(ref stop, 1);

            Assert.IsTrue(
                consumer.Join(TimeSpan.FromSeconds(30)),
                "The drain thread did not finish; the ring may be losing wakeups.");

            // Producers retry on a full ring, so rejections are expected and RejectedCount is free
            // to be large. DroppedCount must still be zero: an event that was retried until it fit
            // was never lost, and counting it as a drop would report thousands of phantom losses.
            Assert.AreEqual(0, queue.DroppedCount);
            Assert.AreEqual(total, received);
        }

        [Test]
        public void EnqueueDrain_ZeroAllocation_AfterWarmup()
        {
            var queue = new MpscEventQueue<int>(256);
            var bus = new EventBus<int>();
            bus.Subscribe(_ => { });

            for (int index = 0; index < 200; index++)
            {
                queue.TryEnqueue(index);
                queue.FlushTo(bus);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 5000; index++)
            {
                queue.TryEnqueue(index);
                queue.FlushTo(bus);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // The whole point of the ring: a ConcurrentQueue<T> would allocate one node per event
            // (~21 bytes/op measured on desktop x64), which is the drip that becomes GC spikes.
            Assert.AreEqual(0, allocated, $"MPSC enqueue/drain allocated {allocated} bytes.");
        }

        [Test]
        public void Clear_DiscardsPendingEvents()
        {
            var queue = new MpscEventQueue<int>(16);
            queue.TryEnqueue(1);
            queue.TryEnqueue(2);

            Assert.AreEqual(0, queue.DroppedCount);

            queue.Clear();

            Assert.IsTrue(queue.IsEmpty);
            Assert.IsFalse(queue.TryDequeue(out _));

            // Entered the ring and never read: the only events the queue can call lost.
            Assert.AreEqual(2, queue.DroppedCount);

            // Clearing an empty ring discards nothing.
            queue.Clear();
            Assert.AreEqual(2, queue.DroppedCount);
        }

        [Test]
        public void Constructor_NonPositiveCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MpscEventQueue<int>(0));
        }
    }

    /// <summary>
    /// Subscription scopes: structural lifecycle instead of string-keyed teardown.
    /// </summary>
    public sealed class SubscriptionScopeTests
    {
        [Test]
        public void Dispose_ReleasesAllSubscriptions()
        {
            var bus = new EventBus<Marker>();
            var scope = new SubscriptionScope();
            int delivered = 0;
            scope.Add(bus, _ => delivered++);
            scope.Add(bus, _ => delivered++);

            Assert.AreEqual(2, scope.Count);
            scope.Dispose();

            bus.Publish(new Marker());

            Assert.AreEqual(0, delivered);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var scope = new SubscriptionScope();
            scope.Dispose();

            Assert.DoesNotThrow(() => scope.Dispose());
        }

        [Test]
        public void Add_AfterDispose_Throws()
        {
            var bus = new EventBus<Marker>();
            var scope = new SubscriptionScope();
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.Add(bus, _ => { }));
        }

        [Test]
        public void CallbackSubscription_WrapsArbitraryTeardown()
        {
            int released = 0;
            var subscription = new CallbackSubscription(() => released++);

            subscription.Dispose();
            subscription.Dispose();

            Assert.AreEqual(1, released);
            Assert.IsTrue(subscription.IsReleased);
        }

        private struct Marker
        {
            public int Value;
        }
    }

    public sealed class EventBusContextTests
    {
        [Test]
        public void RegisterBus_GetBus_ReturnsSameInstance()
        {
            using var context = new EventBusBuilder().Build();
            var bus = new EventBus<Marker>();

            context.RegisterBus(bus);

            Assert.AreSame(bus, context.GetBus<Marker>());
        }

        [Test]
        public void RegisterBus_Duplicate_Throws()
        {
            using var context = new EventBusBuilder().Build();
            context.RegisterBus(new EventBus<Marker>());

            Assert.Throws<InvalidOperationException>(() => context.RegisterBus(new EventBus<Marker>()));
        }

        [Test]
        public void GetOrCreateBus_IsStableAcrossCalls()
        {
            using var context = new EventBusBuilder().Build();

            EventBus<Marker> first = context.GetOrCreateBus<Marker>();
            EventBus<Marker> second = context.GetOrCreateBus<Marker>();

            Assert.AreSame(first, second);
        }

        [Test]
        public void GetDiagnosticsSnapshot_AggregatesAcrossBuses()
        {
            using var context = new EventBusBuilder().Build();
            var first = new EventBus<Marker>(
                new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.ContinueOnError));
            var second = new EventBus<MarkerTwo>();
            context.RegisterBus(first);
            context.RegisterBus(second);

            first.Subscribe(_ => { });
            first.Subscribe(_ => { });
            second.Subscribe(_ => { });
            first.Publish(new Marker());
            second.Publish(new MarkerTwo());

            EventBusDiagnosticsSnapshot snapshot = context.GetDiagnosticsSnapshot();

            Assert.AreEqual(2, snapshot.ActiveBusCount);
            Assert.AreEqual(3, snapshot.SubscriptionCount);
            Assert.AreEqual(2, snapshot.PublishCount);
            // Peak is a max, not a sum: summing it would report a number no bus ever reached.
            Assert.AreEqual(2, snapshot.PeakSubscriptionCount);
        }

        [Test]
        public void Dispose_ReleasesSubscriptionsAndBuses()
        {
            var context = new EventBusBuilder().Build();
            EventBus<Marker> bus = context.GetOrCreateBus<Marker>();
            int delivered = 0;
            var scope = new SubscriptionScope();
            scope.Add(bus, _ => delivered++);

            context.Dispose();

            // Disposal drops every handler, so a publish after disposal must be rejected rather
            // than silently delivered to a half-torn-down graph.
            Assert.Throws<ObjectDisposedException>(() => bus.Publish(new Marker()));
            Assert.AreEqual(0, delivered);
        }

        [Test]
        public void GetDiagnosticsSnapshot_EmptyContext_ReturnsZeroes()
        {
            using var context = new EventBusBuilder().Build();

            EventBusDiagnosticsSnapshot snapshot = context.GetDiagnosticsSnapshot();

            Assert.AreEqual(0, snapshot.ActiveBusCount);
            Assert.AreEqual(0, snapshot.PublishCount);
            Assert.AreEqual(0, snapshot.SubscriberErrorCount);
        }

        private struct Marker
        {
            public int Value;
        }

        private struct MarkerTwo
        {
            public int Value;
        }
    }

    public sealed class EventBusPumpTests
    {
        [Test]
        public void Drain_BudgetAppliesPerSource_NotGlobally()
        {
            var pump = new EventBusPump();
            var queue = new MpscEventQueue<int>(16);
            var stream = new EventStream<int>(16);
            var queueBus = new EventBus<int>();
            var streamBus = new EventBus<int>();
            int fromQueue = 0;
            int fromStream = 0;
            queueBus.Subscribe(_ => fromQueue++);
            streamBus.Subscribe(_ => fromStream++);

            for (int index = 0; index < 10; index++)
            {
                queue.TryEnqueue(index);
                stream.TryWrite(index);
            }

            EventPumpFlush queueFlush = pump.AddQueue(queue, queueBus);
            pump.AddStream(stream, streamBus);
            Assert.AreEqual(2, pump.Count);

            int published = pump.Drain(4);

            // Per source, not global: a flooded queue must not starve the other sources.
            Assert.AreEqual(8, published);
            Assert.AreEqual(4, fromQueue);
            Assert.AreEqual(4, fromStream);
            Assert.IsTrue(pump.Remove(queueFlush));
            Assert.AreEqual(1, pump.Count);
        }

        [Test]
        public void Drain_Unbounded_DrainsEverySource()
        {
            var pump = new EventBusPump();
            var queue = new MpscEventQueue<int>(16);
            var stream = new EventStream<int>(16);
            var queueBus = new EventBus<int>();
            var streamBus = new EventBus<int>();
            int received = 0;
            queueBus.Subscribe(_ => received++);
            streamBus.Subscribe(_ => received++);

            for (int index = 0; index < 10; index++)
            {
                queue.TryEnqueue(index);
                stream.TryWrite(index);
            }

            pump.AddQueue(queue, queueBus);
            pump.AddStream(stream, streamBus);

            Assert.AreEqual(20, pump.Drain());
            Assert.AreEqual(20, received);
        }

        [Test]
        public void Drain_ZeroBudget_PublishesNothing()
        {
            var pump = new EventBusPump();
            var queue = new MpscEventQueue<int>(16);
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ => received++);
            queue.TryEnqueue(1);
            pump.AddQueue(queue, bus);

            Assert.AreEqual(0, pump.Drain(0));
            Assert.AreEqual(0, received);
            Assert.AreEqual(1, queue.PendingCount);
        }

        [Test]
        public void Drain_CallbackRemovesItself_SecondSourceStillDrainsThisTick()
        {
            var pump = new EventBusPump();
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ => received++);

            var selfRemoving = new MpscEventQueue<int>(16);
            selfRemoving.TryEnqueue(1);
            EventPumpFlush self = null;
            self = pump.Add(_ =>
            {
                int published = selfRemoving.FlushTo(bus, int.MaxValue);
                pump.Remove(self);
                return published;
            });

            var second = new MpscEventQueue<int>(16);
            second.TryEnqueue(1);
            pump.AddQueue(second, bus);

            pump.Drain(16);

            // A source that removes itself mid-drain used to shift the list and skip its
            // neighbour in the same tick.
            Assert.AreEqual(2, received);

            // The self-removal took effect at the end of the tick; only the second source remains.
            second.TryEnqueue(2);
            pump.Drain(16);
            Assert.AreEqual(3, received);
            Assert.AreEqual(1, pump.Count);
        }

        [Test]
        public void Drain_CallbackAddsSource_NewSourceStartsNextTick()
        {
            var pump = new EventBusPump();
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ => received++);

            var first = new MpscEventQueue<int>(16);
            first.TryEnqueue(1);
            pump.AddQueue(first, bus);
            MpscEventQueue<int> added = null;
            pump.Add(_ =>
            {
                if (added == null)
                {
                    added = new MpscEventQueue<int>(16);
                    added.TryEnqueue(1);
                    pump.AddQueue(added, bus);
                }

                return 0;
            });

            pump.Drain(16);

            // The source registered during the drain must not execute in the same tick.
            Assert.AreEqual(1, received);

            pump.Drain(16);
            Assert.AreEqual(2, received);
        }

        [Test]
        public void Drain_CallbackClears_RemainingSourcesStillDrainThisTick()
        {
            var pump = new EventBusPump();
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ => received++);

            // The clearing callback is registered first so it runs before the drainable source.
            pump.Add(_ =>
            {
                pump.Clear();
                return 0;
            });
            var second = new MpscEventQueue<int>(16);
            second.TryEnqueue(1);
            pump.AddQueue(second, bus);

            pump.Drain(16);

            // A Clear mid-drain used to empty the list and cut the iteration short.
            Assert.AreEqual(1, received);
            Assert.AreEqual(0, pump.Count);

            pump.Drain(16);
            Assert.AreEqual(1, received);
        }

        [Test]
        public void Remove_UnknownDelegate_ReturnsFalse()
        {
            var pump = new EventBusPump();
            var queue = new MpscEventQueue<int>(16);
            var bus = new EventBus<int>();

            pump.AddQueue(queue, bus);
            Assert.IsFalse(pump.Remove(_ => 0));

            pump.Clear();
            Assert.AreEqual(0, pump.Count);
        }
    }

    /// <summary>
    /// Regression coverage for disposing a bus from inside its own dispatch round. Dispose used to
    /// replace the handler array with an empty one while <see cref="EventBus{T}.Publish"/> was
    /// iterating it, which threw IndexOutOfRangeException on the next slot read.
    /// </summary>
    public sealed class EventBusDisposeDuringDispatchTests
    {
        [Test]
        public void Publish_HandlerDisposesBus_RemainingHandlersStillRun()
        {
            var bus = new EventBus<int>();
            var received = new List<int>();
            bus.Subscribe(_ => { bus.Dispose(); received.Add(1); });
            bus.Subscribe(_ => received.Add(2));
            bus.Subscribe(_ => received.Add(3));

            // The publish round is atomic: dispose is deferred to the end of the round, so the
            // handlers captured by the loop still execute.
            Assert.DoesNotThrow(() => bus.Publish(7));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, received);
            Assert.IsTrue(bus.IsDisposed);
        }

        [Test]
        public void Publish_HandlerDisposesBus_TeardownRunsWhenTheRoundEnds()
        {
            var bus = new EventBus<int>(null, 4);
            int capacityInsideRound = -1;
            bus.Subscribe(_ =>
            {
                bus.Dispose();
                capacityInsideRound = bus.Capacity;
            });

            bus.Publish(1);

            // While the round runs the array is still alive; teardown happens on exit.
            Assert.AreEqual(4, capacityInsideRound);
            Assert.AreEqual(0, bus.Capacity);
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public void Publish_HandlerDisposesBus_IsDisposedIsTrueImmediatelyInsideTheRound()
        {
            var bus = new EventBus<int>();
            bool observed = false;
            bus.Subscribe(_ =>
            {
                bus.Dispose();
                observed = bus.IsDisposed;
            });

            bus.Publish(1);

            Assert.IsTrue(observed);
        }

        [Test]
        public void Publish_HandlerDisposesBus_NestedPublishThrowsObjectDisposed()
        {
            var bus = new EventBus<int>();
            Exception observed = null;
            bus.Subscribe(_ =>
            {
                bus.Dispose();
                try
                {
                    bus.Publish(2);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }
            });

            Assert.DoesNotThrow(() => bus.Publish(1));
            Assert.IsInstanceOf<ObjectDisposedException>(observed);
        }

        [Test]
        public void Publish_HandlerDisposesBus_NestedSubscribeThrowsObjectDisposed()
        {
            var bus = new EventBus<int>();
            Exception observed = null;
            bus.Subscribe(_ =>
            {
                bus.Dispose();
                try
                {
                    bus.Subscribe(_ => { });
                }
                catch (Exception exception)
                {
                    observed = exception;
                }
            });

            Assert.DoesNotThrow(() => bus.Publish(1));
            Assert.IsInstanceOf<ObjectDisposedException>(observed);
        }

        [Test]
        public void Publish_HandlerDisposesBus_StopPolicy_TeardownRunsWhenTheFaultAbortsTheRound()
        {
            var config = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Stop);
            var bus = new EventBus<int>(config);
            int secondRan = 0;
            bus.Subscribe(_ =>
            {
                bus.Dispose();
                throw new InvalidOperationException("stop");
            });
            bus.Subscribe(_ => secondRan++);

            Assert.Throws<InvalidOperationException>(() => bus.Publish(1));
            Assert.AreEqual(0, secondRan);
            Assert.IsTrue(bus.IsDisposed);
            Assert.AreEqual(0, bus.Capacity);
        }

        [Test]
        public void Publish_HandlerDisposesBus_ContinueOnError_AllHandlersRun()
        {
            var config = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.ContinueOnError);
            var bus = new EventBus<int>(config);
            var received = new List<int>();
            bus.Subscribe(_ => { bus.Dispose(); received.Add(1); });
            bus.Subscribe(_ => received.Add(2));

            Assert.DoesNotThrow(() => bus.Publish(1));
            CollectionAssert.AreEqual(new[] { 1, 2 }, received);
            Assert.AreEqual(0, bus.Capacity);
        }

        [Test]
        public void Publish_HandlerDisposesBus_DuringNestedDispatch_OuterRoundStillCompletes()
        {
            var config = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.ContinueOnError);
            var bus = new EventBus<int>(config);
            var received = new List<int>();
            bus.Subscribe(value =>
            {
                received.Add(value);
                if (value == 1)
                {
                    bus.Publish(2);
                }
            });
            bus.Subscribe(value =>
            {
                if (value == 2)
                {
                    bus.Dispose();
                }
            });
            bus.Subscribe(_ => received.Add(3));

            Assert.DoesNotThrow(() => bus.Publish(1));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 3 }, received);
            Assert.IsTrue(bus.IsDisposed);
            Assert.AreEqual(0, bus.Capacity);
        }

        [Test]
        public void Publish_HandlerDisposesBus_SubscriptionHandleDisposalStaysSafe()
        {
            var bus = new EventBus<int>();
            int received = 0;
            IEventSubscription handle = bus.Subscribe(_ => received++);
            bus.Subscribe(_ => bus.Dispose());

            Assert.DoesNotThrow(() => bus.Publish(1));
            Assert.DoesNotThrow(handle.Dispose);
            Assert.IsTrue(handle.IsReleased);
            Assert.AreEqual(1, received);
        }
    }

    public sealed class EventBusConfigurationTests
    {
        [Test]
        public void Default_UsesContinueOnErrorPolicy()
        {
            // One broken listener must not silently cost every later listener its delivery.
            Assert.AreEqual(
                PublishErrorPolicy.ContinueOnError,
                EventBusConfiguration.Default.PublishErrorPolicy);
        }

        [Test]
        public void Constructor_RejectsNonPositiveLimits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EventBusConfiguration(commandQueueCapacity: 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EventBusConfiguration(maxDispatchDepth: 0));
        }

        [Test]
        public void Default_UsesNullLogSink()
        {
            Assert.AreSame(NullEventBusLogSink.Instance, EventBusConfiguration.Default.LogSink);
        }
    }

    public sealed class InProcessCommandPublisherTests
    {
        [Test]
        public async Task Publish_DispatchesToRegisteredHandler()
        {
            using var publisher = new InProcessCommandPublisher();
            int received = 0;
            publisher.RegisterHandler<SpawnAtCommand>(command => received = command.GridX);

            await publisher.PublishAsync(new SpawnAtCommand { GridX = 7 });

            Assert.AreEqual(7, received);
        }

        [Test]
        public async Task DropPolicy_Overflow_DoesNotGrowUnbounded()
        {
            using var publisher = new InProcessCommandPublisher(capacity: 2, CommandOverflowPolicy.Drop);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int outerRuns = 0;

            publisher.RegisterHandler<SpawnAtCommand>(async (command, cancellationToken) =>
            {
                outerRuns++;
                await gate.Task;
            });

            Task first = publisher.PublishAsync(new SpawnAtCommand { GridX = -1 }).AsTask();
            await Task.Yield();

            for (int index = 0; index < 8; index++)
            {
                await publisher.PublishAsync(new SpawnAtCommand { GridX = index });
            }

            Assert.AreEqual(1, outerRuns);
            Assert.LessOrEqual(publisher.PendingCommandCount, 2);

            gate.SetResult(true);
            await first;
        }

        [Test]
        public async Task FailFastPolicy_Overflow_FaultsThePublishTask()
        {
            using var publisher = new InProcessCommandPublisher(capacity: 1, CommandOverflowPolicy.FailFast);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            publisher.RegisterHandler<SpawnAtCommand>(async (command, cancellationToken) =>
                await gate.Task);

            Task first = publisher.PublishAsync(new SpawnAtCommand()).AsTask();
            // The queue is empty, so the second publish is enqueued rather than rejected.
            Task second = publisher.PublishAsync(new SpawnAtCommand()).AsTask();

            // The third exceeds the bounded capacity and must surface as a faulted task. Asserting on
            // the task instead of using ThrowsAsync keeps the failure observable: an un-awaited
            // ThrowsAsync can silently pass even when the assertion fails.
            Task third = publisher.PublishAsync(new SpawnAtCommand()).AsTask();
            await AssertCompletion(third);

            Assert.IsTrue(third.IsFaulted, "Overflow under FailFast must fault the publish task.");
            Assert.IsInstanceOf<InvalidOperationException>(third.Exception?.InnerException);

            gate.SetResult(true);
            await first;
            await second;
        }

        /// <summary>
        /// Awaits a task that is expected to fault, without letting the failure escape.
        /// </summary>
        private static async Task AssertCompletion(Task task)
        {
            try
            {
                await task;
            }
            catch (InvalidOperationException)
            {
                // Expected under FailFast overflow.
            }
        }

        private struct SpawnAtCommand
        {
            public int GridX;
        }
    }

    public sealed class EventBusBuilderTests
    {
        [Test]
        public void Build_VitalRouterBackendWithoutFactory_Throws()
        {
            var config = new EventBusConfiguration(commandBackend: CommandBackend.VitalRouter);
            var builder = new EventBusBuilder().WithConfiguration(config);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void Build_DefaultConfiguration_BuildsContext()
        {
            var builder = new EventBusBuilder();

            using EventBusContext context = builder.Build();

            Assert.IsNotNull(context);
            Assert.IsNotNull(context.Commands);
        }

        [Test]
        public void Build_WithCommandPublisherFactory_UsesIt()
        {
            var publisher = new InProcessCommandPublisher();
            EventBusContext context = new EventBusBuilder()
                .WithCommandPublisherFactory(_ => publisher)
                .Build();

            Assert.AreSame(publisher, context.Commands);
            context.Dispose();
        }
    }

    /// <summary>
    /// Throughput guard. This is not a micro-benchmark: it is a smoke test that catches an
    /// accidental O(n^2) regression in the dispatch loop. Numbers for tuning go in the bench project.
    /// </summary>
    public sealed class EventBusThroughputTests
    {
        [Test]
        public void Publish_500Subscribers_WellUnderFrameBudget()
        {
            // Worst-case fan-out: 500 listeners on one global event.
            var bus = new EventBus<TickEvent>(null, 512);
            for (int index = 0; index < 500; index++)
            {
                bus.Subscribe(_ => { });
            }

            var evt = new TickEvent { Frame = 1 };
            for (int index = 0; index < 100; index++)
            {
                bus.Publish(in evt);
            }

            const int iterations = 100_000;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int index = 0; index < iterations; index++)
            {
                bus.Publish(in evt);
            }

            stopwatch.Stop();

            double nsPerPublish = stopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
            TestContext.WriteLine(
                $"[EventBus Bench] 500 subscribers: {nsPerPublish:F1} ns/publish "
                + $"({iterations / stopwatch.Elapsed.TotalSeconds:F0} publishes/sec).");

            // Generous on purpose: real cost is tens of milliseconds.
            Assert.Less(
                stopwatch.Elapsed.TotalMilliseconds,
                10_000,
                $"100k publishes with 500 subscribers took {stopwatch.Elapsed.TotalMilliseconds:F0}ms");
        }

        private struct TickEvent
        {
            public int Frame;
        }
    }
}
