using System.Threading;
using CycloneGames.Logging.Pipeline;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Logging.Unity.Tests.Editor
{
    public sealed class LoggingBootstrapRecoveryTests
    {
        [SetUp]
        public void SetUp()
        {
            LoggingRuntimeHost.CaptureMainThreadForLifecycle();
            LoggingRuntimeHost.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            LoggingBootstrap.Shutdown(LogFlushMode.Buffered);
            LoggingRuntimeHost.ResetForTests();
        }

        [Test]
        [Timeout(5000)]
        public void Initialize_AfterTimedOutOwnedShutdown_ReportsFailureUntilReinitializeCompletesRetry()
        {
            LoggingSettings settings = CreateSettings();
            var blocker = new BlockingDisposeSink();
            try
            {
                LoggingInitializationResult initialized = LoggingBootstrap.Initialize(settings);
                Assert.IsTrue(initialized.IsInitialized);
                Assert.IsTrue(LoggingBootstrap.TryGetOwnedPipeline(out LogPipeline pipeline));
                Assert.IsTrue(pipeline.RegisterSink(blocker).IsRegistered);

                LogPipelineShutdownResult timedOut = LoggingBootstrap.Shutdown(LogFlushMode.Buffered);

                Assert.AreEqual(LogPipelineShutdownStatus.TimedOut, timedOut.Status);
                Assert.IsTrue(blocker.DisposeEntered.Wait(1000));
                LoggingInitializationResult blockedInitialization = LoggingBootstrap.Initialize(settings);
                Assert.AreEqual(LoggingInitializationStatus.ShutdownFailed, blockedInitialization.Status);
                Assert.IsFalse(blockedInitialization.IsInitialized);

                blocker.DisposeRelease.Set();
                Assert.IsTrue(blocker.DisposeExited.Wait(1000));
                LoggingReinitializationResult recovered = LoggingBootstrap.Reinitialize(settings);

                Assert.IsTrue(recovered.Shutdown.IsComplete);
                Assert.IsTrue(recovered.Initialization.IsInitialized);
                Assert.IsTrue(recovered.Succeeded);
            }
            finally
            {
                blocker.DisposeRelease.Set();
                if (blocker.DisposeEntered.IsSet)
                {
                    blocker.DisposeExited.Wait(1000);
                }

                Object.DestroyImmediate(settings);
                blocker.DisposeEvents();
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
            settings.shutdownDrainTimeoutMs = 25;
            settings.registerUnityConsoleLogSink = true;
            settings.registerConsoleLogSink = false;
            settings.registerFileLogSink = false;
            return settings;
        }

        private sealed class BlockingDisposeSink : ILogSink
        {
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim DisposeRelease = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim DisposeExited = new ManualResetEventSlim(false);

            public void Emit(LogEvent logEvent)
            {
            }

            public void Dispose()
            {
                DisposeEntered.Set();
                DisposeRelease.Wait();
                DisposeExited.Set();
            }

            internal void DisposeEvents()
            {
                DisposeEntered.Dispose();
                DisposeRelease.Dispose();
                DisposeExited.Dispose();
            }
        }
    }
}
