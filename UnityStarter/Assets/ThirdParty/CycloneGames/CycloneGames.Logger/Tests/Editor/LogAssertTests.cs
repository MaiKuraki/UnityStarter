using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.Logging;
using NUnit.Framework;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class LogAssertTests
    {
        private CLogger _logger;
        private RecordingLogger _recordingLogger;
        private ILogWriter _previousWriter;

        [SetUp]
        public void SetUp()
        {
            _previousWriter = LogRuntime.ReplaceWriter(NullLogWriter.Instance);
            CLogAssert.Reset();
            _logger = CLoggerFactory.CreateSingleThreaded();
            _recordingLogger = new RecordingLogger();
            _logger.AddLogger(_recordingLogger);
            Assert.IsTrue(LogRuntime.TryInstallWriter(_logger));
        }

        [TearDown]
        public void TearDown()
        {
            CLogAssert.Reset();
            if (_logger != null)
            {
                LogRuntime.TryReplaceWriter(
                    _logger,
                    _previousWriter ?? NullLogWriter.Instance);
            }

            _logger?.ShutdownInstance();
            _logger = null;
            _previousWriter = null;
        }

        [Test]
        public void StaticAssert_LogsFailureWithCallerLocation()
        {
            CLogAssert.IsTrue(false, "broken", "Checks");
            _logger.Pump(16);

            Assert.AreEqual(1, _recordingLogger.Count);
            Assert.AreEqual(LogLevel.Error, _recordingLogger[0].Level);
            Assert.AreEqual("Checks", _recordingLogger[0].Category);
            Assert.AreEqual("broken", _recordingLogger[0].Message);
            Assert.AreEqual(nameof(StaticAssert_LogsFailureWithCallerLocation), _recordingLogger[0].MemberName);
            StringAssert.EndsWith("LogAssertTests.cs", _recordingLogger[0].FilePath.Replace('\\', '/'));
            Assert.Greater(_recordingLogger[0].LineNumber, 0);
        }

        [Test]
        public void StaticAssert_GenericBuilderDoesNotRunWhenConditionPasses()
        {
            var state = new InvocationState();

            CLogAssert.That(true, state, static (s, sb) =>
            {
                s.Invoked = true;
                sb.Append("should not run");
            }, "Checks");
            _logger.Pump(16);

            Assert.IsFalse(state.Invoked);
            Assert.AreEqual(0, _recordingLogger.Count);
        }

        [Test]
        public void StaticAssert_DisabledDoesNotInvokeBuilderOrWrite()
        {
            CLogAssert.Configure(new CLogAssertOptions { Enabled = false });
            var state = new InvocationState();

            CLogAssert.Fail(state, static (s, sb) =>
            {
                s.Invoked = true;
                sb.Append("disabled");
            }, "Checks");

            Assert.IsFalse(state.Invoked);
            Assert.AreEqual(0, _recordingLogger.Count);
        }

        [Test]
        public void StaticAssert_ThrowBehaviorThrowsWithoutLogging()
        {
            CLogAssert.Configure(new CLogAssertOptions
            {
                FailureBehavior = CLogAssertFailureBehavior.Throw,
                Category = "Checks"
            });

            var exception = Assert.Throws<CLogAssertionException>(() => CLogAssert.Fail("boom"));
            _logger.Pump(16);

            Assert.AreEqual("boom", exception.Message);
            Assert.AreEqual("Checks", exception.Category);
            Assert.AreEqual(0, _recordingLogger.Count);
        }

        [Test]
        public void StaticAssert_LogAndThrowBehaviorLogsBeforeThrowing()
        {
            CLogAssert.Configure(new CLogAssertOptions
            {
                FailureBehavior = CLogAssertFailureBehavior.LogAndThrow,
                FailureLevel = LogSeverity.Fatal,
                Category = "Checks"
            });

            var exception = Assert.Throws<CLogAssertionException>(() => CLogAssert.Fail("fatal"));

            Assert.AreEqual("fatal", exception.Message);
            Assert.AreEqual(1, _recordingLogger.Count);
            Assert.AreEqual(LogLevel.Fatal, _recordingLogger[0].Level);
            Assert.AreEqual("fatal", _recordingLogger[0].Message);
        }

        [Test]
        public void ServiceAssert_AreEqualUsesInjectedLogger()
        {
            using var logger = CLoggerFactory.CreateSingleThreaded();
            var recording = new RecordingLogger();
            logger.AddLogger(recording);
            var assert = new CLogAssertService(logger, new CLogAssertOptions
            {
                FailureLevel = LogSeverity.Warning,
                Category = "ServiceAssert"
            });

            assert.AreEqual(10, 20, "mismatch");
            logger.Pump(16);

            Assert.AreEqual(1, recording.Count);
            Assert.AreEqual(LogLevel.Warning, recording[0].Level);
            Assert.AreEqual("ServiceAssert", recording[0].Category);
            StringAssert.Contains("mismatch", recording[0].Message);
            StringAssert.Contains("Expected: 10", recording[0].Message);
            StringAssert.Contains("Actual: 20", recording[0].Message);
        }

        [Test]
        public void Options_InvalidEnumThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CLogAssert.Configure(new CLogAssertOptions
            {
                FailureBehavior = (CLogAssertFailureBehavior)200
            }));
        }

        private sealed class InvocationState
        {
            public bool Invoked;
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly List<Record> _records = new List<Record>();

            public int Count => _records.Count;
            public Record this[int index] => _records[index];

            public void Log(LogMessage logMessage)
            {
                _records.Add(new Record(
                    logMessage.Level,
                    logMessage.Category,
                    logMessage.OriginalMessage ?? CopyBuilder(logMessage.MessageBuilder),
                    logMessage.FilePath,
                    logMessage.LineNumber,
                    logMessage.MemberName));
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
            public readonly LogLevel Level;
            public readonly string Category;
            public readonly string Message;
            public readonly string FilePath;
            public readonly int LineNumber;
            public readonly string MemberName;

            public Record(LogLevel level, string category, string message, string filePath, int lineNumber, string memberName)
            {
                Level = level;
                Category = category;
                Message = message;
                FilePath = filePath;
                LineNumber = lineNumber;
                MemberName = memberName;
            }
        }
    }
}
