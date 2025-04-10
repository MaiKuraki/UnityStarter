using System;
using System.Threading;

namespace CycloneGames.Factory
{
    public sealed class ObjectPool<TParam1, TValue> : IMemoryPool<TParam1, TValue>, IDisposable
        where TValue : class, IPoolable<TParam1, IMemoryPool>, ITickable, new()
    {
        private struct PoolItem
        {
            public TValue Value;
            public bool IsAvailable;
        }

        private void DestroyItem(ref PoolItem item)
        {
            if (item.Value is IDisposable disposable) disposable.Dispose();
        }

        private readonly IFactory<TValue> _factory;
        private readonly IFactory<TParam1, TValue> _paramFactory;
        // Using arrays for better cache locality and performance 
        private PoolItem[] pool;
        private int[] availableIndices;
        private int availableCount;
        private int activeCount;
        private int totalCount;

        private readonly ReaderWriterLockSlim rwLock = new ReaderWriterLockSlim();

        private int maxObjects;
        private int shrinkThreshold;
        private bool shouldAutoExpand;

        public int NumTotal => totalCount;
        public int NumActive => activeCount;
        public int NumInactive => availableCount;
        public Type ItemType => typeof(TValue);

        public ObjectPool(IFactory<TValue> factory, int initialSize = 5, bool autoExpand = true)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            Initialize(initialSize, autoExpand);
        }

        public ObjectPool(IFactory<TParam1, TValue> paramFactory, int initialSize = 5, bool autoExpand = true)
        {
            _paramFactory = paramFactory ?? throw new ArgumentNullException(nameof(paramFactory));
            Initialize(initialSize, autoExpand);
        }

        public void Initialize(int initialSize = 5, bool bAutoExpand = true)
        {
            if (initialSize <= 0)
                throw new ArgumentException("Initial pool size must be greater than 0", nameof(initialSize));

            maxObjects = initialSize;
            shouldAutoExpand = bAutoExpand;
            shrinkThreshold = initialSize / 2;

            pool = new PoolItem[initialSize];
            availableIndices = new int[initialSize];

            ExpandBy(initialSize);
        }

        public TValue Spawn(TParam1 param)
        {
            if (shouldAutoExpand)
            {
                return SpawnWithAutoExpand(param);
            }

            rwLock.EnterReadLock();
            try
            {
                if (availableCount == 0)
                {
                    rwLock.ExitReadLock();
                    throw new InvalidOperationException("No available objects in the pool");
                }

                return InternalSpawn(param);
            }
            finally
            {
                if (rwLock.IsReadLockHeld) rwLock.ExitReadLock();
            }
        }

        private TValue SpawnWithAutoExpand(TParam1 param)
        {
            rwLock.EnterUpgradeableReadLock();
            try
            {
                if (availableCount == 0)
                {
                    rwLock.EnterWriteLock();
                    try
                    {
                        int newSize = Math.Min(totalCount + maxObjects, int.MaxValue);
                        if (newSize == totalCount)
                        {
                            throw new InvalidOperationException("Pool has reached maximum capacity");
                        }

                        ExpandBy(newSize - totalCount);
                    }
                    finally
                    {
                        rwLock.ExitWriteLock();
                    }
                }

                return InternalSpawn(param);
            }
            finally
            {
                rwLock.ExitUpgradeableReadLock();
            }
        }

