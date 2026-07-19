using System;
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
    }
}
