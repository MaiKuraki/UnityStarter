using System;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.Foundation2D.Runtime.Tests
{
    public sealed class SpriteSequencePlaybackPerformanceTests
    {
        private SpriteSequencePlaybackState _state;

        [SetUp]
        public void SetUp()
        {
            _state = default;
            _state.Initialize(
                SpriteSequencePlaybackDirection.Forward,
                24d,
                SpriteSequencePlaybackMode.Loop,
                16,
                1d,
                0,
                0d,
                SpriteSequenceIntervalHoldMode.Last);
        }

        [Test, Performance]
        public void Advance_WarmedLoop_RecordsManagedAllocation()
        {
            Measure.Method(() => _state.Advance(1d / 120d, 64))
                .WarmupCount(10)
                .MeasurementCount(30)
                .IterationsPerMeasurement(1000)
                .GC()
                .Run();
        }

        [Test]
        public void Advance_WarmedLoop_AllocatesNoManagedMemory()
        {
            for (int i = 0; i < 1024; i++)
            {
                _state.Advance(1d / 120d, 64);
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                _state.Advance(1d / 120d, 64);
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocatedBytes, Is.Zero, "Warmed value-state advancement allocated managed memory.");
        }
    }
}
