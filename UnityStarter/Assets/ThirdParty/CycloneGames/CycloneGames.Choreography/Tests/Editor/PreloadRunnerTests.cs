using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.Choreography.Core;
using NUnit.Framework;

namespace CycloneGames.Choreography.Tests
{
    [TestFixture]
    public sealed class PreloadRunnerTests
    {
        private static List<ChoreographyResourceReference> Refs(params ChoreographyResourceReference[] items)
        {
            return new List<ChoreographyResourceReference>(items);
        }

        [Test]
        public void Preload_CompletesWhenAllHandlesSucceed()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference r1 = new ChoreographyResourceReference("r1", ChoreographyResourceKind.AudioEvent);
            ChoreographyResourceReference r2 = new ChoreographyResourceReference("r2", ChoreographyResourceKind.Vfx);

            PreloadRunner runner = new PreloadRunner(provider);
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(r1, r2), PreloadOptions.Default);
            runner.Update();
            Assert.IsFalse(runner.IsDone, "Batch should still be loading until handles complete.");

            provider.Complete(r1, true);
            provider.Complete(r2, true);
            runner.Update();

            Assert.IsTrue(runner.IsDone);
            Assert.AreEqual(PreloadStatus.Completed, result.Status);
            Assert.AreEqual(2, result.SucceededCount);
            Assert.AreEqual(0, result.FailedCount);
            Assert.AreEqual(1f, runner.Progress, 0.0001f);
        }

        [Test]
        public void Preload_ContinuePolicyReportsFailuresButCompletes()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference r1 = new ChoreographyResourceReference("r1", ChoreographyResourceKind.AudioEvent);
            ChoreographyResourceReference r2 = new ChoreographyResourceReference("r2", ChoreographyResourceKind.Vfx);

            PreloadRunner runner = new PreloadRunner(provider);
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(r1, r2), new PreloadOptions(PreloadFailurePolicy.Continue));
            provider.Complete(r1, true);
            provider.Complete(r2, false, "missing");
            runner.Update();

            Assert.AreEqual(PreloadStatus.Completed, result.Status);
            Assert.AreEqual(1, result.SucceededCount);
            Assert.AreEqual(1, result.FailedCount);
        }

        [Test]
        public void Preload_AbortPolicyFailsFast()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference r1 = new ChoreographyResourceReference("r1", ChoreographyResourceKind.AudioEvent);
            ChoreographyResourceReference r2 = new ChoreographyResourceReference("r2", ChoreographyResourceKind.Vfx);

            PreloadRunner runner = new PreloadRunner(provider);
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(r1, r2), new PreloadOptions(PreloadFailurePolicy.Abort));
            provider.Complete(r2, false, "missing");
            runner.Update();

            Assert.AreEqual(PreloadStatus.Failed, result.Status);
            Assert.IsTrue(runner.IsDone);
        }

        [TestCase(DiagnosticFailureMode.IsEnabled)]
        [TestCase(DiagnosticFailureMode.Write)]
        public void Preload_AbortPolicy_IgnoresOrdinaryDiagnosticsFailure(DiagnosticFailureMode failureMode)
        {
            ChoreographyResourceReference reference = new ChoreographyResourceReference(
                "missing",
                ChoreographyResourceKind.Vfx);
            PreloadRunner runner = new PreloadRunner(
                new NullResourceProvider(),
                new ThrowingChoreographyDiagnostics(failureMode, new InvalidOperationException("sink failed")));

            Assert.DoesNotThrow(() =>
                runner.Begin(Refs(reference), new PreloadOptions(PreloadFailurePolicy.Abort)));
            Assert.AreEqual(PreloadStatus.Failed, runner.Status);
            Assert.IsTrue(runner.IsDone);
        }

        [Test]
        public void Preload_DiagnosticsOutOfMemory_RemainsVisibleToHost()
        {
            ChoreographyResourceReference reference = new ChoreographyResourceReference(
                "missing",
                ChoreographyResourceKind.Vfx);
            PreloadRunner runner = new PreloadRunner(
                new NullResourceProvider(),
                new ThrowingChoreographyDiagnostics(
                    DiagnosticFailureMode.IsEnabled,
                    new OutOfMemoryException("diagnostics allocation failed")));

            Assert.Throws<OutOfMemoryException>(() =>
                runner.Begin(Refs(reference), new PreloadOptions(PreloadFailurePolicy.Abort)));
        }

        [Test]
        public void LoggingDiagnostics_MapsAllOutputLevelsExactly()
        {
            ProbeLogWriter writer = new ProbeLogWriter();
            ChoreographyLoggingDiagnostics diagnostics = new ChoreographyLoggingDiagnostics(writer);
            ChoreographyDiagnosticLevel[] levels =
            {
                ChoreographyDiagnosticLevel.Trace,
                ChoreographyDiagnosticLevel.Debug,
                ChoreographyDiagnosticLevel.Info,
                ChoreographyDiagnosticLevel.Warning,
                ChoreographyDiagnosticLevel.Error,
                ChoreographyDiagnosticLevel.Fatal
            };
            LogSeverity[] severities =
            {
                LogSeverity.Trace,
                LogSeverity.Debug,
                LogSeverity.Info,
                LogSeverity.Warning,
                LogSeverity.Error,
                LogSeverity.Fatal
            };

            for (int i = 0; i < levels.Length; i++)
            {
                Assert.AreEqual((byte)severities[i], (byte)levels[i]);
                Assert.IsTrue(diagnostics.IsEnabled(levels[i], ChoreographyDiagnosticCategories.Root));
                Assert.AreEqual(severities[i], writer.LastSeverity);
            }

            Assert.AreEqual((byte)LogSeverity.None, (byte)ChoreographyDiagnosticLevel.None);
        }

        [Test]
        public void LoggingDiagnostics_NoneAndUnknownLevelsNeverReachWriter()
        {
            ProbeLogWriter writer = new ProbeLogWriter();
            ChoreographyLoggingDiagnostics diagnostics = new ChoreographyLoggingDiagnostics(writer);
            ChoreographyDiagnosticLevel[] invalidLevels =
            {
                ChoreographyDiagnosticLevel.None,
                (ChoreographyDiagnosticLevel)byte.MaxValue
            };

            for (int i = 0; i < invalidLevels.Length; i++)
            {
                ChoreographyDiagnosticLevel level = invalidLevels[i];
                Assert.IsFalse(diagnostics.IsEnabled(level, ChoreographyDiagnosticCategories.Root));
                Assert.DoesNotThrow(() => diagnostics.Write(level, ChoreographyDiagnosticCategories.Root, "ignored"));
                Assert.DoesNotThrow(() => diagnostics.WriteException(
                    level,
                    ChoreographyDiagnosticCategories.Root,
                    new InvalidOperationException("ignored")));
            }

            Assert.AreEqual(0, writer.CallCount);
        }

        [Test]
        public void LoggingDiagnostics_OrdinaryWriterFailuresAreContained()
        {
            ProbeLogWriter writer = new ProbeLogWriter(new InvalidOperationException("writer failed"));
            ChoreographyLoggingDiagnostics diagnostics = new ChoreographyLoggingDiagnostics(writer);

            Assert.IsFalse(diagnostics.IsEnabled(
                ChoreographyDiagnosticLevel.Warning,
                ChoreographyDiagnosticCategories.Root));
            Assert.DoesNotThrow(() => diagnostics.Write(
                ChoreographyDiagnosticLevel.Warning,
                ChoreographyDiagnosticCategories.Root,
                "ignored"));
            Assert.DoesNotThrow(() => diagnostics.WriteException(
                ChoreographyDiagnosticLevel.Error,
                ChoreographyDiagnosticCategories.Root,
                new InvalidOperationException("source")));
        }

        [Test]
        public void LoggingDiagnostics_WriterOutOfMemory_RemainsVisibleToHost()
        {
            ProbeLogWriter writer = new ProbeLogWriter(new OutOfMemoryException("writer allocation failed"));
            ChoreographyLoggingDiagnostics diagnostics = new ChoreographyLoggingDiagnostics(writer);

            Assert.Throws<OutOfMemoryException>(() => diagnostics.IsEnabled(
                ChoreographyDiagnosticLevel.Warning,
                ChoreographyDiagnosticCategories.Root));
        }

        [Test]
        public void Preload_EmptyBatchCompletesImmediately()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            PreloadRunner runner = new PreloadRunner(provider);

            runner.Begin(new List<ChoreographyResourceReference>(), PreloadOptions.Default);

            Assert.AreEqual(PreloadStatus.Completed, runner.Status);
            Assert.AreEqual(1f, runner.Progress, 0.0001f);
        }

        [Test]
        public void Preload_DeduplicatesReferencesBeforeLoading()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference r1 = new ChoreographyResourceReference("r1", ChoreographyResourceKind.AudioEvent);
            ChoreographyResourceReference r2 = new ChoreographyResourceReference("r2", ChoreographyResourceKind.Vfx);
            PreloadRunner runner = new PreloadRunner(provider);
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(r1, r1, r2, r2), PreloadOptions.Default);
            provider.Complete(r1, true);
            provider.Complete(r2, true);
            runner.Update();

            Assert.AreEqual(2, provider.LoadCount);
            Assert.AreEqual(2, runner.TotalCount);
            Assert.AreEqual(2, result.TotalCount);
            Assert.AreEqual(2, result.SucceededCount);
        }

        [Test]
        public void Preload_NullProviderHandleReportsFailure()
        {
            ChoreographyResourceReference reference = new ChoreographyResourceReference("missing", ChoreographyResourceKind.Vfx);
            PreloadRunner runner = new PreloadRunner(new NullResourceProvider());
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(reference), PreloadOptions.Default);
            runner.Update();

            Assert.AreEqual(PreloadStatus.Completed, result.Status);
            Assert.AreEqual(1, result.FailedCount);
            Assert.AreEqual(0, result.SucceededCount);
        }

        [Test]
        public void Preload_BeginsFromAssetWithoutCallerOwnedList()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference resource = new ChoreographyResourceReference("shared", ChoreographyResourceKind.Animation);
            ChoreographySection section = TestFactory.Section(
                "s0",
                1d,
                new[]
                {
                    new ChoreographyTrack(
                        "body",
                        ChoreographyTrackKind.Animation,
                        new[]
                        {
                            new ChoreographyClip("a", resource, 0d, 0.5d),
                            new ChoreographyClip("b", resource, 0.5d, 0.5d)
                        })
                });
            TestChoreographyAsset asset = new TestChoreographyAsset("asset", section);
            PreloadRunner runner = new PreloadRunner(provider);

            runner.Begin(asset, PreloadOptions.Default);
            provider.Complete(resource, true);
            runner.Update();

            Assert.AreEqual(1, provider.LoadCount);
            Assert.AreEqual(1, runner.TotalCount);
            Assert.AreEqual(PreloadStatus.Completed, runner.Status);
        }

        [Test]
        public void ResourceReference_ProviderAndGroupParticipateInIdentity()
        {
            ChoreographyResourceReference left = new ChoreographyResourceReference(
                "Attack",
                ChoreographyResourceKind.BackendCue,
                provider: "CycloneGames.Audio",
                group: "Combat");
            ChoreographyResourceReference differentProvider = new ChoreographyResourceReference(
                "Attack",
                ChoreographyResourceKind.BackendCue,
                provider: "Wwise",
                group: "Combat");
            ChoreographyResourceReference differentGroup = new ChoreographyResourceReference(
                "Attack",
                ChoreographyResourceKind.BackendCue,
                provider: "CycloneGames.Audio",
                group: "UI");

            Assert.AreNotEqual(left, differentProvider);
            Assert.AreNotEqual(left, differentGroup);
        }

        public enum DiagnosticFailureMode
        {
            IsEnabled,
            Write
        }

        private sealed class ThrowingChoreographyDiagnostics : IChoreographyDiagnostics
        {
            private readonly DiagnosticFailureMode _failureMode;
            private readonly Exception _exception;

            public ThrowingChoreographyDiagnostics(DiagnosticFailureMode failureMode, Exception exception)
            {
                _failureMode = failureMode;
                _exception = exception;
            }

            public bool IsEnabled(ChoreographyDiagnosticLevel level, string category)
            {
                if (_failureMode == DiagnosticFailureMode.IsEnabled)
                {
                    throw _exception;
                }

                return true;
            }

            public void Write(
                ChoreographyDiagnosticLevel level,
                string category,
                string message,
                string filePath,
                int lineNumber,
                string memberName)
            {
                if (_failureMode == DiagnosticFailureMode.Write)
                {
                    throw _exception;
                }
            }

            public void WriteException(
                ChoreographyDiagnosticLevel level,
                string category,
                Exception exception,
                string message,
                string filePath,
                int lineNumber,
                string memberName)
            {
                throw _exception;
            }
        }

        private sealed class ProbeLogWriter : ILogWriter
        {
            private readonly Exception _exception;

            public ProbeLogWriter(Exception exception = null)
            {
                _exception = exception;
            }

            public int CallCount { get; private set; }
            public LogSeverity LastSeverity { get; private set; }

            public bool IsEnabled(LogSeverity severity, string category)
            {
                Record(severity);
                return true;
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath,
                int lineNumber,
                string memberName)
            {
                Record(severity);
            }

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath,
                int lineNumber,
                string memberName)
            {
                Record(severity);
            }

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath,
                int lineNumber,
                string memberName)
            {
                Record(severity);
            }

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message,
                string filePath,
                int lineNumber,
                string memberName)
            {
                Record(severity);
            }

            private void Record(LogSeverity severity)
            {
                CallCount++;
                LastSeverity = severity;
                if (_exception != null)
                {
                    throw _exception;
                }
            }
        }
    }
}
