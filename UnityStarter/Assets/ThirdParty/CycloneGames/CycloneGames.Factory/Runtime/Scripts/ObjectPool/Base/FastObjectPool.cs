using System;
using System.Collections.Generic;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// A lightweight, high-performance object pool designed for high-frequency objects like particles and projectiles.
    /// Features: 
    /// - No locks (Main-thread only).
    /// - No Dictionary tracking.
    /// - Smart auto-shrink logic based on usage peaks.
    /// - Minimal GC overhead.
    /// </summary>
    public abstract class FastObjectPool<T> : IMemoryPool<T> where T : class
    {
        protected readonly Stack<T> _pool;

        public int NumTotal => NumActive + NumInactive;
        public int NumActive { get; protected set; }
        public int NumInactive => _pool.Count;
        public Type ItemType => typeof(T);

        /// <summary>
        /// Maximum number of inactive items allowed in the pool. -1 means unlimited.
        /// </summary>
        public int MaxCapacity { get; set; } = -1;

        /// <summary>
        /// Minimum number of items to keep in the pool, preventing over-shrinking during low activity.
        /// </summary>
        public int MinCapacity { get; set; } = 16;

        // How many despawns to wait before checking if we should shrink.
        private const int kCheckInterval = 128;
        // The pool tries to keep capacity at PeakActive * BufferRatio.
        private const float kBufferRatio = 1.25f;
        // Limit how many items can be destroyed in a single frame/check to prevent spikes.
        private const int kMaxDestroyPerCheck = 8;

        private int _peakActiveSinceLastCheck = 0;
        private int _despawnCounter = 0;

        protected FastObjectPool(int initialCapacity = 16)
        {
            _pool = new Stack<T>(initialCapacity);
            _peakActiveSinceLastCheck = 0;
            // If initial capacity is provided, treat it as a suggestion for MinCapacity too, unless specified otherwise.
            if (initialCapacity > 16) MinCapacity = initialCapacity;
        }

        public T Spawn()
        {
            T item;
            while (_pool.Count > 0)
            {
                item = _pool.Pop();
                if (IsValid(item))
                {
                    NumActive++;
                    TrackPeak();
                    OnSpawn(item);
                    return item;
                }
                // If item is invalid (e.g. destroyed externally), it's just dropped here.
            }

            // Pool empty or all popped items were invalid
            item = CreateNew();
            NumActive++;
            TrackPeak();
            OnSpawn(item);
            return item;
        }

        private void TrackPeak()
        {
            if (NumActive > _peakActiveSinceLastCheck)
            {
                _peakActiveSinceLastCheck = NumActive;
            }
        }

        public void Despawn(T item)
        {
            // Crucial check: If the item is already destroyed (Unity fake null), discard it.
            if (!IsValid(item))
            {
                if (NumActive > 0) NumActive--;
                return;
            }

            NumActive--;

            // If pool is too full, destroy immediately to prevent overflow.
            if (MaxCapacity > 0 && _pool.Count >= MaxCapacity)
            {
                OnDespawn(item);
                DestroyItem(item);
                return;
            }

            OnDespawn(item);
            _pool.Push(item);

            _despawnCounter++;
            if (_despawnCounter >= kCheckInterval)
            {
                PerformSmartShrink();
                _despawnCounter = 0;
            }
        }

        public void Despawn(object obj)
        {
            if (obj is T t) Despawn(t);
        }

        public void Clear()
        {
            foreach (var item in _pool)
            {
                if (IsValid(item)) DestroyItem(item);
            }
            _pool.Clear();
            NumActive = 0;
            _peakActiveSinceLastCheck = 0;
        }

        // Abstract methods for subclass
        protected abstract T CreateNew();
        protected abstract void OnSpawn(T item);
        protected abstract void OnDespawn(T item);

        /// <summary>
        /// Checks if the item is valid and safe to use.
        /// Subclasses can override this to handle Unity's "fake null" for destroyed objects.
        /// </summary>
        protected virtual bool IsValid(T item)
        {
            return item != null;
        }

        // Optional hook for destroying an item (e.g. Object.Destroy or Dispose)
        protected virtual void DestroyItem(T item)
        {
            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// Evaluates current usage and gently trims the pool if it's holding too many unused items.
        /// </summary>
        private void PerformSmartShrink()
        {
            // Calculate target capacity based on the peak usage we saw recently.
            // e.g., if peak was 100, we keep 125.
            int targetTotal = (int)(_peakActiveSinceLastCheck * kBufferRatio);

            // Ensure we respect the minimum capacity floor.
            targetTotal = Math.Max(targetTotal, MinCapacity);

            int currentTotal = NumActive + NumInactive;
            if (currentTotal > targetTotal)
            {
                int excess = currentTotal - targetTotal;
                int safeToRemove = Math.Min(excess, _pool.Count);

                int toRemove = Math.Min(safeToRemove, kMaxDestroyPerCheck);

                if (toRemove > 0)
                {
                    ShrinkBy(toRemove);
                }
            }

            // Decay the peak tracker slowly instead of resetting to current active.
            // This acts as a "memory" of recent high load.
            // A decay factor ensures we don't shrink too aggressively after a busy period ends.
            // Math.Max ensures we never drop below current active count.
            _peakActiveSinceLastCheck = Math.Max(NumActive, (int)(_peakActiveSinceLastCheck * 0.5f));
        }

        public void Resize(int size)
        {
            if (NumInactive < size) ExpandBy(size - NumInactive);
            else if (NumInactive > size) ShrinkBy(NumInactive - size);
        }

        public void ExpandBy(int num)
        {
            for (int i = 0; i < num; i++)
            {
                T item = CreateNew();
                OnDespawn(item);
                _pool.Push(item);
            }
        }

        public void ShrinkBy(int num)
        {
            for (int i = 0; i < num && _pool.Count > 0; i++)
            {
                T item = _pool.Pop();
                if (IsValid(item)) DestroyItem(item);
            }
        }
    }
}