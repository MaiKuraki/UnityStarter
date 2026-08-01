using System;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Cheat.Core;
using CycloneGames.Cheat.Runtime;
using CycloneGames.Logging;
using NUnit.Framework;

#if ENABLE_CHEAT
using VitalRouter;
#endif

namespace CycloneGames.Cheat.Tests.Editor
{
    public sealed class CheatRuntimeTests
    {
        [Test]
        public void RuntimeRejectsInvalidConcurrencyLimits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CheatCommandRuntime(maximumConcurrentCommandCount: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CheatCommandRuntime(
                    maximumConcurrentCommandCount:
                    CheatCommandRuntime.AbsoluteMaximumConcurrentCommandCount + 1));

            using var runtime = new CheatCommandRuntime(maximumConcurrentCommandCount: 2);
            Assert.AreEqual(2, runtime.MaximumConcurrentCommandCount);
            Assert.AreEqual(2, runtime.Metrics.MaximumConcurrentCommandCount);
        }

        [Test]
        public void CapacityCapabilityExposesWriterContractAndConstructors()
        {
            Assert.IsNull(typeof(ICheatCommandControl).GetProperty("MaximumConcurrentCommandCount"));
            Assert.False(typeof(ICheatCommandAdmissionPublisher).IsAssignableFrom(
                typeof(ICheatCommandRuntime)));
            Assert.True(typeof(ICheatCommandAdmissionPublisher).IsAssignableFrom(
                typeof(CheatCommandRuntime)));
            Assert.IsNull(typeof(ICheatCommandRuntime).GetProperty("LogWriter"));
            Assert.True(typeof(ICheatLogWriterConfigurable).IsAssignableFrom(
                typeof(CheatCommandRuntime)));
            Assert.NotNull(typeof(CheatCommandRuntime).GetConstructor(new[] { typeof(int) }));
            Assert.NotNull(typeof(CheatCommandRuntime).GetConstructor(new[]
            {
                typeof(int),
                typeof(ILogWriter)
            }));
        }

#if !ENABLE_CHEAT
        [Test]
        public void DisabledRuntimeIsSafeNoOp()
        {
            using var runtime = new CheatCommandRuntime();

            runtime.CancelCommand("Command");
            runtime.ClearAll();

            Assert.False(runtime.IsEnabled);
            Assert.False(runtime.IsCommandRunning("Command"));
            Assert.AreEqual(0, runtime.RunningCommandCount);
            Assert.AreEqual(0, runtime.Metrics.PublishedCommandCount);
        }

        [Test]
        public async Task DisabledAdmissionPublisherReportsBuildGateWithoutDispatch()
        {
            using var runtime = new CheatCommandRuntime(maximumConcurrentCommandCount: 1);
            var admissionPublisher = (ICheatCommandAdmissionPublisher)runtime;

            CheatCommandPublishResult result = await admissionPublisher.TryPublishAsync(
                new CheatCommand("Command"));

            Assert.AreEqual(CheatCommandPublishResult.Disabled, result);
            Assert.AreEqual(0, runtime.Metrics.CapacityRejectedCommandCount);
            Assert.AreEqual(1, admissionPublisher.MaximumConcurrentCommandCount);
        }
#else
        [Test]
        public async Task EnabledRuntimePublishesCommandAndUpdatesMetrics()
        {
            using var runtime = new CheatCommandRuntime();

            await runtime.PublishAsync<CheatCommand>(new CheatCommand("Command"));

            Assert.True(runtime.IsEnabled);
            Assert.AreEqual(1, runtime.Metrics.PublishedCommandCount);
            Assert.AreEqual(1, runtime.Metrics.CompletedCommandCount);
        }

