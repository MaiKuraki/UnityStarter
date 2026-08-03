using System;
using System.Globalization;
using System.Threading;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;
using NUnit.Framework;

namespace CycloneGames.Logging.Unity.Tests.Editor
{
    public sealed class LoggingUnityReliabilityTests
    {
        [Test]
        public void UnityConsoleOptions_RejectBlockPolicyAndCloneDropPolicy()
        {
            UnityConsoleLogSinkOptions invalid = CreateUnityOptions(4, 512);
            invalid.OverflowPolicy = LogQueueOverflowPolicy.Block;
            Assert.Throws<ArgumentOutOfRangeException>(() => UnityConsoleLogSinkOptions.CreateValidated(invalid));

            UnityConsoleLogSinkOptions source = CreateUnityOptions(4, 512);
            source.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            UnityConsoleLogSinkOptions clone = source.Clone();
            Assert.AreEqual(LogQueueOverflowPolicy.DropOldest, clone.OverflowPolicy);
        }

        [Test]
        public void UnityConsoleOptions_UseExactEditorRetentionBudget()
        {
            int retainedCharacters = UnityConsoleLogSinkOptions.EstimateRetainedCharacters(10, 2, 5);
            Assert.AreEqual(10 + 2 + (5 * 3) + UnityConsoleLogSinkOptions.FormattingOverheadCharacters, retainedCharacters);

            UnityConsoleLogSinkOptions invalid = CreateUnityOptions(4, retainedCharacters - 1);
            invalid.MaximumRetainedEntryCharacters = retainedCharacters;
            Assert.Throws<ArgumentOutOfRangeException>(() => UnityConsoleLogSinkOptions.CreateValidated(invalid));
        }

        [Test]
        public void UnityRuntimeHost_DoesNotAllocateConsoleQueueUntilAdapterConfiguration()
        {
            ResetUnityConsoleLogSinkState();
            LoggingRuntimeHost.EnsureInstance();
            Assert.AreEqual(0, LoggingRuntimeHost.ConfiguredMessageCapacityForTests);
            ResetUnityConsoleLogSinkState();
        }

        [Test]
        public void UnityRuntimeHost_FailedSetupDestroysUnpublishedHostAndAllowsRetry()
        {
            ResetUnityConsoleLogSinkState();
            try
            {
                LoggingRuntimeHost.BeforeHostPublishTestHook = () =>
                    throw new InvalidOperationException("Synthetic host setup failure.");

                Assert.Throws<InvalidOperationException>(() => LoggingRuntimeHost.EnsureInstance());
                Assert.AreEqual(0, UnityEngine.Resources.FindObjectsOfTypeAll<LoggingRuntimeHost>().Length);

                LoggingRuntimeHost.BeforeHostPublishTestHook = null;
                Assert.DoesNotThrow(() => LoggingRuntimeHost.EnsureInstance());
                Assert.AreEqual(1, UnityEngine.Resources.FindObjectsOfTypeAll<LoggingRuntimeHost>().Length);
            }
            finally
            {
                LoggingRuntimeHost.BeforeHostPublishTestHook = null;
                ResetUnityConsoleLogSinkState();
            }
        }

        [Test]
        public void UnityConsoleLogSink_DirectFlushRejectsUnknownMode()
        {
            ResetUnityConsoleLogSinkState();
            UnityConsoleLogSink sink = null;
            try
            {
                sink = new UnityConsoleLogSink(CreateUnityOptions(4, 512));

                Assert.Throws<ArgumentOutOfRangeException>(() => sink.TryFlush((LogFlushMode)255));
                Assert.DoesNotThrow(() => sink.TryFlush(LogFlushMode.Buffered));
                Assert.DoesNotThrow(() => sink.TryFlush(LogFlushMode.Durable));
            }
            finally
            {
                sink?.Dispose();
                ResetUnityConsoleLogSinkState();
            }
        }