        private TValue InternalSpawn(TParam1 param)
        {
            rwLock.EnterWriteLock();
            try
            {
                int index = availableIndices[--availableCount];
                ref PoolItem item = ref pool[index];
                item.IsAvailable = false;
                activeCount++;

                TValue value = item.Value;
                value.OnSpawned(param, this);

                return value;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public void Despawn(TValue item)
        {
            if (item == null)
            {
                return;
            }

            rwLock.EnterWriteLock();
            try
            {
                if (totalCount == 0)
                {
                    throw new InvalidOperationException($"You are trying to despawn an EXISTING object: [{typeof(TValue)}], but the pool is empty, the object maybe not released properly, it may cause memory leak");
                }

                // Linear search for the item (could be optimized with a dictionary if needed)
                for (int i = 0; i < totalCount; i++)
                {
                    if (ReferenceEquals(pool[i].Value, item))
                    {
                        if (pool[i].IsAvailable)
                        {
                            throw new InvalidOperationException("Item already despawned");
                        }

                        item.OnDespawned();

                        pool[i].IsAvailable = true;
                        availableIndices[availableCount++] = i;
                        activeCount--;

                        AutoShrinkIfNeeded();
                        return;
                    }
                }

                throw new InvalidOperationException("Item not found in pool");
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public void Despawn(object obj)
        {
            if (obj is TValue value)
            {
                Despawn(value);
            }
            else
            {
                throw new InvalidOperationException("Object type mismatch");
            }
        }

        public void Resize(int desiredPoolSize)
        {
            if (desiredPoolSize < 0)
                throw new ArgumentOutOfRangeException(nameof(desiredPoolSize), "Size cannot be negative");

            rwLock.EnterWriteLock();
            try
            {
                int difference = desiredPoolSize - totalCount;
                if (difference > 0)
                {
                    ExpandBy(difference);
                }
                else if (difference < 0)
                {
                    ShrinkBy(-difference);
                }
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public void Clear()
        {
            rwLock.EnterWriteLock();
            try
            {
                // Properly dispose of all objects 
                for (int i = 0; i < totalCount; i++)
                {
                    if (!pool[i].IsAvailable)
                    {
                        pool[i].Value.OnDespawned();
                    }
                    DestroyItem(ref pool[i]);
                }

                // Reset counts 
                availableCount = 0;
                activeCount = 0;
                totalCount = 0;

                // Reset arrays 
                Array.Clear(pool, 0, pool.Length);
                Array.Clear(availableIndices, 0, availableIndices.Length);

                // Alternative: Reset arrays to initial capacity 
                // pool = new PoolItem[maxObjects];
                // availableIndices = new int[maxObjects];
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public void ExpandBy(int numToAdd)
        {
            if (numToAdd <= 0) return;

            rwLock.EnterWriteLock();
            try
            {
                if (totalCount + numToAdd > pool.Length)
                {
                    int newSize = Math.Min(totalCount + numToAdd, int.MaxValue);
                    Array.Resize(ref pool, newSize);
                    Array.Resize(ref availableIndices, newSize);
                }

                for (int i = 0; i < numToAdd; i++)
                {
                    int index = totalCount + i;
                    TValue newItem;

                    if (_factory != null)
                    {
                        newItem = _factory.Create();
                    }
                    else if (_paramFactory != null)
                    {
                        newItem = _paramFactory.Create(default(TParam1));
                    }
                    else
                    {
                        throw new InvalidOperationException("No factory provided for object creation");
                    }

                    pool[index] = new PoolItem { Value = newItem, IsAvailable = true };
                    availableIndices[availableCount++] = index;
                }

                totalCount += numToAdd;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public void ShrinkBy(int numToRemove)
        {
            if (numToRemove <= 0) return;

            rwLock.EnterWriteLock();
            try
            {
                if (numToRemove > availableCount)
                {
                    throw new InvalidOperationException("Cannot remove more items than are available");
                }

                // Sort available indices to remove from the end 
                Array.Sort(availableIndices, 0, availableCount);

                // Remove items from the end 
                for (int i = 0; i < numToRemove; i++)
                {
                    int index = availableIndices[--availableCount];

                    DestroyItem(ref pool[index]);   // Release memory
                    pool[index] = default;
                }

                totalCount -= numToRemove;

                // Compact the pool array if needed 
                if (totalCount < pool.Length / 2)
                {
                    Array.Resize(ref pool, totalCount);
                    Array.Resize(ref availableIndices, totalCount);
                }
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        private void AutoShrinkIfNeeded()
        {
            if (availableCount > shrinkThreshold && totalCount > maxObjects)
            {
                int numToRemove = Math.Min(availableCount - shrinkThreshold, totalCount - maxObjects);
                ShrinkBy(numToRemove);
            }
        }

        public void Dispose()
        {
            rwLock.EnterWriteLock();
            try
            {
                Clear();
                rwLock.Dispose();
            }
            finally
            {
                if (rwLock != null) rwLock.ExitWriteLock();
            }
        }

        public void Tick()
        {
            rwLock.EnterReadLock();
            try
            {
                // Iterate through all active items 
                for (int i = 0; i < totalCount; i++)
                {
                    if (!pool[i].IsAvailable)
                    {
                        pool[i].Value.Tick();
                    }
                }
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }
    }
}