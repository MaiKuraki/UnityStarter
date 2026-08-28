using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Tests
{
    public sealed class EventBusTests
    {
        [Test]
        public void Subscribe_Publish_DeliversToAllHandlers()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            int a = 0;
            int b = 0;
            bus.Subscribe(evt => a = evt.Score);
            bus.Subscribe(evt => b = evt.Score);

            var evt = new ScoreChanged { Score = 42 };
            bus.Publish(in evt);

            Assert.That(a, Is.EqualTo(42));
            Assert.That(b, Is.EqualTo(42));
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            int delivered = 0;
            Action<ScoreChanged> handler = evt => delivered++;
            IEventSubscription subscription = bus.Subscribe(handler);
            subscription.Dispose();

            var evt = new ScoreChanged();
            bus.Publish(in evt);

            Assert.That(delivered, Is.Zero);
            Assert.That(bus.TombstoneCount, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_ReturnsTrueWhenFound_AndFalseOtherwise()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            Action<ScoreChanged> handler = _ => { };
            bus.Subscribe(handler);

            Assert.That(bus.Unsubscribe(handler), Is.True);
            Assert.That(bus.Unsubscribe(handler), Is.False);
        }

        [Test]
        public void Unsubscribe_AfterBusDisposed_IsSafeNoOp()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            Action<ScoreChanged> handler = _ => { };
            IEventSubscription subscription = bus.Subscribe(handler);

            bus.Dispose();

            // Deferred scope disposal (e.g. MonoBehaviour OnDestroy after context disposed the bus)
            // must not throw.
            Assert.DoesNotThrow(() => subscription.Dispose());
            Assert.That(bus.Unsubscribe(handler), Is.False);
        }

        [Test]
        public void Publish_StopPolicy_PropagatesFirstHandlerException()
        {
            var config = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Stop);
            var bus = new EventBus<ScoreChanged>(config);
            int after = 0;
            bus.Subscribe(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe(_ => after++);

            Assert.Throws<InvalidOperationException>(() => bus.Publish(new ScoreChanged()));
            Assert.That(after, Is.Zero);
        }

        [Test]
        public void Publish_SwallowPolicy_ContinuesToRemainingHandlers()
        {
            var config = new EventBusConfiguration(publishErrorPolicy: PublishErrorPolicy.Swallow);
            var bus = new EventBus<ScoreChanged>(config);
            int after = 0;
            bus.Subscribe(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe(_ => after++);

            Assert.DoesNotThrow(() => bus.Publish(new ScoreChanged()));
            Assert.That(after, Is.EqualTo(1));
        }

        [Test]
        public void SubscribeDuringPublish_DoesNotFireThisRound()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            bool secondFired = false;
            bus.Subscribe(evt =>
            {
                bus.Subscribe(_ => secondFired = true);
            });

            var evt = new ScoreChanged();
            bus.Publish(in evt);

            Assert.That(secondFired, Is.False);

            bus.Publish(in evt);
            Assert.That(secondFired, Is.True);
        }

        [Test]
        public void UnsubscribeDuringPublish_SkipsNulledSlot()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            int first = 0;
            int second = 0;
            Action<ScoreChanged> secondHandler = _ => second++;

            // The first handler unsubscribes the second before the iteration reaches it, so the
            // second handler's slot is already null when the loop arrives and it is skipped.
            bus.Subscribe(_ =>
            {
                first++;
                bus.Unsubscribe(secondHandler);
            });
            bus.Subscribe(secondHandler);

            var evt = new ScoreChanged();
            bus.Publish(in evt);

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.Zero);
        }

        [Test]
        public void SubscriptionScope_Dispose_ReleasesAllSubscriptions()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            var scope = new SubscriptionScope();
            int delivered = 0;
            scope.Add(bus, _ => delivered++);
            scope.Add(bus, _ => delivered++);

            Assert.That(scope.Count, Is.EqualTo(2));
            scope.Dispose();

            var evt = new ScoreChanged();
            bus.Publish(in evt);

            Assert.That(delivered, Is.Zero);
        }

        [Test]
        public void Publish_ZeroAllocation_AfterWarmup()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            bus.Subscribe(_ => { });
            bus.Subscribe(_ => { });

            // Warm up generic/array paths.
            var evt = new ScoreChanged { Score = 1 };
            for (int index = 0; index < 4; index++)
            {
                bus.Publish(in evt);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1000; index++)
            {
                bus.Publish(in evt);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // Proxy assertion: managed allocation only. It does not replace Profiler GC.Alloc
            // measurement for the actual frame.
            Assert.That(allocated, Is.Zero, $"Publish allocated {allocated} bytes.");
        }

        [Test]
        public void Clear_RemovesAllSubscriptions_AndAllowsReuse()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            int delivered = 0;
            bus.Subscribe(_ => delivered++);
            bus.Subscribe(_ => delivered++);

            bus.Clear();

            Assert.That(bus.SubscriptionCount, Is.Zero);
            Assert.That(bus.TombstoneCount, Is.Zero);

            var evt = new ScoreChanged();
            bus.Publish(in evt);
            Assert.That(delivered, Is.Zero);

            // The bus is reusable: subscribing again works.
            bus.Subscribe(_ => delivered++);
            bus.Publish(in evt);
            Assert.That(delivered, Is.EqualTo(1));
        }

        [Test]
        public void Clear_PreservesDiagnosticCounters()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            bus.Subscribe(_ => { });
            var evt = new ScoreChanged();
            bus.Publish(in evt);

            long publishCountBeforeClear = bus.PublishCount;
            bus.Clear();

            Assert.That(bus.PublishCount, Is.EqualTo(publishCountBeforeClear));
        }

        [Test]
        public void Clear_DuringDispatch_Throws()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            bus.Subscribe(_ => bus.Clear());

            Assert.Throws<InvalidOperationException>(() => bus.Publish(new ScoreChanged()));
        }

        [Test]
        public void Snapshot_ReflectsCounters()
        {
            var bus = new EventBus<ScoreChanged>(EventBusConfiguration.Default);
            bus.Subscribe(_ => { });
            var evt = new ScoreChanged();
            bus.Publish(in evt);

            EventBusSnapshot snapshot = bus.GetSnapshot();

            Assert.That(snapshot.SubscriptionCount, Is.EqualTo(1));
            Assert.That(snapshot.TombstoneCount, Is.Zero);
            Assert.That(snapshot.PublishCount, Is.EqualTo(1));
        }

        private struct ScoreChanged
        {
            public int Score;
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

            Assert.That(received, Is.EqualTo(7));
        }

        [Test]
        public async Task DropPolicy_Overflow_DoesNotGrowUnbounded()
        {
            using var publisher = new InProcessCommandPublisher(capacity: 2, CommandOverflowPolicy.Drop);
            var outerGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int outerRuns = 0;

            publisher.RegisterHandler<SpawnAtCommand>(async (command, cancellationToken) =>
            {
                outerRuns++;
                await outerGate.Task;
            });

            Task first = publisher.PublishAsync(new SpawnAtCommand { GridX = -1 }).AsTask();
            await Task.Yield();

            // Publish more commands while the first handler is still running. Capacity is 2, so at
            // most 2 are enqueued; the rest are dropped without throwing.
            for (int index = 0; index < 8; index++)
            {
                await publisher.PublishAsync(new SpawnAtCommand { GridX = index });
            }

            Assert.That(outerRuns, Is.EqualTo(1));
            Assert.That(publisher.PendingCommandCount, Is.LessThanOrEqualTo(2));

            outerGate.SetResult(true);
            await first;
        }

        [Test]
        public async Task FailFastPolicy_Overflow_Throws()
        {
            using var publisher = new InProcessCommandPublisher(capacity: 1, CommandOverflowPolicy.FailFast);
            var outerGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            publisher.RegisterHandler<SpawnAtCommand>(async (command, cancellationToken) =>
            {
                await outerGate.Task;
            });

            Task first = publisher.PublishAsync(new SpawnAtCommand()).AsTask();

            // Second publish while the first handler is still running: the queue is empty, so it is
            // enqueued. The third exceeds the capacity and must fail fast.
            Task second = publisher.PublishAsync(new SpawnAtCommand()).AsTask();

            // ThrowsAsync returns the exception directly (not Task<Exception>), so it is not awaited.
            // .AsTask() keeps the async lambda target-typed to AsyncTestDelegate.
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await publisher.PublishAsync(new SpawnAtCommand()).AsTask());

            outerGate.SetResult(true);
            await first;
            await second;
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

            Assert.That(context, Is.Not.Null);
            Assert.That(context.Commands, Is.Not.Null);
        }
    }

    public sealed class EventBusBenchmarkTests
    {
        private struct TickEvent
        {
            public int Frame;
        }

        [Test]
        public void Publish_Throughput_500Subscribers_WellUnderFrameBudget()
        {
            // Worst-case subscriber fan-out: 500 monsters/players all listening to one global event.
            var bus = new EventBus<TickEvent>();
            for (int index = 0; index < 500; index++)
            {
                bus.Subscribe(_ => { });
            }

            var evt = new TickEvent { Frame = 1 };
            for (int index = 0; index < 100; index++)
            {
                bus.Publish(in evt); // warm-up
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

            // Extremely generous guard (real is ~1-2 µs): only catches an accidental O(n^2) regression.
            Assert.That(
                stopwatch.Elapsed.TotalMilliseconds,
                Is.LessThan(10_000),
                $"100k publishes with 500 subscribers took {stopwatch.Elapsed.TotalMilliseconds:F0}ms");
        }
    }

    public sealed class ConcurrentEventQueueTests
    {
        [Test]
        public void Enqueue_FlushTo_DeliversInOrder()
        {
            var queue = new ConcurrentEventQueue<int>();
            var bus = new EventBus<int>();
            var received = new System.Collections.Generic.List<int>();
            bus.Subscribe(received.Add);

            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);

            queue.FlushTo(bus);

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, received);
        }

        [Test]
        public void ConcurrentEnqueue_FromMultipleThreads_DeliversAll()
        {
            var queue = new ConcurrentEventQueue<int>();
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ => received++);

            const int producerCount = 4;
            const int eventsPerProducer = 1000;
            var tasks = new Task[producerCount];
            for (int producer = 0; producer < producerCount; producer++)
            {
                int offset = producer * eventsPerProducer;
                tasks[producer] = Task.Run(() =>
                {
                    for (int index = 0; index < eventsPerProducer; index++)
                    {
                        int value = offset + index;
                        queue.Enqueue(value);
                    }
                });
            }

            Task.WaitAll(tasks);
            queue.FlushTo(bus);

            Assert.That(received, Is.EqualTo(producerCount * eventsPerProducer));
        }

        [Test]
        public void FlushTo_MaxEvents_BoundsSingleFlush()
        {
            var queue = new ConcurrentEventQueue<int>();
            var bus = new EventBus<int>();
            int received = 0;
            bus.Subscribe(_ => received++);

            for (int index = 0; index < 10; index++)
            {
                queue.Enqueue(index);
            }

            int flushed = queue.FlushTo(bus, maxEvents: 3);

            Assert.That(flushed, Is.EqualTo(3));
            Assert.That(received, Is.EqualTo(3));
            Assert.That(queue.IsEmpty, Is.False);

            queue.FlushTo(bus);
            Assert.That(received, Is.EqualTo(10));
            Assert.That(queue.IsEmpty, Is.True);
        }
    }
}