        [Test]
        public async Task AdmissionIsAtomicWhenDuplicateAndUniqueCommandsCompeteForLastSlot()
        {
            var logWriter = new RecordingLogWriter();
            using var runtime = new CheatCommandRuntime(
                maximumConcurrentCommandCount: 2,
                logWriter: logWriter);
            var router = new Router();
            using var releaseHandlers = new ManualResetEventSlim(false);
            using var firstHandlerEntered = new ManualResetEventSlim(false);
            using var secondHandlerEntered = new ManualResetEventSlim(false);
            int handlerCount = 0;
            using var subscription = router.Subscribe<CheatCommand>((_, __) =>
            {
                int current = Interlocked.Increment(ref handlerCount);
                if (current == 1)
                {
                    firstHandlerEntered.Set();
                }
                else if (current == 2)
                {
                    secondHandlerEntered.Set();
                }

                releaseHandlers.Wait(TimeSpan.FromSeconds(10));
            });
            var options = new CheatCommandExecutionOptions(router);
            Task<CheatCommandPublishResult> anchorTask = null;
            Task<CheatCommandPublishResult> duplicateTask = null;
            Task<CheatCommandPublishResult> uniqueTask = null;

            try
            {
                anchorTask = Task.Run(async () => await runtime.TryPublishAsync(
                    new CheatCommand("Anchor"),
                    options));
                Assert.True(firstHandlerEntered.Wait(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(1, runtime.RunningCommandCount);

                using var startCompetitors = new ManualResetEventSlim(false);
                duplicateTask = Task.Run(async () =>
                {
                    startCompetitors.Wait();
                    return await runtime.TryPublishAsync(new CheatCommand("Anchor"), options);
                });
                uniqueTask = Task.Run(async () =>
                {
                    startCompetitors.Wait();
                    return await runtime.TryPublishAsync(new CheatCommand("Unique"), options);
                });

                startCompetitors.Set();
                Assert.True(secondHandlerEntered.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(duplicateTask.Wait(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(CheatCommandPublishResult.DuplicateRejected, duplicateTask.Result);
                Assert.AreEqual(2, runtime.RunningCommandCount);
                Assert.LessOrEqual(
                    runtime.RunningCommandCount,
                    runtime.MaximumConcurrentCommandCount);

                CheatCommandPublishResult fullDuplicate = await runtime.TryPublishAsync(
                    new CheatCommand("Anchor"),
                    options);
                CheatCommandPublishResult capacityResult = await runtime.TryPublishAsync(
                    new CheatCommand("ThirdUnique"),
                    options);

                Assert.AreEqual(CheatCommandPublishResult.DuplicateRejected, fullDuplicate);
                Assert.AreEqual(CheatCommandPublishResult.CapacityRejected, capacityResult);
                Assert.AreEqual(2, runtime.RunningCommandCount);
                Assert.AreEqual(2, runtime.Metrics.PublishedCommandCount);
                Assert.AreEqual(2, runtime.Metrics.DroppedDuplicateCount);
                Assert.AreEqual(1, runtime.Metrics.CapacityRejectedCommandCount);
                Assert.AreEqual(0, logWriter.ErrorCount);
                Assert.AreEqual(0, logWriter.ExceptionCount);
            }
            finally
            {
                releaseHandlers.Set();
                if (anchorTask != null)
                {
                    await anchorTask;
                }

                if (uniqueTask != null)
                {
                    await uniqueTask;
                }
            }

            Assert.AreEqual(CheatCommandPublishResult.Published, anchorTask.Result);
            Assert.AreEqual(CheatCommandPublishResult.Published, uniqueTask.Result);
            Assert.AreEqual(0, runtime.RunningCommandCount);
            Assert.AreEqual(2, runtime.Metrics.CompletedCommandCount);
            Assert.AreEqual(0, runtime.Metrics.FaultedCommandCount);
        }

        [Test]
        public async Task CancelCommandRunsCancellationCallbacksOutsideAdmissionLockAndAllowsReentry()
        {
            using var runtime = new CheatCommandRuntime(maximumConcurrentCommandCount: 2);
            var router = new Router();
            var options = new CheatCommandExecutionOptions(router);
            object admissionLock = GetAdmissionLock(runtime);
            using var handlerEntered = new ManualResetEventSlim(false);
            using var releaseHandler = new ManualResetEventSlim(false);
            using var callbackCompleted = new ManualResetEventSlim(false);
            Exception callbackFailure = null;
            bool callbackObservedAdmissionLock = true;
            CheatCommandPublishResult nestedResult = default;
            using var subscription = router.Subscribe<CheatCommand>((command, context) =>
            {
                if (!string.Equals(command.CommandId, "Reentrant", StringComparison.Ordinal))
                {
                    return;
                }

                context.CancellationToken.Register(() =>
                {
                    try
                    {
                        callbackObservedAdmissionLock = Monitor.IsEntered(admissionLock);
                        nestedResult = runtime.TryPublishAsync(
                            new CheatCommand("Nested"),
                            options).GetAwaiter().GetResult();
                        runtime.CancelCommand("Reentrant", router);
                        runtime.ClearAll();
                    }
                    catch (Exception exception)
                    {
                        callbackFailure = exception;
                    }
                    finally
                    {
                        callbackCompleted.Set();
                        releaseHandler.Set();
                    }
                });

                handlerEntered.Set();
                releaseHandler.Wait(TimeSpan.FromSeconds(10));
            });

            Task<CheatCommandPublishResult> publishTask = null;
            try
            {
                publishTask = Task.Run(async () => await runtime.TryPublishAsync(
                    new CheatCommand("Reentrant"),
                    options));
                Assert.True(handlerEntered.Wait(TimeSpan.FromSeconds(5)));

                Task cancelTask = Task.Run(() => runtime.CancelCommand("Reentrant", router));
                Assert.True(callbackCompleted.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(cancelTask.Wait(TimeSpan.FromSeconds(5)));
                await publishTask;
            }
            finally
            {
                releaseHandler.Set();
                if (publishTask != null && !publishTask.IsCompleted)
                {
                    await publishTask;
                }
            }

            Assert.IsNull(callbackFailure);
            Assert.False(callbackObservedAdmissionLock);
            Assert.AreEqual(CheatCommandPublishResult.Published, nestedResult);
            Assert.AreEqual(CheatCommandPublishResult.Published, publishTask.Result);
            Assert.AreEqual(0, runtime.RunningCommandCount);
            Assert.AreEqual(1, runtime.Metrics.CancelRequestedCount);
        }

        [Test]
        public async Task DisposeDetachesStateBeforeRunningCancellationCallbacksOutsideAdmissionLock()
        {
            using var runtime = new CheatCommandRuntime(maximumConcurrentCommandCount: 1);
            var router = new Router();
            var options = new CheatCommandExecutionOptions(router);
            object admissionLock = GetAdmissionLock(runtime);
            using var handlerEntered = new ManualResetEventSlim(false);
            using var releaseHandler = new ManualResetEventSlim(false);
            using var callbackCompleted = new ManualResetEventSlim(false);
            bool callbackObservedAdmissionLock = true;
            bool callbackObservedDetachedState = false;
            using var subscription = router.Subscribe<CheatCommand>((_, context) =>
            {
                context.CancellationToken.Register(() =>
                {
                    callbackObservedAdmissionLock = Monitor.IsEntered(admissionLock);
                    callbackObservedDetachedState = !runtime.IsCommandRunning("DisposeTarget");
                    runtime.Dispose();
                    callbackCompleted.Set();
                    releaseHandler.Set();
                });

                handlerEntered.Set();
                releaseHandler.Wait(TimeSpan.FromSeconds(10));
            });

            Task<CheatCommandPublishResult> publishTask = null;
            try
            {
                publishTask = Task.Run(async () => await runtime.TryPublishAsync(
                    new CheatCommand("DisposeTarget"),
                    options));
                Assert.True(handlerEntered.Wait(TimeSpan.FromSeconds(5)));

                Task disposeTask = Task.Run(runtime.Dispose);
                Assert.True(callbackCompleted.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(disposeTask.Wait(TimeSpan.FromSeconds(5)));
                await publishTask;
            }
            finally
            {
                releaseHandler.Set();
                if (publishTask != null && !publishTask.IsCompleted)
                {
                    await publishTask;
                }
            }

            Assert.False(callbackObservedAdmissionLock);
            Assert.True(callbackObservedDetachedState);
            Assert.False(runtime.IsEnabled);
            Assert.AreEqual(0, runtime.RunningCommandCount);
            Assert.AreEqual(CheatCommandPublishResult.Published, publishTask.Result);
        }

        [Test]
        public async Task ClearAllUsesStableSnapshotWhenFirstCancellationCompletesEveryHandler()
        {
            const int CommandCount = 4;
            using var runtime = new CheatCommandRuntime(maximumConcurrentCommandCount: CommandCount);
            var router = new Router();
            var options = new CheatCommandExecutionOptions(
                router,
                CheatDuplicatePolicy.AllowParallel);
            using var handlersEntered = new CountdownEvent(CommandCount);
            using var releaseHandlers = new ManualResetEventSlim(false);
            int cancellationCallbackCount = 0;
            using var subscription = router.Subscribe<CheatCommand>((_, context) =>
            {
                context.CancellationToken.Register(() =>
                {
                    Interlocked.Increment(ref cancellationCallbackCount);
                    releaseHandlers.Set();
                });

                handlersEntered.Signal();
                releaseHandlers.Wait(TimeSpan.FromSeconds(10));
            });

            var publishTasks = new Task<CheatCommandPublishResult>[CommandCount];
            try
            {
                for (int i = 0; i < publishTasks.Length; i++)
                {
                    publishTasks[i] = Task.Run(async () => await runtime.TryPublishAsync(
                        new CheatCommand("Batch"),
                        options));
                }

                Assert.True(handlersEntered.Wait(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(CommandCount, runtime.RunningCommandCount);

                runtime.ClearAll();
                await Task.WhenAll(publishTasks);
            }
            finally
            {
                releaseHandlers.Set();
                await Task.WhenAll(publishTasks);
            }

            Assert.AreEqual(CommandCount, cancellationCallbackCount);
            Assert.AreEqual(0, runtime.RunningCommandCount);
            Assert.AreEqual(0, runtime.Metrics.FaultedCommandCount);
            for (int i = 0; i < publishTasks.Length; i++)
            {
                Assert.AreEqual(CheatCommandPublishResult.Published, publishTasks[i].Result);
            }
        }

        [Test]
        public async Task ClearAllContinuesWhenCancellationCallbacksAndLogWriterThrow()
        {
            const int CommandCount = 2;
            using var runtime = new CheatCommandRuntime(
                maximumConcurrentCommandCount: CommandCount,
                logWriter: new ThrowingLogWriter());
            var router = new Router();
            var options = new CheatCommandExecutionOptions(
                router,
                CheatDuplicatePolicy.AllowParallel);
            using var handlersEntered = new CountdownEvent(CommandCount);
            using var releaseHandlers = new ManualResetEventSlim(false);
            int cancellationCallbackCount = 0;
            using var subscription = router.Subscribe<CheatCommand>((_, context) =>
            {
                context.CancellationToken.Register(() =>
                {
                    Interlocked.Increment(ref cancellationCallbackCount);
                    releaseHandlers.Set();
                    throw new InvalidOperationException("Expected cancellation callback failure.");
                });

                handlersEntered.Signal();
                releaseHandlers.Wait(TimeSpan.FromSeconds(10));
            });

            var publishTasks = new Task<CheatCommandPublishResult>[CommandCount];
            try
            {
                for (int i = 0; i < publishTasks.Length; i++)
                {
                    publishTasks[i] = Task.Run(async () => await runtime.TryPublishAsync(
                        new CheatCommand("ThrowingBatch"),
                        options));
                }

                Assert.True(handlersEntered.Wait(TimeSpan.FromSeconds(5)));
                Assert.DoesNotThrow(runtime.ClearAll);
                await Task.WhenAll(publishTasks);
            }
            finally
            {
                releaseHandlers.Set();
                await Task.WhenAll(publishTasks);
            }

            Assert.AreEqual(CommandCount, cancellationCallbackCount);
            Assert.AreEqual(0, runtime.Metrics.CancelRequestedCount);
            Assert.AreEqual(0, runtime.RunningCommandCount);
        }
#endif

#if ENABLE_CHEAT
        private static object GetAdmissionLock(CheatCommandRuntime runtime)
        {
            FieldInfo field = typeof(CheatCommandRuntime).GetField(
                "_admissionLock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return field.GetValue(runtime);
        }
#endif

        private sealed class RecordingLogWriter : ILogWriter
        {
            private int _errorCount;
            private int _exceptionCount;

            public int ErrorCount => Volatile.Read(ref _errorCount);
            public int ExceptionCount => Volatile.Read(ref _exceptionCount);

            public bool IsEnabled(LogSeverity severity, string category) => true;

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => RecordSeverity(severity);

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => RecordSeverity(severity);

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => RecordSeverity(severity);

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                RecordSeverity(severity);
                Interlocked.Increment(ref _exceptionCount);
            }

            private void RecordSeverity(LogSeverity severity)
            {
                if (severity >= LogSeverity.Error && severity < LogSeverity.None)
                {
                    Interlocked.Increment(ref _errorCount);
                }
            }
        }

        private sealed class ThrowingLogWriter : ILogWriter
        {
            public bool IsEnabled(LogSeverity severity, string category) => true;

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Throw();

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Throw();

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Throw();

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Throw();

            private static void Throw() =>
                throw new InvalidOperationException("Expected log writer failure.");
        }
    }
}
