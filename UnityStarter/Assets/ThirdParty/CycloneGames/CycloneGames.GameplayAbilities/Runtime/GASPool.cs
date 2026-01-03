using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Contract for poolable objects. Implement to enable pool lifecycle callbacks.
    /// </summary>
    public interface IGASPoolable
    {
        /// <summary>Called when retrieved from pool. Initialize state here.</summary>
        void OnGetFromPool();

        /// <summary>Called before returning to pool. Reset all state to prevent leaks.</summary>
        void OnReturnToPool();
    }

    /// <summary>
    /// High-performance, thread-safe object pool with three-tier adaptive capacity.
    /// - Target: Normal operating capacity, maintained during steady state
    /// - Peak: Maximum capacity during load spikes, allows expansion without GC pressure
    /// - Max: Absolute hard limit to prevent memory leaks
    /// 
    /// Features:
    /// - ConcurrentStack for lock-free thread-safe access
    /// - Platform-adaptive capacity configuration
    /// - Smart auto-shrink based on peak usage tracking
    /// - Unity "fake null" validation support
    /// - Full statistics for profiling
    /// </summary>
    public sealed class GASPool<T> where T : class, IGASPoolable, new()
    {
        #region Platform Configuration

#if UNITY_IOS || UNITY_ANDROID || UNITY_SWITCH
        private const int kDefaultTarget = 64;
        private const int kDefaultPeak = 256;
        private const int kDefaultMax = 512;
        private const int kShrinkInterval = 32;
        private const int kMaxShrinkPerCheck = 8;
#elif UNITY_STANDALONE || UNITY_PS4 || UNITY_PS5 || UNITY_XBOXONE || UNITY_GAMECORE
        private const int kDefaultTarget = 256;
        private const int kDefaultPeak = 1024;
        private const int kDefaultMax = 4096;
        private const int kShrinkInterval = 128;
        private const int kMaxShrinkPerCheck = 32;
#else
        private const int kDefaultTarget = 128;
        private const int kDefaultPeak = 512;
        private const int kDefaultMax = 2048;
        private const int kShrinkInterval = 64;
        private const int kMaxShrinkPerCheck = 16;
#endif
        private const float kBufferRatio = 1.25f;
        private const float kPeakDecayFactor = 0.5f;

        #endregion

        #region Instance Fields

        private readonly ConcurrentStack<T> _pool;
        private readonly int _targetCapacity;
        private readonly int _peakCapacity;
        private readonly int _maxCapacity;
        private readonly int _shrinkInterval;
        private readonly int _maxShrinkPerCheck;
        private readonly Func<T, bool> _validationFunc;

        private int _activeCount;
        private int _peakActive;
        private int _peakActiveSinceLastCheck;
        private int _returnCounter;
        private long _totalGets;
        private long _totalMisses;
        private long _totalDiscards;

        #endregion

        #region Static Default Instance

        private static GASPool<T> s_Default;
        private static readonly object s_DefaultLock = new object();

        /// <summary>
        /// Thread-safe lazy-initialized shared pool instance.
        /// </summary>
        public static GASPool<T> Shared
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (s_Default != null) return s_Default;
                lock (s_DefaultLock)
                {
                    s_Default ??= new GASPool<T>();
                }
                return s_Default;
            }
        }

        /// <summary>
        /// Replaces the shared instance. Use for custom configuration.
        /// </summary>
        public static void SetSharedInstance(GASPool<T> instance)
        {
            lock (s_DefaultLock)
            {
                s_Default = instance;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a pool with platform-adaptive default settings.
        /// </summary>
        public GASPool() : this(kDefaultTarget, kDefaultPeak, kDefaultMax) { }

        /// <summary>
        /// Creates a pool with custom three-tier configuration.
        /// </summary>
        /// <param name="targetCapacity">Normal operating capacity.</param>
        /// <param name="peakCapacity">Maximum during load spikes.</param>
        /// <param name="maxCapacity">Absolute hard limit. -1 for unlimited (not recommended).</param>
        /// <param name="validationFunc">Optional validation for Unity objects (handles "fake null").</param>
        public GASPool(int targetCapacity, int peakCapacity = -1, int maxCapacity = -1,
            Func<T, bool> validationFunc = null)
        {
            _targetCapacity = Math.Max(1, targetCapacity);
            _peakCapacity = peakCapacity > 0 ? peakCapacity : _targetCapacity * 4;
            _maxCapacity = maxCapacity > 0 ? maxCapacity : _peakCapacity * 2;
            _shrinkInterval = kShrinkInterval;
            _maxShrinkPerCheck = kMaxShrinkPerCheck;
            _validationFunc = validationFunc;
            _pool = new ConcurrentStack<T>();
        }

        #endregion

        #region Core Pool Operations

        /// <summary>
        /// Retrieves an instance from pool or creates new. Thread-safe, lock-free fast path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get()
        {
            Interlocked.Increment(ref _totalGets);

            T item;
            while (_pool.TryPop(out item))
            {
                // Validate item (handles Unity "fake null" for destroyed objects)
                if (IsValid(item))
                {
                    TrackActive(1);
                    item.OnGetFromPool();
                    return item;
                }
                // Invalid item discarded, try next
            }

            // Pool empty - create new instance
            Interlocked.Increment(ref _totalMisses);
            TrackActive(1);
            item = new T();
            item.OnGetFromPool();
            return item;
        }

        /// <summary>
        /// Returns an instance to the pool. Thread-safe with capacity enforcement.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return(T item)
        {
            if (item == null || !IsValid(item)) return;

            TrackActive(-1);
            item.OnReturnToPool();

            int poolCount = _pool.Count;

            // Hard limit - discard when exceeding max
            if (_maxCapacity > 0 && poolCount >= _maxCapacity)
            {
                Interlocked.Increment(ref _totalDiscards);
                return;
            }

            _pool.Push(item);

            // Trigger shrink when exceeding peak capacity
            if (poolCount > _peakCapacity)
            {
                TryTrimExcess();
            }

            // Periodic smart shrink check
            int counter = Interlocked.Increment(ref _returnCounter);
            if (counter >= _shrinkInterval)
            {
                Interlocked.Exchange(ref _returnCounter, 0);
                PerformSmartShrink();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValid(T item)
        {
            if (item == null) return false;
            if (_validationFunc != null) return _validationFunc(item);

            // Handle Unity "fake null" - UnityEngine.Object overrides == operator
            if (item is UnityEngine.Object unityObj)
            {
                return unityObj != null;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrackActive(int delta)
        {
            int newCount = Interlocked.Add(ref _activeCount, delta);

            if (delta > 0)
            {
                int currentPeak;
                do
                {
                    currentPeak = _peakActiveSinceLastCheck;
                    if (newCount <= currentPeak) break;
                } while (Interlocked.CompareExchange(ref _peakActiveSinceLastCheck, newCount, currentPeak) != currentPeak);

                do
                {
                    currentPeak = _peakActive;
                    if (newCount <= currentPeak) break;
                } while (Interlocked.CompareExchange(ref _peakActive, newCount, currentPeak) != currentPeak);
            }
        }

        #endregion

        #region Auto-Scaling

        private int _isTrimming = 0;

        private void TryTrimExcess()
        {
            if (Interlocked.CompareExchange(ref _isTrimming, 1, 0) != 0)
                return;

            try
            {
                int poolCount = _pool.Count;
                int toRemove = poolCount - _targetCapacity;

                if (toRemove <= 0) return;

                for (int i = 0; i < toRemove && _pool.TryPop(out _); i++) { }
            }
            finally
            {
                Volatile.Write(ref _isTrimming, 0);
            }
        }

        private void PerformSmartShrink()
        {
            int peakSinceCheck = Volatile.Read(ref _peakActiveSinceLastCheck);
            int targetTotal = (int)(peakSinceCheck * kBufferRatio);
            targetTotal = Math.Max(targetTotal, _targetCapacity);

            int active = Volatile.Read(ref _activeCount);
            int poolCount = _pool.Count;
            int currentTotal = active + poolCount;

            if (currentTotal > targetTotal && poolCount > _targetCapacity)
            {
                int excess = currentTotal - targetTotal;
                int toRemove = Math.Min(excess, _maxShrinkPerCheck);
                toRemove = Math.Min(toRemove, poolCount - _targetCapacity);

                for (int i = 0; i < toRemove && _pool.TryPop(out _); i++) { }
            }

            int currentPeakCheck = Volatile.Read(ref _peakActiveSinceLastCheck);
            int decayedPeak = Math.Max(active, (int)(currentPeakCheck * kPeakDecayFactor));
            Interlocked.Exchange(ref _peakActiveSinceLastCheck, decayedPeak);
        }

        #endregion

        #region Pool Management

        /// <summary>
        /// Pre-warms pool to target capacity during loading screens.
        /// </summary>
        public void Warm(int count = -1)
        {
            int toCreate = count > 0 ? count : _targetCapacity;
            toCreate = Math.Min(toCreate, _peakCapacity - _pool.Count);

            for (int i = 0; i < toCreate; i++)
            {
                var item = new T();
                _pool.Push(item);
            }
        }

        /// <summary>
        /// Clears all pooled instances.
        /// </summary>
        public void Clear()
        {
            _pool.Clear();
            Interlocked.Exchange(ref _peakActiveSinceLastCheck, 0);
            Interlocked.Exchange(ref _returnCounter, 0);
        }

        /// <summary>
        /// Aggressively shrinks pool to target capacity. Call during scene transitions.
        /// </summary>
        public void AggressiveShrink()
        {
            while (_pool.Count > _targetCapacity)
            {
                if (!_pool.TryPop(out _)) break;
            }
            int active = Volatile.Read(ref _activeCount);
            Interlocked.Exchange(ref _peakActiveSinceLastCheck, active);
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Gets current pool statistics.
        /// </summary>
        public GASPoolStatistics GetStatistics()
        {
            long gets = Volatile.Read(ref _totalGets);
            long misses = Volatile.Read(ref _totalMisses);
            float hitRate = gets > 0 ? (float)(gets - misses) / gets : 0f;

            return new GASPoolStatistics
            {
                PoolSize = _pool.Count,
                ActiveCount = Volatile.Read(ref _activeCount),
                PeakActive = Volatile.Read(ref _peakActive),
                TotalGets = gets,
                TotalMisses = misses,
                TotalDiscards = Volatile.Read(ref _totalDiscards),
                HitRate = hitRate,
                TargetCapacity = _targetCapacity,
                PeakCapacity = _peakCapacity,
                MaxCapacity = _maxCapacity
            };
        }

        /// <summary>
        /// Resets statistics counters.
        /// </summary>
        public void ResetStatistics()
        {
            Interlocked.Exchange(ref _totalGets, 0);
            Interlocked.Exchange(ref _totalMisses, 0);
            Interlocked.Exchange(ref _totalDiscards, 0);
            int active = Volatile.Read(ref _activeCount);
            Interlocked.Exchange(ref _peakActive, active);
        }

        #endregion
    }

    /// <summary>
    /// Statistics snapshot for a GASPool instance.
    /// </summary>
    public struct GASPoolStatistics
    {
        public int PoolSize;
        public int ActiveCount;
        public int PeakActive;
        public long TotalGets;
        public long TotalMisses;
        public long TotalDiscards;
        public float HitRate;
        public int TargetCapacity;
        public int PeakCapacity;
        public int MaxCapacity;

        public override string ToString() =>
            $"Pool={PoolSize} Active={ActiveCount} Peak={PeakActive} Hit={HitRate:P0}";
    }

    /// <summary>
    /// Registry for centralized pool management.
    /// </summary>
    public static class GASPoolRegistry
    {
        private static readonly ConcurrentDictionary<Type, object> s_Pools = new();

        public static void Register<T>(GASPool<T> pool) where T : class, IGASPoolable, new()
            => s_Pools[typeof(T)] = pool;

        public static GASPool<T> GetOrCreate<T>() where T : class, IGASPoolable, new()
        {
            if (s_Pools.TryGetValue(typeof(T), out var existing))
                return (GASPool<T>)existing;

            var pool = GASPool<T>.Shared;
            s_Pools[typeof(T)] = pool;
            return pool;
        }

        public static void AggressiveShrinkAll()
        {
            foreach (var kvp in s_Pools)
            {
                kvp.Value.GetType().GetMethod("AggressiveShrink")?.Invoke(kvp.Value, null);
            }
        }

        public static void ClearAll()
        {
            foreach (var kvp in s_Pools)
            {
                kvp.Value.GetType().GetMethod("Clear")?.Invoke(kvp.Value, null);
            }
        }
    }
}