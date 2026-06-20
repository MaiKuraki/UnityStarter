using System;
using CycloneGames.Hash.Core;

namespace CycloneGames.AIPerception.Networking
{
    public static class AIPerceptionNetworkHash
    {
        public static ulong Compute(ReadOnlySpan<AIPerceptionDetectionEntry> entries)
        {
            ulong hash = Fnv1a64.OffsetBasis;

            for (int i = 0; i < entries.Length; i++)
            {
                AIPerceptionDetectionEntry entry = entries[i];
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, entry.TargetNetworkId);
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)entry.PerceptibleTypeId);
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)entry.SensorKind);
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)entry.Flags);
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.LastKnownPosition.X));
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.LastKnownPosition.Y));
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.LastKnownPosition.Z));
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.Distance));
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, FloatBits(entry.Visibility));
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)entry.DetectionTick);
                hash = Fnv1a64.CombineUInt32LittleEndian(hash, (uint)entry.SourceSensorId);
            }

            return hash == 0UL ? Fnv1a64.OffsetBasis : hash;
        }

        public static ulong Compute(in AIPerceptionDetectionEntry entry)
        {
            ReadOnlySpan<AIPerceptionDetectionEntry> span = stackalloc AIPerceptionDetectionEntry[] { entry };
            return Compute(span);
        }

        private static uint FloatBits(float value)
        {
            return unchecked((uint)BitConverter.SingleToInt32Bits(value));
        }
    }
}
