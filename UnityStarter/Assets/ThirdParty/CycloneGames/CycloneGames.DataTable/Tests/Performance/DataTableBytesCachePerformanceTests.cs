using NUnit.Framework;

using Unity.PerformanceTesting;

namespace CycloneGames.DataTable.Tests.Performance
{
    public sealed class DataTableBytesCachePerformanceTests
    {
        private const int PayloadCount = 16_384;
        private const int PayloadsPerStep = 64;

        private DataTableBytesCache _cache;
        private DataTableBytesCacheReleaseBudget _budget;
        private DataTableBytesCacheReleaseResult _result;

        [Test, Performance]
        public void ReleaseSixteenThousandPayloads_UsesForwardOnlyBoundedSteps()
        {
            _budget = new DataTableBytesCacheReleaseBudget(PayloadsPerStep, 0);
            Measure.Method(ReleaseAll)
                .SetUp(CreateClosedCache)
                .CleanUp(DisposeCache)
                .WarmupCount(3)
                .MeasurementCount(10)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();
        }

        private void CreateClosedCache()
        {
            _cache = new DataTableBytesCache(
                new DataTableLoadLimits(PayloadCount, 1, PayloadCount),
                capacity: PayloadCount);
            for (int index = 0; index < PayloadCount; index++)
            {
                _cache.AddOwned(index.ToString(), new byte[] { 1 });
            }

            _cache.Close();
        }

        private void ReleaseAll()
        {
            while (!_cache.IsReleaseComplete)
            {
                _result = _cache.ReleaseStep(in _budget);
            }
        }

        private void DisposeCache()
        {
            Assert.That(_result.IsComplete, Is.True);
            _cache?.Dispose();
            _cache = null;
        }
    }
}
