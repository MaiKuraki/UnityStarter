using System;

namespace CycloneGames.Networking.Buffers
{
    /// <summary>
    /// Compatibility facade over the default instance-scoped buffer pool.
    ///
    /// All mutable counters and idle buffers now live in a
    /// <see cref="NetworkBufferPoolInstance"/>. Composition roots that need session
    /// isolation, independent configuration, or parallel test isolation should
    /// construct and inject their own instance instead of using this process-wide
    /// default. The static members are retained so existing call sites keep working.
    /// </summary>
    public static class NetworkBufferPool
    {
        private static readonly NetworkBufferPoolInstance s_default = new NetworkBufferPoolInstance();

        /// <summary>Default process-wide pool. Prefer injecting an instance for new code.</summary>
        public static NetworkBufferPoolInstance Default => s_default;

        public static int Count => s_default.Count;

        public static int OutstandingCount => s_default.OutstandingCount;

        public static int InvalidReturnCount => s_default.InvalidReturnCount;

        public static int MaxPoolSize => s_default.MaxPoolSize;

        public static bool ClearBuffersOnReturn => s_default.ClearBuffersOnReturn;

        public static NetworkBufferPoolMemorySnapshot GetMemorySnapshot() => s_default.GetMemorySnapshot();

        public static void Configure(int maxPoolSize, bool clearBuffersOnReturn = false) =>
            s_default.Configure(maxPoolSize, clearBuffersOnReturn);

        public static NetworkBuffer Get() => s_default.Get();

        public static NetworkBuffer GetWithData(ArraySegment<byte> data) => s_default.GetWithData(data);

        public static NetworkBuffer GetWithData(ReadOnlySpan<byte> data) => s_default.GetWithData(data);

        internal static void ReturnLease(NetworkBufferStorage storage, long leaseToken) =>
            s_default.ReturnLease(storage, leaseToken);

        public static void Clear() => s_default.Clear();

        public static NetworkBufferPoolTrimResult TrimIdleStep(int targetIdleCount, int maxWork) =>
            s_default.TrimIdleStep(targetIdleCount, maxWork);
    }
}
