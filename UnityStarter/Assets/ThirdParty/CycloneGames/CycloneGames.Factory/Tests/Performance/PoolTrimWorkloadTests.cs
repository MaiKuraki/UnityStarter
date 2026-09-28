using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Report-only trim workload probe: "peak -> idle -> second peak". Timing is reported as a
    /// distribution; the deterministic <c>Diagnostics.TotalCreated</c> delta is additionally recorded
    /// as a custom sample group so the same XML carries a machine-independent counter alongside the
    /// timing, since the counter is invariant to which items eviction removes.
    /// </summary>
    public sealed class PoolTrimWorkloadTests
    {
        private const int WARMUP_COUNT = 5;
        private const int MEASUREMENT_COUNT = 15;
        private const int ITERATIONS_PER_MEASUREMENT = 1;

        private static int _workloadSink;

        [Test, Performance]
        public void PeakIdleSecondPeak_TrimWorkload()
        {
            Measure.Method(TrimWorkloadStep)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void PeakIdleSecondPeak_DeterministicCounters()
        {
            TrimWorkloadResult result = PoolWorkload.ExecuteTrimWorkload();

            // Deterministic counters (machine independent): safe to diff before/after the trim change.
            Measure.Custom("TrimWorkload.TotalCreatedAfterWorkload", result.TotalCreatedAfterWorkload);
            Measure.Custom("TrimWorkload.TotalCreatedDelta", result.TotalCreatedDelta);
            Measure.Custom("TrimWorkload.PeakInactive", result.PeakInactive);
            Measure.Custom("TrimWorkload.TrimmedInactive", result.TrimmedInactive);

            TestContext.WriteLine(
                "TrimWorkload deterministic counters: " +
                $"TotalCreatedAfter={result.TotalCreatedAfterWorkload}, " +
                $"TotalCreatedDelta={result.TotalCreatedDelta}, " +
                $"PeakInactive={result.PeakInactive}, " +
                $"TrimmedInactive={result.TrimmedInactive}");
        }

        private static void TrimWorkloadStep()
        {
            TrimWorkloadResult result = PoolWorkload.ExecuteTrimWorkload();
            _workloadSink = result.TotalCreatedAfterWorkload;
        }
    }
}
