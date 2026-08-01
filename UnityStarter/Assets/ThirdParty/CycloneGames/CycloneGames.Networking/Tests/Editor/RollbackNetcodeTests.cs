using System;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.Networking.Lockstep;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class RollbackNetcodeTests
    {
        [Test]
        public void ReceiveRemoteInput_Misprediction_Resimulates_With_Confirmed_Input()
        {
            var simulation = new TestRollbackSimulation();
            var rollback = new RollbackNetcode<TestInput, TestState>(
                peerCount: 2,
                localPeerId: 0,
                simulation: simulation,
                maxRollbackFrames: 8,
                tickRate: 60);

            rollback.AdvanceFrame(new TestInput { Value = 1 });
            rollback.ReceiveRemoteInput(1, 0, new TestInput { Value = 5 });

            Assert.AreEqual(1, rollback.RollbackCount);
            Assert.AreEqual(6, simulation.State.Value);
        }

        [Test]
        public void RingBuffer_Reuse_Does_Not_Reapply_Stale_Confirmed_Input()
        {
            var simulation = new TestRollbackSimulation();
            var rollback = new RollbackNetcode<TestInput, TestState>(2, 0, simulation, maxRollbackFrames: 8);

            rollback.ReceiveRemoteInput(1, 0, new TestInput { Value = 5 });
            for (int frame = 0; frame <= 32; frame++)
                rollback.AdvanceFrame(new TestInput { Value = 1 });

            Assert.AreEqual(38, simulation.State.Value);
        }

        [Test]
        public void LastConfirmedFrame_Advances_Only_Across_Contiguous_Frames()
        {
            var simulation = new TestRollbackSimulation();
            var rollback = new RollbackNetcode<TestInput, TestState>(2, 0, simulation, maxRollbackFrames: 8);

            rollback.ReceiveRemoteInput(1, 2, default);
            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);
            Assert.AreEqual(-1, rollback.LastConfirmedFrame);

            rollback.ReceiveRemoteInput(1, 0, default);
            Assert.AreEqual(0, rollback.LastConfirmedFrame);

            rollback.ReceiveRemoteInput(1, 1, default);
            Assert.AreEqual(2, rollback.LastConfirmedFrame);
        }

        [Test]
        public void Future_Confirmed_Input_Does_Not_Leak_Into_Earlier_Prediction()
        {
            var simulation = new TestRollbackSimulation(repeatLastKnownInput: true);
            var rollback = new RollbackNetcode<TestInput, TestState>(2, 0, simulation, maxRollbackFrames: 8);

            rollback.ReceiveRemoteInput(1, 2, new TestInput { Value = 9 });
            rollback.AdvanceFrame(default);

            Assert.AreEqual(0, simulation.State.Value);
        }

        [Test]
        public void ReceiveRemoteInput_Correction_Propagates_Through_Subsequent_Predictions()
        {
            var simulation = new TestRollbackSimulation(repeatLastKnownInput: true);
            var rollback = new RollbackNetcode<TestInput, TestState>(2, 0, simulation, maxRollbackFrames: 8);

            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);

            rollback.ReceiveRemoteInput(1, 0, new TestInput { Value = 5 });

            Assert.AreEqual(1, rollback.RollbackCount);
            Assert.AreEqual(15, simulation.State.Value);

            rollback.AdvanceFrame(default);
            Assert.AreEqual(20, simulation.State.Value);
        }

        [Test]
        public void ReceiveRemoteInput_TooOldAlias_DoesNotOverwriteFutureInput()
        {
            var simulation = new TestRollbackSimulation();
            var rollback = new RollbackNetcode<TestInput, TestState>(2, 0, simulation, maxRollbackFrames: 8);

            for (int frame = 0; frame < 100; frame++)
                rollback.AdvanceFrame(default);

            // With maxRollbackFrames=8 the ring has 32 slots. Frames 88 and 120
            // alias the same slot; the stale frame must not replace the future one.
            rollback.ReceiveRemoteInput(1, 120, new TestInput { Value = 7 });
            rollback.ReceiveRemoteInput(1, 88, new TestInput { Value = 99 });

            for (int frame = 100; frame <= 120; frame++)
                rollback.AdvanceFrame(default);

            Assert.AreEqual(7, simulation.State.Value);
        }

        [Test]
        public void ReceiveRemoteInput_FutureAlias_DoesNotOverwriteRetainedRollbackHistory()
        {
            var simulation = new TestRollbackSimulation();
            var rollback = new RollbackNetcode<TestInput, TestState>(2, 0, simulation, maxRollbackFrames: 8);

            for (int frame = 0; frame < 100; frame++)
                rollback.AdvanceFrame(default);

            // Frame 131 aliases retained frame 99 and must be rejected until that
            // history slot is outside the rollback window.
            rollback.ReceiveRemoteInput(1, 131, new TestInput { Value = 13 });
            rollback.ReceiveRemoteInput(1, 99, new TestInput { Value = 5 });

            Assert.AreEqual(1, rollback.RollbackCount);
            Assert.AreEqual(5, simulation.State.Value);
        }

        [Test]
        public void Constructor_RejectsRollbackBufferSizeThatCannotBeRepresented()
        {
            var simulation = new TestRollbackSimulation();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RollbackNetcode<TestInput, TestState>(
                    2,
                    0,
                    simulation,
                    maxRollbackFrames: 300_000_000));
        }

        [Test]
        public void FrameAdvantage_CountsOnlySimulatedUnconfirmedFrames()
        {
            var simulation = new TestRollbackSimulation();
            var rollback = new RollbackNetcode<TestInput, TestState>(
                2,
                0,
                simulation,
                maxRollbackFrames: 1);

            Assert.AreEqual(0, rollback.FrameAdvantage);
            Assert.IsFalse(rollback.ShouldStall());

            rollback.AdvanceFrame(default);

            Assert.AreEqual(1, rollback.FrameAdvantage);
            Assert.IsTrue(rollback.ShouldStall());

            rollback.ReceiveRemoteInput(1, 0, default);

            Assert.AreEqual(0, rollback.FrameAdvantage);
            Assert.IsFalse(rollback.ShouldStall());
        }

        [Test]
        public void ReceiveRemoteInput_BeyondRollbackWindow_ReportsThroughCoreDiagnosticsPort()
        {
            var simulation = new TestRollbackSimulation();
            var diagnostics = new RecordingNetworkingDiagnostics();
            var rollback = new RollbackNetcode<TestInput, TestState>(
                peerCount: 2,
                localPeerId: 0,
                simulation: simulation,
                diagnostics: diagnostics,
                maxRollbackFrames: 2,
                tickRate: 60);

            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);
            rollback.ReceiveRemoteInput(1, 0, new TestInput { Value = 5 });

            Assert.AreEqual(1, diagnostics.WriteCount);
            Assert.AreEqual(NetworkingDiagnosticLevel.Warning, diagnostics.LastLevel);
            Assert.AreEqual(NetworkingDiagnosticCategories.Root, diagnostics.LastCategory);
            StringAssert.Contains("Rollback depth 3 exceeds max 2", diagnostics.LastMessage);
            Assert.AreEqual(0, rollback.RollbackCount);
        }

        [TestCase(DiagnosticFailureMode.IsEnabled)]
        [TestCase(DiagnosticFailureMode.Write)]
        public void ReceiveRemoteInput_BeyondRollbackWindow_IgnoresOrdinaryDiagnosticsFailure(
            DiagnosticFailureMode failureMode)
        {
            var simulation = new TestRollbackSimulation();
            var rollback = new RollbackNetcode<TestInput, TestState>(
                peerCount: 2,
                localPeerId: 0,
                simulation: simulation,
                diagnostics: new ThrowingNetworkingDiagnostics(
                    failureMode,
                    new InvalidOperationException("sink failed")),
                maxRollbackFrames: 2,
                tickRate: 60);

            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);

            Assert.DoesNotThrow(() => rollback.ReceiveRemoteInput(1, 0, new TestInput { Value = 5 }));
            Assert.AreEqual(3, rollback.CurrentFrame);
            Assert.AreEqual(0, rollback.RollbackCount);
        }

        [Test]
        public void ReceiveRemoteInput_DiagnosticsOutOfMemory_RemainsVisibleToHost()
        {
            var simulation = new TestRollbackSimulation();
            var rollback = new RollbackNetcode<TestInput, TestState>(
                peerCount: 2,
                localPeerId: 0,
                simulation: simulation,
                diagnostics: new ThrowingNetworkingDiagnostics(
                    DiagnosticFailureMode.IsEnabled,
                    new OutOfMemoryException("diagnostics allocation failed")),
                maxRollbackFrames: 2,
                tickRate: 60);

            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);
            rollback.AdvanceFrame(default);

            Assert.Throws<OutOfMemoryException>(() =>
                rollback.ReceiveRemoteInput(1, 0, new TestInput { Value = 5 }));
        }

        [Test]
        public void LogWriterAdapter_MapsAllOutputLevelsExactly()
        {
            ProbeLogWriter writer = new ProbeLogWriter();
            NetworkingLogWriterAdapter diagnostics = new NetworkingLogWriterAdapter(writer);
            NetworkingDiagnosticLevel[] levels =
            {
                NetworkingDiagnosticLevel.Trace,
                NetworkingDiagnosticLevel.Debug,
                NetworkingDiagnosticLevel.Info,
                NetworkingDiagnosticLevel.Warning,
                NetworkingDiagnosticLevel.Error,
                NetworkingDiagnosticLevel.Fatal
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
                Assert.IsTrue(diagnostics.IsEnabled(levels[i], NetworkingDiagnosticCategories.Root));
                Assert.AreEqual(severities[i], writer.LastSeverity);
            }

            Assert.AreEqual((byte)LogSeverity.None, (byte)NetworkingDiagnosticLevel.None);
        }

        [Test]
        public void LogWriterAdapter_NoneAndUnknownLevelsNeverReachWriter()
        {
            ProbeLogWriter writer = new ProbeLogWriter();
            NetworkingLogWriterAdapter diagnostics = new NetworkingLogWriterAdapter(writer);
            NetworkingDiagnosticLevel[] invalidLevels =
            {
                NetworkingDiagnosticLevel.None,
                (NetworkingDiagnosticLevel)byte.MaxValue
            };

            for (int i = 0; i < invalidLevels.Length; i++)
            {
                NetworkingDiagnosticLevel level = invalidLevels[i];
                Assert.IsFalse(diagnostics.IsEnabled(level, NetworkingDiagnosticCategories.Root));
                Assert.DoesNotThrow(() => diagnostics.Write(level, NetworkingDiagnosticCategories.Root, "ignored"));
                Assert.DoesNotThrow(() => diagnostics.WriteException(
                    level,
                    NetworkingDiagnosticCategories.Root,
                    new InvalidOperationException("ignored")));
            }

            Assert.AreEqual(0, writer.CallCount);
        }

        [Test]
        public void LogWriterAdapter_OrdinaryWriterFailuresAreContained()
        {
            ProbeLogWriter writer = new ProbeLogWriter(new InvalidOperationException("writer failed"));
            NetworkingLogWriterAdapter diagnostics = new NetworkingLogWriterAdapter(writer);

            Assert.IsFalse(diagnostics.IsEnabled(
                NetworkingDiagnosticLevel.Warning,
                NetworkingDiagnosticCategories.Root));
            Assert.DoesNotThrow(() => diagnostics.Write(
                NetworkingDiagnosticLevel.Warning,
                NetworkingDiagnosticCategories.Root,
                "ignored"));
            Assert.DoesNotThrow(() => diagnostics.WriteException(
                NetworkingDiagnosticLevel.Error,
                NetworkingDiagnosticCategories.Root,
                new InvalidOperationException("source")));
        }

        [Test]
        public void LogWriterAdapter_WriterOutOfMemory_RemainsVisibleToHost()
        {
            ProbeLogWriter writer = new ProbeLogWriter(new OutOfMemoryException("writer allocation failed"));
            NetworkingLogWriterAdapter diagnostics = new NetworkingLogWriterAdapter(writer);

            Assert.Throws<OutOfMemoryException>(() => diagnostics.IsEnabled(
                NetworkingDiagnosticLevel.Warning,
                NetworkingDiagnosticCategories.Root));
        }

        private struct TestInput : IEquatable<TestInput>
        {
            public int Value;

            public bool Equals(TestInput other) => Value == other.Value;
        }

        private struct TestState
        {
            public int Value;
        }

        private sealed class TestRollbackSimulation : RollbackNetcode<TestInput, TestState>.IRollbackSimulation
        {
            private readonly bool _repeatLastKnownInput;

            public TestState State;

            public TestRollbackSimulation(bool repeatLastKnownInput = false)
            {
                _repeatLastKnownInput = repeatLastKnownInput;
            }

            public TestInput PredictInput(int peerId, TestInput lastKnownInput)
            {
                return _repeatLastKnownInput ? lastKnownInput : default;
            }

            public TestState SaveState()
            {
                return State;
            }

            public void LoadState(in TestState state)
            {
                State = state;
            }

            public void Simulate(ReadOnlySpan<TestInput> peerInputs)
            {
                for (int i = 0; i < peerInputs.Length; i++)
                    State.Value += peerInputs[i].Value;
            }

            public void OnRollback(int framesToRollback)
            {
            }
        }

        private sealed class RecordingNetworkingDiagnostics : INetworkingDiagnostics
        {
            public int WriteCount { get; private set; }
            public NetworkingDiagnosticLevel LastLevel { get; private set; }
            public string LastCategory { get; private set; }
            public string LastMessage { get; private set; }

            public bool IsEnabled(NetworkingDiagnosticLevel level, string category) => true;

            public void Write(
                NetworkingDiagnosticLevel level,
                string category,
                string message,
                string filePath,
                int lineNumber,
                string memberName)
            {
                WriteCount++;
                LastLevel = level;
                LastCategory = category;
                LastMessage = message;
            }

            public void WriteException(
                NetworkingDiagnosticLevel level,
                string category,
                Exception exception,
                string message,
                string filePath,
                int lineNumber,
                string memberName)
            {
                Write(level, category, message, filePath, lineNumber, memberName);
            }
        }

        public enum DiagnosticFailureMode
        {
            IsEnabled,
            Write
        }

        private sealed class ThrowingNetworkingDiagnostics : INetworkingDiagnostics
        {
            private readonly DiagnosticFailureMode _failureMode;
            private readonly Exception _exception;

            public ThrowingNetworkingDiagnostics(DiagnosticFailureMode failureMode, Exception exception)
            {
                _failureMode = failureMode;
                _exception = exception;
            }

            public bool IsEnabled(NetworkingDiagnosticLevel level, string category)
            {
                if (_failureMode == DiagnosticFailureMode.IsEnabled)
                {
                    throw _exception;
                }

                return true;
            }

            public void Write(
                NetworkingDiagnosticLevel level,
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
                NetworkingDiagnosticLevel level,
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
