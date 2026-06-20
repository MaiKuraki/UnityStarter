using System;
using CycloneGames.Networking.Simulation;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class DeterministicSimulationAdapterTests
    {
        private readonly struct CounterInput : IEquatable<CounterInput>
        {
            public readonly int Delta;

            public CounterInput(int delta)
            {
                Delta = delta;
            }

            public bool Equals(CounterInput other)
            {
                return Delta == other.Delta;
            }

            public override bool Equals(object obj)
            {
                return obj is CounterInput other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Delta;
            }
        }

        private readonly struct CounterState
        {
            public readonly int Value;

            public CounterState(int value)
            {
                Value = value;
            }
        }

        // Genre-agnostic deterministic simulation: new state = old state + sum of this frame's input deltas.
        private sealed class CounterSimulation : IDeterministicSimulation<CounterInput, CounterState>
        {
            public CounterState Simulate(in CounterState state, ReadOnlySpan<CounterInput> inputs)
            {
                int sum = state.Value;
                for (int i = 0; i < inputs.Length; i++)
                {
                    sum += inputs[i].Delta;
                }

                return new CounterState(sum);
            }

            public bool StatesEqual(in CounterState a, in CounterState b)
            {
                return a.Value == b.Value;
            }
        }

        [Test]
        public void LockstepAdapter_Advances_State_With_Peer_Inputs()
        {
            var adapter = new DeterministicLockstepAdapter<CounterInput, CounterState>(new CounterSimulation(), new CounterState(0));

            adapter.SimulateFrame(0, new[] { new CounterInput(3), new CounterInput(4) });
            Assert.AreEqual(7, adapter.State.Value);

            adapter.SimulateFrame(1, new[] { new CounterInput(10) });
            Assert.AreEqual(17, adapter.State.Value);
        }

        [Test]
        public void RollbackAdapter_Saves_And_Restores_State()
        {
            var adapter = new DeterministicRollbackAdapter<CounterInput, CounterState>(new CounterSimulation(), new CounterState(0));

            adapter.Simulate(new[] { new CounterInput(5) });
            Assert.AreEqual(5, adapter.State.Value);

            CounterState snapshot = adapter.SaveState();
            adapter.Simulate(new[] { new CounterInput(100) });
            Assert.AreEqual(105, adapter.State.Value);

            adapter.LoadState(snapshot);
            Assert.AreEqual(5, adapter.State.Value);
        }

        [Test]
        public void RollbackAdapter_Default_Input_Prediction_Repeats_Last_Known()
        {
            var adapter = new DeterministicRollbackAdapter<CounterInput, CounterState>(new CounterSimulation());

            CounterInput predicted = adapter.PredictInput(peerId: 1, new CounterInput(9));

            Assert.AreEqual(9, predicted.Delta);
        }

        [Test]
        public void PredictionAdapter_Simulates_Captures_And_Compares()
        {
            var adapter = new DeterministicPredictionAdapter<CounterInput, CounterState>(
                new CounterSimulation(),
                () => new CounterInput(2),
                new CounterState(0));

            Assert.AreEqual(2, adapter.CaptureInput().Delta);

            adapter.SimulateStep(new CounterInput(5), 0.016f);
            Assert.AreEqual(5, adapter.State.Value);
            Assert.AreEqual(5, adapter.CaptureState().Value);

            adapter.ApplyState(new CounterState(50));
            Assert.AreEqual(50, adapter.State.Value);

            Assert.IsTrue(adapter.StatesMatch(new CounterState(7), new CounterState(7)));
            Assert.IsFalse(adapter.StatesMatch(new CounterState(7), new CounterState(8)));
        }

        [Test]
        public void Same_Simulation_Produces_Identical_State_Across_All_Topologies()
        {
            var simulation = new CounterSimulation();
            int[] sequence = { 3, -1, 7, 2, 5 };

            var lockstep = new DeterministicLockstepAdapter<CounterInput, CounterState>(simulation, new CounterState(0));
            var rollback = new DeterministicRollbackAdapter<CounterInput, CounterState>(simulation, new CounterState(0));
            var prediction = new DeterministicPredictionAdapter<CounterInput, CounterState>(
                simulation,
                () => default,
                new CounterState(0));

            for (int frame = 0; frame < sequence.Length; frame++)
            {
                var single = new[] { new CounterInput(sequence[frame]) };
                lockstep.SimulateFrame(frame, single);
                rollback.Simulate(single);
                prediction.SimulateStep(new CounterInput(sequence[frame]), 0.016f);
            }

            Assert.AreEqual(16, lockstep.State.Value);
            Assert.AreEqual(lockstep.State.Value, rollback.State.Value);
            Assert.AreEqual(lockstep.State.Value, prediction.State.Value);
        }
    }
}
