using System;
using CycloneGames.Networking.Prediction;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class PredictionBufferTests
    {
        [Test]
        public void TryGet_ZeroTick_ReturnsFalse_AfterClear()
        {
            var buffer = new PredictionBuffer<int>(8);

            buffer.Set(NetworkTickId.Zero, 10);
            Assert.IsTrue(buffer.TryGet(NetworkTickId.Zero, out int value));
            Assert.AreEqual(10, value);

            buffer.Clear();

            Assert.IsFalse(buffer.TryGet(NetworkTickId.Zero, out _));
        }

        [Test]
        public void Invalidate_Makes_Existing_Tick_Unavailable()
        {
            var buffer = new PredictionBuffer<int>(8);
            var tick = new NetworkTickId(3);

            buffer.Set(tick, 123);
            buffer.Invalidate(tick);

            Assert.IsFalse(buffer.TryGet(tick, out _));
        }

        [Test]
        public void Constructor_Rejects_NonPositive_Or_Unrepresentable_Capacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionBuffer<int>(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionBuffer<int>((1 << 30) + 1));
        }

        [Test]
        public void GetRef_Rejects_Tick_After_RingSlot_Is_Reused()
        {
            var buffer = new PredictionBuffer<int>(2);
            var oldTick = NetworkTickId.Zero;
            buffer.Set(oldTick, 10);
            buffer.Set(new NetworkTickId(2), 20);

            Assert.Throws<InvalidOperationException>(() =>
            {
                ref int value = ref buffer.GetRef(oldTick);
                value++;
            });
        }

        [Test]
        public void ProcessServerState_Terminates_When_CurrentTick_Is_Int64MaxValue()
        {
            var target = new CountingPredictable();
            var prediction = new ClientPredictionSystem<int, int>(target, bufferSize: 8);
            var serverTick = new NetworkTickId(long.MaxValue - 1L);
            var currentTick = new NetworkTickId(long.MaxValue);
            prediction.RecordPrediction(serverTick, 1);
            prediction.RecordPrediction(currentTick, 2);

            bool rolledBack = prediction.ProcessServerState(
                serverTick,
                serverState: 10,
                currentTick,
                tickDeltaTime: 1f / 60f);

            Assert.IsTrue(rolledBack);
            Assert.AreEqual(1, target.SimulateCount);
        }

        private sealed class CountingPredictable : IPredictable<int, int>
        {
            private int _state;

            public int SimulateCount { get; private set; }
            public int CaptureInput() => 0;
            public int CaptureState() => _state;
            public void ApplyState(in int state) => _state = state;
            public void SimulateStep(in int input, float deltaTime)
            {
                SimulateCount++;
                _state += input;
            }

            public bool StatesMatch(in int predicted, in int authoritative) => false;
        }
    }
}
