using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Lightweight handle for perceptibles. Avoids GC by using ID + generation instead of references.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PerceptibleHandle : IEquatable<PerceptibleHandle>
    {
        public readonly int RegistryId;
        public readonly int Id;
        public readonly int Generation;

        public static readonly PerceptibleHandle Invalid = new PerceptibleHandle(0, -1, 0);

        /// <summary>
        /// Creates a process-local handle without a registry identity. This overload is intended
        /// for adapters and tests that only compare handles; a registry will never resolve it.
        /// </summary>
        public PerceptibleHandle(int id, int generation)
            : this(0, id, generation)
        {
        }

        public PerceptibleHandle(int registryId, int id, int generation)
        {
            RegistryId = registryId;
            Id = id;
            Generation = generation;
        }

        public bool IsValid => Id >= 0 && Generation > 0;

        public bool Equals(PerceptibleHandle other) =>
            RegistryId == other.RegistryId && Id == other.Id && Generation == other.Generation;
        public override bool Equals(object obj) => obj is PerceptibleHandle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RegistryId, Id, Generation);

        public static bool operator ==(PerceptibleHandle left, PerceptibleHandle right) => left.Equals(right);
        public static bool operator !=(PerceptibleHandle left, PerceptibleHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// Blittable perceptible data for Jobs/Burst.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PerceptibleData
    {
        public const byte DetectableFlag = 1 << 0;
        public const byte SoundSourceFlag = 1 << 1;

        public int RegistryId;
        public int Id;
        public int Generation;
        public int TypeId;
        public byte Flags;
        public float DetectionRadius;
        public float Loudness;
        public float3 Position;
        public float3 LOSPoint;

        public bool IsDetectable
        {
            get => (Flags & DetectableFlag) != 0;
            set => Flags = value
                ? (byte)(Flags | DetectableFlag)
                : (byte)(Flags & ~DetectableFlag);
        }

        public bool IsSoundSource
        {
            get => (Flags & SoundSourceFlag) != 0;
            set => Flags = value
                ? (byte)(Flags | SoundSourceFlag)
                : (byte)(Flags & ~SoundSourceFlag);
        }

        public PerceptibleHandle ToHandle() => new PerceptibleHandle(RegistryId, Id, Generation);
    }

    /// <summary>
    /// Sensor detection result. Visibility decays with age for memory entries.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DetectionResult
    {
        public PerceptibleHandle Target;
        public float Distance;
        public float3 LastKnownPosition;
        public double DetectionTime;
        public float Visibility;
        public SensorType SensorType;
        public bool IsFromMemory;
    }

    /// <summary>
    /// Stimulus memory entry that persists after the target leaves sensor range.
    /// Visibility decays linearly from <see cref="DetectionResult.Visibility"/> to 0 over MemoryDuration.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StimulusMemoryEntry
    {
        public PerceptibleHandle Target;
        public float3 LastKnownPosition;
        public double LastDetectedTime;
        public float VisibilityAtLastDetection;
        public SensorType SensorType;
        public float DistanceAtDetection;
        internal uint RefreshVersion;
    }

    /// <summary>
    /// Bounded persistent storage used by one sensor. Values are normalized at runtime so that
    /// existing serialized configurations whose new fields are zero receive safe defaults.
    /// </summary>
    [Serializable]
    public struct PerceptionSensorCapacity
    {
        [Min(1)] public int InitialCandidateCapacity;
        [Min(1)] public int MaximumCandidates;
        [Min(1)] public int InitialResultCapacity;
        [Min(1)] public int MaximumResults;
        [Min(1)] public int InitialMemoryCapacity;
        [Min(1)] public int MaximumMemoryEntries;

        public static PerceptionSensorCapacity Default => new PerceptionSensorCapacity
        {
            InitialCandidateCapacity = 64,
            MaximumCandidates = 16384,
            InitialResultCapacity = 32,
            MaximumResults = 1024,
            InitialMemoryCapacity = 32,
            MaximumMemoryEntries = 1024
        };

        internal PerceptionSensorCapacity Normalize()
        {
            PerceptionSensorCapacity defaults = Default;
            int maximumCandidates = MaximumCandidates > 0
                ? MaximumCandidates
                : defaults.MaximumCandidates;
            int maximumResults = MaximumResults > 0
                ? MaximumResults
                : defaults.MaximumResults;
            int maximumMemory = MaximumMemoryEntries > 0
                ? MaximumMemoryEntries
                : defaults.MaximumMemoryEntries;

            return new PerceptionSensorCapacity
            {
                InitialCandidateCapacity = math.clamp(
                    InitialCandidateCapacity > 0 ? InitialCandidateCapacity : defaults.InitialCandidateCapacity,
                    1,
                    maximumCandidates),
                MaximumCandidates = maximumCandidates,
                InitialResultCapacity = math.clamp(
                    InitialResultCapacity > 0 ? InitialResultCapacity : defaults.InitialResultCapacity,
                    1,
                    maximumResults),
                MaximumResults = maximumResults,
                InitialMemoryCapacity = math.clamp(
                    InitialMemoryCapacity > 0 ? InitialMemoryCapacity : defaults.InitialMemoryCapacity,
                    1,
                    maximumMemory),
                MaximumMemoryEntries = maximumMemory
            };
        }

        internal bool HasSameLimits(in PerceptionSensorCapacity other)
        {
            return InitialCandidateCapacity == other.InitialCandidateCapacity &&
                   MaximumCandidates == other.MaximumCandidates &&
                   InitialResultCapacity == other.InitialResultCapacity &&
                   MaximumResults == other.MaximumResults &&
                   InitialMemoryCapacity == other.InitialMemoryCapacity &&
                   MaximumMemoryEntries == other.MaximumMemoryEntries;
        }
    }

    public enum SensorUpdateStatus : byte
    {
        Uninitialized = 0,
        Ready = 1,
        NoTargets = 2,
        CandidateCapacityExceeded = 3,
        ResultCapacityExceeded = 4,
        LineOfSightBudgetExceeded = 5,
        InvalidConfiguration = 6,
        Disposed = 7,
        OcclusionBudgetExceeded = 8,
        CoordinateRangeExceeded = 9
    }

    /// <summary>
    /// Reason why a target was detected.
    /// </summary>
    public enum DetectionReason
    {
        None = 0,
        VisualContact = 1,
        SoundHeard = 2,
        ProximityAlert = 3
    }
}
