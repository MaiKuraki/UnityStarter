using System;
using CycloneGames.DeterministicMath;
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
            public TestState State;

            public TestInput PredictInput(int peerId, TestInput lastKnownInput)
            {
                return default;
            }

            public TestState SaveState()
            {
                return State;
            }

            public void LoadState(in TestState state)
            {
                State = state;
            }

            public void Simulate(ReadOnlySpan<TestInput> peerInputs, FPInt64 deltaTime)
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
