using System;
using System.Threading;
using CycloneGames.Logging;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class LoggerLifecycleReentrancyTests
    {
        [SetUp]
        public void SetUp()
        {
            LoggerUpdater.CaptureMainThreadForLifecycle();
            CLogger.Shutdown(LogFlushMode.Buffered);
            LoggerUpdater.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            CLogger.Shutdown(LogFlushMode.Buffered);
            LoggerUpdater.ResetForTests();
        }

        [Test]
        [Timeout(5000)]
        public void ExplicitShutdown_WhenSinkFlushReentersSameOwner_ReturnsInProgressWithoutRecursion()
        {
            CLogger owner = CLoggerFactory.CreateSingleThreaded(CreateOptions());
            var sink = new ReentrantFlushSink(
                () => owner.ShutdownInstance(LogFlushMode.Buffered, 1000));
            Assert.IsTrue(owner.AddLogger(sink));

            LoggerShutdownResult outer = default;
            try
            {
                outer = owner.ShutdownInstance(LogFlushMode.Buffered, 2000);

                Assert.IsTrue(outer.IsComplete, "The outer explicit shutdown must finish.");
                Assert.AreEqual(LoggerShutdownStatus.InProgress, sink.NestedStatus);
                Assert.AreEqual(1, sink.FlushCallCount, "Shutdown re-entry must not recurse into sink flushing.");
                Assert.IsFalse(sink.RecursionDetected);
                Assert.AreEqual(1, sink.DisposeCount);
            }
            finally
            {
                if (!outer.IsComplete)
                {
                    owner.ShutdownInstance(LogFlushMode.Buffered, 2000);
                }
            }
        }

        [Test]
        [Timeout(5000)]
        public void GlobalShutdown_WhenSinkFlushReentersStaticShutdown_ReturnsInProgressWithoutRecursion()
        {
            Assert.IsTrue(CLogger.ConfigureSingleThreadedProcessing(CreateOptions()));
            CLogger global = CLogger.Instance;
            var sink = new ReentrantFlushSink(
                () => CLogger.Shutdown(LogFlushMode.Buffered));
            Assert.IsTrue(global.AddLogger(sink));

            try
            {
                LoggerShutdownResult outer = CLogger.Shutdown(LogFlushMode.Buffered);

                Assert.IsTrue(outer.IsComplete, "The outer global shutdown must finish.");
                Assert.AreEqual(LoggerShutdownStatus.InProgress, sink.NestedStatus);
                Assert.AreEqual(1, sink.FlushCallCount, "Static shutdown re-entry must not recurse into sink flushing.");
                Assert.IsFalse(sink.RecursionDetected);
                Assert.AreEqual(1, sink.DisposeCount);
            }
            finally
            {
                CLogger.Shutdown(LogFlushMode.Buffered);
            }
        }

        [Test]
        [Timeout(5000)]
        public void ExplicitShutdown_WhenAsyncSinkDisposeReentersOwner_ReturnsInProgressWithoutDeadlock()
        {
            CLogger owner = CLoggerFactory.CreateSingleThreaded(CreateOptions());
            int shutdownThreadId = Environment.CurrentManagedThreadId;
            var nestedCompleted = new ManualResetEventSlim(false);
            var sink = new ReentrantDisposeSink(
                () => owner.ShutdownInstance(LogFlushMode.Buffered, 1000),
                nestedCompleted);
            Assert.IsTrue(owner.AddLogger(sink));

            LoggerShutdownResult outer = default;
            try
            {
                outer = owner.ShutdownInstance(LogFlushMode.Buffered, 2000);

                Assert.IsTrue(outer.IsComplete, "The outer shutdown must not wait on a reentrant disposal callback.");
                Assert.IsTrue(nestedCompleted.Wait(1000), "The asynchronous sink disposal did not finish.");
                Assert.AreNotEqual(shutdownThreadId, sink.DisposeThreadId, "Sink disposal must use the disposal executor in Editor tests.");
                Assert.AreEqual(LoggerShutdownStatus.InProgress, sink.NestedStatus);
                Assert.AreEqual(1, sink.DisposeCount);
            }
            finally
            {
                if (!outer.IsComplete)
                {
                    owner.ShutdownInstance(LogFlushMode.Buffered, 2000);
                }
            }
        }

        [Test]
        [Timeout(5000)]
        public void StaticShutdown_WhenDirectInstanceShutdownWinsAfterDetach_DoesNotRestoreStoppedGlobal()
        {
            Assert.IsTrue(CLogger.ConfigureSingleThreadedProcessing(CreateOptions()));
            CLogger global = CLogger.Instance;
            var detached = new ManualResetEventSlim(false);
            var releaseStatic = new ManualResetEventSlim(false);
            LoggerShutdownResult staticResult = default;
            Exception staticException = null;
            CLogger.GlobalShutdownDetachedTestHook = instance =>
            {
                if (instance != null)
                {
                    detached.Set();
                    releaseStatic.Wait(2000);
                }
            };
            var staticThread = new Thread(() =>
            {
                try
                {
                    staticResult = CLogger.Shutdown(LogFlushMode.Buffered);
                }
                catch (Exception exception)
                {
                    staticException = exception;
                }
            });

            try
            {
                staticThread.Start();
                Assert.IsTrue(detached.Wait(1000));

                LoggerShutdownResult directResult = global.ShutdownInstance(LogFlushMode.Buffered, 2000);
                releaseStatic.Set();

                Assert.IsTrue(staticThread.Join(2000));
                Assert.IsNull(staticException);
                Assert.IsTrue(directResult.IsComplete);
                Assert.IsTrue(staticResult.IsComplete);
                Assert.IsFalse(CLogger.TryGetInstance(out _), "A stopped instance must not be restored as the global logger.");
            }
            finally
            {
                releaseStatic.Set();
                staticThread.Join(2000);
                CLogger.GlobalShutdownDetachedTestHook = null;
                CLogger.Shutdown(LogFlushMode.Buffered);
                detached.Dispose();
                releaseStatic.Dispose();
            }
        }

        [Test]
        [Timeout(5000)]
        public void ConcurrentStaticShutdown_WaitsForOwnerAndFinishesWithoutDeadlock()
        {
            Assert.IsTrue(CLogger.ConfigureSingleThreadedProcessing(CreateOptions()));
            CLogger.Instance.AddLogger(new CountingSink());
            var detached = new ManualResetEventSlim(false);
            var releaseFirst = new ManualResetEventSlim(false);
            LoggerShutdownResult firstResult = default;
            LoggerShutdownResult secondResult = default;
            CLogger.GlobalShutdownDetachedTestHook = instance =>
            {
                if (instance != null)
                {
                    detached.Set();
                    releaseFirst.Wait(2000);
                }
            };
            var first = new Thread(() => firstResult = CLogger.Shutdown(LogFlushMode.Buffered));
            var second = new Thread(() => secondResult = CLogger.Shutdown(LogFlushMode.Buffered));

            try
            {
                first.Start();
                Assert.IsTrue(detached.Wait(1000));
                second.Start();
                releaseFirst.Set();

                Assert.IsTrue(first.Join(2000));
                Assert.IsTrue(second.Join(2000));
                Assert.IsTrue(firstResult.IsComplete);
                Assert.AreEqual(LoggerShutdownStatus.NotStarted, secondResult.Status);
                Assert.IsFalse(CLogger.TryGetInstance(out _));
            }
            finally
            {
                releaseFirst.Set();
                first.Join(2000);
                second.Join(2000);
                CLogger.GlobalShutdownDetachedTestHook = null;
                CLogger.Shutdown(LogFlushMode.Buffered);
                detached.Dispose();
                releaseFirst.Dispose();
            }
        }

        [Test]
        [Timeout(5000)]
        public void ExplicitDispose_DuringUnrelatedGlobalShutdown_StillDisposesItsSinks()
        {
            Assert.IsTrue(CLogger.ConfigureSingleThreadedProcessing(CreateOptions()));
            CLogger.Instance.AddLogger(new CountingSink());
            CLogger explicitLogger = CLoggerFactory.CreateSingleThreaded(CreateOptions());
            var explicitSink = new CountingSink();
            Assert.IsTrue(explicitLogger.AddLogger(explicitSink));
            var detached = new ManualResetEventSlim(false);
            var releaseGlobal = new ManualResetEventSlim(false);
            CLogger.GlobalShutdownDetachedTestHook = instance =>
            {
                if (instance != null)
                {
                    detached.Set();
                    releaseGlobal.Wait(2000);
                }
            };
            var globalShutdown = new Thread(() => CLogger.Shutdown(LogFlushMode.Buffered));

            try
            {
                globalShutdown.Start();
                Assert.IsTrue(detached.Wait(1000));

                explicitLogger.Dispose();

                Assert.AreEqual(1, explicitSink.DisposeCount);
                releaseGlobal.Set();
                Assert.IsTrue(globalShutdown.Join(2000));
            }
            finally
            {
                releaseGlobal.Set();
                globalShutdown.Join(2000);
                CLogger.GlobalShutdownDetachedTestHook = null;
                explicitLogger.ShutdownInstance(LogFlushMode.Buffered, 2000);
                CLogger.Shutdown(LogFlushMode.Buffered);
                detached.Dispose();
                releaseGlobal.Dispose();
            }
        }

        [Test]
        public void LastUnityAdapterDispose_AfterMainThreadPump_DestroysHiddenHost()
        {
            LoggerUpdater.Configure(CreateOptions());
            var adapter = new UnityLogger();
            try
            {
                Assert.AreEqual(1, Resources.FindObjectsOfTypeAll<LoggerUpdater>().Length);

                adapter.Dispose();
                LoggerUpdater.PumpOnce();

                Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<LoggerUpdater>().Length);
            }
            finally
            {
                adapter.Dispose();
                LoggerUpdater.PumpOnce();
            }
        }

        [Test]
        [Timeout(5000)]
        public void SubsystemReset_DoesNotShutdownOrDisposeForeignGlobalOwner()
        {
            Assert.IsTrue(CLogger.ConfigureSingleThreadedProcessing(CreateOptions()));
            CLogger foreign = CLogger.Instance;
            var sink = new CountingSink();
            Assert.IsTrue(foreign.AddLogger(sink));

            try
            {
                LoggerUpdater.ResetForTests();

                Assert.AreEqual(0, sink.DisposeCount, "Subsystem reset must not dispose a foreign-owned global backend.");
                foreign.Write(
                    LogSeverity.Info,
                    "still-owned",
                    filePath: string.Empty,
                    memberName: string.Empty);
                foreign.Pump(1);
                Assert.AreEqual(1, sink.LogCount, "Subsystem reset must not stop a foreign-owned global backend.");
            }
            finally
            {
                if (CLogger.TryGetInstance(out CLogger current) && ReferenceEquals(current, foreign))
                {
                    CLogger.Shutdown(LogFlushMode.Buffered);
                }
                else
                {
                    foreign.ShutdownInstance(LogFlushMode.Buffered, 2000);
                }
            }
        }

        private static LoggerProcessingOptions CreateOptions()
        {
            return new LoggerProcessingOptions
            {
                MaxQueuedMessages = 8,
                MaxQueuedCharacters = 1024,
                MaxMessageCharacters = 128,
                MaxCategoryCharacters = 32,
                MaxSourcePathCharacters = 32,
                MaxMemberNameCharacters = 32,
                ReservedCriticalMessages = 0,
                ReservedCriticalCharacters = 0,
                UnityConsoleMaxQueuedMessages = 8,
                UnityConsoleMaxQueuedCharacters = 1024,
                OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
                CriticalLevel = LogLevel.Error,
                ShutdownDrainTimeoutMs = 2000
            };
        }

        private sealed class ReentrantFlushSink : ILogger, IFlushableLogger
        {
            private readonly Func<LoggerShutdownResult> _shutdown;
            private int _flushDepth;

            internal int DisposeCount;
            internal int FlushCallCount;
            internal LoggerShutdownStatus NestedStatus;
            internal bool RecursionDetected;

            internal ReentrantFlushSink(Func<LoggerShutdownResult> shutdown)
            {
                _shutdown = shutdown;
            }

            public void Log(LogMessage logMessage)
            {
            }

            public bool TryFlush(LogFlushMode mode)
            {
                int depth = Interlocked.Increment(ref _flushDepth);
                Interlocked.Increment(ref FlushCallCount);
                try
                {
                    if (depth > 1)
                    {
                        RecursionDetected = true;
                        return false;
                    }

                    NestedStatus = _shutdown().Status;
                    return true;
                }
                finally
                {
                    Interlocked.Decrement(ref _flushDepth);
                }
            }

            public void Dispose()
            {
                Interlocked.Increment(ref DisposeCount);
            }
        }

        private sealed class ReentrantDisposeSink : ILogger
        {
            private readonly Func<LoggerShutdownResult> _shutdown;
            private readonly ManualResetEventSlim _completed;
            private int _disposeStarted;
            private int _disposeThreadId;
            private int _nestedStatus = -1;

            internal int DisposeCount;
            internal int DisposeThreadId => Volatile.Read(ref _disposeThreadId);
            internal LoggerShutdownStatus NestedStatus =>
                (LoggerShutdownStatus)Volatile.Read(ref _nestedStatus);

            internal ReentrantDisposeSink(
                Func<LoggerShutdownResult> shutdown,
                ManualResetEventSlim completed)
            {
                _shutdown = shutdown;
                _completed = completed;
            }

            public void Log(LogMessage logMessage)
            {
            }

            public void Dispose()
            {
                Interlocked.Increment(ref DisposeCount);
                if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
                {
                    return;
                }

                Volatile.Write(ref _disposeThreadId, Environment.CurrentManagedThreadId);
                try
                {
                    LoggerShutdownResult nested = _shutdown();
                    Volatile.Write(ref _nestedStatus, (int)nested.Status);
                }
                finally
                {
                    _completed.Set();
                }
            }
        }

        private sealed class CountingSink : ILogger
        {
            internal int DisposeCount;
            internal int LogCount;

            public void Log(LogMessage logMessage)
            {
                Interlocked.Increment(ref LogCount);
            }

            public void Dispose()
            {
                Interlocked.Increment(ref DisposeCount);
            }
        }
    }
}
