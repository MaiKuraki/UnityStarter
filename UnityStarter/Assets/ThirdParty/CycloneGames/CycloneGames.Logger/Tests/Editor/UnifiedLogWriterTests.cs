using System;
using System.Text;
using CycloneGames.Logging;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class UnifiedLogWriterTests
    {
        private ILogWriter _previousWriter;

        [SetUp]
        public void SetUp()
        {
            LoggerUpdater.CaptureMainThreadForLifecycle();
            LoggerBootstrap.Shutdown();
            CLogger.Shutdown();
            _previousWriter = LogRuntime.ReplaceWriter(NullLogWriter.Instance);
#if UNITY_INCLUDE_TESTS
            LoggerBootstrap.BeforeProcessWriterInstallTestHook = null;
#endif
        }

        [TearDown]
        public void TearDown()
        {
#if UNITY_INCLUDE_TESTS
            LoggerBootstrap.BeforeProcessWriterInstallTestHook = null;
#endif
            LoggerBootstrap.Shutdown();
            CLogger.Shutdown();
            ILogWriter currentWriter = LogRuntime.Writer;
            LogRuntime.TryReplaceWriter(
                currentWriter,
                _previousWriter ?? NullLogWriter.Instance);
            _previousWriter = null;
        }

        [Test]
        public void CLogger_ImplementsUnifiedWriterWithoutStaticGlobalAccess()
        {
            CLogger logger = CLoggerFactory.CreateSingleThreaded();
            var sink = new RecordingSink();
            try
            {
                ILogWriter writer = logger;
                Assert.IsFalse(writer.IsEnabled(LogSeverity.Info, "CycloneGames.Tests"));

                Assert.IsTrue(logger.AddLogger(sink));
                Assert.IsTrue(writer.IsEnabled(LogSeverity.Info, "CycloneGames.Tests"));

                writer.Write(LogSeverity.Info, "CycloneGames.Tests", "message");
                logger.Pump();

                Assert.AreEqual(LogLevel.Info, sink.Level);
                Assert.AreEqual("CycloneGames.Tests", sink.Category);
                Assert.AreEqual("message", sink.Message);
            }
            finally
            {
                logger.ShutdownInstance();
            }
        }

        [Test]
        public void CLogger_UnifiedExceptionWriterFormatsBoundedExceptionText()
        {
            CLogger logger = CLoggerFactory.CreateSingleThreaded();
            var sink = new RecordingSink();
            try
            {
                logger.AddLogger(sink);
                ILogWriter writer = logger;
                var exception = new InvalidOperationException("failure");

                writer.WriteException(
                    LogSeverity.Error,
                    "CycloneGames.Tests",
                    exception,
                    "operation failed");
                logger.Pump();

                StringAssert.StartsWith("operation failed", sink.Message);
                StringAssert.Contains(typeof(InvalidOperationException).FullName, sink.Message);
                StringAssert.Contains("failure", sink.Message);
            }
            finally
            {
                logger.ShutdownInstance();
            }
        }

        [Test]
        public void LoggerBootstrap_InitializeReinitializeAndShutdownOwnProcessWriter()
        {
            LoggerSettings settings = ScriptableObject.CreateInstance<LoggerSettings>();
            settings.processing = LoggerSettings.ProcessingMode.ForceSingleThread;
            settings.registerUnityLogger = true;
            settings.registerConsoleLogger = false;
            settings.registerFileLogger = false;

            try
            {
                LoggerInitializationResult initialization = LoggerBootstrap.Initialize(settings);
                Assert.AreEqual(LoggerInitializationStatus.Initialized, initialization.Status);
                Assert.IsTrue(initialization.ProcessWriterInstalled);
                Assert.IsInstanceOf<CLogger>(LogRuntime.Writer);
                ILogWriter firstWriter = LogRuntime.Writer;

                LoggerInitializationResult repeated = LoggerBootstrap.Initialize(settings);
                Assert.AreEqual(LoggerInitializationStatus.AlreadyInitialized, repeated.Status);
                Assert.AreSame(firstWriter, LogRuntime.Writer);

                LoggerReinitializationResult reinitialization = LoggerBootstrap.Reinitialize(settings);
                Assert.IsTrue(reinitialization.Succeeded);
                Assert.AreEqual(LoggerInitializationStatus.Initialized, reinitialization.Initialization.Status);
                Assert.IsInstanceOf<CLogger>(LogRuntime.Writer);
                Assert.AreNotSame(firstWriter, LogRuntime.Writer);

                LoggerShutdownResult shutdown = LoggerBootstrap.Shutdown();
                Assert.IsTrue(shutdown.IsComplete);
                Assert.AreSame(NullLogWriter.Instance, LogRuntime.Writer);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void LoggerBootstrap_DoesNotTakeOwnershipOfExistingGlobalLogger()
        {
            CLogger existing = CLogger.Instance;

            LoggerInitializationResult initialization = LoggerBootstrap.Initialize();
            Assert.AreEqual(LoggerInitializationStatus.ExistingLoggerNotOwned, initialization.Status);

            LoggerShutdownResult shutdown = LoggerBootstrap.Shutdown();
            Assert.AreEqual(LoggerShutdownStatus.NotStarted, shutdown.Status);
            Assert.IsTrue(CLogger.TryGetInstance(out CLogger current));
            Assert.AreSame(existing, current);
        }

        [Test]
        public void LoggerBootstrap_InitializePreservesExistingProcessWriterWithoutCreatingSidecar()
        {
            LoggerSettings settings = CreateSettings();
            var foreignWriter = new RecordingWriter();
            LogRuntime.ReplaceWriter(foreignWriter);

            try
            {
                LoggerInitializationResult initialization = LoggerBootstrap.Initialize(settings);

                Assert.AreEqual(
                    LoggerInitializationStatus.ExistingProcessWriterNotOwned,
                    initialization.Status);
                Assert.IsFalse(initialization.IsInitialized);
                Assert.IsFalse(initialization.ProcessWriterInstalled);
                Assert.AreSame(foreignWriter, LogRuntime.Writer);
                Assert.IsFalse(CLogger.TryGetInstance(out _));
                Assert.IsFalse(LoggerBootstrap.TryGetOwnedLogger(out _));

                LoggerInitializationResult repeated = LoggerBootstrap.Initialize(settings);
                Assert.AreEqual(
                    LoggerInitializationStatus.ExistingProcessWriterNotOwned,
                    repeated.Status);
                Assert.AreSame(foreignWriter, LogRuntime.Writer);

                LoggerShutdownResult shutdown = LoggerBootstrap.Shutdown();
                Assert.AreEqual(LoggerShutdownStatus.NotStarted, shutdown.Status);
                Assert.AreSame(foreignWriter, LogRuntime.Writer);
                Assert.AreEqual(0, foreignWriter.DisposeCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void LoggerBootstrap_ReinitializePreservesExistingProcessWriter()
        {
            LoggerSettings settings = CreateSettings();
            var foreignWriter = new RecordingWriter();
            LogRuntime.ReplaceWriter(foreignWriter);

            try
            {
                LoggerReinitializationResult result = LoggerBootstrap.Reinitialize(settings);

                Assert.AreEqual(LoggerShutdownStatus.NotStarted, result.Shutdown.Status);
                Assert.AreEqual(
                    LoggerInitializationStatus.ExistingProcessWriterNotOwned,
                    result.Initialization.Status);
                Assert.IsFalse(result.Succeeded);
                Assert.AreSame(foreignWriter, LogRuntime.Writer);
                Assert.IsFalse(CLogger.TryGetInstance(out _));
                Assert.AreEqual(0, foreignWriter.DisposeCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

#if UNITY_INCLUDE_TESTS
        [Test]
        public void LoggerBootstrap_ProcessWriterRaceRollsBackCreatedBackendAndPreservesWinner()
        {
            LoggerSettings settings = CreateSettings();
            var winningWriter = new RecordingWriter();
            LoggerBootstrap.BeforeProcessWriterInstallTestHook = () =>
            {
                LoggerBootstrap.BeforeProcessWriterInstallTestHook = null;
                Assert.IsTrue(LogRuntime.TryInstallWriter(winningWriter));
            };

            try
            {
                LoggerInitializationResult result = LoggerBootstrap.Initialize(settings);

                Assert.AreEqual(
                    LoggerInitializationStatus.ExistingProcessWriterNotOwned,
                    result.Status);
                Assert.IsFalse(result.IsInitialized);
                Assert.IsFalse(result.ProcessWriterInstalled);
                Assert.AreSame(winningWriter, LogRuntime.Writer);
                Assert.IsFalse(CLogger.TryGetInstance(out _));
                Assert.IsFalse(LoggerBootstrap.TryGetOwnedLogger(out _));
                Assert.AreEqual(0, winningWriter.DisposeCount);
            }
            finally
            {
                LoggerBootstrap.BeforeProcessWriterInstallTestHook = null;
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }
#endif

        [Test]
        public void LoggerBootstrap_ShutdownPreservesWriterThatReplacedOwnedProcessWriter()
        {
            LoggerSettings settings = CreateSettings();
            var foreignWriter = new RecordingWriter();

            try
            {
                LoggerInitializationResult initialization = LoggerBootstrap.Initialize(settings);
                Assert.IsTrue(initialization.ProcessWriterInstalled);

                ILogWriter ownedWriter = LogRuntime.ReplaceWriter(foreignWriter);
                Assert.IsInstanceOf<CLogger>(ownedWriter);

                LoggerInitializationResult repeated = LoggerBootstrap.Initialize(settings);
                Assert.AreEqual(LoggerInitializationStatus.AlreadyInitialized, repeated.Status);
                Assert.IsFalse(repeated.ProcessWriterInstalled);

                LoggerShutdownResult shutdown = LoggerBootstrap.Shutdown();
                Assert.IsTrue(shutdown.IsComplete);
                Assert.AreSame(foreignWriter, LogRuntime.Writer);
                Assert.IsFalse(CLogger.TryGetInstance(out _));
                Assert.AreEqual(0, foreignWriter.DisposeCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        private static LoggerSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<LoggerSettings>();
            settings.processing = LoggerSettings.ProcessingMode.ForceSingleThread;
            settings.registerUnityLogger = true;
            settings.registerConsoleLogger = false;
            settings.registerFileLogger = false;
            return settings;
        }

        private sealed class RecordingSink : ILogger
        {
            public LogLevel Level { get; private set; }
            public string Category { get; private set; }
            public string Message { get; private set; }

            public void Log(LogMessage logMessage)
            {
                Level = logMessage.Level;
                Category = logMessage.Category;
                var builder = new StringBuilder();
                logMessage.AppendMessageTo(builder);
                Message = builder.ToString();
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingWriter : ILogWriter, IDisposable
        {
            public int DisposeCount { get; private set; }

            public bool IsEnabled(LogSeverity severity, string category)
            {
                return true;
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
