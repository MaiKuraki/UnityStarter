using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class CLoggerTests
    {
        private CLogger _logger;
        private RecordingLogger _recordingLogger;

        [SetUp]
        public void SetUp()
        {
            CLogger.Shutdown();
            _logger = CLoggerFactory.CreateSingleThreaded();
            _recordingLogger = new RecordingLogger();
            _logger.AddLogger(_recordingLogger);
        }

        [TearDown]
        public void TearDown()
        {
            _logger?.Dispose();
            _logger = null;
            CLogger.Shutdown();
            CLogger.ConfigureThreadedProcessing();
        }

        [Test]
        public void Pump_ProcessesNoMoreThanMaxItems()
        {
            _logger.EnqueueMessage(LogLevel.Info, "first", "Flow", "CLoggerTests.cs", 10, nameof(Pump_ProcessesNoMoreThanMaxItems));
            _logger.EnqueueMessage(LogLevel.Info, "second", "Flow", "CLoggerTests.cs", 11, nameof(Pump_ProcessesNoMoreThanMaxItems));
            _logger.EnqueueMessage(LogLevel.Info, "third", "Flow", "CLoggerTests.cs", 12, nameof(Pump_ProcessesNoMoreThanMaxItems));

            _logger.Pump(2);
            Assert.AreEqual(2, _recordingLogger.Count);

            _logger.Pump(16);
            Assert.AreEqual(3, _recordingLogger.Count);
        }

        [Test]
        public void SeverityFilter_DropsMessagesBelowCurrentLevel()
        {
            _logger.SetLogLevel(LogLevel.Warning);

            _logger.EnqueueMessage(LogLevel.Info, "filtered", "Severity", "CLoggerTests.cs", 20, nameof(SeverityFilter_DropsMessagesBelowCurrentLevel));
            _logger.EnqueueMessage(LogLevel.Error, "accepted", "Severity", "CLoggerTests.cs", 21, nameof(SeverityFilter_DropsMessagesBelowCurrentLevel));
            _logger.Pump(16);

            Assert.AreEqual(1, _recordingLogger.Count);
            Assert.AreEqual(LogLevel.Error, _recordingLogger[0].Level);
            Assert.AreEqual("accepted", _recordingLogger[0].Message);
        }

        [Test]
        public void WhiteListFilter_OnlyAcceptsMatchingCategory()
        {
            _logger.SetLogFilter(LogFilter.LogWhiteList);
            _logger.AddToWhiteList("Gameplay");

            _logger.EnqueueMessage(LogLevel.Info, "ignored", "Audio", "CLoggerTests.cs", 30, nameof(WhiteListFilter_OnlyAcceptsMatchingCategory));
            _logger.EnqueueMessage(LogLevel.Info, "accepted", "Gameplay", "CLoggerTests.cs", 31, nameof(WhiteListFilter_OnlyAcceptsMatchingCategory));
            _logger.Pump(16);

            Assert.AreEqual(1, _recordingLogger.Count);
            Assert.AreEqual("Gameplay", _recordingLogger[0].Category);
        }

        [Test]
        public void WhiteListFilter_DropsEmptyCategory()
        {
            _logger.SetLogFilter(LogFilter.LogWhiteList);
            _logger.AddToWhiteList("Gameplay");

            _logger.EnqueueMessage(LogLevel.Info, "ignored", null, "CLoggerTests.cs", 35, nameof(WhiteListFilter_DropsEmptyCategory));
            _logger.Pump(16);

            Assert.AreEqual(0, _recordingLogger.Count);
        }

        [Test]
        public void BlackListFilter_DropsMatchingCategory()
        {
            _logger.SetLogFilter(LogFilter.LogNoBlackList);
            _logger.AddToBlackList("Net");

            _logger.EnqueueMessage(LogLevel.Info, "ignored", "Net", "CLoggerTests.cs", 40, nameof(BlackListFilter_DropsMatchingCategory));
            _logger.EnqueueMessage(LogLevel.Info, "accepted", "UI", "CLoggerTests.cs", 41, nameof(BlackListFilter_DropsMatchingCategory));
            _logger.Pump(16);

            Assert.AreEqual(1, _recordingLogger.Count);
            Assert.AreEqual("UI", _recordingLogger[0].Category);
        }

        [Test]
        public void BuilderOverload_DoesNotInvokeBuilderWhenMessageIsFiltered()
        {
            bool invoked = false;
            _logger.SetLogLevel(LogLevel.Error);

            _logger.EnqueueMessage(
                LogLevel.Info,
                sb =>
                {
                    invoked = true;
                    sb.Append("should not run");
                },
                "Builder",
                "CLoggerTests.cs",
                50,
                nameof(BuilderOverload_DoesNotInvokeBuilderWhenMessageIsFiltered));
            _logger.Pump(16);

            Assert.IsFalse(invoked);
            Assert.AreEqual(0, _recordingLogger.Count);
        }

        [Test]
        public void GenericBuilderOverload_UsesStateWithoutCapturingCallerData()
        {
            _logger.EnqueueMessage(
                LogLevel.Info,
                42,
                static (state, sb) => sb.Append("value=").Append(state),
                "Builder",
                "CLoggerTests.cs",
                60,
                nameof(GenericBuilderOverload_UsesStateWithoutCapturingCallerData));
            _logger.Pump(16);

            Assert.AreEqual(1, _recordingLogger.Count);
            Assert.AreEqual("value=42", _recordingLogger[0].Message);
        }

        [Test]
        public void DispatchToLoggers_ContinuesAfterLoggerThrows()
        {
            _logger.ClearLoggers();
            _recordingLogger = new RecordingLogger();
            _logger.AddLogger(new ThrowingLogger());
            _logger.AddLogger(_recordingLogger);

            _logger.EnqueueMessage(LogLevel.Info, "survives", "Dispatch", "CLoggerTests.cs", 70, nameof(DispatchToLoggers_ContinuesAfterLoggerThrows));
            _logger.Pump(16);

            Assert.AreEqual(1, _recordingLogger.Count);
            Assert.AreEqual("survives", _recordingLogger[0].Message);
        }

        [Test]
        public void AddLoggerUnique_RegistersOnlyOneLoggerPerConcreteType()
        {
            _logger.ClearLoggers();
            var first = new RecordingLogger();
            var second = new RecordingLogger();

            _logger.AddLoggerUnique(first);
            _logger.AddLoggerUnique(second);
            _logger.EnqueueMessage(LogLevel.Info, "unique", "Registration", "CLoggerTests.cs", 80, nameof(AddLoggerUnique_RegistersOnlyOneLoggerPerConcreteType));
            _logger.Pump(16);

            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(0, second.Count);
        }

        [Test]
        public void ProcessingQueue_DropsNewestWhenQueueIsFull()
        {
            using var logger = CLoggerFactory.CreateSingleThreaded(new LoggerProcessingOptions
            {
                MaxQueuedMessages = 2,
                OverflowPolicy = LogQueueOverflowPolicy.DropNewest
            });
            var recording = new RecordingLogger();
            logger.AddLogger(recording);

            logger.EnqueueMessage(LogLevel.Info, "first", "Queue", "CLoggerTests.cs", 90, nameof(ProcessingQueue_DropsNewestWhenQueueIsFull));
            logger.EnqueueMessage(LogLevel.Info, "second", "Queue", "CLoggerTests.cs", 91, nameof(ProcessingQueue_DropsNewestWhenQueueIsFull));
            logger.EnqueueMessage(LogLevel.Info, "third", "Queue", "CLoggerTests.cs", 92, nameof(ProcessingQueue_DropsNewestWhenQueueIsFull));
            logger.Pump(16);

            Assert.AreEqual(2, recording.Count);
            Assert.AreEqual("first", recording[0].Message);
            Assert.AreEqual("second", recording[1].Message);
            Assert.AreEqual(1, logger.GetProcessingStatistics().DroppedMessageCount);
        }

        [Test]
        public void ProcessingQueue_GuaranteedLevelDisplacesOldestMessage()
        {
            using var logger = CLoggerFactory.CreateSingleThreaded(new LoggerProcessingOptions
            {
                MaxQueuedMessages = 2,
                OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
                GuaranteedLevel = LogLevel.Error
            });
            var recording = new RecordingLogger();
            logger.AddLogger(recording);

            logger.EnqueueMessage(LogLevel.Info, "first", "Queue", "CLoggerTests.cs", 100, nameof(ProcessingQueue_GuaranteedLevelDisplacesOldestMessage));
            logger.EnqueueMessage(LogLevel.Info, "second", "Queue", "CLoggerTests.cs", 101, nameof(ProcessingQueue_GuaranteedLevelDisplacesOldestMessage));
            logger.EnqueueMessage(LogLevel.Error, "error", "Queue", "CLoggerTests.cs", 102, nameof(ProcessingQueue_GuaranteedLevelDisplacesOldestMessage));
            logger.Pump(16);

            Assert.AreEqual(2, recording.Count);
            Assert.AreEqual("second", recording[0].Message);
            Assert.AreEqual("error", recording[1].Message);
            Assert.AreEqual(1, logger.GetProcessingStatistics().DroppedMessageCount);
        }

        [Test]
        public void Factory_CanCreateLoggerForDiInterface()
        {
            ICLogger logger = CLoggerFactory.CreateSingleThreaded();
            try
            {
                var recording = new RecordingLogger();
                logger.AddLogger(recording);
                logger.Log(LogLevel.Info, "di", "Factory");
                logger.Pump(16);

                Assert.AreEqual(1, recording.Count);
                Assert.AreEqual("di", recording[0].Message);
            }
            finally
            {
                logger.Dispose();
            }
        }

        [Test]
        public void Shutdown_AllowsGlobalInstanceRecreation()
        {
            CLogger.Shutdown();
            CLogger.ConfigureSingleThreadedProcessing();
            var first = CLogger.Instance;

            CLogger.Shutdown();
            CLogger.ConfigureSingleThreadedProcessing();

            var second = CLogger.Instance;
            Assert.AreNotSame(first, second);

            CLogger.Shutdown();
        }

        [Test]
        public void SuppressedGlobalStaticLogging_DoesNotCreateGlobalInstance()
        {
            CLogger.Shutdown();
            CLogger.ConfigureGlobalStaticLoggingSuppressed(true);
            bool invoked = false;

            CLogger.LogInfo(1, (state, sb) =>
            {
                invoked = true;
                sb.Append(state);
            }, "Suppressed");

            Assert.IsFalse(invoked);
            Assert.IsFalse(CLogger.TryGetInstance(out _));
            CLogger.ConfigureGlobalStaticLoggingSuppressed(false);
        }

        [Test]
        public void ExplicitGlobalInstance_DisablesSuppressedStaticLogging()
        {
            CLogger.Shutdown();
            CLogger.ConfigureSingleThreadedProcessing();
            CLogger.ConfigureGlobalStaticLoggingSuppressed(true);

            var global = CLogger.Instance;
            var recording = new RecordingLogger();
            global.AddLogger(recording);

            CLogger.LogInfo(7, static (state, sb) => sb.Append(state), "Suppressed");
            global.Pump(16);

            Assert.AreEqual(1, recording.Count);
            Assert.AreEqual("7", recording[0].Message);
            CLogger.Shutdown();
        }

        [Test]
        public void UnityLoggerFormatMessage_UsesHrefPathAndLineForConsoleNavigation()
        {
            var message = LogMessagePool.Get();
            string sourcePath = Path.Combine(UnityEngine.Application.dataPath, "Game", "Foo.cs");
            string expectedFullPath = sourcePath.Replace('\\', '/');
            try
            {
                message.Initialize(
                    DateTime.Now,
                    LogLevel.Info,
                    "hello",
                    null,
                    "Gameplay",
                    sourcePath,
                    42,
                    nameof(UnityLoggerFormatMessage_UsesHrefPathAndLineForConsoleNavigation));

                string formatted = UnityLogger.FormatMessage(message);

                StringAssert.Contains("[Gameplay] hello", formatted);
                StringAssert.Contains("href=\"Assets/Game/Foo.cs:42\"", formatted);
                StringAssert.Contains("(at Assets/Game/Foo.cs:42)", formatted);
                Assert.IsTrue(LoggerEditorLinkRegistry.TryGetFullPath("Assets/Game/Foo.cs", 42, out var fullPath));
                Assert.AreEqual(expectedFullPath, fullPath);
            }
            finally
            {
                LogMessagePool.Return(message);
            }
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

        private sealed class ThrowingLogger : ILogger
        {
            public void Log(LogMessage logMessage)
            {
                throw new InvalidOperationException("expected test failure");
            }

            public void Dispose()
            {
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
