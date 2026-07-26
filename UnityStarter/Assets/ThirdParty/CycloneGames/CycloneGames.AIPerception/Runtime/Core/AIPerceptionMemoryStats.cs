using System;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>Owner-local storage and admission statistics for one perceptible registry.</summary>
    public readonly struct PerceptibleRegistryMemoryStats
    {
        public PerceptibleRegistryMemoryStats(
            int perceptibleCount,
            int maximumPerceptibleCount,
            int peakPerceptibleCount,
            long rejectedRegistrationCount,
            int slotCapacity,
            int detectableSnapshotCount,
            int managedSnapshotCapacity,
            int nativeSnapshotCapacity,
            int spatialCellCount,
            int sensorManagerCount)
        {
            PerceptibleCount = perceptibleCount;
            MaximumPerceptibleCount = maximumPerceptibleCount;
            PeakPerceptibleCount = peakPerceptibleCount;
            RejectedRegistrationCount = rejectedRegistrationCount;
            SlotCapacity = slotCapacity;
            DetectableSnapshotCount = detectableSnapshotCount;
            ManagedSnapshotCapacity = managedSnapshotCapacity;
            NativeSnapshotCapacity = nativeSnapshotCapacity;
            SpatialCellCount = spatialCellCount;
            SensorManagerCount = sensorManagerCount;
        }

        public int PerceptibleCount { get; }
        public int MaximumPerceptibleCount { get; }
        public int PeakPerceptibleCount { get; }
        public long RejectedRegistrationCount { get; }
        public int SlotCapacity { get; }
        public int DetectableSnapshotCount { get; }
        public int ManagedSnapshotCapacity { get; }
        public int NativeSnapshotCapacity { get; }
        public int SpatialCellCount { get; }
        public int SensorManagerCount { get; }
    }

    /// <summary>Storage and workload statistics for one built-in sensor.</summary>
    public readonly struct AIPerceptionSensorMemoryStats
    {
        public AIPerceptionSensorMemoryStats(
            SensorType sensorType,
            int candidateCount,
            int candidateCapacity,
            int maximumCandidateCount,
            int peakCandidateCount,
            int resultCount,
            int resultCapacity,
            int maximumResultCount,
            int peakResultCount,
            int memoryCount,
            int memoryCapacity,
            int maximumMemoryCount,
            int peakMemoryCount,
            int nativeBufferCount,
            int nativeBufferCapacity,
            long updateCount,
            int lastUpdateWorkload,
            int peakUpdateWorkload,
            long candidateCapacityRejectedCount,
            long resultCapacityRejectedCount,
            long memoryEvictionCount)
        {
            SensorType = sensorType;
            CandidateCount = candidateCount;
            CandidateCapacity = candidateCapacity;
            MaximumCandidateCount = maximumCandidateCount;
            PeakCandidateCount = peakCandidateCount;
            ResultCount = resultCount;
            ResultCapacity = resultCapacity;
            MaximumResultCount = maximumResultCount;
            PeakResultCount = peakResultCount;
            MemoryCount = memoryCount;
            MemoryCapacity = memoryCapacity;
            MaximumMemoryCount = maximumMemoryCount;
            PeakMemoryCount = peakMemoryCount;
            NativeBufferCount = nativeBufferCount;
            NativeBufferCapacity = nativeBufferCapacity;
            UpdateCount = updateCount;
            LastUpdateWorkload = lastUpdateWorkload;
            PeakUpdateWorkload = peakUpdateWorkload;
            CandidateCapacityRejectedCount = candidateCapacityRejectedCount;
            ResultCapacityRejectedCount = resultCapacityRejectedCount;
            MemoryEvictionCount = memoryEvictionCount;
        }

        public SensorType SensorType { get; }
        public int CandidateCount { get; }
        public int CandidateCapacity { get; }
        public int MaximumCandidateCount { get; }
        public int PeakCandidateCount { get; }
        public int ResultCount { get; }
        public int ResultCapacity { get; }
        public int MaximumResultCount { get; }
        public int PeakResultCount { get; }
        public int MemoryCount { get; }
        public int MemoryCapacity { get; }
        public int MaximumMemoryCount { get; }
        public int PeakMemoryCount { get; }
        public int NativeBufferCount { get; }
        public int NativeBufferCapacity { get; }
        public long UpdateCount { get; }
        public int LastUpdateWorkload { get; }
        public int PeakUpdateWorkload { get; }
        public long CandidateCapacityRejectedCount { get; }
        public long ResultCapacityRejectedCount { get; }
        public long MemoryEvictionCount { get; }
    }

    /// <summary>Aggregate owner-local diagnostics for one sensor manager and its registry.</summary>
    public readonly struct AIPerceptionMemoryStats
    {
        public AIPerceptionMemoryStats(
            in PerceptibleRegistryMemoryStats registry,
            int sensorCount,
            int maximumSensorCount,
            int peakSensorCount,
            long rejectedSensorRegistrationCount,
            int lodLevelCount,
            int builtInSensorCount,
            int customSensorCount,
            int candidateCount,
            int candidateCapacity,
            int maximumCandidateCount,
            int peakCandidateCount,
            int resultCount,
            int resultCapacity,
            int maximumResultCount,
            int peakResultCount,
            int memoryCount,
            int memoryCapacity,
            int maximumMemoryCount,
            int peakMemoryCount,
            int nativeBufferCount,
            int nativeBufferCapacity,
            long sensorUpdateCount,
            int lastUpdateWorkload,
            int peakUpdateWorkload,
            long candidateCapacityRejectedCount,
            long resultCapacityRejectedCount,
            long memoryEvictionCount)
        {
            Registry = registry;
            SensorCount = sensorCount;
            MaximumSensorCount = maximumSensorCount;
            PeakSensorCount = peakSensorCount;
            RejectedSensorRegistrationCount = rejectedSensorRegistrationCount;
            LodLevelCount = lodLevelCount;
            BuiltInSensorCount = builtInSensorCount;
            CustomSensorCount = customSensorCount;
            CandidateCount = candidateCount;
            CandidateCapacity = candidateCapacity;
            MaximumCandidateCount = maximumCandidateCount;
            PeakCandidateCount = peakCandidateCount;
            ResultCount = resultCount;
            ResultCapacity = resultCapacity;
            MaximumResultCount = maximumResultCount;
            PeakResultCount = peakResultCount;
            MemoryCount = memoryCount;
            MemoryCapacity = memoryCapacity;
            MaximumMemoryCount = maximumMemoryCount;
            PeakMemoryCount = peakMemoryCount;
            NativeBufferCount = nativeBufferCount;
            NativeBufferCapacity = nativeBufferCapacity;
            SensorUpdateCount = sensorUpdateCount;
            LastUpdateWorkload = lastUpdateWorkload;
            PeakUpdateWorkload = peakUpdateWorkload;
            CandidateCapacityRejectedCount = candidateCapacityRejectedCount;
            ResultCapacityRejectedCount = resultCapacityRejectedCount;
            MemoryEvictionCount = memoryEvictionCount;
        }

        public PerceptibleRegistryMemoryStats Registry { get; }
        public int SensorCount { get; }
        public int MaximumSensorCount { get; }
        public int PeakSensorCount { get; }
        public long RejectedSensorRegistrationCount { get; }
        public int LodLevelCount { get; }
        public int BuiltInSensorCount { get; }
        public int CustomSensorCount { get; }
        public int CandidateCount { get; }
        public int CandidateCapacity { get; }
        public int MaximumCandidateCount { get; }
        public int PeakCandidateCount { get; }
        public int ResultCount { get; }
        public int ResultCapacity { get; }
        public int MaximumResultCount { get; }
        public int PeakResultCount { get; }
        public int MemoryCount { get; }
        public int MemoryCapacity { get; }
        public int MaximumMemoryCount { get; }
        public int PeakMemoryCount { get; }
        public int NativeBufferCount { get; }
        public int NativeBufferCapacity { get; }
        public long SensorUpdateCount { get; }
        public int LastUpdateWorkload { get; }
        public int PeakUpdateWorkload { get; }
        public long CandidateCapacityRejectedCount { get; }
        public long ResultCapacityRejectedCount { get; }
        public long MemoryEvictionCount { get; }
    }

    internal interface IAIPerceptionSensorMemoryOwner
    {
        int LastUpdateWorkload { get; }
        AIPerceptionSensorMemoryStats GetMemoryStats();
    }

    internal sealed class SensorMemoryCounters
    {
        public long UpdateCount { get; private set; }
        public int LastUpdateWorkload { get; private set; }
        public int PeakUpdateWorkload { get; private set; }
        public int PeakCandidateCount { get; private set; }
        public int PeakResultCount { get; private set; }
        public int PeakMemoryCount { get; private set; }
        public long CandidateCapacityRejectedCount { get; private set; }
        public long ResultCapacityRejectedCount { get; private set; }
        public long MemoryEvictionCount { get; private set; }

        public void BeginUpdate()
        {
            UpdateCount = SaturatingIncrement(UpdateCount);
            LastUpdateWorkload = 0;
        }

        public void RecordCandidates(int count)
        {
            LastUpdateWorkload = Math.Max(0, count);
            PeakUpdateWorkload = Math.Max(PeakUpdateWorkload, LastUpdateWorkload);
            PeakCandidateCount = Math.Max(PeakCandidateCount, LastUpdateWorkload);
        }

        public void RecordCandidateCapacityRejected(int boundedWorkload)
        {
            CandidateCapacityRejectedCount = SaturatingIncrement(CandidateCapacityRejectedCount);
            RecordCandidates(boundedWorkload);
        }

        public void RecordResultCount(int count)
        {
            PeakResultCount = Math.Max(PeakResultCount, Math.Max(0, count));
        }

        public void RecordMemoryCount(int count)
        {
            PeakMemoryCount = Math.Max(PeakMemoryCount, Math.Max(0, count));
        }

        public void RecordResultCapacityRejected()
        {
            ResultCapacityRejectedCount = SaturatingIncrement(ResultCapacityRejectedCount);
        }

        public void RecordMemoryEviction()
        {
            MemoryEvictionCount = SaturatingIncrement(MemoryEvictionCount);
        }

        private static long SaturatingIncrement(long value)
        {
            return value == long.MaxValue ? long.MaxValue : value + 1L;
        }
    }
}
