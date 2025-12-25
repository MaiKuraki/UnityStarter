using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Thread-safe object pool for LogMessage instances with three-tier adaptive capacity management.
    /// - Target: Normal operating capacity, maintained during steady state
    /// - Peak: Maximum capacity during load spikes, allows auto-expansion without GC
    /// - Max: Absolute hard limit to prevent memory leaks
    /// </summary>
    public static class LogMessagePool
    {
        private static readonly ConcurrentQueue<LogMessage> Pool = new();
        
        private const int TargetPoolSize = 256;
        private const int PeakPoolSize = 4096;
        private const int MaxPoolSize = 8192;
        
        private static int _poolSize = 0;
        private static int _isTrimming = 0;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static long _totalGets = 0;
        private static long _totalReturns = 0;
        private static long _totalDiscards = 0;
        private static long _trimCount = 0;
        private static int _peakSize = 0;
#endif

        public static LogMessage Get()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Interlocked.Increment(ref _totalGets);
#endif
            if (Pool.TryDequeue(out var message))
            {
                Interlocked.Decrement(ref _poolSize);
                return message;
            }
            return new LogMessage();
        }

        public static void Return(LogMessage message)
        {
            if (message == null) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Interlocked.Increment(ref _totalReturns);
#endif

            int currentSize = Volatile.Read(ref _poolSize);
            
            // Hard limit: discard only when exceeding absolute maximum
            if (currentSize >= MaxPoolSize)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Interlocked.Increment(ref _totalDiscards);
#endif
                message.Reset();
                return;
            }

            message.Reset();
            Pool.Enqueue(message);
            int newSize = Interlocked.Increment(ref _poolSize);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int peak = Volatile.Read(ref _peakSize);
            if (newSize > peak)
            {
                Interlocked.CompareExchange(ref _peakSize, newSize, peak);
            }
#endif

            // Trigger trimming when exceeding peak capacity
            if (newSize > PeakPoolSize)
            {
                TryTrimExcess();
            }
        }

        /// <summary>
        /// Prewarms the pool to target capacity to reduce cold-start allocations.
        /// </summary>
        public static void Prewarm(int count = TargetPoolSize)
        {
            count = Math.Min(Math.Max(count, 0), PeakPoolSize);
            int current = Volatile.Read(ref _poolSize);
            int toAdd = Math.Min(count - current, PeakPoolSize - current);
            
            for (int i = 0; i < toAdd; i++)
            {
                Pool.Enqueue(new LogMessage());
                Interlocked.Increment(ref _poolSize);
            }
        }

        private static void TryTrimExcess()
        {
            if (Interlocked.CompareExchange(ref _isTrimming, 1, 0) != 0)
                return;

            try
            {
                int currentSize = Volatile.Read(ref _poolSize);
                int toRemove = currentSize - TargetPoolSize;

                if (toRemove <= 0) return;

                for (int i = 0; i < toRemove && Pool.TryDequeue(out _); i++)
                {
                    Interlocked.Decrement(ref _poolSize);
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Interlocked.Increment(ref _trimCount);
#endif
            }
            finally
            {
                Volatile.Write(ref _isTrimming, 0);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static PoolStatistics GetStatistics()
        {
            return new PoolStatistics
            {
                CurrentSize = Volatile.Read(ref _poolSize),
                PeakSize = Volatile.Read(ref _peakSize),
                TotalGets = Interlocked.Read(ref _totalGets),
                TotalReturns = Interlocked.Read(ref _totalReturns),
                TotalDiscards = Interlocked.Read(ref _totalDiscards),
                TrimCount = Interlocked.Read(ref _trimCount)
            };
        }

        public static void ResetStatistics()
        {
            Interlocked.Exchange(ref _totalGets, 0);
            Interlocked.Exchange(ref _totalReturns, 0);
            Interlocked.Exchange(ref _totalDiscards, 0);
            Interlocked.Exchange(ref _trimCount, 0);
            Interlocked.Exchange(ref _peakSize, 0);
        }

        public struct PoolStatistics
        {
            public int CurrentSize;
            public int PeakSize;
            public long TotalGets;
            public long TotalReturns;
            public long TotalDiscards;
            public long TrimCount;
            
            public double HitRate => TotalGets > 0 ? (TotalGets - TotalReturns + CurrentSize) / (double)TotalGets : 0;
            public double DiscardRate => TotalReturns > 0 ? TotalDiscards / (double)TotalReturns : 0;
        }
#endif
    }
}