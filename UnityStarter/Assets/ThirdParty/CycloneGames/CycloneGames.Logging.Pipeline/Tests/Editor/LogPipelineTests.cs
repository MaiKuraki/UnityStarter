using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.Logging;
using NUnit.Framework;

namespace CycloneGames.Logging.Pipeline.Tests.Editor
{
    public sealed class LogPipelineTests
    {
        private LogPipeline _pipeline;
        private RecordingSink _recordingSink;

        [SetUp]
        public void SetUp()
        {
            _pipeline = LogPipelineFactory.CreateSingleThreaded();
            _recordingSink = new RecordingSink();
            _pipeline.AddSink(_recordingSink);
        }

        [TearDown]
        public void TearDown()
        {
            _pipeline?.Dispose();
            _pipeline = null;
        }

        [Test]
        public void Pump_ProcessesNoMoreThanMaxItems()
        {
            _pipeline.EnqueueMessage(LogSeverity.Info, "first", "Flow", "LogPipelineTests.cs", 10, nameof(Pump_ProcessesNoMoreThanMaxItems));
            _pipeline.EnqueueMessage(LogSeverity.Info, "second", "Flow", "LogPipelineTests.cs", 11, nameof(Pump_ProcessesNoMoreThanMaxItems));
            _pipeline.EnqueueMessage(LogSeverity.Info, "third", "Flow", "LogPipelineTests.cs", 12, nameof(Pump_ProcessesNoMoreThanMaxItems));

            _pipeline.Pump(2);
            Assert.AreEqual(2, _recordingSink.Count);

            _pipeline.Pump(16);
            Assert.AreEqual(3, _recordingSink.Count);
        }

        [Test]
        public void SeverityFilter_DropsMessagesBelowCurrentLevel()
        {
            _pipeline.SetMinimumSeverity(LogSeverity.Warning);

            _pipeline.EnqueueMessage(LogSeverity.Info, "filtered", "Severity", "LogPipelineTests.cs", 20, nameof(SeverityFilter_DropsMessagesBelowCurrentLevel));
            _pipeline.EnqueueMessage(LogSeverity.Error, "accepted", "Severity", "LogPipelineTests.cs", 21, nameof(SeverityFilter_DropsMessagesBelowCurrentLevel));
            _pipeline.Pump(16);

            Assert.AreEqual(1, _recordingSink.Count);
            Assert.AreEqual(LogSeverity.Error, _recordingSink[0].Severity);
            Assert.AreEqual("accepted", _recordingSink[0].Message);
        }

        [Test]
        public void AllowListFilter_OnlyAcceptsMatchingCategory()
        {
            _pipeline.SetCategoryFilter(LogCategoryFilterMode.AllowList);
            _pipeline.AddAllowedCategory("Gameplay");

            _pipeline.EnqueueMessage(LogSeverity.Info, "ignored", "Audio", "LogPipelineTests.cs", 30, nameof(AllowListFilter_OnlyAcceptsMatchingCategory));
            _pipeline.EnqueueMessage(LogSeverity.Info, "accepted", "Gameplay", "LogPipelineTests.cs", 31, nameof(AllowListFilter_OnlyAcceptsMatchingCategory));
            _pipeline.Pump(16);

            Assert.AreEqual(1, _recordingSink.Count);
            Assert.AreEqual("Gameplay", _recordingSink[0].Category);
        }

        [Test]
        public void AllowListFilter_DropsEmptyCategory()
        {
            _pipeline.SetCategoryFilter(LogCategoryFilterMode.AllowList);
            _pipeline.AddAllowedCategory("Gameplay");

            _pipeline.EnqueueMessage(LogSeverity.Info, "ignored", null, "LogPipelineTests.cs", 35, nameof(AllowListFilter_DropsEmptyCategory));
            _pipeline.Pump(16);

            Assert.AreEqual(0, _recordingSink.Count);
        }