        [Test]
        public void UnityConsoleOptions_RejectUnboundedArrayAndCharacterBudgets()
        {
            UnityConsoleLogSinkOptions excessiveMessages = UnityConsoleLogSinkOptions.Default;
            excessiveMessages.MaxQueuedMessages = int.MaxValue;
            Assert.Throws<ArgumentOutOfRangeException>(() => UnityConsoleLogSinkOptions.CreateValidated(excessiveMessages));

            UnityConsoleLogSinkOptions excessiveCharacters = UnityConsoleLogSinkOptions.Default;
            excessiveCharacters.MaxQueuedCharacters = int.MaxValue;
            Assert.Throws<ArgumentOutOfRangeException>(() => UnityConsoleLogSinkOptions.CreateValidated(excessiveCharacters));
        }

        [Test]
        public void UnityConsoleSink_BackgroundConfigurationFailsBeforeQueueMutation()
        {
            ResetUnityConsoleLogSinkState();
            Exception failure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    using var sink = new UnityConsoleLogSink(CreateUnityOptions(4, 512));
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(1000));
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.AreEqual(0, LoggingRuntimeHost.ConfiguredMessageCapacityForTests);
            ResetUnityConsoleLogSinkState();
        }

        [Test]
        public void RuntimeHost_DisablesAutomaticPumpAfterFirstTerminalFault()
        {
            ResetUnityConsoleLogSinkState();
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded(CreateOptions(4, 512));
            var sink = new OutOfMemoryEmitSink();
            Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);
            ((ILogWriter)pipeline).Write(
                LogSeverity.Info,
                "RuntimeHost",
                "fatal",
                "LoggingUnityReliabilityTests.cs",
                1,
                nameof(RuntimeHost_DisablesAutomaticPumpAfterFirstTerminalFault));

