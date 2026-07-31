using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class LoggerBootstrapRecoveryTests
    {
        [SetUp]
        public void SetUp()
        {
            LoggerUpdater.CaptureMainThreadForLifecycle();
            CLogger.Shutdown(LogFlushMode.Buffered);
            LoggerUpdater.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            LoggerBootstrap.Shutdown(LogFlushMode.Buffered);
            CLogger.Shutdown(LogFlushMode.Buffered);
            LoggerUpdater.ResetForTests();
        }

        [Test]
        [Timeout(5000)]
        public void Initialize_AfterTimedOutOwnedShutdown_ReportsFailureUntilReinitializeCompletesRetry()
        {
            LoggerSettings settings = CreateSettings();
            var blocker = new BlockingDisposeSink();
            try
            {
                LoggerInitializationResult initialized = LoggerBootstrap.Initialize(settings);
                Assert.IsTrue(initialized.IsInitialized);
                Assert.IsTrue(CLogger.Instance.AddLogger(blocker));

                LoggerShutdownResult timedOut = LoggerBootstrap.Shutdown(LogFlushMode.Buffered);

                Assert.AreEqual(LoggerShutdownStatus.TimedOut, timedOut.Status);
                Assert.IsTrue(blocker.DisposeEntered.Wait(1000));
                LoggerInitializationResult blockedInitialization = LoggerBootstrap.Initialize(settings);
                Assert.AreEqual(LoggerInitializationStatus.ShutdownFailed, blockedInitialization.Status);
                Assert.IsFalse(blockedInitialization.IsInitialized);

                blocker.DisposeRelease.Set();
                Assert.IsTrue(blocker.DisposeExited.Wait(1000));
                LoggerReinitializationResult recovered = LoggerBootstrap.Reinitialize(settings);

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
            settings.shutdownDrainTimeoutMs = 25;
            settings.registerUnityLogger = true;
            settings.registerConsoleLogger = false;
            settings.registerFileLogger = false;
            return settings;
        }

        private sealed class BlockingDisposeSink : ILogger
        {
            internal readonly ManualResetEventSlim DisposeEntered = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim DisposeRelease = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim DisposeExited = new ManualResetEventSlim(false);

            public void Log(LogMessage logMessage)
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
