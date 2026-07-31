using System.Threading;
using CycloneGames.Logger.Editor;
using CycloneGames.Logging;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class LoggerEditorLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            LoggerUpdater.CaptureMainThreadForLifecycle();
            CLogger.Shutdown(LogFlushMode.Buffered);
            LoggerUpdater.ResetForTests();
            LoggerEditorBootstrap.ResetLifecycleStateForTests();
        }

        [TearDown]
        public void TearDown()
        {
            LoggerEditorBootstrap.ResetLifecycleStateForTests();
            LoggerBootstrap.Shutdown(LogFlushMode.Buffered);
            CLogger.Shutdown(LogFlushMode.Buffered);
            LoggerUpdater.ResetForTests();
        }

        [Test]
        public void ApplicationQuittingBeforeExitingPlayMode_ConvergesAndEnteredEditCanInitializeFreshOwner()
        {
            LoggerSettings settings = CreateSettings();
            try
            {
                Assert.IsTrue(LoggerBootstrap.Initialize(settings).IsInitialized);

                LoggerUpdater.ProcessApplicationQuittingForTests();
                LoggerEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.ExitingPlayMode);
                LoggerEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.EnteredEditMode);

                Assert.IsTrue(LoggerBootstrap.Initialize(settings).IsInitialized);
            }
            finally
            {
                LoggerBootstrap.Shutdown(LogFlushMode.Buffered);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ExitingPlayModeBeforeApplicationQuitting_ConvergesAndEnteredEditCanInitializeFreshOwner()
        {
            LoggerSettings settings = CreateSettings();
            try
            {
                Assert.IsTrue(LoggerBootstrap.Initialize(settings).IsInitialized);

                LoggerEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.ExitingPlayMode);
                LoggerUpdater.ProcessApplicationQuittingForTests();
                LoggerEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.EnteredEditMode);

                Assert.IsTrue(LoggerBootstrap.Initialize(settings).IsInitialized);
            }
            finally
            {
                LoggerBootstrap.Shutdown(LogFlushMode.Buffered);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void EditorExitCallbacks_PreserveForeignGlobalOwnerAndDispatchAffinity()
        {
            Assert.IsTrue(CLogger.ConfigureSingleThreadedProcessing(CreateProcessingOptions()));
            CLogger foreign = CLogger.Instance;
            var sink = new CountingSink();
            Assert.IsTrue(foreign.AddLogger(sink));

            LoggerEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.ExitingEditMode);
            LoggerUpdater.ProcessApplicationQuittingForTests();

            Assert.IsTrue(CLogger.TryGetInstance(out CLogger current));
            Assert.AreSame(foreign, current);
            Assert.AreEqual(0, sink.DisposeCount);
            foreign.Write(LogSeverity.Info, "foreign-owner", filePath: string.Empty, memberName: string.Empty);
            Assert.AreEqual(0, sink.LogCount, "The package host must not pump a foreign single-threaded logger.");
            LoggerUpdater.PumpOnce();
            Assert.AreEqual(0, sink.LogCount, "Unity lifecycle pumping must preserve foreign dispatch affinity.");
            foreign.Pump(1);
            Assert.AreEqual(1, sink.LogCount);
        }

        private static LoggerSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<LoggerSettings>();
            settings.processing = LoggerSettings.ProcessingMode.ForceSingleThread;
            settings.maxQueuedMessages = 8;
            settings.maxQueuedCharacters = 1024;
            settings.maxMessageCharacters = 128;
            settings.maxCategoryCharacters = 32;
            settings.maxSourcePathCharacters = 32;
            settings.maxMemberNameCharacters = 32;
            settings.reservedCriticalMessages = 0;
            settings.reservedCriticalCharacters = 0;
            settings.unityConsoleMaxQueuedMessages = 8;
            settings.unityConsoleMaxQueuedCharacters = 1024;
            settings.shutdownDrainTimeoutMs = 1000;
            settings.registerUnityLogger = true;
            settings.registerConsoleLogger = false;
            settings.registerFileLogger = false;
            return settings;
        }

        private static LoggerProcessingOptions CreateProcessingOptions()
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
                ShutdownDrainTimeoutMs = 1000
            };
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
