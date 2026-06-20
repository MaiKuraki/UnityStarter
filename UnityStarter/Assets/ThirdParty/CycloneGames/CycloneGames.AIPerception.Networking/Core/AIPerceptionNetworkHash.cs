using System;

namespace CycloneGames.AIPerception.Networking
{
    public static class AIPerceptionNetworkHash
    {
        private const uint FNV_OFFSET = 2166136261u;
        private const uint FNV_PRIME = 16777619u;

        public static uint Compute(ReadOnlySpan<AIPerceptionDetectionEntry> entries)
        {
            uint hash = FNV_OFFSET;

            for (int i = 0; i < entries.Length; i++)
            {
                AIPerceptionDetectionEntry entry = entries[i];
                hash = Combine(hash, entry.TargetNetworkId);
                hash = Combine(hash, (uint)entry.PerceptibleTypeId);
                hash = Combine(hash, (uint)entry.SensorKind);
                hash = Combine(hash, (uint)entry.Flags);
                hash = Combine(hash, FloatBits(entry.LastKnownPosition.X));
                hash = Combine(hash, FloatBits(entry.LastKnownPosition.Y));
                hash = Combine(hash, FloatBits(entry.LastKnownPosition.Z));
                hash = Combine(hash, FloatBits(entry.Distance));
                hash = Combine(hash, FloatBits(entry.Visibility));
                hash = Combine(hash, (uint)entry.DetectionTick);
                hash = Combine(hash, (uint)entry.SourceSensorId);
            }

            return hash == 0u ? FNV_OFFSET : hash;
        }

        public static uint Compute(in AIPerceptionDetectionEntry entry)
        {
            ReadOnlySpan<AIPerceptionDetectionEntry> span = stackalloc AIPerceptionDetectionEntry[] { entry };
            return Compute(span);
        }

        private static uint Combine(uint hash, uint value)
        {
            unchecked
            {
                hash ^= value & 0xFFu;
                hash *= FNV_PRIME;
                hash ^= (value >> 8) & 0xFFu;
                hash *= FNV_PRIME;
                hash ^= (value >> 16) & 0xFFu;
                hash *= FNV_PRIME;
                hash ^= (value >> 24) & 0xFFu;
                hash *= FNV_PRIME;
                return hash;
            }
        }

        private static uint FloatBits(float value)
        {
            return unchecked((uint)BitConverter.SingleToInt32Bits(value));
        }
    }
}
