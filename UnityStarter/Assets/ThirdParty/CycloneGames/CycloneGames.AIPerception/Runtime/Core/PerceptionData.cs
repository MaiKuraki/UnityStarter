using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Lightweight handle for perceptibles. Avoids GC by using ID + generation instead of references.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PerceptibleHandle : IEquatable<PerceptibleHandle>
    {
        public readonly int Id;
        public readonly int Generation;

        public static readonly PerceptibleHandle Invalid = new PerceptibleHandle(-1, 0);

        public PerceptibleHandle(int id, int generation)
        {
            Id = id;
            Generation = generation;
        }

        public bool IsValid => Id >= 0;

        public bool Equals(PerceptibleHandle other) => Id == other.Id && Generation == other.Generation;
        public override bool Equals(object obj) => obj is PerceptibleHandle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, Generation);

        public static bool operator ==(PerceptibleHandle left, PerceptibleHandle right) => left.Equals(right);
        public static bool operator !=(PerceptibleHandle left, PerceptibleHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// Blittable perceptible data for Jobs/Burst.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PerceptibleData
    {
        public int Id;
        public int Generation;
        public int TypeId; // Changed to int for extensibility
        public byte Flags; // Bit 0: IsDetectable
        public float DetectionRadius;
        public float Loudness; // For sound sources
        public float3 Position;
        public float3 LOSPoint;

        public bool IsDetectable
        {
            get => (Flags & 1) != 0;
            set => Flags = value ? (byte)(Flags | 1) : (byte)(Flags & ~1);
        }

        public PerceptibleHandle ToHandle() => new PerceptibleHandle(Id, Generation);
    }

    /// <summary>
    /// Sensor detection result.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DetectionResult
    {
        public PerceptibleHandle Target;
        public float Distance;
        public float3 LastKnownPosition;
        public float DetectionTime;
        public float Visibility; // 0-1 for sight, 0-1 loudness for hearing
        public int SensorType; // 0=Sight, 1=Hearing
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
