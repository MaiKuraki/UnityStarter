using System.Threading;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;
using CycloneGames.Logging.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Logging.Unity.Tests.Editor
{
    public sealed class LoggingEditorLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            LoggingRuntimeHost.CaptureMainThreadForLifecycle();
            LoggingRuntimeHost.ResetForTests();
            LoggingEditorBootstrap.ResetLifecycleStateForTests();
        }

        [TearDown]
        public void TearDown()
        {
            LoggingEditorBootstrap.ResetLifecycleStateForTests();
            LoggingBootstrap.Shutdown(LogFlushMode.Buffered);
            LoggingRuntimeHost.ResetForTests();
        }

        [Test]
        public void ApplicationQuittingBeforeExitingPlayMode_ConvergesAndEnteredEditCanInitializeFreshOwner()
        {
            LoggingSettings settings = CreateSettings();
            try
            {
                Assert.IsTrue(LoggingBootstrap.Initialize(settings).IsInitialized);

                LoggingRuntimeHost.ProcessApplicationQuittingForTests();
                LoggingEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.ExitingPlayMode);
                LoggingEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.EnteredEditMode);

                Assert.IsTrue(LoggingBootstrap.Initialize(settings).IsInitialized);
            }
            finally
            {
                LoggingBootstrap.Shutdown(LogFlushMode.Buffered);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ExitingPlayModeBeforeApplicationQuitting_ConvergesAndEnteredEditCanInitializeFreshOwner()
        {
            LoggingSettings settings = CreateSettings();
            try
            {
                Assert.IsTrue(LoggingBootstrap.Initialize(settings).IsInitialized);

                LoggingEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.ExitingPlayMode);
                LoggingRuntimeHost.ProcessApplicationQuittingForTests();
                LoggingEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.EnteredEditMode);

                Assert.IsTrue(LoggingBootstrap.Initialize(settings).IsInitialized);
            }
            finally
            {
                LoggingBootstrap.Shutdown(LogFlushMode.Buffered);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void EditorExitCallbacks_PreserveExternalPipelineOwnershipAndDispatchAffinity()
        {
            LogPipeline external = LogPipelineFactory.CreateSingleThreaded(CreateProcessingOptions());
            var sink = new CountingSink();
            try
            {
                Assert.IsTrue(external.RegisterSink(sink).IsRegistered);

                LoggingEditorBootstrap.ProcessPlayModeStateChangeForTests(PlayModeStateChange.ExitingEditMode);
                LoggingRuntimeHost.ProcessApplicationQuittingForTests();

                Assert.AreEqual(0, sink.DisposeCount);
                ((ILogWriter)external).Write(
                    LogSeverity.Info,
                    "CycloneGames.Logging.Unity.Tests.ExternalOwner",
                    "external-owner",
                    filePath: string.Empty,
                    memberName: string.Empty);
                Assert.AreEqual(0, sink.LogCount, "The package host must not pump an external single-threaded pipeline.");
                LoggingRuntimeHost.PumpOnce();
                Assert.AreEqual(0, sink.LogCount, "Unity lifecycle pumping must preserve external dispatch affinity.");
                external.Pump(1);
                Assert.AreEqual(1, sink.LogCount);
            }
            finally
            {
                external.Shutdown(LogFlushMode.Buffered, 2000);
            }
        }

        private static LoggingSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<LoggingSettings>();
            settings.executionMode = LoggingSettings.ExecutionMode.SingleThreaded;
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
            settings.registerUnityConsoleLogSink = true;
            settings.registerConsoleLogSink = false;
            settings.registerFileLogSink = false;
            return settings;
        }

        private static LogPipelineOptions CreateProcessingOptions()
        {
            return new LogPipelineOptions
            {
                MaxQueuedMessages = 8,
                MaxQueuedCharacters = 1024,
                MaxMessageCharacters = 128,
                MaxCategoryCharacters = 32,
                MaxSourcePathCharacters = 32,
                MaxMemberNameCharacters = 32,
                ReservedCriticalMessages = 0,
                ReservedCriticalCharacters = 0,
                ShutdownDrainTimeoutMs = 1000
            };
        }

        private sealed class CountingSink : ILogSink
        {
            internal int DisposeCount;
            internal int LogCount;

            public void Emit(LogEvent logEvent)
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
