namespace CycloneGames.Networking.Buffers
{
    public readonly struct NetworkBufferPoolMemorySnapshot
    {
        internal NetworkBufferPoolMemorySnapshot(
            int idleBufferCount,
            int outstandingBufferCount,
            int maximumIdleBufferCount,
            int invalidReturnCount,
            long pressureTrimmedBufferCount,
            bool clearsBuffersOnReturn)
        {
            IdleBufferCount = idleBufferCount;
            OutstandingBufferCount = outstandingBufferCount;
            MaximumIdleBufferCount = maximumIdleBufferCount;
            InvalidReturnCount = invalidReturnCount;
            PressureTrimmedBufferCount = pressureTrimmedBufferCount;
            ClearsBuffersOnReturn = clearsBuffersOnReturn;
        }

        public int IdleBufferCount { get; }
        public int OutstandingBufferCount { get; }
        public int MaximumIdleBufferCount { get; }
        public int InvalidReturnCount { get; }
        public long PressureTrimmedBufferCount { get; }
        public bool ClearsBuffersOnReturn { get; }
    }

    public readonly struct NetworkBufferPoolTrimResult
    {
        internal NetworkBufferPoolTrimResult(int workConsumed, int remainingIdleBufferCount)
        {
            WorkConsumed = workConsumed;
            RemainingIdleBufferCount = remainingIdleBufferCount;
        }

        public int WorkConsumed { get; }
        public int RemainingIdleBufferCount { get; }
    }
}
