using CycloneGames.Networking.Prediction;
using CycloneGames.Networking.Simulation;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class PredictionBufferTests
    {
        [Test]
        public void TryGet_ZeroTick_ReturnsFalse_AfterClear()
        {
            var buffer = new PredictionBuffer<int>(8);

            buffer.Set(NetworkTick.Zero, 10);
            Assert.IsTrue(buffer.TryGet(NetworkTick.Zero, out int value));
            Assert.AreEqual(10, value);

            buffer.Clear();

            Assert.IsFalse(buffer.TryGet(NetworkTick.Zero, out _));
        }

        [Test]
        public void Invalidate_Makes_Existing_Tick_Unavailable()
        {
            var buffer = new PredictionBuffer<int>(8);
            var tick = new NetworkTick(3);

            buffer.Set(tick, 123);
            buffer.Invalidate(tick);

            Assert.IsFalse(buffer.TryGet(tick, out _));
        }
    }
}
