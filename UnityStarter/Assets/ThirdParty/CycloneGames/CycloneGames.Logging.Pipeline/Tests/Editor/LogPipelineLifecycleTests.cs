using System;
using System.Reflection;
using System.Threading;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;
using NUnit.Framework;

namespace CycloneGames.Logging.Pipeline.Tests.Editor
{
    public sealed class LogPipelineLifecycleTests
    {
        [Test]
        public void SinkDisposal_ReentrantShutdownReportsInProgressAndOuterShutdownCompletes()
        {
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded(CreateOptions());
            LogPipelineShutdownResult reentrantResult = default;
            bool flushResult = true;
            bool removeResult = true;
            bool clearRejected = false;
            CallbackSink sink = null;
            sink = new CallbackSink(() =>
            {
                reentrantResult = pipeline.Shutdown(LogFlushMode.Buffered, 1000);
                flushResult = pipeline.TryFlush(LogFlushMode.Buffered, 1000);
                removeResult = pipeline.RemoveSink(sink, 1000);
                try
                {
                    pipeline.ClearSinks();
                }
                catch (InvalidOperationException)
                {
                    clearRejected = true;
                }
            });
            Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);

            LogPipelineShutdownResult outerResult = pipeline.Shutdown(LogFlushMode.Buffered, 2000);

            Assert.AreEqual(LogPipelineShutdownStatus.InProgress, reentrantResult.Status);
            Assert.IsFalse(flushResult);
            Assert.IsFalse(removeResult);
            Assert.IsTrue(clearRejected);
            Assert.IsTrue(outerResult.IsComplete);
            Assert.AreEqual(1, sink.DisposeCount);
        }

        [Test]
        public void ConcurrentShutdown_IsIdempotentAndDisposesOwnedSinkOnce()
        {
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded(CreateOptions());
            var sink = new CountingSink();
            Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);

            LogPipelineShutdownResult first = default;
            LogPipelineShutdownResult second = default;
            using var start = new ManualResetEventSlim(false);
            var firstThread = new Thread(() =>
            {
                start.Wait();
                first = pipeline.Shutdown(LogFlushMode.Buffered, 2000);
            });
            var secondThread = new Thread(() =>
            {
                start.Wait();
                second = pipeline.Shutdown(LogFlushMode.Buffered, 2000);
            });

            firstThread.Start();
            secondThread.Start();
            start.Set();

