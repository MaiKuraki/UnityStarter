using System;
using CycloneGames.Hash.Core;

namespace CycloneGames.AIPerception.Networking
{
    /// <summary>Deterministic checksum and ordering helpers for canonical v1 detection entries.</summary>
    public static class AIPerceptionNetworkHash
    {
        public static ulong Compute(ReadOnlySpan<AIPerceptionDetectionEntry> entries)
        {
            ulong hash = Fnv1a64.OffsetBasis;
            for (int i = 0; i < entries.Length; i++)
            {
                hash = Append(hash, in entries[i]);
            }

            return hash == 0UL ? Fnv1a64.OffsetBasis : hash;
        }

        public static ulong Compute(in AIPerceptionDetectionEntry entry)
        {
            ulong hash = Append(Fnv1a64.OffsetBasis, in entry);
            return hash == 0UL ? Fnv1a64.OffsetBasis : hash;
        }

        public static ulong Append(ulong hash, in AIPerceptionDetectionEntry entry)
        {
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, entry.TargetNetworkId);
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, unchecked((uint)entry.PerceptibleTypeId));
            hash = CombineByte(hash, (byte)entry.SensorKind);
            hash = CombineByte(hash, (byte)entry.Flags);
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.LastKnownPosition.X));
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.LastKnownPosition.Y));
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.LastKnownPosition.Z));
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.Distance));
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.Visibility));
            hash = Fnv1a64.CombineUInt32LittleEndian(hash, unchecked((uint)entry.DetectionTick));
            return Fnv1a64.CombineUInt32LittleEndian(hash, unchecked((uint)entry.SourceSensorId));
        }

        /// <summary>
        /// Total ordering used by snapshots. Exact duplicates compare equal and are not canonical.
        /// </summary>
        public static int CompareCanonical(
            in AIPerceptionDetectionEntry left,
            in AIPerceptionDetectionEntry right)
        {
            int comparison = left.TargetNetworkId.CompareTo(right.TargetNetworkId);
            if (comparison != 0) return comparison;
            comparison = ((byte)left.SensorKind).CompareTo((byte)right.SensorKind);
            if (comparison != 0) return comparison;
            comparison = left.SourceSensorId.CompareTo(right.SourceSensorId);
            if (comparison != 0) return comparison;
            comparison = left.PerceptibleTypeId.CompareTo(right.PerceptibleTypeId);
            if (comparison != 0) return comparison;
            comparison = ((byte)left.Flags).CompareTo((byte)right.Flags);
            if (comparison != 0) return comparison;
            comparison = left.DetectionTick.CompareTo(right.DetectionTick);
            if (comparison != 0) return comparison;
            comparison = left.LastKnownPosition.X.CompareTo(right.LastKnownPosition.X);
            if (comparison != 0) return comparison;
            comparison = left.LastKnownPosition.Y.CompareTo(right.LastKnownPosition.Y);
            if (comparison != 0) return comparison;
            comparison = left.LastKnownPosition.Z.CompareTo(right.LastKnownPosition.Z);
            if (comparison != 0) return comparison;
            comparison = left.Distance.CompareTo(right.Distance);
            return comparison != 0 ? comparison : left.Visibility.CompareTo(right.Visibility);
        }

        private static ulong CombineByte(ulong hash, byte value)
        {
            unchecked
            {
                hash ^= value;
                return hash * Fnv1a64.Prime;
            }
        }

        private static uint FloatBits(float value)
        {
            return unchecked((uint)BitConverter.SingleToInt32Bits(value));
        }
    }
}
