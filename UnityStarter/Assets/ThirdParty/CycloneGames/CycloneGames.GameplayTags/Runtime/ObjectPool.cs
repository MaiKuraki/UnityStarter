using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Runtime
{
    /// <summary>
    /// A generic object pool with capacity management, smart shrinking, and statistics tracking.
    /// Designed for memory-safe usage on low-end devices.
    /// </summary>
    public class CustomObjectPool<T> where T : class, new()
    {
        #region Pool Configuration
        
        // Platform-adaptive default capacities
#if UNITY_IOS || UNITY_ANDROID || UNITY_SWITCH
        private const int kDefaultMaxCapacity = 64;   // Mobile/Switch: conservative memory
        private const int kDefaultMinCapacity = 8;
#elif UNITY_STANDALONE || UNITY_PS4 || UNITY_PS5 || UNITY_XBOXONE || UNITY_GAMECORE
        private const int kDefaultMaxCapacity = 256;  // PC/Console: larger pools
        private const int kDefaultMinCapacity = 16;
#else
        private const int kDefaultMaxCapacity = 128;  // Default fallback
        private const int kDefaultMinCapacity = 8;
#endif
        private const int kShrinkCheckInterval = 32;
        private const int kMaxShrinkPerCheck = 4;
        private const float kBufferRatio = 1.5f;
        
        #endregion
        
        private readonly Stack<T> _stack = new Stack<T>();
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        
        // Capacity management
        private int _maxCapacity = kDefaultMaxCapacity;
        private int _minCapacity = kDefaultMinCapacity;
        private int _activeCount = 0;
        private int _peakActiveSinceLastCheck = 0;
        private int _releaseCounter = 0;
        
        // Statistics
        private long _totalGets = 0;
        private long _totalMisses = 0;
        private int _peakActive = 0;

        public CustomObjectPool(Action<T> onGet = null, Action<T> onRelease = null, int maxCapacity = kDefaultMaxCapacity, int minCapacity = kDefaultMinCapacity)
        {
            _onGet = onGet;
            _onRelease = onRelease;
            _maxCapacity = maxCapacity > 0 ? maxCapacity : kDefaultMaxCapacity;
            _minCapacity = minCapacity > 0 ? minCapacity : kDefaultMinCapacity;
        }
        
        /// <summary>
        /// Sets the maximum pool capacity. Set to -1 for unlimited (not recommended).
        /// </summary>
        public void SetMaxCapacity(int maxCapacity) => _maxCapacity = maxCapacity;
        
        /// <summary>
        /// Sets the minimum pool capacity to prevent over-shrinking.
        /// </summary>
        public void SetMinCapacity(int minCapacity) => _minCapacity = minCapacity;
        
        /// <summary>
        /// Gets pool statistics for profiling.
        /// </summary>
        public (int PoolSize, int ActiveCount, int PeakActive, long TotalGets, long TotalMisses, float HitRate) GetStatistics()
        {
            float hitRate = _totalGets > 0 ? (float)(_totalGets - _totalMisses) / _totalGets : 0f;
            lock (_stack)
            {
                return (_stack.Count, _activeCount, _peakActive, _totalGets, _totalMisses, hitRate);
            }
        }
        
        /// <summary>
        /// Resets pool statistics.
        /// </summary>
        public void ResetStatistics()
        {
            _totalGets = 0;
            _totalMisses = 0;
            _peakActive = _activeCount;
        }
        
        /// <summary>
        /// Pre-warms the pool with a specified number of instances.
        /// </summary>
        public void WarmPool(int count)
        {
            lock (_stack)
            {
                for (int i = 0; i < count && (_maxCapacity < 0 || _stack.Count < _maxCapacity); i++)
                {
                    _stack.Push(new T());
                }
            }
        }
        
        /// <summary>
        /// Clears the entire pool.
        /// </summary>
        public void ClearPool()
        {
            lock (_stack)
            {
                _stack.Clear();
                _activeCount = 0;
                _peakActiveSinceLastCheck = 0;
                _releaseCounter = 0;
            }
        }
        
        /// <summary>
        /// Aggressively shrinks the pool to minimum capacity.
        /// Call during scene transitions to free memory.
        /// </summary>
        public void AggressiveShrink()
        {
            lock (_stack)
            {
                while (_stack.Count > _minCapacity)
                {
                    _stack.Pop();
                }
                _peakActiveSinceLastCheck = _activeCount;
            }
        }

        public T Get()
        {
            T element;
            _totalGets++;
            
            lock (_stack)
            {
                if (_stack.Count == 0)
                {
                    element = new T();
                    _totalMisses++;
                }
                else
                {
                    element = _stack.Pop();
                }
                
                _activeCount++;
                if (_activeCount > _peakActiveSinceLastCheck)
                {
                    _peakActiveSinceLastCheck = _activeCount;
                }
                if (_activeCount > _peakActive)
                {
                    _peakActive = _activeCount;
                }
            }
            
            _onGet?.Invoke(element);
            return element;
        }

        public void Release(T element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            
            _onRelease?.Invoke(element);
            
            lock (_stack)
            {
                if (_activeCount > 0) _activeCount--;
                
                // Enforce max capacity to prevent memory overflow
                if (_maxCapacity > 0 && _stack.Count >= _maxCapacity)
                {
                    // Discard this instance instead of pooling
                    return;
                }
                
                _stack.Push(element);
                
                // Periodic smart shrink check
                _releaseCounter++;
                if (_releaseCounter >= kShrinkCheckInterval)
                {
                    _releaseCounter = 0;
                    PerformSmartShrink();
                }
            }
        }
        
        private void PerformSmartShrink()
        {
            // Must be called inside lock
            int targetCapacity = (int)(_peakActiveSinceLastCheck * kBufferRatio);
            targetCapacity = Math.Max(targetCapacity, _minCapacity);
            
            int currentTotal = _activeCount + _stack.Count;
            if (currentTotal > targetCapacity && _stack.Count > _minCapacity)
            {
                int excess = currentTotal - targetCapacity;
                int toRemove = Math.Min(excess, kMaxShrinkPerCheck);
                toRemove = Math.Min(toRemove, _stack.Count - _minCapacity);
                
                for (int i = 0; i < toRemove && _stack.Count > _minCapacity; i++)
                {
                    _stack.Pop();
                }
            }
            
            // Decay the peak to allow gradual shrinking
            _peakActiveSinceLastCheck = Math.Max(_activeCount, (int)(_peakActiveSinceLastCheck * 0.5f));
        }
    }

    public static class Pools
    {
        public static class ListPool<T>
        {
            private static readonly CustomObjectPool<List<T>> s_Pool = new CustomObjectPool<List<T>>(null, l => l.Clear());

            public static List<T> Get() => s_Pool.Get();

            public static void Release(List<T> toRelease) => s_Pool.Release(toRelease);
            
            public static PooledObject<List<T>> Get(out List<T> value)
            {
                var G = new PooledObject<List<T>>(Get(), s_Pool);
                value = G.Value;
                return G;
            }
            
            /// <summary>
            /// Sets the maximum pool capacity for this ListPool type.
            /// </summary>
            public static void SetMaxCapacity(int maxCapacity) => s_Pool.SetMaxCapacity(maxCapacity);
            
            /// <summary>
            /// Sets the minimum pool capacity for this ListPool type.
            /// </summary>
            public static void SetMinCapacity(int minCapacity) => s_Pool.SetMinCapacity(minCapacity);
            
            /// <summary>
            /// Pre-warms the pool.
            /// </summary>
            public static void WarmPool(int count) => s_Pool.WarmPool(count);
            
            /// <summary>
            /// Clears the pool.
            /// </summary>
            public static void ClearPool() => s_Pool.ClearPool();
            
            /// <summary>
            /// Aggressively shrinks the pool to minimum capacity.
            /// </summary>
            public static void AggressiveShrink() => s_Pool.AggressiveShrink();
            
            /// <summary>
            /// Gets pool statistics.
            /// </summary>
            public static (int PoolSize, int ActiveCount, int PeakActive, long TotalGets, long TotalMisses, float HitRate) GetStatistics() 
                => s_Pool.GetStatistics();
        }
        
        public static class GameplayTagContainerPool
        {
            private static readonly CustomObjectPool<GameplayTagContainer> s_Pool = new CustomObjectPool<GameplayTagContainer>(null, c => c.Clear());
            
            public static GameplayTagContainer Get() => s_Pool.Get();

            public static void Release(GameplayTagContainer toRelease) => s_Pool.Release(toRelease);
            
            public static PooledObject<GameplayTagContainer> Get(out GameplayTagContainer value)
            {
                var G = new PooledObject<GameplayTagContainer>(Get(), s_Pool);
                value = G.Value;
                return G;
            }
            
            /// <summary>
            /// Sets the maximum pool capacity.
            /// </summary>
            public static void SetMaxCapacity(int maxCapacity) => s_Pool.SetMaxCapacity(maxCapacity);
            
            /// <summary>
            /// Aggressively shrinks the pool to minimum capacity.
            /// </summary>
            public static void AggressiveShrink() => s_Pool.AggressiveShrink();
            
            /// <summary>
            /// Gets pool statistics.
            /// </summary>
            public static (int PoolSize, int ActiveCount, int PeakActive, long TotalGets, long TotalMisses, float HitRate) GetStatistics() 
                => s_Pool.GetStatistics();
        }
    }
    
    /// <summary>
    /// Implement 'using' for pooled objects
    /// </summary>
    public readonly struct PooledObject<T> : IDisposable where T : class, new()
    {
        public readonly T Value;
        private readonly CustomObjectPool<T> _pool;

        internal PooledObject(T value, CustomObjectPool<T> pool)
        {
            Value = value;
            _pool = pool;
        }

        void IDisposable.Dispose()
        {
            _pool.Release(Value);
        }
    }
}