            Assert.IsTrue(firstThread.Join(3000));
            Assert.IsTrue(secondThread.Join(3000));
            Assert.IsTrue(first.IsComplete);
            Assert.IsTrue(second.IsComplete);
            Assert.AreEqual(1, sink.DisposeCount);
        }

        [Test]
        public void RemoveSink_InvalidQuiescenceTimeoutDoesNotMutateOwnership()
        {
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded(CreateOptions());
            var sink = new CountingSink();
            Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);

            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.RemoveSink(sink, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.RemoveSink(
                sink,
                LogPipelineOptions.MaxSupportedShutdownDrainTimeoutMs + 1));
            Assert.AreEqual(0, sink.DisposeCount);

            LogPipelineShutdownResult shutdown = pipeline.Shutdown(LogFlushMode.Buffered, 2000);
            Assert.IsTrue(shutdown.IsComplete);
            Assert.AreEqual(1, sink.DisposeCount);
        }

        [Test]
        public void FlushAndShutdown_InvalidArgumentsDoNotMutateLifecycleOrSinkHealth()
        {
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded(CreateOptions());
            var sink = new FlushTrackingSink();
            Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);

            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.TryFlush((LogFlushMode)255, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.TryFlush(LogFlushMode.Buffered, -2));
            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.TryFlush(
                LogFlushMode.Buffered,
                LogPipelineOptions.MaxSupportedShutdownDrainTimeoutMs + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Shutdown((LogFlushMode)255, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Shutdown(LogFlushMode.Buffered, -2));
            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Shutdown(
                LogFlushMode.Buffered,
                LogPipelineOptions.MaxSupportedShutdownDrainTimeoutMs + 1));

            Assert.AreEqual(0, sink.FlushCount);
            Assert.AreEqual(0, sink.DisposeCount);
            Assert.AreEqual(0, pipeline.GetStatistics().SinkFailureCount);
            Assert.IsTrue(((ILogWriter)pipeline).IsEnabled(LogSeverity.Info, "Lifecycle"));

            LogPipelineShutdownResult shutdown = pipeline.Shutdown(LogFlushMode.Buffered, 2000);
            Assert.IsTrue(shutdown.IsComplete);
            Assert.AreEqual(1, sink.FlushCount);
            Assert.AreEqual(1, sink.DisposeCount);
        }

        [Test]
        public void ConsoleLogSink_DirectFlushRejectsUnknownMode()
        {
            using var sink = new ConsoleLogSink();

            Assert.Throws<ArgumentOutOfRangeException>(() => sink.TryFlush((LogFlushMode)255));
            Assert.DoesNotThrow(() => sink.TryFlush(LogFlushMode.Buffered));
            Assert.DoesNotThrow(() => sink.TryFlush(LogFlushMode.Durable));
        }

        [Test]
        public void ExplicitPipelines_HaveIndependentOwnershipAndDoNotMutateProcessWriter()
        {
            ILogWriter previous = LogRuntime.Writer;
            LogPipeline first = LogPipelineFactory.CreateSingleThreaded(CreateOptions());
            LogPipeline second = LogPipelineFactory.CreateSingleThreaded(CreateOptions());
            try
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(previous, first));

                LogPipelineShutdownResult secondResult = second.Shutdown(LogFlushMode.Buffered, 2000);

                Assert.IsTrue(secondResult.IsComplete);
                Assert.AreSame(first, LogRuntime.Writer);
            }
            finally
            {
                LogRuntime.TryReplaceWriter(first, previous);
                first.Shutdown(LogFlushMode.Buffered, 2000);
                second.Shutdown(LogFlushMode.Buffered, 2000);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmitCallback_LifecycleOperationsFailFastWithoutMutatingOwnership(bool threaded)
        {
            LogPipeline pipeline = threaded
                ? LogPipelineFactory.CreateThreaded(CreateOptions())
                : LogPipelineFactory.CreateSingleThreaded(CreateOptions());
            using var callbackCompleted = new ManualResetEventSlim(false);
            LogPipelineShutdownResult shutdownResult = default;
            bool flushResult = true;
            bool removeResult = true;
            bool clearRejected = false;
            CallbackEmitSink sink = null;
            sink = new CallbackEmitSink(() =>
            {
                shutdownResult = pipeline.Shutdown(LogFlushMode.Buffered, 2000);
                flushResult = pipeline.TryFlush(LogFlushMode.Buffered, 2000);
                removeResult = pipeline.RemoveSink(sink, 2000);
                try
                {
                    pipeline.ClearSinks();
                }
                catch (InvalidOperationException)
                {
                    clearRejected = true;
                }

                callbackCompleted.Set();
            });
            Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);

            pipeline.EnqueueMessage(
                LogSeverity.Info,
                "callback",
                "Lifecycle",
                "LogPipelineLifecycleTests.cs",
                1,
                nameof(EmitCallback_LifecycleOperationsFailFastWithoutMutatingOwnership));
            if (!threaded)
            {
                pipeline.Pump(1);
            }

            Assert.IsTrue(callbackCompleted.Wait(1000), "The sink callback did not complete within the bounded interval.");
            Assert.AreEqual(LogPipelineShutdownStatus.InProgress, shutdownResult.Status);
            Assert.IsFalse(flushResult);
            Assert.IsFalse(removeResult);
            Assert.IsTrue(clearRejected);

            LogPipelineShutdownResult finalResult = pipeline.Shutdown(LogFlushMode.Buffered, 2000);
            Assert.IsTrue(finalResult.IsComplete);
            Assert.AreEqual(1, sink.DisposeCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SinkOutOfMemory_FaultsPipelineAndRemainsObservable(bool threaded)
        {
            LogPipeline pipeline = threaded
                ? LogPipelineFactory.CreateThreaded(CreateOptions())
                : LogPipelineFactory.CreateSingleThreaded(CreateOptions());
            using var emitEntered = new ManualResetEventSlim(false);
            var sink = new OutOfMemorySink(emitEntered);
            Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);

            pipeline.EnqueueMessage(
                LogSeverity.Info,
                "fatal",
                "Reliability",
                "LogPipelineLifecycleTests.cs",
                2,
                nameof(SinkOutOfMemory_FaultsPipelineAndRemainsObservable));

            if (threaded)
            {
                Assert.IsTrue(emitEntered.Wait(1000));
                Assert.IsTrue(SpinWait.SpinUntil(() => IsOutOfMemoryObservable(pipeline), 1000));
            }
            else
            {
                Assert.Throws<OutOfMemoryException>(() => pipeline.Pump(1));
            }

            ILogWriter writer = pipeline;
            Assert.Throws<OutOfMemoryException>(() => writer.IsEnabled(LogSeverity.Info, "Reliability"));
            Assert.IsTrue(((ILogPipelineMonitor)pipeline).IsFaulted);
            LogPipelineShutdownResult shutdown = pipeline.Shutdown(LogFlushMode.Buffered, 2000);
            Assert.IsTrue(shutdown.IsComplete);
            Assert.AreEqual(LogPipelineShutdownStatus.CompletedWithFailures, shutdown.Status);
            Assert.AreEqual(1, sink.DisposeCount);
        }

        [Test]
        public void SynchronousDisposal_OutOfMemoryCompletesAllOwnedSinksBeforeRethrow()
        {
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded(CreateOptions());
            var fatalSink = new OutOfMemoryDisposeSink();
            var healthySink = new CountingSink();
            Assert.IsTrue(pipeline.RegisterSink(fatalSink).IsRegistered);
            Assert.IsTrue(pipeline.RegisterSink(healthySink).IsRegistered);

            FieldInfo stopRequested = typeof(LogPipeline).GetField(
                "_sinkDisposalStopRequested",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(stopRequested, Is.Not.Null);
            stopRequested.SetValue(pipeline, true);

            Assert.Throws<OutOfMemoryException>(() => pipeline.ClearSinks());
            Assert.AreEqual(1, fatalSink.DisposeCount);
            Assert.AreEqual(1, healthySink.DisposeCount);
            Assert.AreEqual(0, pipeline.GetStatistics().PendingSinkDisposalCount);

            LogPipelineShutdownResult shutdown = pipeline.Shutdown(LogFlushMode.Buffered, 2000);
            Assert.IsTrue(shutdown.IsComplete);
            Assert.AreEqual(LogPipelineShutdownStatus.CompletedWithFailures, shutdown.Status);
        }

        [Test]
        public void ThreadedProcessor_RunsMaintenanceDuringContinuousDispatch()
        {
            LogPipelineOptions options = LogPipelineOptions.Default;
            options.MaintenanceIntervalMs = 10;
            LogPipeline pipeline = LogPipelineFactory.CreateThreaded(options);
            const int MessageCount = 64;
            using var maintenanceObserved = new ManualResetEventSlim(false);
            var sink = new SlowMaintainableSink(maintenanceObserved);
            Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);

            for (int i = 0; i < MessageCount; i++)
            {
                pipeline.EnqueueMessage(
                    LogSeverity.Info,
                    "maintenance",
                    "Reliability",
                    "LogPipelineLifecycleTests.cs",
                    3,
                    nameof(ThreadedProcessor_RunsMaintenanceDuringContinuousDispatch));
            }

            bool observed = maintenanceObserved.Wait(1000);
            LogPipelineStatistics statistics = pipeline.GetStatistics();
            Assert.IsTrue(
                observed,
                $"Maintenance was starved by continuous dispatch. Emitted={sink.EmitCount}, " +
                $"Queued={statistics.QueuedCount}, Processed={statistics.ProcessedMessageCount}, " +
                $"Faulted={pipeline.IsFaulted}.");
            Assert.Less(sink.EmitsObservedAtFirstMaintenance, MessageCount);
            Assert.IsTrue(pipeline.Shutdown(LogFlushMode.Buffered, 3000).IsComplete);
        }

        [Test]
        public void ProcessingOptions_RejectUnboundedArrayAndCharacterBudgets()
        {
            LogPipelineOptions excessiveMessages = LogPipelineOptions.Default;
            excessiveMessages.MaxQueuedMessages = int.MaxValue;
            Assert.Throws<ArgumentOutOfRangeException>(() => LogPipelineOptions.CreateValidated(excessiveMessages));

            LogPipelineOptions excessiveCharacters = LogPipelineOptions.Default;
            excessiveCharacters.MaxQueuedCharacters = int.MaxValue;
            Assert.Throws<ArgumentOutOfRangeException>(() => LogPipelineOptions.CreateValidated(excessiveCharacters));
        }

        private static bool IsOutOfMemoryObservable(LogPipeline pipeline)
        {
            try
            {
                ((ILogWriter)pipeline).IsEnabled(LogSeverity.Info, "Reliability");
                return false;
            }
            catch (OutOfMemoryException)
            {
                return true;
            }
        }

        private static LogPipelineOptions CreateOptions()
        {
            return new LogPipelineOptions
            {
                MaxQueuedMessages = 8,
                MaxQueuedCharacters = 4096,
                MaxMessageCharacters = 512,
                MaxCategoryCharacters = 128,
                MaxSourcePathCharacters = 512,
                MaxMemberNameCharacters = 128,
                MaxFilterCategories = 16,
                MaxFilterCharacters = 2048,
                ReservedCriticalMessages = 1,
                ReservedCriticalCharacters = 512,
                ShutdownDrainTimeoutMs = 2000,
                MaintenanceIntervalMs = 10,
                CriticalSeverity = LogSeverity.Error
            };
        }

        private sealed class CallbackSink : ILogSink, IIdempotentLogSinkDisposal
        {
            private readonly Action _onDispose;
            private int _disposeCount;

            internal CallbackSink(Action onDispose)
            {
                _onDispose = onDispose;
            }

            internal int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Emit(LogEvent logEvent)
            {
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
                _onDispose();
            }
        }

        private sealed class CountingSink : ILogSink, IIdempotentLogSinkDisposal
        {
            private int _disposeCount;

            internal int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Emit(LogEvent logEvent)
            {
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
            }
        }

        private sealed class FlushTrackingSink : ILogSink, IFlushableLogSink, IIdempotentLogSinkDisposal
        {
            private int _flushCount;
            private int _disposeCount;

            internal int FlushCount => Volatile.Read(ref _flushCount);
            internal int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Emit(LogEvent logEvent)
            {
            }

            public bool TryFlush(LogFlushMode mode)
            {
                Interlocked.Increment(ref _flushCount);
                return true;
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
            }
        }

        private sealed class CallbackEmitSink : ILogSink, IIdempotentLogSinkDisposal
        {
            private readonly Action _onEmit;
            private int _disposeCount;

            internal CallbackEmitSink(Action onEmit)
            {
                _onEmit = onEmit;
            }

            internal int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Emit(LogEvent logEvent)
            {
                _onEmit();
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
            }
        }

        private sealed class OutOfMemorySink : ILogSink, IIdempotentLogSinkDisposal
        {
            private readonly ManualResetEventSlim _emitEntered;
            private int _disposeCount;

            internal OutOfMemorySink(ManualResetEventSlim emitEntered)
            {
                _emitEntered = emitEntered;
            }

            internal int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Emit(LogEvent logEvent)
            {
                _emitEntered.Set();
                throw new OutOfMemoryException("Synthetic sink failure.");
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
            }
        }

        private sealed class OutOfMemoryDisposeSink : ILogSink, IIdempotentLogSinkDisposal
        {
            private int _disposeCount;

            internal int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Emit(LogEvent logEvent)
            {
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
                throw new OutOfMemoryException("Synthetic sink disposal failure.");
            }
        }

        private sealed class SlowMaintainableSink : ILogSink, IMaintainableLogSink
        {
            private readonly ManualResetEventSlim _maintenanceObserved;
            private int _emitCount;
            private int _emitsObservedAtFirstMaintenance = int.MaxValue;

            internal SlowMaintainableSink(ManualResetEventSlim maintenanceObserved)
            {
                _maintenanceObserved = maintenanceObserved;
            }

            internal int EmitsObservedAtFirstMaintenance => Volatile.Read(ref _emitsObservedAtFirstMaintenance);
            internal int EmitCount => Volatile.Read(ref _emitCount);

            public void Emit(LogEvent logEvent)
            {
                Interlocked.Increment(ref _emitCount);
                Thread.Sleep(1);
            }

            public void PerformMaintenance()
            {
                Interlocked.CompareExchange(
                    ref _emitsObservedAtFirstMaintenance,
                    Volatile.Read(ref _emitCount),
                    int.MaxValue);
                _maintenanceObserved.Set();
            }

            public void Dispose()
            {
            }
        }
    }
}
