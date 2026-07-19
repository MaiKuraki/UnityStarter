using System;
using System.Threading;

namespace CycloneGames.Logger
{
    internal static class LogMessagePool
    {
        private const int DefaultPrewarmCount = 256;
        private const int MaxRetainedCount = 4096;

        private static readonly object SyncRoot = new object();
        private static readonly LogMessage[] Items = new LogMessage[MaxRetainedCount];

        private static int _count;
        private static int _peakSize;
        private static long _totalGets;
        private static long _totalReturns;
        private static long _totalMisses;
        private static long _totalDiscards;
        private static long _invalidReturns;

        internal static LogMessage Get()
        {
            Interlocked.Increment(ref _totalGets);

            lock (SyncRoot)
            {
                if (_count > 0)
                {
                    int index = --_count;
                    LogMessage message = Items[index];
                    Items[index] = null;
                    if (!message.TryMarkRented())
                    {
                        throw new InvalidOperationException("LogMessage pool state is corrupted.");
                    }

                    return message;
                }
            }

            Interlocked.Increment(ref _totalMisses);
            return new LogMessage();
        }

        internal static void Return(LogMessage message)
        {
            if (message == null)
            {
                return;
            }

            Interlocked.Increment(ref _totalReturns);
            if (!message.TryMarkReturned())
            {
                Interlocked.Increment(ref _invalidReturns);
                return;
            }

            message.Reset();
            lock (SyncRoot)
            {
                if (_count >= Items.Length)
                {
                    Interlocked.Increment(ref _totalDiscards);
                    return;
                }

                Items[_count++] = message;
                if (_count > _peakSize)
                {
                    _peakSize = _count;
                }
            }
        }

        internal static void Prewarm(int count = DefaultPrewarmCount)
        {
            count = Math.Min(Math.Max(count, 0), Items.Length);
            lock (SyncRoot)
            {
                while (_count < count)
                {
                    var message = new LogMessage();
                    if (!message.TryMarkReturned())
                    {
                        throw new InvalidOperationException("Unable to initialize LogMessage pool state.");
                    }

                    Items[_count++] = message;
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
                Interlocked.Read(ref _totalDiscards),
                Interlocked.Read(ref _invalidReturns));
        }

        internal static void ResetStatistics()
        {
            Interlocked.Exchange(ref _totalGets, 0);
            Interlocked.Exchange(ref _totalReturns, 0);
            Interlocked.Exchange(ref _totalMisses, 0);
            Interlocked.Exchange(ref _totalDiscards, 0);
            Interlocked.Exchange(ref _invalidReturns, 0);
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
            internal readonly long InvalidReturns;

            internal double HitRate => TotalGets > 0 ? 1.0 - (double)TotalMisses / TotalGets : 1.0;
            internal double DiscardRate => TotalReturns > 0 ? (double)TotalDiscards / TotalReturns : 0.0;

            internal PoolStatistics(
                int currentSize,
                int peakSize,
                long totalGets,
                long totalReturns,
                long totalMisses,
                long totalDiscards,
                long invalidReturns)
            {
                CurrentSize = currentSize;
                PeakSize = peakSize;
                TotalGets = totalGets;
                TotalReturns = totalReturns;
                TotalMisses = totalMisses;
                TotalDiscards = totalDiscards;
                InvalidReturns = invalidReturns;
            }
        }
    }
}
