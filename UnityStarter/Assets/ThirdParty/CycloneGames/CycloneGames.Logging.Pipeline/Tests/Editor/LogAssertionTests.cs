using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;
using NUnit.Framework;

namespace CycloneGames.Logging.Pipeline.Tests.Editor
{
    public sealed class LogAssertionTests
    {
        private LogPipeline _pipeline;
        private RecordingSink _recordingSink;
        private LogAssertionService _assertions;

        [SetUp]
        public void SetUp()
        {
            _pipeline = LogPipelineFactory.CreateSingleThreaded();
            _recordingSink = new RecordingSink();
            _pipeline.RegisterSink(_recordingSink);
            _assertions = new LogAssertionService(_pipeline);
        }

        [TearDown]
        public void TearDown()
        {
            _pipeline?.Shutdown();
            _pipeline = null;
            _assertions = null;
        }

        [Test]
        public void AssertionService_LogsFailureWithCallerLocation()
        {
            _assertions.IsTrue(false, "broken", "Checks");
            _pipeline.Pump(16);

            Assert.AreEqual(1, _recordingSink.Count);
            Assert.AreEqual(LogSeverity.Error, _recordingSink[0].Severity);
            Assert.AreEqual("Checks", _recordingSink[0].Category);
            Assert.AreEqual("broken", _recordingSink[0].Message);
            Assert.AreEqual(nameof(AssertionService_LogsFailureWithCallerLocation), _recordingSink[0].MemberName);
            StringAssert.EndsWith("LogAssertionTests.cs", _recordingSink[0].FilePath.Replace('\\', '/'));
            Assert.Greater(_recordingSink[0].LineNumber, 0);
        }

        [Test]
        public void AssertionService_GenericBuilderDoesNotRunWhenConditionPasses()
        {
            var state = new InvocationState();

            _assertions.That(true, state, static (s, sb) =>
            {
                s.Invoked = true;
                sb.Append("should not run");
            }, "Checks");
            _pipeline.Pump(16);

            Assert.IsFalse(state.Invoked);
            Assert.AreEqual(0, _recordingSink.Count);
        }

        [Test]
        public void AssertionService_DisabledDoesNotInvokeBuilderOrWrite()
        {
            _assertions.Configure(new LogAssertionOptions { Enabled = false });
            var state = new InvocationState();

            _assertions.Fail(state, static (s, sb) =>
            {
                s.Invoked = true;
                sb.Append("disabled");
            }, "Checks");

            Assert.IsFalse(state.Invoked);
            Assert.AreEqual(0, _recordingSink.Count);
        }

        [Test]
        public void AssertionService_ThrowBehaviorThrowsWithoutLogging()
        {
            _assertions.Configure(new LogAssertionOptions
            {
                FailureBehavior = LogAssertionFailureBehavior.Throw,
                Category = "Checks"
            });

            var exception = Assert.Throws<LogAssertionException>(() => _assertions.Fail("boom"));
            _pipeline.Pump(16);

            Assert.AreEqual("boom", exception.Message);
            Assert.AreEqual("Checks", exception.Category);
            Assert.AreEqual(0, _recordingSink.Count);
        }

        [Test]
        public void AssertionService_LogAndThrowBehaviorLogsBeforeThrowing()
        {
            _assertions.Configure(new LogAssertionOptions
            {
                FailureBehavior = LogAssertionFailureBehavior.LogAndThrow,
                FailureSeverity = LogSeverity.Fatal,
                Category = "Checks"
            });

            var exception = Assert.Throws<LogAssertionException>(() => _assertions.Fail("fatal"));

            Assert.AreEqual("fatal", exception.Message);
            Assert.AreEqual(1, _recordingSink.Count);
            Assert.AreEqual(LogSeverity.Fatal, _recordingSink[0].Severity);
            Assert.AreEqual("fatal", _recordingSink[0].Message);
        }

        [Test]
        public void ServiceAssert_AreEqualUsesInjectedWriter()
        {
            using var pipeline = LogPipelineFactory.CreateSingleThreaded();
            var recording = new RecordingSink();
            pipeline.RegisterSink(recording);
            var assert = new LogAssertionService(pipeline, new LogAssertionOptions
            {
                FailureSeverity = LogSeverity.Warning,
                Category = "ServiceAssert"
            });

            assert.AreEqual(10, 20, "mismatch");
            pipeline.Pump(16);

            Assert.AreEqual(1, recording.Count);
            Assert.AreEqual(LogSeverity.Warning, recording[0].Severity);
            Assert.AreEqual("ServiceAssert", recording[0].Category);
            StringAssert.Contains("mismatch", recording[0].Message);
            StringAssert.Contains("Expected: 10", recording[0].Message);
            StringAssert.Contains("Actual: 20", recording[0].Message);
        }

        [Test]
        public void Options_InvalidEnumThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _assertions.Configure(new LogAssertionOptions
            {
                FailureBehavior = (LogAssertionFailureBehavior)200
            }));
        }

        private sealed class InvocationState
        {
            public bool Invoked;
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