            Assert.Throws<OutOfMemoryException>(() => LoggingRuntimeHost.PumpPipelineWithinBudget(pipeline));
            Assert.DoesNotThrow(() => LoggingRuntimeHost.PumpPipelineWithinBudget(pipeline));
            Assert.IsTrue(pipeline.IsFaulted);
            Assert.AreEqual(LogPipelineShutdownStatus.CompletedWithFailures, pipeline.Shutdown().Status);
            ResetUnityConsoleLogSinkState();
        }

        [Test]
        public void RuntimeHost_ObservesThreadedTerminalFaultOnceBeforeDisablingPump()
        {
            ResetUnityConsoleLogSinkState();
            LogPipeline pipeline = LogPipelineFactory.CreateThreaded(CreateOptions(4, 512));
            var sink = new OutOfMemoryEmitSink();
            Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);
            ((ILogWriter)pipeline).Write(
                LogSeverity.Info,
                "RuntimeHost",
                "fatal",
                "LoggingUnityReliabilityTests.cs",
                1,
                nameof(RuntimeHost_ObservesThreadedTerminalFaultOnceBeforeDisablingPump));

            Assert.IsTrue(SpinWait.SpinUntil(() => pipeline.IsFaulted, 1000));
            Assert.Throws<OutOfMemoryException>(() => LoggingRuntimeHost.PumpPipelineWithinBudget(pipeline));
            Assert.DoesNotThrow(() => LoggingRuntimeHost.PumpPipelineWithinBudget(pipeline));
            Assert.AreEqual(LogPipelineShutdownStatus.CompletedWithFailures, pipeline.Shutdown().Status);
            ResetUnityConsoleLogSinkState();
        }
        [Test]
        public void UnityQueue_OldGenerationCannotCommitOrCancelNewReservation()
        {
            ResetUnityConsoleLogSinkState();
            LoggingRuntimeHost.Configure(CreateUnityOptions(4, 256));
            Assert.IsTrue(LoggingRuntimeHost.TryReserve(LogSeverity.Info, 16, out LoggingRuntimeHost.Reservation oldReservation));

            ResetUnityConsoleLogSinkState();
            LoggingRuntimeHost.Configure(CreateUnityOptions(4, 256));
            Assert.IsTrue(LoggingRuntimeHost.TryReserve(LogSeverity.Info, 16, out LoggingRuntimeHost.Reservation currentReservation));
            LoggingRuntimeHost.CancelReservation(oldReservation);

            Assert.IsFalse(LoggingRuntimeHost.Commit(LogSeverity.Info, "old", oldReservation));
            Assert.IsTrue(LoggingRuntimeHost.Commit(LogSeverity.Info, "current", currentReservation));
            UnityConsoleLogSinkStatistics statistics = LoggingRuntimeHost.GetStatistics();
            Assert.AreEqual(1, statistics.QueuedCount);
            Assert.AreEqual(1, statistics.DroppedMessageCount);

            LoggingRuntimeHost.Shutdown(false);
            ResetUnityConsoleLogSinkState();
        }

        [Test]
        public void UnityQueue_CommitCannotConsumeCharactersOutsideItsReservation()
        {
            ResetUnityConsoleLogSinkState();
            LoggingRuntimeHost.Configure(CreateUnityOptions(4, 512));
            Assert.IsTrue(LoggingRuntimeHost.TryReserve(LogSeverity.Info, 3, out LoggingRuntimeHost.Reservation reservation));

            Assert.IsFalse(LoggingRuntimeHost.Commit(LogSeverity.Info, "four", reservation));
            UnityConsoleLogSinkStatistics statistics = LoggingRuntimeHost.GetStatistics();
            Assert.AreEqual(0, statistics.QueuedCount);
            Assert.AreEqual(0, statistics.ReservedCount);
            Assert.AreEqual(1, statistics.DroppedMessageCount);
            LoggingRuntimeHost.Shutdown(false);
            ResetUnityConsoleLogSinkState();
        }

        [Test]
        public void UnityQueue_EditorSourcePathParticipatesInRetainedCharacterBudget()
        {
            ResetUnityConsoleLogSinkState();
            LoggingRuntimeHost.Configure(CreateUnityOptions(4, 512));
            Assert.IsTrue(LoggingRuntimeHost.TryReserve(LogSeverity.Info, 4, out LoggingRuntimeHost.Reservation undersized));
            Assert.IsFalse(LoggingRuntimeHost.Commit(LogSeverity.Info, "a", undersized, "path", 1));

            Assert.IsTrue(LoggingRuntimeHost.TryReserve(LogSeverity.Info, 5, out LoggingRuntimeHost.Reservation exact));
            Assert.IsTrue(LoggingRuntimeHost.Commit(LogSeverity.Info, "a", exact, "path", 1));

            UnityConsoleLogSinkStatistics statistics = LoggingRuntimeHost.GetStatistics();
            Assert.AreEqual(1, statistics.QueuedCount);
            Assert.AreEqual(5, statistics.QueuedCharacters);
            Assert.AreEqual(1, statistics.DroppedMessageCount);
            LoggingRuntimeHost.Shutdown(false);
            ResetUnityConsoleLogSinkState();
        }

        [Test]
        public void UnityFormatting_UsesCultureInvariantLineNumberWithoutAllocationPolicyExpansion()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            customCulture.NumberFormat.NegativeSign = new string('!', 1024);
            try
            {
                CultureInfo.CurrentCulture = customCulture;
                string formatted = FormatThroughPublicWriter(
                    LogSeverity.Info,
                    "Formatting",
                    "message",
                    "Source.cs",
                    -123,
                    nameof(UnityFormatting_UsesCultureInvariantLineNumberWithoutAllocationPolicyExpansion));

                StringAssert.Contains("-123", formatted);
                StringAssert.DoesNotContain(customCulture.NumberFormat.NegativeSign, formatted);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [TestCase(LogQueueOverflowPolicy.DropNewest, "old")]
        [TestCase(LogQueueOverflowPolicy.DropOldest, "new")]
        public void UnityQueue_UsesDedicatedNonBlockingOverflowPolicy(
            LogQueueOverflowPolicy policy,
            string expectedMessage)
        {
            ResetUnityConsoleLogSinkState();
            UnityConsoleLogSinkOptions options = CreateUnityOptions(1, 512);
            options.OverflowPolicy = policy;
            LoggingRuntimeHost.Configure(options);
            Assert.IsTrue(LoggingRuntimeHost.TryReserve(LogSeverity.Info, 16, out LoggingRuntimeHost.Reservation first));
            Assert.IsTrue(LoggingRuntimeHost.Commit(LogSeverity.Info, "old", first));

            bool secondAccepted = LoggingRuntimeHost.TryReserve(
                LogSeverity.Info,
                16,
                out LoggingRuntimeHost.Reservation second);
            if (secondAccepted)
            {
                Assert.IsTrue(LoggingRuntimeHost.Commit(LogSeverity.Info, "new", second));
            }

            Assert.AreEqual(policy == LogQueueOverflowPolicy.DropOldest, secondAccepted);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Log, expectedMessage);
            Assert.IsTrue(LoggingRuntimeHost.TryFlushUnityQueue(100));
            Assert.AreEqual(1, LoggingRuntimeHost.GetStatistics().DroppedMessageCount);
            LoggingRuntimeHost.Shutdown(false);
            ResetUnityConsoleLogSinkState();
        }

        [Test]
        public void UnitySubsystemReset_BlocksWhenExplicitAdapterOwnerSurvives()
        {
            ResetUnityConsoleLogSinkState();
            LoggingRuntimeHost.Configure(CreateUnityOptions(4, 512));
            int adapterGeneration = LoggingRuntimeHost.RegisterAdapter();

            LoggingRuntimeHost.SubsystemResetStatus status = LoggingRuntimeHost.ResetForTests();
            Assert.AreEqual(LoggingRuntimeHost.SubsystemResetStatus.ExternalAdaptersPreserved, status);
            Assert.Throws<InvalidOperationException>(() => LoggingRuntimeHost.Configure(CreateUnityOptions(4, 512)));

            LoggingRuntimeHost.UnregisterAdapter(adapterGeneration);
            LoggingRuntimeHost.Shutdown(false);
            ResetUnityConsoleLogSinkState();
            LoggingRuntimeHost.Configure(CreateUnityOptions(4, 512));
            LoggingRuntimeHost.Shutdown(false);
            ResetUnityConsoleLogSinkState();
        }

        [Test]
        public void UnitySubsystemReset_DestroysUnownedHiddenHost()
        {
            ResetUnityConsoleLogSinkState();
            LoggingRuntimeHost.Configure(CreateUnityOptions(4, 512));
            LoggingRuntimeHost.EnsureInstance();
            Assert.AreEqual(1, UnityEngine.Resources.FindObjectsOfTypeAll<LoggingRuntimeHost>().Length);

            ResetUnityConsoleLogSinkState();

            Assert.AreEqual(0, UnityEngine.Resources.FindObjectsOfTypeAll<LoggingRuntimeHost>().Length);
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
        private static UnityConsoleLogSinkOptions CreateUnityOptions(int maxMessages, int maxCharacters)
        {
            int boundedCharacters = Math.Max(1, maxCharacters);
            return new UnityConsoleLogSinkOptions
            {
                MaxQueuedMessages = Math.Max(1, maxMessages),
                MaxQueuedCharacters = boundedCharacters,
                MaximumRetainedEntryCharacters = Math.Min(64, boundedCharacters),
                ReservedCriticalMessages = 0,
                ReservedCriticalCharacters = 0,
                OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
                CriticalSeverity = LogSeverity.Error
            };
        }
        private static void ResetUnityConsoleLogSinkState()
        {
            LoggingRuntimeHost.ResetForTests();
        }

        private static string FormatThroughPublicWriter(
            LogSeverity severity,
            string category,
            string message,
            string filePath,
            int lineNumber,
            string memberName)
        {
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded(CreateOptions(4, 512));
            var sink = new FormattingSink();
            try
            {
                Assert.IsTrue(pipeline.RegisterSink(sink).IsRegistered);
                ((ILogWriter)pipeline).Write(
                    severity,
                    category,
                    message,
                    filePath,
                    lineNumber,
                    memberName);
                pipeline.Pump(1);
                Assert.IsNotNull(sink.FormattedMessage);
                return sink.FormattedMessage;
            }
            finally
            {
                pipeline.Shutdown(LogFlushMode.Buffered, 2000);
            }
        }

        private sealed class FormattingSink : ILogSink
        {
            internal string FormattedMessage;

            public void Emit(LogEvent logEvent)
            {
                FormattedMessage = UnityConsoleLogSink.FormatMessage(logEvent);
            }

            public void Dispose()
            {
            }
        }

        private sealed class OutOfMemoryEmitSink : ILogSink
        {
            public void Emit(LogEvent logEvent)
            {
                throw new OutOfMemoryException("Synthetic Unity host pump failure.");
            }

            public void Dispose()
            {
            }
        }
    }
}
