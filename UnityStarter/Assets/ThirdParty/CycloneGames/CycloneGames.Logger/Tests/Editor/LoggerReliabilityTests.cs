using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class LoggerReliabilityTests
    {
        [Test]
        public void FullQueue_DoesNotInvokeBuilderWithoutReservation()
        {
            using var logger = CreateSingleThreaded(maxMessages: 1, maxCharacters: 128);
            var sink = new RecordingSink();
            logger.AddLogger(sink);
            logger.Log(LogLevel.Info, "first", filePath: string.Empty, memberName: string.Empty);

            bool invoked = false;
            logger.Log(LogLevel.Info, builder =>
            {
                invoked = true;
                builder.Append("second");
            }, filePath: string.Empty, memberName: string.Empty);

            logger.Pump(8);
            Assert.IsFalse(invoked);
            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(1, logger.GetProcessingStatistics().DroppedNewestCount);
        }

        [Test]
        public void DropOldest_NormalMessageNeverEvictsQueuedCriticalMessage()
        {
            var options = CreateOptions(2, 128);
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            using var logger = CLoggerFactory.CreateSingleThreaded(options);
            var sink = new RecordingSink();
            logger.AddLogger(sink);

            logger.Log(LogLevel.Error, "critical", filePath: string.Empty, memberName: string.Empty);
            logger.Log(LogLevel.Info, "normal-old", filePath: string.Empty, memberName: string.Empty);
            logger.Log(LogLevel.Info, "normal-new", filePath: string.Empty, memberName: string.Empty);
            logger.Pump(8);

            CollectionAssert.AreEqual(new[] { "critical", "normal-new" }, sink.Messages);
            Assert.AreEqual(0, logger.GetProcessingStatistics().DroppedCriticalCount);
        }

        [Test]
        public void FullQueue_DropOldestBuilderDoesNotPreEvictBeforeActualSizeIsKnown()
        {
            LoggerProcessingOptions options = CreateOptions(1, 128);
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            using var logger = CLoggerFactory.CreateSingleThreaded(options);
            var sink = new RecordingSink();
            logger.AddLogger(sink);
            logger.Log(LogLevel.Info, "old", filePath: string.Empty, memberName: string.Empty);

            bool invoked = false;
            logger.Log(
                LogLevel.Info,
                builder =>
                {
                    invoked = true;
                    builder.Append('x');
                },
                filePath: string.Empty,
                memberName: string.Empty);
            logger.Pump(1);

            Assert.IsFalse(invoked);
            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual("old", sink.Messages[0]);
            Assert.AreEqual(0, logger.GetProcessingStatistics().DroppedOldestCount);
            Assert.AreEqual(1, logger.GetProcessingStatistics().DroppedNewestCount);
        }

        [Test]
        public void ThrowingBuilder_WithCapacityEmitsDiagnosticReplacement()
        {
            using var logger = CreateSingleThreaded(2, 256);
            var sink = new RecordingSink();
            logger.AddLogger(sink);

            logger.Log(
                LogLevel.Info,
                static builder => throw new InvalidOperationException("Expected builder failure."),
                filePath: string.Empty,
                memberName: string.Empty);
            logger.Pump(1);

            Assert.AreEqual(1, sink.Count);
            StringAssert.Contains("log message builder failed: InvalidOperationException", sink.Messages[0]);
            Assert.AreEqual(1, logger.GetProcessingStatistics().MessageBuilderFailureCount);
        }

        [Test]
        public void ThrowingBuilders_UseBoundedQueueAndOneEmergencyReportPerLogger()
        {
            using var logger = CreateSingleThreaded(2, 256);
            var sink = new RecordingSink();
            logger.AddLogger(sink);

            for (int i = 0; i < 32; i++)
            {
                logger.Log(
                    LogLevel.Info,
                    static builder => throw new InvalidOperationException("Expected builder failure."),
                    filePath: string.Empty,
                    memberName: string.Empty);
                logger.Pump(1);
            }

            LogProcessingStatistics statistics = logger.GetProcessingStatistics();
            Assert.AreEqual(32, sink.Count);
            Assert.AreEqual(32, statistics.MessageBuilderFailureCount);
            Assert.AreEqual(0, statistics.ReservedCount);
            Assert.AreEqual(1, logger.MessageBuilderFailureEmergencyReportCount);
        }

        [Test]
        public void FatalBuilderException_PropagatesAndReleasesReservation()
        {
            using var logger = CreateSingleThreaded(2, 256);
            logger.AddLogger(new RecordingSink());

            Assert.Throws<OutOfMemoryException>(() => logger.Log(
                LogLevel.Info,
                static builder => throw new OutOfMemoryException("Synthetic test failure."),
                filePath: string.Empty,
                memberName: string.Empty));

            Assert.AreEqual(0, logger.GetProcessingStatistics().ReservedCount);
        }

        [Test]
        public void ThrowingTimestampProvider_IsQuarantinedAfterFirstObservedFailure()
        {
            int providerCalls = 0;
            LoggerProcessingOptions options = CreateOptions(2048, 128 * 1024);
            using var logger = CLoggerFactory.CreateSingleThreaded(
                options,
                () =>
                {
                    Interlocked.Increment(ref providerCalls);
                    throw new InvalidOperationException("Expected timestamp failure.");
                });
            var sink = new RecordingSink();
            logger.AddLogger(sink);
            logger.Log(LogLevel.Info, "trip", filePath: string.Empty, memberName: string.Empty);

            var producers = new Thread[8];
            for (int i = 0; i < producers.Length; i++)
            {
                producers[i] = new Thread(() =>
                {
                    for (int messageIndex = 0; messageIndex < 100; messageIndex++)
                    {
                        logger.Log(LogLevel.Info, "message", filePath: string.Empty, memberName: string.Empty);
                    }
                });
                producers[i].Start();
            }

            for (int i = 0; i < producers.Length; i++)
            {
                Assert.IsTrue(producers[i].Join(2000));
            }

            logger.Pump(2048);
            Assert.AreEqual(1, providerCalls);
            Assert.AreEqual(801, sink.Count);
            Assert.AreEqual(1, logger.GetProcessingStatistics().TimestampProviderFailureCount);
        }

        [Test]
        public void FatalTimestampFailure_DoesNotRentOrLeakBuilderPoolState()
        {
            using var logger = CLoggerFactory.CreateSingleThreaded(
                CreateOptions(4, 256),
                static () => throw new OutOfMemoryException("Synthetic timestamp failure."));
            logger.AddLogger(new RecordingSink());
            LoggerMemoryStatistics before = CLogger.GetMemoryStatistics();
            bool builderInvoked = false;

            Assert.Throws<OutOfMemoryException>(() => logger.Log(
                LogLevel.Info,
                builder =>
                {
                    builderInvoked = true;
                    builder.Append("unreachable");
                },
                filePath: string.Empty,
                memberName: string.Empty));

            LoggerMemoryStatistics after = CLogger.GetMemoryStatistics();
            Assert.IsFalse(builderInvoked);
            Assert.AreEqual(before.RetainedStringBuilders, after.RetainedStringBuilders);
            Assert.AreEqual(0, logger.GetProcessingStatistics().ReservedCount);
        }

        [Test]
        public void CharacterBudget_DropsEntryThatWouldExceedBound()
        {
            using var logger = CreateSingleThreaded(maxMessages: 4, maxCharacters: 10);
            var sink = new RecordingSink();
            logger.AddLogger(sink);

            logger.Log(LogLevel.Info, "1234567", filePath: string.Empty, memberName: string.Empty);
            logger.Log(LogLevel.Info, "abcd", filePath: string.Empty, memberName: string.Empty);
            LogProcessingStatistics beforePump = logger.GetProcessingStatistics();
            logger.Pump(8);

            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(7, beforePump.PeakQueuedCharacters);
            Assert.AreEqual(1, beforePump.DroppedNewestCount);
        }

        [Test]
        public void ProcessingOptions_RejectAggregateEntryLimitsBeyondQueueBudget()
        {
            LoggerProcessingOptions options = CreateOptions(4, 128);
            options.MaxMessageCharacters = 100;
            options.MaxCategoryCharacters = 20;
            options.MaxSourcePathCharacters = 20;
            options.MaxMemberNameCharacters = 20;

            Assert.Throws<ArgumentOutOfRangeException>(() => LoggerProcessingOptions.CreateValidated(options));
        }

        [Test]
        public void ProcessingOptions_RejectFilterCharacterBudgetSmallerThanOneCategory()
        {
            LoggerProcessingOptions options = CreateOptions(4, 128);
            options.MaxCategoryCharacters = 8;
            options.MaxFilterCharacters = 7;

            Assert.Throws<ArgumentOutOfRangeException>(() => LoggerProcessingOptions.CreateValidated(options));
        }

        [Test]
        public void ProcessingOptions_RejectUnityConsoleBlockPolicyAndCloneDropPolicy()
        {
            LoggerProcessingOptions invalid = CreateOptions(4, 512);
            invalid.UnityConsoleOverflowPolicy = LogQueueOverflowPolicy.Block;
            Assert.Throws<ArgumentOutOfRangeException>(() => LoggerProcessingOptions.CreateValidated(invalid));

            LoggerProcessingOptions source = CreateOptions(4, 512);
            source.UnityConsoleOverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            LoggerProcessingOptions clone = source.Clone();
            Assert.AreEqual(LogQueueOverflowPolicy.DropOldest, clone.UnityConsoleOverflowPolicy);
        }

        [Test]
        public void CriticalCommitRejectedAfterStop_IsCountedAsCriticalDrop()
        {
            LoggerProcessingOptions options = CreateOptions(4, 256);
            var queue = new BoundedLogQueue(options);
            Assert.IsTrue(queue.TryReserve(LogLevel.Error, 16, true, out int reservedCharacters));
            LogMessage message = LogMessagePool.Get();
            message.Initialize(DateTime.UtcNow, LogLevel.Error, "critical", null, null, null, 0, null, 16, 1, 1, 1);
            queue.CompleteAdding();

            Assert.IsFalse(queue.TryCommit(message, reservedCharacters, message.GetRetainedCharacterCount()));
            LogMessagePool.Return(message);
            LogProcessingStatistics statistics = queue.GetStatistics();
            Assert.AreEqual(1, statistics.RejectedAfterStopCount);
            Assert.AreEqual(1, statistics.DroppedCriticalCount);
            queue.Dispose();
        }

        [Test]
        public void CommitCannotConsumeCharactersOutsideItsReservation()
        {
            LoggerProcessingOptions options = CreateOptions(4, 32);
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            var queue = new BoundedLogQueue(options);

            Assert.IsTrue(queue.TryReserve(LogLevel.Info, 5, true, out int firstReservation));
            LogMessage first = LogMessagePool.Get();
            first.Initialize(DateTime.UtcNow, LogLevel.Info, "first", null, null, null, 0, null, 16, 1, 1, 1);
            Assert.IsTrue(queue.TryCommit(first, firstReservation, first.GetRetainedCharacterCount()));

            Assert.IsTrue(queue.TryReserve(LogLevel.Info, 3, true, out int secondReservation));
            LogMessage second = LogMessagePool.Get();
            second.Initialize(DateTime.UtcNow, LogLevel.Info, "four", null, null, null, 0, null, 16, 1, 1, 1);
            Assert.IsFalse(queue.TryCommit(second, secondReservation, second.GetRetainedCharacterCount()));
            LogMessagePool.Return(second);

            LogProcessingStatistics statistics = queue.GetStatistics();
            Assert.AreEqual(1, statistics.QueuedCount);
            Assert.AreEqual(0, statistics.ReservedCount);
            Assert.AreEqual(0, statistics.DroppedOldestCount);
            Assert.AreEqual(1, statistics.DroppedNewestCount);
            Assert.IsTrue(queue.TryDequeue(out LogMessage retained, out int retainedCharacters));
            Assert.AreSame(first, retained);
            LogMessagePool.Return(retained);
            queue.CompleteProcessing(retainedCharacters);
            queue.Dispose();
        }

        [Test]
        public void CategoryFilters_EnforceCombinedEntryAndCharacterBudgets()
        {
            LoggerProcessingOptions options = CreateOptions(4, 128);
            options.MaxCategoryCharacters = 8;
            options.MaxFilterCategories = 2;
            options.MaxFilterCharacters = 8;
            using var logger = CLoggerFactory.CreateSingleThreaded(options);

            logger.AddToWhiteList("aa");
            logger.AddToBlackList("bbb");
            logger.AddToWhiteList("AA");
            Assert.Throws<InvalidOperationException>(() => logger.AddToBlackList("c"));
            Assert.Throws<ArgumentOutOfRangeException>(() => logger.AddToWhiteList("123456789"));

            LogProcessingStatistics statistics = logger.GetProcessingStatistics();
            Assert.AreEqual(2, statistics.FilterCategoryCount);
            Assert.AreEqual(5, statistics.FilterCharacters);
            Assert.AreEqual(2, statistics.RejectedFilterMutationCount);

            logger.RemoveFromWhiteList("aA");
            logger.AddToBlackList("cccc");
            statistics = logger.GetProcessingStatistics();
            Assert.AreEqual(2, statistics.FilterCategoryCount);
            Assert.AreEqual(7, statistics.FilterCharacters);
        }

        [Test]
        public void BlackListFilter_FailsClosedForCategoryBeyondCanonicalLimit()
        {
            LoggerProcessingOptions options = CreateOptions(4, 128);
            options.MaxCategoryCharacters = 3;
            using var logger = CLoggerFactory.CreateSingleThreaded(options);
            var sink = new RecordingSink();
            logger.AddLogger(sink);
            logger.SetLogFilter(LogFilter.LogNoBlackList);
            logger.AddToBlackList("Net");

            logger.Log(LogLevel.Info, "blocked", "NetSuffix", string.Empty, 0, string.Empty);
            logger.Log(LogLevel.Info, "accepted", "UI", string.Empty, 0, string.Empty);
            logger.Pump(4);

            CollectionAssert.AreEqual(new[] { "accepted" }, sink.Messages);
        }

        [Test]
        public void OversizedMessage_IsBoundedAndMarkedTruncated()
        {
            LoggerProcessingOptions options = CreateOptions(4, 64);
            options.MaxMessageCharacters = 5;
            using var logger = CLoggerFactory.CreateSingleThreaded(options);
            var sink = new RecordingSink();
            logger.AddLogger(sink);

            logger.Log(LogLevel.Info, "abcdefgh", filePath: string.Empty, memberName: string.Empty);
            logger.Pump(8);

            Assert.AreEqual("abcde [truncated]", sink.Messages[0]);
        }

        [Test]
        public void QueueOwnedPayload_BoundsMessageBuilderAndMetadataReferences()
        {
            LoggerProcessingOptions options = CreateOptions(4, 128);
            options.MaxMessageCharacters = 5;
            options.MaxCategoryCharacters = 4;
            options.MaxSourcePathCharacters = 6;
            options.MaxMemberNameCharacters = 3;
            using var logger = CLoggerFactory.CreateSingleThreaded(options);
            var sink = new PayloadShapeSink();
            logger.AddLogger(sink);

            logger.Log(
                LogLevel.Info,
                new string('m', 1024),
                new string('c', 32),
                new string('p', 64),
                17,
                new string('n', 32));
            logger.Pump(1);

            Assert.AreEqual(5, sink.OriginalMessageLength);
            Assert.AreEqual(0, sink.MessageBuilderCapacity);
            Assert.AreEqual(4, sink.CategoryLength);
            Assert.AreEqual(6, sink.FilePathLength);
            Assert.AreEqual(3, sink.MemberNameLength);
            Assert.IsTrue(sink.WasTruncated);

            logger.Log(
                LogLevel.Info,
                static builder => builder.Append('x', 1024),
                new string('c', 32),
                new string('p', 64),
                18,
                new string('n', 32));
            logger.Pump(1);

            Assert.LessOrEqual(sink.OriginalMessageLength, 5);
            Assert.LessOrEqual(sink.MessageBuilderCapacity, 5);
            Assert.AreEqual(4, sink.CategoryLength);
            Assert.AreEqual(6, sink.FilePathLength);
            Assert.AreEqual(3, sink.MemberNameLength);
            Assert.IsTrue(sink.WasTruncated);
        }

        [Test]
        public void AddLoggerUnique_DisposesRejectedDifferentInstance()
        {
            using var logger = CreateSingleThreaded(4, 128);
            var accepted = new DisposableSink();
            var rejected = new DisposableSink();

            Assert.IsTrue(logger.AddLoggerUnique(accepted));
            Assert.IsFalse(logger.AddLoggerUnique(rejected));
            Assert.AreEqual(0, accepted.DisposeCount);
            Assert.AreEqual(1, rejected.DisposeCount);
        }

        [Test]
        public void RepeatedSinkFailures_QuarantineOnlyFailingSink()
        {
            LoggerProcessingOptions options = CreateOptions(8, 1024);
            options.SinkFailureThreshold = 2;
            using var logger = CLoggerFactory.CreateSingleThreaded(options);
            var failing = new ThrowingSink();
            var recording = new RecordingSink();
            logger.AddLogger(failing);
            logger.AddLogger(recording);

            for (int i = 0; i < 3; i++)
            {
                logger.Log(LogLevel.Info, "message", filePath: string.Empty, memberName: string.Empty);
                logger.Pump(1);
            }

            LogProcessingStatistics statistics = logger.GetProcessingStatistics();
            Assert.AreEqual(2, failing.CallCount);
            Assert.AreEqual(3, recording.Count);
            Assert.AreEqual(2, statistics.SinkFailureCount);
            Assert.AreEqual(1, statistics.QuarantinedSinkCount);
        }

        [Test]
        public void RepeatedQuarantine_RemovesAndDisposesEachFailedRegistration()
        {
            LoggerProcessingOptions options = CreateOptions(8, 1024);
            options.SinkFailureThreshold = 1;
            using var logger = CLoggerFactory.CreateSingleThreaded(options);

            for (int i = 0; i < 8; i++)
            {
                var sink = new ThrowingDisposableSink();
                Assert.IsTrue(logger.AddLoggerUnique(sink));
                logger.Log(LogLevel.Info, "message", filePath: string.Empty, memberName: string.Empty);
                logger.Pump(1);
                Assert.AreEqual(1, sink.CallCount);
                Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref sink.DisposeCount) == 1, 2000));
                Assert.AreEqual(1, sink.DisposeCount);
            }

            LogProcessingStatistics statistics = logger.GetProcessingStatistics();
            Assert.AreEqual(8, statistics.SinkFailureCount);
            Assert.AreEqual(8, statistics.QuarantinedSinkCount);
        }

        [Test]
        public void RemoveLogger_ReturnsFalseUntilEarlierDispatchQuiesces()
        {
            using var logger = CLoggerFactory.CreateThreaded(CreateOptions(8, 1024));
            var sink = new BlockingSink();
            logger.AddLogger(sink);
            logger.Log(LogLevel.Info, "blocked", filePath: string.Empty, memberName: string.Empty);
            Assert.IsTrue(sink.Entered.Wait(2000), "Sink did not receive the message.");

            Assert.IsFalse(logger.RemoveLogger(sink, 10));
            Assert.IsFalse(logger.RemoveLogger(sink, 10));
            sink.Release.Set();
            Assert.IsTrue(logger.RemoveLogger(sink, 2000));
            sink.Dispose();
        }

        [Test]
        public void RemoveLogger_ReturnsFalseWhenSinkWasNeverRegistered()
        {
            using var logger = CreateSingleThreaded(4, 128);
            var sink = new DisposableSink();

            Assert.IsFalse(logger.RemoveLogger(sink));
            Assert.AreEqual(0, sink.DisposeCount);
            sink.Dispose();
        }

        [Test]
        public void InFlightEntry_RemainsInsideQueueCapacityBudget()
        {
            using var logger = CLoggerFactory.CreateThreaded(CreateOptions(1, 64));
            var sink = new BlockingSink();
            logger.AddLogger(sink);
            logger.Log(LogLevel.Info, "first", filePath: string.Empty, memberName: string.Empty);
            Assert.IsTrue(sink.Entered.Wait(2000), "Sink did not receive the first message.");

            logger.Log(LogLevel.Info, "second", filePath: string.Empty, memberName: string.Empty);
            LogProcessingStatistics statistics = logger.GetProcessingStatistics();

            Assert.AreEqual(1, statistics.InFlightCount);
            Assert.AreEqual(1, statistics.PeakQueuedCount);
            Assert.AreEqual(1, statistics.DroppedNewestCount);
            sink.Release.Set();
            Assert.IsTrue(logger.RemoveLogger(sink, 2000));
            sink.Dispose();
        }

        [Test]
        public void BudgetedSingleThreadPump_StopsBetweenSlowEntries()
        {
            using var logger = CLoggerFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var sink = new SlowSink(20);
            logger.AddLogger(sink);
            logger.Log(LogLevel.Info, "one", filePath: string.Empty, memberName: string.Empty);
            logger.Log(LogLevel.Info, "two", filePath: string.Empty, memberName: string.Empty);
            logger.Log(LogLevel.Info, "three", filePath: string.Empty, memberName: string.Empty);

            logger.PumpWithinBudget(3, 1);

            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(2, logger.GetProcessingStatistics().QueuedCount);
        }

        [Test]
        public void ConcurrentRemoveAndShutdown_TransfersOwnershipExactlyOnceWithoutThrowing()
        {
            var logger = CLoggerFactory.CreateThreaded(CreateOptions(8, 1024));
            var sink = new BlockingDisposableSink();
            logger.AddLogger(sink);
            logger.Log(LogLevel.Info, "blocked", filePath: string.Empty, memberName: string.Empty);
            Assert.IsTrue(sink.Entered.Wait(2000), "Sink did not receive the message.");

            Exception removeException = null;
            Exception shutdownException = null;
            bool removeResult = false;
            var removeThread = new Thread(() =>
            {
                try
                {
                    removeResult = logger.RemoveLogger(sink, 5000);
                    if (removeResult)
                    {
                        sink.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    removeException = exception;
                }
            });
            var shutdownThread = new Thread(() =>
            {
                try
                {
                    logger.ShutdownInstance(LogFlushMode.Buffered, 5000);
                }
                catch (Exception exception)
                {
                    shutdownException = exception;
                }
            });

            removeThread.Start();
            Thread.Sleep(20);
            Assert.IsTrue(removeThread.IsAlive, "RemoveLogger did not enter its quiescence wait.");
            shutdownThread.Start();
            sink.Release.Set();

            Assert.IsTrue(removeThread.Join(5000));
            Assert.IsTrue(shutdownThread.Join(5000));
            Assert.IsNull(removeException);
            Assert.IsNull(shutdownException);
            Assert.AreEqual(1, sink.DisposeCount);
            logger.Dispose();
            sink.DisposeEvents();
        }

        [Test]
        public void Shutdown_DoesNotReportCompleteWhileQuarantinedSinkDisposalIsRunning()
        {
            LoggerProcessingOptions options = CreateOptions(4, 256);
            options.SinkFailureThreshold = 1;
            options.ShutdownDrainTimeoutMs = 50;
            var logger = CLoggerFactory.CreateSingleThreaded(options);
            var sink = new ThrowingBlockingDisposeSink();
            logger.AddLogger(sink);
            logger.Log(LogLevel.Info, "failure", filePath: string.Empty, memberName: string.Empty);

            var pumpThread = new Thread(() => logger.Pump(1));
            pumpThread.Start();
            Assert.IsTrue(sink.DisposeEntered.Wait(2000), "Quarantined sink disposal did not start.");

            LoggerShutdownResult timedOut = logger.ShutdownInstance(LogFlushMode.Buffered, 20);
            Assert.AreEqual(LoggerShutdownStatus.TimedOut, timedOut.Status);
            Assert.IsFalse(timedOut.IsComplete);
            Assert.IsTrue(logger.IsSinkDisposalExecutorRunning);

            sink.DisposeRelease.Set();
            Assert.IsTrue(pumpThread.Join(2000));
            Assert.IsTrue(
                SpinWait.SpinUntil(() => !logger.IsSinkDisposalExecutorRunning, 2000),
                "Timed-out shutdown did not request eventual disposal-executor termination.");
            LoggerShutdownResult completed = logger.ShutdownInstance(LogFlushMode.Buffered, 2000);
            Assert.IsTrue(completed.IsComplete);
            Assert.AreEqual(1, sink.DisposeCount);
            logger.Dispose();
            sink.DisposeEvents();
        }

        [Test]
        public void TimedOutShutdown_PreservesSingleSerializedDisposalOwner()
        {
            LoggerProcessingOptions options = CreateOptions(4, 256);
            options.SinkFailureThreshold = 1;
            var logger = CLoggerFactory.CreateSingleThreaded(options);
            var first = new ThrowingBlockingDisposeSink();
            var second = new BlockingLogDisposeTrackingSink();
            using var workerExitEntered = new ManualResetEventSlim();
            using var allowWorkerExit = new ManualResetEventSlim();
            logger.SinkDisposalBeforeExitTestHook = () =>
            {
                workerExitEntered.Set();
                allowWorkerExit.Wait();
            };
            logger.AddLogger(first);

            LogMessage firstMessage = LogMessagePool.Get();
            firstMessage.Initialize(DateTime.UtcNow, LogLevel.Info, "first", null, null, null, 0, null, 16, 1, 1, 1);
            logger.DispatchToLoggers(firstMessage);
            LogMessagePool.Return(firstMessage);
            Assert.IsTrue(first.DisposeEntered.Wait(2000), "First sink disposal did not start.");

            logger.AddLogger(second);
            LogMessage secondMessage = LogMessagePool.Get();
            secondMessage.Initialize(DateTime.UtcNow, LogLevel.Info, "second", null, null, null, 0, null, 16, 1, 1, 1);
            var dispatchThread = new Thread(() => logger.DispatchToLoggers(secondMessage));
            dispatchThread.Start();
            Assert.IsTrue(second.LogEntered.Wait(2000), "Second sink dispatch did not start.");

            LoggerShutdownResult timedOut = logger.ShutdownInstance(LogFlushMode.Buffered, 20);
            Assert.AreEqual(LoggerShutdownStatus.TimedOut, timedOut.Status);
            Assert.IsFalse(second.DisposeEntered.IsSet);

            first.DisposeRelease.Set();
            Assert.IsTrue(workerExitEntered.Wait(2000), "Disposal worker did not enter its atomic owner handoff.");
            second.LogRelease.Set();
            Assert.IsFalse(dispatchThread.Join(100), "Pending sink scheduling bypassed the worker-owner handoff lock.");

            allowWorkerExit.Set();
            Assert.IsTrue(dispatchThread.Join(2000));
            LogMessagePool.Return(secondMessage);
            Assert.IsTrue(second.DisposeEntered.Wait(2000));
            Assert.IsTrue(SpinWait.SpinUntil(() => !logger.IsSinkDisposalExecutorRunning, 2000));
            logger.SinkDisposalBeforeExitTestHook = null;
            Assert.IsTrue(logger.ShutdownInstance(LogFlushMode.Buffered, 2000).IsComplete);
            logger.Dispose();
            first.DisposeEvents();
            second.DisposeEvents();
        }

        [Test]
        public void ShutdownRetry_PreservesFlushFailureAcrossAsynchronousDisposalTimeout()
        {
            LoggerProcessingOptions options = CreateOptions(4, 256);
            options.ShutdownDrainTimeoutMs = 50;
            var logger = CLoggerFactory.CreateSingleThreaded(options);
            var sink = new FlushFailingBlockingDisposeSink();
            logger.AddLogger(sink);

            LoggerShutdownResult timedOut = logger.ShutdownInstance(LogFlushMode.Durable, 20);
            Assert.AreEqual(LoggerShutdownStatus.TimedOut, timedOut.Status);
            Assert.IsFalse(timedOut.SinksFlushed);
            Assert.IsTrue(sink.DisposeEntered.Wait(2000));

            sink.DisposeRelease.Set();
            LoggerShutdownResult completed = logger.ShutdownInstance(LogFlushMode.Durable, 2000);
            Assert.AreEqual(LoggerShutdownStatus.CompletedWithFailures, completed.Status);
            Assert.IsFalse(completed.SinksFlushed);
            Assert.AreEqual(1, sink.FlushCount);
            Assert.AreEqual(1, sink.DisposeCount);
            logger.Dispose();
            sink.DisposeEvents();
        }

        [Test]
        public void ThrowingSinkDispose_IsReportedAsCompletedWithFailures()
        {
            var logger = CLoggerFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var sink = new ThrowingDisposeSink();
            logger.AddLogger(sink);

            LoggerShutdownResult result = logger.ShutdownInstance(LogFlushMode.Buffered, 2000);

            Assert.AreEqual(LoggerShutdownStatus.CompletedWithFailures, result.Status);
            Assert.IsTrue(result.SinksFlushed);
            Assert.AreEqual(1, logger.GetProcessingStatistics().SinkDisposalFailureCount);
            Assert.AreEqual(3, sink.DisposeCount);
            logger.Dispose();
        }

        [Test]
        public void TransientSinkDisposeFailure_IsRetriedWithinBound()
        {
            var logger = CLoggerFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var sink = new TransientThrowingDisposeSink();
            logger.AddLogger(sink);

            LoggerShutdownResult result = logger.ShutdownInstance(LogFlushMode.Buffered, 2000);

            Assert.IsTrue(result.IsComplete);
            Assert.AreNotEqual(LoggerShutdownStatus.CompletedWithFailures, result.Status);
            Assert.AreEqual(2, sink.DisposeCount);
            Assert.AreEqual(0, logger.GetProcessingStatistics().SinkDisposalFailureCount);
            logger.Dispose();
        }

        [Test]
        public void NonIdempotentSinkDisposeFailure_IsNotRetried()
        {
            var logger = CLoggerFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var sink = new NonRetryableThrowingDisposeSink();
            logger.AddLogger(sink);

            LoggerShutdownResult result = logger.ShutdownInstance(LogFlushMode.Buffered, 2000);

            Assert.AreEqual(LoggerShutdownStatus.CompletedWithFailures, result.Status);
            Assert.AreEqual(1, sink.DisposeCount);
            logger.Dispose();
        }

        [Test]
        public void UnityQueue_OldGenerationCannotCommitOrCancelNewReservation()
        {
            ResetUnityLoggerState();
            LoggerUpdater.Configure(CreateOptions(4, 256));
            Assert.IsTrue(LoggerUpdater.TryReserve(LogLevel.Info, 16, out LoggerUpdater.Reservation oldReservation));

            ResetUnityLoggerState();
            LoggerUpdater.Configure(CreateOptions(4, 256));
            Assert.IsTrue(LoggerUpdater.TryReserve(LogLevel.Info, 16, out LoggerUpdater.Reservation currentReservation));
            LoggerUpdater.CancelReservation(oldReservation);

            Assert.IsFalse(LoggerUpdater.Commit(LogLevel.Info, "old", oldReservation));
            Assert.IsTrue(LoggerUpdater.Commit(LogLevel.Info, "current", currentReservation));
            UnityLoggerStatistics statistics = LoggerUpdater.GetStatistics();
            Assert.AreEqual(1, statistics.QueuedCount);
            Assert.AreEqual(1, statistics.DroppedMessageCount);

            LoggerUpdater.Shutdown(false);
            ResetUnityLoggerState();
        }

        [Test]
        public void UnityQueue_CommitCannotConsumeCharactersOutsideItsReservation()
        {
            ResetUnityLoggerState();
            LoggerUpdater.Configure(CreateOptions(4, 512));
            Assert.IsTrue(LoggerUpdater.TryReserve(LogLevel.Info, 3, out LoggerUpdater.Reservation reservation));

            Assert.IsFalse(LoggerUpdater.Commit(LogLevel.Info, "four", reservation));
            UnityLoggerStatistics statistics = LoggerUpdater.GetStatistics();
            Assert.AreEqual(0, statistics.QueuedCount);
            Assert.AreEqual(0, statistics.ReservedCount);
            Assert.AreEqual(1, statistics.DroppedMessageCount);
            LoggerUpdater.Shutdown(false);
            ResetUnityLoggerState();
        }

        [Test]
        public void UnityFormatting_UsesCultureInvariantLineNumberWithoutAllocationPolicyExpansion()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            customCulture.NumberFormat.NegativeSign = new string('!', 1024);
            LogMessage message = LogMessagePool.Get();
            try
            {
                CultureInfo.CurrentCulture = customCulture;
                message.Initialize(DateTime.UtcNow, LogLevel.Info, "message", null, null, "Source.cs", -123, null, 64, 8, 64, 8);

                string formatted = UnityLogger.FormatMessage(message);

                StringAssert.Contains("-123", formatted);
                StringAssert.DoesNotContain(customCulture.NumberFormat.NegativeSign, formatted);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                LogMessagePool.Return(message);
            }
        }

        [TestCase(LogQueueOverflowPolicy.DropNewest, "old")]
        [TestCase(LogQueueOverflowPolicy.DropOldest, "new")]
        public void UnityQueue_UsesDedicatedNonBlockingOverflowPolicy(
            LogQueueOverflowPolicy policy,
            string expectedMessage)
        {
            ResetUnityLoggerState();
            LoggerProcessingOptions options = CreateOptions(1, 512);
            options.UnityConsoleOverflowPolicy = policy;
            LoggerUpdater.Configure(options);
            Assert.IsTrue(LoggerUpdater.TryReserve(LogLevel.Info, 16, out LoggerUpdater.Reservation first));
            Assert.IsTrue(LoggerUpdater.Commit(LogLevel.Info, "old", first));

            bool secondAccepted = LoggerUpdater.TryReserve(
                LogLevel.Info,
                16,
                out LoggerUpdater.Reservation second);
            if (secondAccepted)
            {
                Assert.IsTrue(LoggerUpdater.Commit(LogLevel.Info, "new", second));
            }

            Assert.AreEqual(policy == LogQueueOverflowPolicy.DropOldest, secondAccepted);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Log, expectedMessage);
            Assert.IsTrue(LoggerUpdater.TryFlushUnityQueue(100));
            Assert.AreEqual(1, LoggerUpdater.GetStatistics().DroppedMessageCount);
            LoggerUpdater.Shutdown(false);
            ResetUnityLoggerState();
        }

        [Test]
        public void UnitySubsystemReset_BlocksWhenExplicitAdapterOwnerSurvives()
        {
            ResetUnityLoggerState();
            LoggerUpdater.Configure(CreateOptions(4, 512));
            int adapterGeneration = LoggerUpdater.RegisterAdapter();
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                "CycloneGames.Logger: Explicit UnityLogger owners survived subsystem reset. Dispose their CLogger/UnityLogger owners before starting a new runtime.");

            ResetUnityLoggerState();
            Assert.Throws<InvalidOperationException>(() => LoggerUpdater.Configure(CreateOptions(4, 512)));

            LoggerUpdater.UnregisterAdapter(adapterGeneration);
            LoggerUpdater.Shutdown(false);
            ResetUnityLoggerState();
            LoggerUpdater.Configure(CreateOptions(4, 512));
            LoggerUpdater.Shutdown(false);
            ResetUnityLoggerState();
        }

        [Test]
        public void BlockedDisposalExecutor_BoundsTotalOwnedSinkBacklog()
        {
            var logger = CLoggerFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var blocker = new BlockingDisposeOnlySink();
            Assert.IsTrue(logger.AddLogger(blocker));
            for (int i = 1; i < 256; i++)
            {
                Assert.IsTrue(logger.AddLogger(new DisposableSink()));
            }

            Exception clearException = null;
            var clearThread = new Thread(() =>
            {
                try
                {
                    logger.ClearLoggers();
                }
                catch (Exception exception)
                {
                    clearException = exception;
                }
            });
            clearThread.Start();
            Assert.IsTrue(blocker.DisposeEntered.Wait(2000));
            Assert.IsTrue(clearThread.Join(3000));
            Assert.IsNull(clearException);
            Assert.AreEqual(256, logger.GetProcessingStatistics().PendingSinkDisposalCount);
            Assert.IsFalse(logger.AddLogger(blocker), "A sink already owned by the disposal executor must not be re-registered.");

            var rejected = new DisposableSink();
            Assert.IsFalse(logger.AddLogger(rejected));
            Assert.AreEqual(0, rejected.DisposeCount);
            rejected.Dispose();

            blocker.DisposeRelease.Set();
            Assert.IsTrue(logger.ShutdownInstance(LogFlushMode.Buffered, 5000).IsComplete);
            logger.Dispose();
            blocker.DisposeEvents();
        }

        [Test]
        public void GlobalInstance_RejectsDirectInstanceShutdown()
        {
            CLogger.Shutdown();
            Assert.IsTrue(CLogger.ConfigureSingleThreadedProcessing(CreateOptions(4, 512)));
            CLogger global = CLogger.Instance;

            Assert.Throws<InvalidOperationException>(() => global.ShutdownInstance());
            Assert.IsTrue(CLogger.Shutdown().IsComplete);
        }

        [Test]
        public void ThreadedProcessor_MultipleProducersFlushAllAcceptedEntries()
        {
            const int ProducerCount = 4;
            const int PerProducer = 500;
            LoggerProcessingOptions options = CreateOptions(ProducerCount * PerProducer + 1, 512 * 1024);
            options.ShutdownDrainTimeoutMs = 5000;
            using var logger = CLoggerFactory.CreateThreaded(options);
            var sink = new CountingSink();
            logger.AddLogger(sink);
            var threads = new Thread[ProducerCount];

            for (int producer = 0; producer < ProducerCount; producer++)
            {
                int state = producer;
                threads[producer] = new Thread(() =>
                {
                    for (int i = 0; i < PerProducer; i++)
                    {
                        logger.Log(LogLevel.Info, state, static (value, builder) => builder.Append(value), filePath: string.Empty, memberName: string.Empty);
                    }
                });
                threads[producer].Start();
            }

            for (int i = 0; i < threads.Length; i++)
            {
                Assert.IsTrue(threads[i].Join(5000));
            }

            Assert.IsTrue(logger.TryFlush(LogFlushMode.Buffered, 5000));
            Assert.AreEqual(ProducerCount * PerProducer, sink.Count);
            Assert.AreEqual(0, logger.GetProcessingStatistics().DroppedMessageCount);
        }

        [Test]
        public void LogMessagePool_DoubleReturnIsRejectedWithoutDuplicateRental()
        {
            LogMessagePool.ResetStatistics();
            LogMessage message = LogMessagePool.Get();
            LogMessagePool.Return(message);
            LogMessagePool.Return(message);

            Assert.AreEqual(1, LogMessagePool.GetStatistics().InvalidReturns);
            LogMessage rented = LogMessagePool.Get();
            Assert.AreSame(message, rented);
            LogMessagePool.Return(rented);
        }

        private static CLogger CreateSingleThreaded(int maxMessages, int maxCharacters)
        {
            return CLoggerFactory.CreateSingleThreaded(CreateOptions(maxMessages, maxCharacters));
        }

        private static LoggerProcessingOptions CreateOptions(int maxMessages, int maxCharacters)
        {
            return new LoggerProcessingOptions
            {
                MaxQueuedMessages = maxMessages,
                MaxQueuedCharacters = maxCharacters,
                MaxMessageCharacters = Math.Max(1, Math.Min(64, maxCharacters - 3)),
                MaxCategoryCharacters = 1,
                MaxSourcePathCharacters = 1,
                MaxMemberNameCharacters = 1,
                ReservedCriticalMessages = 0,
                ReservedCriticalCharacters = 0,
                UnityConsoleMaxQueuedMessages = Math.Max(1, maxMessages),
                UnityConsoleMaxQueuedCharacters = Math.Max(512, maxCharacters),
                OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
                CriticalLevel = LogLevel.Error,
                ShutdownDrainTimeoutMs = 2000
            };
        }

        private static void ResetUnityLoggerState()
        {
            var method = typeof(LoggerUpdater).GetMethod(
                "ResetStaticState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method);
            method.Invoke(null, null);
        }

        private sealed class RecordingSink : ILogger
        {
            internal readonly List<string> Messages = new List<string>();
            internal int Count => Messages.Count;

            public void Log(LogMessage logMessage)
            {
                var builder = new StringBuilder();
                logMessage.AppendMessageTo(builder);
                Messages.Add(builder.ToString());
            }

            public void Dispose()
            {
            }
        }

        private sealed class DisposableSink : ILogger
        {
            internal int DisposeCount;
            public void Log(LogMessage logMessage) { }
            public void Dispose() => DisposeCount++;
        }

        private sealed class PayloadShapeSink : ILogger
        {
            internal int OriginalMessageLength;
            internal int MessageBuilderCapacity;
            internal int CategoryLength;
            internal int FilePathLength;
            internal int MemberNameLength;
            internal bool WasTruncated;

            public void Log(LogMessage logMessage)
            {
                OriginalMessageLength = logMessage.OriginalMessage?.Length ?? 0;
                MessageBuilderCapacity = logMessage.MessageBuilder?.Capacity ?? 0;
                CategoryLength = logMessage.Category?.Length ?? 0;
                FilePathLength = logMessage.FilePath?.Length ?? 0;
                MemberNameLength = logMessage.MemberName?.Length ?? 0;
                WasTruncated = logMessage.WasTruncated;
            }

            public void Dispose()
            {
            }
        }

        private sealed class ThrowingSink : ILogger
        {
            internal int CallCount;
            public void Log(LogMessage logMessage)
            {
                CallCount++;
                throw new InvalidOperationException("Expected test failure.");
            }

            public void Dispose() { }
        }

        private sealed class SlowSink : ILogger
        {
            private readonly int _delayMs;
            internal int Count;

            internal SlowSink(int delayMs)
            {
                _delayMs = delayMs;
            }

            public void Log(LogMessage logMessage)
            {
                Thread.Sleep(_delayMs);
                Count++;
            }

            public void Dispose()
            {
            }
        }

        private sealed class ThrowingDisposableSink : ILogger
        {
            internal int CallCount;
            internal int DisposeCount;

            public void Log(LogMessage logMessage)
            {
                CallCount++;
                throw new InvalidOperationException("Expected test failure.");
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class BlockingSink : ILogger
        {
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();

            public void Log(LogMessage logMessage)
            {
                Entered.Set();
                Release.Wait();
            }

            public void Dispose()
            {
                Entered.Dispose();
                Release.Dispose();
            }
        }

        private sealed class BlockingDisposableSink : ILogger
        {
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            internal int DisposeCount;

            public void Log(LogMessage logMessage)
            {
                Entered.Set();
                Release.Wait();
            }

            public void Dispose()
            {
                Interlocked.Increment(ref DisposeCount);
            }

            internal void DisposeEvents()
            {
                Entered.Dispose();
                Release.Dispose();
            }
        }

        private sealed class ThrowingBlockingDisposeSink : ILogger
        {
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim DisposeRelease = new ManualResetEventSlim();
            internal int DisposeCount;

            public void Log(LogMessage logMessage)
            {
                throw new InvalidOperationException("Expected test failure.");
            }

            public void Dispose()
            {
                Interlocked.Increment(ref DisposeCount);
                DisposeEntered.Set();
                DisposeRelease.Wait();
            }

            internal void DisposeEvents()
            {
                DisposeEntered.Dispose();
                DisposeRelease.Dispose();
            }
        }

        private sealed class FlushFailingBlockingDisposeSink : ILogger, IFlushableLogger
        {
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim DisposeRelease = new ManualResetEventSlim();
            internal int FlushCount;
            internal int DisposeCount;

            public void Log(LogMessage logMessage)
            {
            }

            public bool TryFlush(LogFlushMode mode)
            {
                FlushCount++;
                return false;
            }

            public void Dispose()
            {
                Interlocked.Increment(ref DisposeCount);
                DisposeEntered.Set();
                DisposeRelease.Wait();
            }

            internal void DisposeEvents()
            {
                DisposeEntered.Dispose();
                DisposeRelease.Dispose();
            }
        }

        private sealed class BlockingLogDisposeTrackingSink : ILogger
        {
            internal readonly ManualResetEventSlim LogEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim LogRelease = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();

            public void Log(LogMessage logMessage)
            {
                LogEntered.Set();
                LogRelease.Wait();
            }

            public void Dispose()
            {
                DisposeEntered.Set();
            }

            internal void DisposeEvents()
            {
                LogEntered.Dispose();
                LogRelease.Dispose();
                DisposeEntered.Dispose();
            }
        }

        private sealed class ThrowingDisposeSink : ILogger, IIdempotentLoggerSinkDisposal
        {
            internal int DisposeCount;

            public void Log(LogMessage logMessage)
            {
            }

            public void Dispose()
            {
                DisposeCount++;
                throw new InvalidOperationException("Expected dispose failure.");
            }
        }

        private sealed class TransientThrowingDisposeSink : ILogger, IIdempotentLoggerSinkDisposal
        {
            internal int DisposeCount;

            public void Log(LogMessage logMessage)
            {
            }

            public void Dispose()
            {
                DisposeCount++;
                if (DisposeCount == 1)
                {
                    throw new InvalidOperationException("Expected transient dispose failure.");
                }
            }
        }

        private sealed class NonRetryableThrowingDisposeSink : ILogger
        {
            internal int DisposeCount;

            public void Log(LogMessage logMessage)
            {
            }

            public void Dispose()
            {
                DisposeCount++;
                throw new InvalidOperationException("Expected non-retryable dispose failure.");
            }
        }

        private sealed class BlockingDisposeOnlySink : ILogger
        {
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim DisposeRelease = new ManualResetEventSlim();

            public void Log(LogMessage logMessage)
            {
            }

            public void Dispose()
            {
                DisposeEntered.Set();
                DisposeRelease.Wait();
            }

            internal void DisposeEvents()
            {
                DisposeEntered.Dispose();
                DisposeRelease.Dispose();
            }
        }

        private sealed class CountingSink : ILogger
        {
            private int _count;
            internal int Count => Volatile.Read(ref _count);
            public void Log(LogMessage logMessage) => Interlocked.Increment(ref _count);
            public void Dispose() { }
        }
    }
}
