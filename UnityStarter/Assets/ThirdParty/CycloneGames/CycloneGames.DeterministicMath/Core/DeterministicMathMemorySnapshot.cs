namespace CycloneGames.DeterministicMath
{
    /// <summary>Allocation-free diagnostics for fixed immutable deterministic-math tables.</summary>
    public readonly struct DeterministicMathMemorySnapshot
    {
        public DeterministicMathMemorySnapshot(
            int cordicAtanElementCount,
            long cordicAtanPayloadBytes)
        {
            CordicAtanElementCount = cordicAtanElementCount;
            CordicAtanPayloadBytes = cordicAtanPayloadBytes;
        }

        /// <summary>Exact number of Q32.32 values in the fixed CORDIC atan table.</summary>
        public int CordicAtanElementCount { get; }

        /// <summary>
        /// Derived element payload bytes only. Managed array headers, alignment, and allocator overhead are excluded.
        /// </summary>
        public long CordicAtanPayloadBytes { get; }
    }
}