        [Test]
        public void DenyListFilter_DropsMatchingCategory()
        {
            _pipeline.SetCategoryFilter(LogCategoryFilterMode.DenyList);
            _pipeline.AddDeniedCategory("Net");

            _pipeline.EnqueueMessage(LogSeverity.Info, "ignored", "Net", "LogPipelineTests.cs", 40, nameof(DenyListFilter_DropsMatchingCategory));
            _pipeline.EnqueueMessage(LogSeverity.Info, "accepted", "UI", "LogPipelineTests.cs", 41, nameof(DenyListFilter_DropsMatchingCategory));
            _pipeline.Pump(16);

            Assert.AreEqual(1, _recordingSink.Count);
            Assert.AreEqual("UI", _recordingSink[0].Category);
        }

        [Test]
        public void BuilderOverload_DoesNotInvokeBuilderWhenMessageIsFiltered()
        {
            bool invoked = false;
            _pipeline.SetMinimumSeverity(LogSeverity.Error);

            _pipeline.EnqueueMessage(
                LogSeverity.Info,
                sb =>
                {
                    invoked = true;
                    sb.Append("should not run");
                },
                "Builder",
                "LogPipelineTests.cs",
                50,
                nameof(BuilderOverload_DoesNotInvokeBuilderWhenMessageIsFiltered));
            _pipeline.Pump(16);

            Assert.IsFalse(invoked);
            Assert.AreEqual(0, _recordingSink.Count);
        }

        [Test]
        public void GenericBuilderOverload_UsesStateWithoutCapturingCallerData()
        {
            _pipeline.EnqueueMessage(
                LogSeverity.Info,
                42,
                static (state, sb) => sb.Append("value=").Append(state),
                "Builder",
                "LogPipelineTests.cs",
                60,
                nameof(GenericBuilderOverload_UsesStateWithoutCapturingCallerData));
            _pipeline.Pump(16);

            Assert.AreEqual(1, _recordingSink.Count);
            Assert.AreEqual("value=42", _recordingSink[0].Message);
        }

        [Test]
        public void DispatchToSinks_ContinuesAfterSinkThrows()
        {
            _pipeline.ClearSinks();
            _recordingSink = new RecordingSink();
            _pipeline.AddSink(new ThrowingSink());
            _pipeline.AddSink(_recordingSink);

            _pipeline.EnqueueMessage(LogSeverity.Info, "survives", "Dispatch", "LogPipelineTests.cs", 70, nameof(DispatchToSinks_ContinuesAfterSinkThrows));
            _pipeline.Pump(16);

            Assert.AreEqual(1, _recordingSink.Count);
            Assert.AreEqual("survives", _recordingSink[0].Message);
        }

        [Test]
        public void TryAddSink_RegistersOnlyOneSinkPerConcreteType()
        {
            _pipeline.ClearSinks();
            var first = new RecordingSink();
            var second = new RecordingSink();

            _pipeline.TryAddSink(first);
            _pipeline.TryAddSink(second);
            _pipeline.EnqueueMessage(LogSeverity.Info, "unique", "Registration", "LogPipelineTests.cs", 80, nameof(TryAddSink_RegistersOnlyOneSinkPerConcreteType));
            _pipeline.Pump(16);

            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(0, second.Count);
        }

