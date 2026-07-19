using System;
using System.Text;
using System.Threading;

namespace CycloneGames.Logger.Util
{
    internal static class StringBuilderPool
    {
        private const int DefaultCapacity = 256;
        private const int MaxCapacityToRetain = 4096;
        private const int DefaultPrewarmCount = 128;
        private const int MaxRetainedCount = 512;

        private static readonly object SyncRoot = new object();
        private static readonly StringBuilder[] Items = new StringBuilder[MaxRetainedCount];

        private static int _count;
        private static int _peakSize;
        private static long _totalGets;
        private static long _totalReturns;
        private static long _totalMisses;
        private static long _totalDiscards;

        internal static StringBuilder Get()
        {
            Interlocked.Increment(ref _totalGets);
            lock (SyncRoot)
            {
                if (_count > 0)
                {
                    int index = --_count;
                    StringBuilder builder = Items[index];
                    Items[index] = null;
                    return builder;
                }
            }

            Interlocked.Increment(ref _totalMisses);
            return new StringBuilder(DefaultCapacity);
        }

        internal static void Return(StringBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            Interlocked.Increment(ref _totalReturns);
            if (builder.Capacity > MaxCapacityToRetain)
            {
                Interlocked.Increment(ref _totalDiscards);
                return;
            }

            builder.Clear();
            lock (SyncRoot)
            {
                if (_count >= Items.Length)
                {
                    Interlocked.Increment(ref _totalDiscards);
                    return;
                }

                Items[_count++] = builder;
                if (_count > _peakSize)
                {
                    _peakSize = _count;
                }
            }
        }

        internal static string GetStringAndReturn(StringBuilder builder)
        {
            if (builder == null)
            {
                return string.Empty;
            }

            string result = builder.ToString();
            Return(builder);
            return result;
        }

        internal static void Prewarm(int count = DefaultPrewarmCount)
        {
            count = Math.Min(Math.Max(count, 0), Items.Length);
            lock (SyncRoot)
            {
                while (_count < count)
                {
                    Items[_count++] = new StringBuilder(DefaultCapacity);
                }

                if (_count > _peakSize)
                {
                    _peakSize = _count;
                }
            }
        }

        internal static void Clear()
        {
            lock (SyncRoot)
            {
                Array.Clear(Items, 0, _count);
                _count = 0;
            }
        }

        internal static PoolStatistics GetStatistics()
        {
            int count;
            int peak;
            lock (SyncRoot)
            {
                count = _count;
                peak = _peakSize;
            }

            return new PoolStatistics(
                count,
                peak,
                Interlocked.Read(ref _totalGets),
                Interlocked.Read(ref _totalReturns),
                Interlocked.Read(ref _totalMisses),
                Interlocked.Read(ref _totalDiscards));
        }

        internal static void ResetStatistics()
        {
            Interlocked.Exchange(ref _totalGets, 0);
            Interlocked.Exchange(ref _totalReturns, 0);
            Interlocked.Exchange(ref _totalMisses, 0);
            Interlocked.Exchange(ref _totalDiscards, 0);
            lock (SyncRoot)
            {
                _peakSize = _count;
            }
        }

        internal readonly struct PoolStatistics
        {
            internal readonly int CurrentSize;
            internal readonly int PeakSize;
            internal readonly long TotalGets;
            internal readonly long TotalReturns;
            internal readonly long TotalMisses;
            internal readonly long TotalDiscards;

            internal double HitRate => TotalGets > 0 ? 1.0 - (double)TotalMisses / TotalGets : 1.0;
            internal double DiscardRate => TotalReturns > 0 ? (double)TotalDiscards / TotalReturns : 0.0;

            internal PoolStatistics(
                int currentSize,
                int peakSize,
                long totalGets,
                long totalReturns,
                long totalMisses,
                long totalDiscards)
            {
                CurrentSize = currentSize;
                PeakSize = peakSize;
                TotalGets = totalGets;
                TotalReturns = totalReturns;
                TotalMisses = totalMisses;
                TotalDiscards = totalDiscards;
            }
        }
    }
}
