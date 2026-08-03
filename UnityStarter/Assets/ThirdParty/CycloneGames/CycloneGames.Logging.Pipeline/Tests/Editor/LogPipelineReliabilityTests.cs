using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;
using NUnit.Framework;

namespace CycloneGames.Logging.Pipeline.Tests.Editor
{
    public sealed class LogPipelineReliabilityTests
    {
        [Test]
        public void FullQueue_DoesNotInvokeBuilderWithoutReservation()
        {
            using var logger = CreateSingleThreaded(maxMessages: 1, maxCharacters: 128);
            var sink = new RecordingSink();
            logger.RegisterSink(sink);
            logger.Write(LogSeverity.Info, "first", filePath: string.Empty, memberName: string.Empty);

            bool invoked = false;
            logger.Write(LogSeverity.Info, builder =>
            {
                invoked = true;
                builder.Append("second");
            }, filePath: string.Empty, memberName: string.Empty);

            logger.Pump(8);
            Assert.IsFalse(invoked);
            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(1, logger.GetStatistics().DroppedNewestCount);
        }

        [Test]
        public void DropOldest_NormalMessageNeverEvictsQueuedCriticalMessage()
        {
            var options = CreateOptions(2, 128);
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            using var logger = LogPipelineFactory.CreateSingleThreaded(options);
            var sink = new RecordingSink();
            logger.RegisterSink(sink);

            logger.Write(LogSeverity.Error, "critical", filePath: string.Empty, memberName: string.Empty);
            logger.Write(LogSeverity.Info, "normal-old", filePath: string.Empty, memberName: string.Empty);
            logger.Write(LogSeverity.Info, "normal-new", filePath: string.Empty, memberName: string.Empty);
            logger.Pump(8);

            CollectionAssert.AreEqual(new[] { "critical", "normal-new" }, sink.Messages);
            Assert.AreEqual(0, logger.GetStatistics().DroppedCriticalCount);
        }