        [Test]
        public void ProcessingQueue_DropsNewestWhenQueueIsFull()
        {
            using var pipeline = LogPipelineFactory.CreateSingleThreaded(new LogPipelineOptions
            {
                MaxQueuedMessages = 2,
                ReservedCriticalMessages = 0,
                ReservedCriticalCharacters = 0,
                OverflowPolicy = LogQueueOverflowPolicy.DropNewest
            });
            var recording = new RecordingSink();
            pipeline.AddSink(recording);

            pipeline.EnqueueMessage(LogSeverity.Info, "first", "Queue", "LogPipelineTests.cs", 90, nameof(ProcessingQueue_DropsNewestWhenQueueIsFull));
            pipeline.EnqueueMessage(LogSeverity.Info, "second", "Queue", "LogPipelineTests.cs", 91, nameof(ProcessingQueue_DropsNewestWhenQueueIsFull));
            pipeline.EnqueueMessage(LogSeverity.Info, "third", "Queue", "LogPipelineTests.cs", 92, nameof(ProcessingQueue_DropsNewestWhenQueueIsFull));
            pipeline.Pump(16);

            Assert.AreEqual(2, recording.Count);
            Assert.AreEqual("first", recording[0].Message);
            Assert.AreEqual("second", recording[1].Message);
            Assert.AreEqual(1, pipeline.GetStatistics().DroppedMessageCount);
        }

        [Test]
        public void ProcessingQueue_CriticalReservationPreservesOldestNormalMessage()
        {
            using var pipeline = LogPipelineFactory.CreateSingleThreaded(new LogPipelineOptions
            {
                MaxQueuedMessages = 2,
                OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
                CriticalSeverity = LogSeverity.Error
            });
            var recording = new RecordingSink();
            pipeline.AddSink(recording);

            pipeline.EnqueueMessage(LogSeverity.Info, "first", "Queue", "LogPipelineTests.cs", 100, nameof(ProcessingQueue_CriticalReservationPreservesOldestNormalMessage));
            pipeline.EnqueueMessage(LogSeverity.Info, "second", "Queue", "LogPipelineTests.cs", 101, nameof(ProcessingQueue_CriticalReservationPreservesOldestNormalMessage));
            pipeline.EnqueueMessage(LogSeverity.Error, "error", "Queue", "LogPipelineTests.cs", 102, nameof(ProcessingQueue_CriticalReservationPreservesOldestNormalMessage));
            pipeline.Pump(16);

            Assert.AreEqual(2, recording.Count);
            Assert.AreEqual("first", recording[0].Message);
            Assert.AreEqual("error", recording[1].Message);
            Assert.AreEqual(1, pipeline.GetStatistics().DroppedMessageCount);
        }

        [Test]
        public void Factory_CreatesBackendUsableThroughUnifiedWriter()
        {
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded();
            try
            {
                var recording = new RecordingSink();
                pipeline.AddSink(recording);
                pipeline.Write(LogSeverity.Info, "di", "Factory");
                pipeline.Pump(16);

                Assert.AreEqual(1, recording.Count);
                Assert.AreEqual("di", recording[0].Message);
            }
            finally
            {
                pipeline.Dispose();
            }
        }

        private sealed class RecordingSink : ILogSink
        {
            private readonly List<Record> _records = new List<Record>();

            public int Count => _records.Count;

            public Record this[int index] => _records[index];

            public void Emit(LogEvent logEvent)
            {
                _records.Add(new Record(
                    logEvent.Severity,
                    logEvent.Category,
                    logEvent.OriginalMessage ?? CopyBuilder(logEvent.MessageBuilder),
                    logEvent.FilePath,
                    logEvent.LineNumber,
                    logEvent.MemberName));
            }

            public void Dispose()
            {
            }

            private static string CopyBuilder(StringBuilder builder)
            {
                return builder == null ? string.Empty : builder.ToString();
            }
        }

        private sealed class ThrowingSink : ILogSink
        {
            public void Emit(LogEvent logEvent)
            {
                throw new InvalidOperationException("expected test failure");
            }

            public void Dispose()
            {
            }
        }

        private readonly struct Record
        {
            public readonly LogSeverity Severity;
            public readonly string Category;
            public readonly string Message;
            public readonly string FilePath;
            public readonly int LineNumber;
            public readonly string MemberName;

            public Record(LogSeverity level, string category, string message, string filePath, int lineNumber, string memberName)
            {
                Severity = level;
                Category = category;
                Message = message;
                FilePath = filePath;
                LineNumber = lineNumber;
                MemberName = memberName;
            }
        }
    }
}