        [Test]
        public void FullQueue_DropOldestBuilderDoesNotPreEvictBeforeActualSizeIsKnown()
        {
            LogPipelineOptions options = CreateOptions(1, 128);
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            using var logger = LogPipelineFactory.CreateSingleThreaded(options);
            var sink = new RecordingSink();
            logger.RegisterSink(sink);
            logger.Write(LogSeverity.Info, "old", filePath: string.Empty, memberName: string.Empty);

            bool invoked = false;
            logger.Write(
                LogSeverity.Info,
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
            Assert.AreEqual(0, logger.GetStatistics().DroppedOldestCount);
            Assert.AreEqual(1, logger.GetStatistics().DroppedNewestCount);
        }

        [Test]
        public void ThrowingBuilder_WithCapacityEmitsDiagnosticReplacement()
        {
            using var logger = CreateSingleThreaded(2, 256);
            var sink = new RecordingSink();
            logger.RegisterSink(sink);

            logger.Write(
                LogSeverity.Info,
                static builder => throw new InvalidOperationException("Expected builder failure."),
                filePath: string.Empty,
                memberName: string.Empty);
            logger.Pump(1);

            Assert.AreEqual(1, sink.Count);
            StringAssert.Contains("log message builder failed: InvalidOperationException", sink.Messages[0]);
            Assert.AreEqual(1, logger.GetStatistics().MessageBuilderFailureCount);
        }

        [Test]
        public void ThrowingBuilders_UseBoundedQueueAndOneEmergencyReportPerPipeline()
        {
            using var logger = CreateSingleThreaded(2, 256);
            var sink = new RecordingSink();
            logger.RegisterSink(sink);

            for (int i = 0; i < 32; i++)
            {
                logger.Write(
                    LogSeverity.Info,
                    static builder => throw new InvalidOperationException("Expected builder failure."),
                    filePath: string.Empty,
                    memberName: string.Empty);
                logger.Pump(1);
            }

            LogPipelineStatistics statistics = logger.GetStatistics();
            Assert.AreEqual(32, sink.Count);
            Assert.AreEqual(32, statistics.MessageBuilderFailureCount);
            Assert.AreEqual(0, statistics.ReservedCount);
            Assert.AreEqual(1, logger.MessageBuilderFailureEmergencyReportCount);
        }

        [Test]
        public void FatalBuilderException_PropagatesAndReleasesReservation()
        {
            using var logger = CreateSingleThreaded(2, 256);
            logger.RegisterSink(new RecordingSink());

            Assert.Throws<OutOfMemoryException>(() => logger.Write(
                LogSeverity.Info,
                static builder => throw new OutOfMemoryException("Synthetic test failure."),
                filePath: string.Empty,
                memberName: string.Empty));

            Assert.AreEqual(0, logger.GetStatistics().ReservedCount);
        }

        [Test]
        public void ThrowingTimestampProvider_IsQuarantinedAfterFirstObservedFailure()
        {
            int providerCalls = 0;
            LogPipelineOptions options = CreateOptions(2048, 128 * 1024);
            using var logger = LogPipelineFactory.CreateSingleThreaded(
                options,
                () =>
                {
                    Interlocked.Increment(ref providerCalls);
                    throw new InvalidOperationException("Expected timestamp failure.");
                });
            var sink = new RecordingSink();
            logger.RegisterSink(sink);
            logger.Write(LogSeverity.Info, "trip", filePath: string.Empty, memberName: string.Empty);

            var producers = new Thread[8];
            for (int i = 0; i < producers.Length; i++)
            {
                producers[i] = new Thread(() =>
                {
                    for (int messageIndex = 0; messageIndex < 100; messageIndex++)
                    {
                        logger.Write(LogSeverity.Info, "message", filePath: string.Empty, memberName: string.Empty);
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
            Assert.AreEqual(1, logger.GetStatistics().TimestampProviderFailureCount);
        }

        [Test]
        public void FatalTimestampFailure_DoesNotRentOrLeakBuilderPoolState()
        {
            using var logger = LogPipelineFactory.CreateSingleThreaded(
                CreateOptions(4, 256),
                static () => throw new OutOfMemoryException("Synthetic timestamp failure."));
            logger.RegisterSink(new RecordingSink());
            LogMemoryPoolStatistics before = LogMemoryPools.GetStatistics();
            bool builderInvoked = false;

            Assert.Throws<OutOfMemoryException>(() => logger.Write(
                LogSeverity.Info,
                builder =>
                {
                    builderInvoked = true;
                    builder.Append("unreachable");
                },
                filePath: string.Empty,
                memberName: string.Empty));

            LogMemoryPoolStatistics after = LogMemoryPools.GetStatistics();
            Assert.IsFalse(builderInvoked);
            Assert.AreEqual(before.RetainedStringBuilders, after.RetainedStringBuilders);
            Assert.AreEqual(0, logger.GetStatistics().ReservedCount);
        }

        [Test]
        public void CharacterBudget_DropsEntryThatWouldExceedBound()
        {
            using var logger = CreateSingleThreaded(maxMessages: 4, maxCharacters: 10);
            var sink = new RecordingSink();
            logger.RegisterSink(sink);

            logger.Write(LogSeverity.Info, "1234567", filePath: string.Empty, memberName: string.Empty);
            logger.Write(LogSeverity.Info, "abcd", filePath: string.Empty, memberName: string.Empty);
            LogPipelineStatistics beforePump = logger.GetStatistics();
            logger.Pump(8);

            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(7, beforePump.PeakQueuedCharacters);
            Assert.AreEqual(1, beforePump.DroppedNewestCount);
        }

        [Test]
        public void ProcessingOptions_RejectAggregateEntryLimitsBeyondQueueBudget()
        {
            LogPipelineOptions options = CreateOptions(4, 128);
            options.MaxMessageCharacters = 100;
            options.MaxCategoryCharacters = 20;
            options.MaxSourcePathCharacters = 20;
            options.MaxMemberNameCharacters = 20;

            Assert.Throws<ArgumentOutOfRangeException>(() => LogPipelineOptions.CreateValidated(options));
        }

        [Test]
        public void ProcessingOptions_RejectFilterCharacterBudgetSmallerThanOneCategory()
        {
            LogPipelineOptions options = CreateOptions(4, 128);
            options.MaxCategoryCharacters = 8;
            options.MaxFilterCharacters = 7;

            Assert.Throws<ArgumentOutOfRangeException>(() => LogPipelineOptions.CreateValidated(options));
        }

        [Test]
        public void CriticalCommitRejectedAfterStop_IsCountedAsCriticalDrop()
        {
            LogPipelineOptions options = CreateOptions(4, 256);
            var queue = new BoundedLogQueue(options);
            Assert.IsTrue(queue.TryReserve(LogSeverity.Error, 16, true, out int reservedCharacters));
            LogEvent message = LogEventPool.Get();
            message.Initialize(DateTime.UtcNow, LogSeverity.Error, "critical", null, null, null, 0, null, 16, 1, 1, 1);
            queue.CompleteAdding();

            Assert.IsFalse(queue.TryCommit(message, reservedCharacters, message.GetRetainedCharacterCount()));
            LogEventPool.Return(message);
            LogPipelineStatistics statistics = queue.GetStatistics();
            Assert.AreEqual(1, statistics.RejectedAfterStopCount);
            Assert.AreEqual(1, statistics.DroppedCriticalCount);
            queue.Dispose();
        }

        [Test]
        public void CommitCannotConsumeCharactersOutsideItsReservation()
        {
            LogPipelineOptions options = CreateOptions(4, 32);
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            var queue = new BoundedLogQueue(options);

            Assert.IsTrue(queue.TryReserve(LogSeverity.Info, 5, true, out int firstReservation));
            LogEvent first = LogEventPool.Get();
            first.Initialize(DateTime.UtcNow, LogSeverity.Info, "first", null, null, null, 0, null, 16, 1, 1, 1);
            Assert.IsTrue(queue.TryCommit(first, firstReservation, first.GetRetainedCharacterCount()));

            Assert.IsTrue(queue.TryReserve(LogSeverity.Info, 3, true, out int secondReservation));
            LogEvent second = LogEventPool.Get();
            second.Initialize(DateTime.UtcNow, LogSeverity.Info, "four", null, null, null, 0, null, 16, 1, 1, 1);
            Assert.IsFalse(queue.TryCommit(second, secondReservation, second.GetRetainedCharacterCount()));
            LogEventPool.Return(second);

            LogPipelineStatistics statistics = queue.GetStatistics();
            Assert.AreEqual(1, statistics.QueuedCount);
            Assert.AreEqual(0, statistics.ReservedCount);
            Assert.AreEqual(0, statistics.DroppedOldestCount);
            Assert.AreEqual(1, statistics.DroppedNewestCount);
            Assert.IsTrue(queue.TryDequeue(out LogEvent retained, out int retainedCharacters));
            Assert.AreSame(first, retained);
            LogEventPool.Return(retained);
            queue.CompleteProcessing(retainedCharacters);
            queue.Dispose();
        }

        [Test]
        public void CategoryFilters_EnforceCombinedEntryAndCharacterBudgets()
        {
            LogPipelineOptions options = CreateOptions(4, 128);
            options.MaxCategoryCharacters = 8;
            options.MaxFilterCategories = 2;
            options.MaxFilterCharacters = 8;
            using var logger = LogPipelineFactory.CreateSingleThreaded(options);

            logger.AddAllowedCategory("aa");
            logger.AddDeniedCategory("bbb");
            logger.AddAllowedCategory("AA");
            Assert.Throws<InvalidOperationException>(() => logger.AddDeniedCategory("c"));
            Assert.Throws<ArgumentOutOfRangeException>(() => logger.AddAllowedCategory("123456789"));

            LogPipelineStatistics statistics = logger.GetStatistics();
            Assert.AreEqual(2, statistics.FilterCategoryCount);
            Assert.AreEqual(5, statistics.FilterCharacters);
            Assert.AreEqual(2, statistics.RejectedFilterMutationCount);

            logger.RemoveAllowedCategory("aA");
            logger.AddDeniedCategory("cccc");
            statistics = logger.GetStatistics();
            Assert.AreEqual(2, statistics.FilterCategoryCount);
            Assert.AreEqual(7, statistics.FilterCharacters);
        }

        [Test]
        public void BlackListFilter_FailsClosedForCategoryBeyondCanonicalLimit()
        {
            LogPipelineOptions options = CreateOptions(4, 128);
            options.MaxCategoryCharacters = 3;
            using var logger = LogPipelineFactory.CreateSingleThreaded(options);
            var sink = new RecordingSink();
            logger.RegisterSink(sink);
            logger.CategoryFilter = LogCategoryFilterMode.DenyList;
            logger.AddDeniedCategory("Net");

            logger.Write(LogSeverity.Info, "blocked", "NetSuffix", string.Empty, 0, string.Empty);
            logger.Write(LogSeverity.Info, "accepted", "UI", string.Empty, 0, string.Empty);
            logger.Pump(4);

            CollectionAssert.AreEqual(new[] { "accepted" }, sink.Messages);
        }

        [Test]
        public void OversizedMessage_IsBoundedAndMarkedTruncated()
        {
            LogPipelineOptions options = CreateOptions(4, 64);
            options.MaxMessageCharacters = 5;
            using var logger = LogPipelineFactory.CreateSingleThreaded(options);
            var sink = new RecordingSink();
            logger.RegisterSink(sink);

            logger.Write(LogSeverity.Info, "abcdefgh", filePath: string.Empty, memberName: string.Empty);
            logger.Pump(8);

            Assert.AreEqual("abcde [truncated]", sink.Messages[0]);
        }

        [Test]
        public void QueueOwnedPayload_BoundsMessageBuilderAndMetadataReferences()
        {
            LogPipelineOptions options = CreateOptions(4, 128);
            options.MaxMessageCharacters = 5;
            options.MaxCategoryCharacters = 4;
            options.MaxSourcePathCharacters = 6;
            options.MaxMemberNameCharacters = 3;
            using var logger = LogPipelineFactory.CreateSingleThreaded(options);
            var sink = new PayloadShapeSink();
            logger.RegisterSink(sink);

            logger.Write(
                LogSeverity.Info,
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

            logger.Write(
                LogSeverity.Info,
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
        public void RegisterSink_RejectedDifferentInstanceRemainsCallerOwned()
        {
            using var logger = CreateSingleThreaded(4, 128);
            var accepted = new DisposableSink();
            var rejected = new DisposableSink();

            LogSinkRegistrationResult acceptedResult = logger.RegisterSink(
                accepted,
                LogSinkRegistrationMode.UniqueExactType);
            LogSinkRegistrationResult rejectedResult = logger.RegisterSink(
                rejected,
                LogSinkRegistrationMode.UniqueExactType);

            Assert.AreEqual(LogSinkRegistrationStatus.Registered, acceptedResult.Status);
            Assert.IsTrue(acceptedResult.PipelineOwnsSink);
            Assert.AreEqual(LogSinkRegistrationStatus.RejectedDuplicateType, rejectedResult.Status);
            Assert.IsTrue(rejectedResult.CallerRetainsOwnership);
            Assert.AreEqual(0, accepted.DisposeCount);
            Assert.AreEqual(0, rejected.DisposeCount);
            rejected.Dispose();
            Assert.AreEqual(1, rejected.DisposeCount);
        }

        [Test]
        public void RegisterSink_RepeatedSameInstanceReportsExistingOwnership()
        {
            using var logger = CreateSingleThreaded(4, 128);
            var sink = new DisposableSink();

            LogSinkRegistrationResult first = logger.RegisterSink(sink);
            LogSinkRegistrationResult repeated = logger.RegisterSink(sink);

            Assert.AreEqual(LogSinkRegistrationStatus.Registered, first.Status);
            Assert.AreEqual(LogSinkRegistrationStatus.AlreadyRegistered, repeated.Status);
            Assert.IsTrue(repeated.IsRegistered);
            Assert.IsTrue(repeated.PipelineOwnsSink);
            Assert.IsFalse(repeated.CallerRetainsOwnership);
        }

        [Test]
        public void RegisterSink_AfterShutdownLeavesNewSinkCallerOwned()
        {
            var logger = CreateSingleThreaded(4, 128);
            Assert.IsTrue(logger.Shutdown(LogFlushMode.Buffered).IsComplete);
            var sink = new DisposableSink();

            LogSinkRegistrationResult result = logger.RegisterSink(sink);

            Assert.AreEqual(LogSinkRegistrationStatus.RejectedPipelineStopping, result.Status);
            Assert.IsFalse(result.IsRegistered);
            Assert.IsTrue(result.CallerRetainsOwnership);
            Assert.AreEqual(0, sink.DisposeCount);
            sink.Dispose();
            logger.Dispose();
        }

        [Test]
        public void RegisterSink_InvalidModeThrowsWithoutTakingOwnership()
        {
            using var logger = CreateSingleThreaded(4, 128);
            var sink = new DisposableSink();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                logger.RegisterSink(sink, unchecked((LogSinkRegistrationMode)byte.MaxValue)));
            Assert.AreEqual(0, sink.DisposeCount);
            sink.Dispose();
        }

        [Test]
        public void RepeatedSinkFailures_QuarantineOnlyFailingSink()
        {
            LogPipelineOptions options = CreateOptions(8, 1024);
            options.SinkFailureThreshold = 2;
            using var logger = LogPipelineFactory.CreateSingleThreaded(options);
            var failing = new ThrowingSink();
            var recording = new RecordingSink();
            logger.RegisterSink(failing);
            logger.RegisterSink(recording);

            for (int i = 0; i < 3; i++)
            {
                logger.Write(LogSeverity.Info, "message", filePath: string.Empty, memberName: string.Empty);
                logger.Pump(1);
            }

            LogPipelineStatistics statistics = logger.GetStatistics();
            Assert.AreEqual(2, failing.CallCount);
            Assert.AreEqual(3, recording.Count);
            Assert.AreEqual(2, statistics.SinkFailureCount);
            Assert.AreEqual(1, statistics.QuarantinedSinkCount);
        }

        [Test]
        public void RepeatedQuarantine_RemovesAndDisposesEachFailedRegistration()
        {
            LogPipelineOptions options = CreateOptions(8, 1024);
            options.SinkFailureThreshold = 1;
            using var logger = LogPipelineFactory.CreateSingleThreaded(options);

            for (int i = 0; i < 8; i++)
            {
                var sink = new ThrowingDisposableSink();
                Assert.IsTrue(logger.RegisterSink(
                    sink,
                    LogSinkRegistrationMode.UniqueExactType).IsRegistered);
                logger.Write(LogSeverity.Info, "message", filePath: string.Empty, memberName: string.Empty);
                logger.Pump(1);
                Assert.AreEqual(1, sink.CallCount);
                Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref sink.DisposeCount) == 1, 2000));
                Assert.AreEqual(1, sink.DisposeCount);
            }

            LogPipelineStatistics statistics = logger.GetStatistics();
            Assert.AreEqual(8, statistics.SinkFailureCount);
            Assert.AreEqual(8, statistics.QuarantinedSinkCount);
        }

        [Test]
        public void RemoveSink_ReturnsFalseUntilEarlierDispatchQuiesces()
        {
            using var logger = LogPipelineFactory.CreateThreaded(CreateOptions(8, 1024));
            var sink = new BlockingSink();
            logger.RegisterSink(sink);
            logger.Write(LogSeverity.Info, "blocked", filePath: string.Empty, memberName: string.Empty);
            Assert.IsTrue(sink.Entered.Wait(2000), "Sink did not receive the message.");

            Assert.IsFalse(logger.RemoveSink(sink, 10));
            Assert.IsFalse(logger.RemoveSink(sink, 10));
            sink.Release.Set();
            Assert.IsTrue(logger.RemoveSink(sink, 2000));
            sink.Dispose();
        }

        [Test]
        public void RemoveSink_ReturnsFalseWhenSinkWasNeverRegistered()
        {
            using var logger = CreateSingleThreaded(4, 128);
            var sink = new DisposableSink();

            Assert.IsFalse(logger.RemoveSink(sink));
            Assert.AreEqual(0, sink.DisposeCount);
            sink.Dispose();
        }

        [Test]
        public void InFlightEntry_RemainsInsideQueueCapacityBudget()
        {
            using var logger = LogPipelineFactory.CreateThreaded(CreateOptions(1, 64));
            var sink = new BlockingSink();
            logger.RegisterSink(sink);
            logger.Write(LogSeverity.Info, "first", filePath: string.Empty, memberName: string.Empty);
            Assert.IsTrue(sink.Entered.Wait(2000), "Sink did not receive the first message.");

            logger.Write(LogSeverity.Info, "second", filePath: string.Empty, memberName: string.Empty);
            LogPipelineStatistics statistics = logger.GetStatistics();

            Assert.AreEqual(1, statistics.InFlightCount);
            Assert.AreEqual(1, statistics.PeakQueuedCount);
            Assert.AreEqual(1, statistics.DroppedNewestCount);
            sink.Release.Set();
            Assert.IsTrue(logger.RemoveSink(sink, 2000));
            sink.Dispose();
        }

        [Test]
        public void BudgetedSingleThreadPump_StopsBetweenSlowEntries()
        {
            using var logger = LogPipelineFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var sink = new SlowSink(20);
            logger.RegisterSink(sink);
            logger.Write(LogSeverity.Info, "one", filePath: string.Empty, memberName: string.Empty);
            logger.Write(LogSeverity.Info, "two", filePath: string.Empty, memberName: string.Empty);
            logger.Write(LogSeverity.Info, "three", filePath: string.Empty, memberName: string.Empty);

            logger.PumpWithinBudget(3, 1);

            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(2, logger.GetStatistics().QueuedCount);
        }

        [Test]
        public void ConcurrentRemoveAndShutdown_TransfersOwnershipExactlyOnceWithoutThrowing()
        {
            var logger = LogPipelineFactory.CreateThreaded(CreateOptions(8, 1024));
            var sink = new BlockingDisposableSink();
            logger.RegisterSink(sink);
            logger.Write(LogSeverity.Info, "blocked", filePath: string.Empty, memberName: string.Empty);
            Assert.IsTrue(sink.Entered.Wait(2000), "Sink did not receive the message.");

            Exception removeException = null;
            Exception shutdownException = null;
            bool removeResult = false;
            var removeThread = new Thread(() =>
            {
                try
                {
                    removeResult = logger.RemoveSink(sink, 5000);
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
                    logger.Shutdown(LogFlushMode.Buffered, 5000);
                }
                catch (Exception exception)
                {
                    shutdownException = exception;
                }
            });

            removeThread.Start();
            Thread.Sleep(20);
            Assert.IsTrue(removeThread.IsAlive, "RemoveSink did not enter its quiescence wait.");
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
            LogPipelineOptions options = CreateOptions(4, 256);
            options.SinkFailureThreshold = 1;
            options.ShutdownDrainTimeoutMs = 50;
            var logger = LogPipelineFactory.CreateSingleThreaded(options);
            var sink = new ThrowingBlockingDisposeSink();
            logger.RegisterSink(sink);
            logger.Write(LogSeverity.Info, "failure", filePath: string.Empty, memberName: string.Empty);

            var pumpThread = new Thread(() => logger.Pump(1));
            pumpThread.Start();
            Assert.IsTrue(sink.DisposeEntered.Wait(2000), "Quarantined sink disposal did not start.");

            LogPipelineShutdownResult timedOut = logger.Shutdown(LogFlushMode.Buffered, 20);
            Assert.AreEqual(LogPipelineShutdownStatus.TimedOut, timedOut.Status);
            Assert.IsFalse(timedOut.IsComplete);
            Assert.IsTrue(logger.IsSinkDisposalExecutorRunning);

            sink.DisposeRelease.Set();
            Assert.IsTrue(pumpThread.Join(2000));
            Assert.IsTrue(
                SpinWait.SpinUntil(() => !logger.IsSinkDisposalExecutorRunning, 2000),
                "Timed-out shutdown did not request eventual disposal-executor termination.");
            LogPipelineShutdownResult completed = logger.Shutdown(LogFlushMode.Buffered, 2000);
            Assert.IsTrue(completed.IsComplete);
            Assert.AreEqual(1, sink.DisposeCount);
            logger.Dispose();
            sink.DisposeEvents();
        }

        [Test]
        public void TimedOutShutdown_PreservesSingleSerializedDisposalOwner()
        {
            LogPipelineOptions options = CreateOptions(4, 256);
            options.SinkFailureThreshold = 1;
            var logger = LogPipelineFactory.CreateSingleThreaded(options);
            var first = new ThrowingBlockingDisposeSink();
            var second = new BlockingLogDisposeTrackingSink();
            using var workerExitEntered = new ManualResetEventSlim();
            using var allowWorkerExit = new ManualResetEventSlim();
            logger.SinkDisposalBeforeExitTestHook = () =>
            {
                workerExitEntered.Set();
                allowWorkerExit.Wait();
            };
            logger.RegisterSink(first);

            LogEvent firstMessage = LogEventPool.Get();
            firstMessage.Initialize(DateTime.UtcNow, LogSeverity.Info, "first", null, null, null, 0, null, 16, 1, 1, 1);
            logger.DispatchToSinks(firstMessage);
            LogEventPool.Return(firstMessage);
            Assert.IsTrue(first.DisposeEntered.Wait(2000), "First sink disposal did not start.");

            logger.RegisterSink(second);
            LogEvent secondMessage = LogEventPool.Get();
            secondMessage.Initialize(DateTime.UtcNow, LogSeverity.Info, "second", null, null, null, 0, null, 16, 1, 1, 1);
            var dispatchThread = new Thread(() => logger.DispatchToSinks(secondMessage));
            dispatchThread.Start();
            Assert.IsTrue(second.LogEntered.Wait(2000), "Second sink dispatch did not start.");

            LogPipelineShutdownResult timedOut = logger.Shutdown(LogFlushMode.Buffered, 20);
            Assert.AreEqual(LogPipelineShutdownStatus.TimedOut, timedOut.Status);
            Assert.IsFalse(second.DisposeEntered.IsSet);

            first.DisposeRelease.Set();
            Assert.IsTrue(workerExitEntered.Wait(2000), "Disposal worker did not enter its atomic owner handoff.");
            second.LogRelease.Set();
            Assert.IsFalse(dispatchThread.Join(100), "Pending sink scheduling bypassed the worker-owner handoff lock.");

            allowWorkerExit.Set();
            Assert.IsTrue(dispatchThread.Join(2000));
            LogEventPool.Return(secondMessage);
            Assert.IsTrue(second.DisposeEntered.Wait(2000));
            Assert.IsTrue(SpinWait.SpinUntil(() => !logger.IsSinkDisposalExecutorRunning, 2000));
            logger.SinkDisposalBeforeExitTestHook = null;
            Assert.IsTrue(logger.Shutdown(LogFlushMode.Buffered, 2000).IsComplete);
            logger.Dispose();
            first.DisposeEvents();
            second.DisposeEvents();
        }

        [Test]
        public void ShutdownRetry_PreservesFlushFailureAcrossAsynchronousDisposalTimeout()
        {
            LogPipelineOptions options = CreateOptions(4, 256);
            options.ShutdownDrainTimeoutMs = 50;
            var logger = LogPipelineFactory.CreateSingleThreaded(options);
            var sink = new FlushFailingBlockingDisposeSink();
            logger.RegisterSink(sink);

            LogPipelineShutdownResult timedOut = logger.Shutdown(LogFlushMode.Durable, 20);
            Assert.AreEqual(LogPipelineShutdownStatus.TimedOut, timedOut.Status);
            Assert.IsFalse(timedOut.SinksFlushed);
            Assert.IsTrue(sink.DisposeEntered.Wait(2000));

            LogSinkRegistrationResult repeated = logger.RegisterSink(sink);
            Assert.AreEqual(LogSinkRegistrationStatus.AlreadyOwnedByPipeline, repeated.Status);
            Assert.IsTrue(repeated.PipelineOwnsSink);
            Assert.IsFalse(repeated.CallerRetainsOwnership);

            sink.DisposeRelease.Set();
            LogPipelineShutdownResult completed = logger.Shutdown(LogFlushMode.Durable, 2000);
            Assert.AreEqual(LogPipelineShutdownStatus.CompletedWithFailures, completed.Status);
            Assert.IsFalse(completed.SinksFlushed);
            Assert.AreEqual(1, sink.FlushCount);
            Assert.AreEqual(1, sink.DisposeCount);
            logger.Dispose();
            sink.DisposeEvents();
        }

        [Test]
        public void ThrowingSinkDispose_IsReportedAsCompletedWithFailures()
        {
            var logger = LogPipelineFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var sink = new ThrowingDisposeSink();
            logger.RegisterSink(sink);

            LogPipelineShutdownResult result = logger.Shutdown(LogFlushMode.Buffered, 2000);

            Assert.AreEqual(LogPipelineShutdownStatus.CompletedWithFailures, result.Status);
            Assert.IsTrue(result.SinksFlushed);
            Assert.AreEqual(1, logger.GetStatistics().SinkDisposalFailureCount);
            Assert.AreEqual(3, sink.DisposeCount);
            logger.Dispose();
        }

        [Test]
        public void TransientSinkDisposeFailure_IsRetriedWithinBound()
        {
            var logger = LogPipelineFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var sink = new TransientThrowingDisposeSink();
            logger.RegisterSink(sink);

            LogPipelineShutdownResult result = logger.Shutdown(LogFlushMode.Buffered, 2000);

            Assert.IsTrue(result.IsComplete);
            Assert.AreNotEqual(LogPipelineShutdownStatus.CompletedWithFailures, result.Status);
            Assert.AreEqual(2, sink.DisposeCount);
            Assert.AreEqual(0, logger.GetStatistics().SinkDisposalFailureCount);
            logger.Dispose();
        }

        [Test]
        public void NonIdempotentSinkDisposeFailure_IsNotRetried()
        {
            var logger = LogPipelineFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var sink = new NonRetryableThrowingDisposeSink();
            logger.RegisterSink(sink);

            LogPipelineShutdownResult result = logger.Shutdown(LogFlushMode.Buffered, 2000);

            Assert.AreEqual(LogPipelineShutdownStatus.CompletedWithFailures, result.Status);
            Assert.AreEqual(1, sink.DisposeCount);
            logger.Dispose();
        }

        [Test]
        public void BlockedDisposalExecutor_BoundsTotalOwnedSinkBacklog()
        {
            var logger = LogPipelineFactory.CreateSingleThreaded(CreateOptions(4, 256));
            var blocker = new BlockingDisposeOnlySink();
            Assert.IsTrue(logger.RegisterSink(blocker).IsRegistered);
            for (int i = 1; i < 256; i++)
            {
                Assert.IsTrue(logger.RegisterSink(new DisposableSink()).IsRegistered);
            }

            Exception clearException = null;
            var clearThread = new Thread(() =>
            {
                try
                {
                    logger.ClearSinks();
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
            Assert.AreEqual(256, logger.GetStatistics().PendingSinkDisposalCount);
            LogSinkRegistrationResult ownedResult = logger.RegisterSink(blocker);
            Assert.AreEqual(LogSinkRegistrationStatus.AlreadyOwnedByPipeline, ownedResult.Status);
            Assert.IsTrue(ownedResult.PipelineOwnsSink);

            var rejected = new DisposableSink();
            LogSinkRegistrationResult rejectedResult = logger.RegisterSink(rejected);
            Assert.AreEqual(LogSinkRegistrationStatus.RejectedCapacity, rejectedResult.Status);
            Assert.IsTrue(rejectedResult.CallerRetainsOwnership);
            Assert.AreEqual(0, rejected.DisposeCount);
            rejected.Dispose();

            blocker.DisposeRelease.Set();
            Assert.IsTrue(logger.Shutdown(LogFlushMode.Buffered, 5000).IsComplete);
            logger.Dispose();
            blocker.DisposeEvents();
        }

        [Test]
        public void ThreadedProcessor_MultipleProducersFlushAllAcceptedEntries()
        {
            const int ProducerCount = 4;
            const int PerProducer = 500;
            LogPipelineOptions options = CreateOptions(ProducerCount * PerProducer + 1, 512 * 1024);
            options.ShutdownDrainTimeoutMs = 5000;
            using var logger = LogPipelineFactory.CreateThreaded(options);
            var sink = new CountingSink();
            logger.RegisterSink(sink);
            var threads = new Thread[ProducerCount];

            for (int producer = 0; producer < ProducerCount; producer++)
            {
                int state = producer;
                threads[producer] = new Thread(() =>
                {
                    for (int i = 0; i < PerProducer; i++)
                    {
                        logger.Write(LogSeverity.Info, state, static (value, builder) => builder.Append(value), filePath: string.Empty, memberName: string.Empty);
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
            Assert.AreEqual(0, logger.GetStatistics().DroppedMessageCount);
        }

        [Test]
        public void LogEventPool_DoubleReturnIsRejectedWithoutDuplicateRental()
        {
            LogEventPool.ResetStatistics();
            LogEvent message = LogEventPool.Get();
            LogEventPool.Return(message);
            LogEventPool.Return(message);

            Assert.AreEqual(1, LogEventPool.GetStatistics().InvalidReturns);
            LogEvent rented = LogEventPool.Get();
            Assert.AreSame(message, rented);
            LogEventPool.Return(rented);
        }
        private static LogPipeline CreateSingleThreaded(int maxMessages, int maxCharacters)
        {
            return LogPipelineFactory.CreateSingleThreaded(CreateOptions(maxMessages, maxCharacters));
        }

        private static LogPipelineOptions CreateOptions(int maxMessages, int maxCharacters)
        {
            return new LogPipelineOptions
            {
                MaxQueuedMessages = maxMessages,
                MaxQueuedCharacters = maxCharacters,
                MaxMessageCharacters = Math.Max(1, Math.Min(64, maxCharacters - 3)),
                MaxCategoryCharacters = 1,
                MaxSourcePathCharacters = 1,
                MaxMemberNameCharacters = 1,
                ReservedCriticalMessages = 0,
                ReservedCriticalCharacters = 0,
                OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
                CriticalSeverity = LogSeverity.Error,
                ShutdownDrainTimeoutMs = 2000
            };
        }
        private sealed class RecordingSink : ILogSink
        {
            internal readonly List<string> Messages = new List<string>();
            internal int Count => Messages.Count;

            public void Emit(LogEvent logEvent)
            {
                var builder = new StringBuilder();
                logEvent.AppendMessageTo(builder);
                Messages.Add(builder.ToString());
            }

            public void Dispose()
            {
            }
        }

        private sealed class DisposableSink : ILogSink
        {
            internal int DisposeCount;
            public void Emit(LogEvent logEvent) { }
            public void Dispose() => DisposeCount++;
        }

        private sealed class PayloadShapeSink : ILogSink
        {
            internal int OriginalMessageLength;
            internal int MessageBuilderCapacity;
            internal int CategoryLength;
            internal int FilePathLength;
            internal int MemberNameLength;
            internal bool WasTruncated;

            public void Emit(LogEvent logEvent)
            {
                OriginalMessageLength = logEvent.OriginalMessage?.Length ?? 0;
                MessageBuilderCapacity = logEvent.MessageBuilder?.Capacity ?? 0;
                CategoryLength = logEvent.Category?.Length ?? 0;
                FilePathLength = logEvent.FilePath?.Length ?? 0;
                MemberNameLength = logEvent.MemberName?.Length ?? 0;
                WasTruncated = logEvent.WasTruncated;
            }

            public void Dispose()
            {
            }
        }

        private sealed class ThrowingSink : ILogSink
        {
            internal int CallCount;
            public void Emit(LogEvent logEvent)
            {
                CallCount++;
                throw new InvalidOperationException("Expected test failure.");
            }

            public void Dispose() { }
        }

        private sealed class SlowSink : ILogSink
        {
            private readonly int _delayMs;
            internal int Count;

            internal SlowSink(int delayMs)
            {
                _delayMs = delayMs;
            }

            public void Emit(LogEvent logEvent)
            {
                Thread.Sleep(_delayMs);
                Count++;
            }

            public void Dispose()
            {
            }
        }

        private sealed class ThrowingDisposableSink : ILogSink
        {
            internal int CallCount;
            internal int DisposeCount;

            public void Emit(LogEvent logEvent)
            {
                CallCount++;
                throw new InvalidOperationException("Expected test failure.");
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class BlockingSink : ILogSink
        {
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();

            public void Emit(LogEvent logEvent)
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

        private sealed class BlockingDisposableSink : ILogSink
        {
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            internal int DisposeCount;

            public void Emit(LogEvent logEvent)
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

        private sealed class ThrowingBlockingDisposeSink : ILogSink
        {
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim DisposeRelease = new ManualResetEventSlim();
            internal int DisposeCount;

            public void Emit(LogEvent logEvent)
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

        private sealed class FlushFailingBlockingDisposeSink : ILogSink, IFlushableLogSink
        {
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim DisposeRelease = new ManualResetEventSlim();
            internal int FlushCount;
            internal int DisposeCount;

            public void Emit(LogEvent logEvent)
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

        private sealed class BlockingLogDisposeTrackingSink : ILogSink
        {
            internal readonly ManualResetEventSlim LogEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim LogRelease = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();

            public void Emit(LogEvent logEvent)
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

        private sealed class ThrowingDisposeSink : ILogSink, IIdempotentLogSinkDisposal
        {
            internal int DisposeCount;

            public void Emit(LogEvent logEvent)
            {
            }

            public void Dispose()
            {
                DisposeCount++;
                throw new InvalidOperationException("Expected dispose failure.");
            }
        }

        private sealed class TransientThrowingDisposeSink : ILogSink, IIdempotentLogSinkDisposal
        {
            internal int DisposeCount;

            public void Emit(LogEvent logEvent)
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

        private sealed class NonRetryableThrowingDisposeSink : ILogSink
        {
            internal int DisposeCount;

            public void Emit(LogEvent logEvent)
            {
            }

            public void Dispose()
            {
                DisposeCount++;
                throw new InvalidOperationException("Expected non-retryable dispose failure.");
            }
        }

        private sealed class BlockingDisposeOnlySink : ILogSink
        {
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim DisposeRelease = new ManualResetEventSlim();

            public void Emit(LogEvent logEvent)
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

        private sealed class CountingSink : ILogSink
        {
            private int _count;
            internal int Count => Volatile.Read(ref _count);
            public void Emit(LogEvent logEvent) => Interlocked.Increment(ref _count);
            public void Dispose() { }
        }
    }
}

