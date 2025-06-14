using System;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Defines the basic, non-generic contract for a memory pool.
    /// </summary>
    public interface IMemoryPool
    {
        /// <summary>
        /// Gets the total number of items managed by the pool (both active and inactive).
        /// </summary>
        int NumTotal { get; }

        /// <summary>
        /// Gets the number of items currently spawned and in use.
        /// </summary>
        int NumActive { get; }

        /// <summary>
        /// Gets the number of items currently available in the pool, ready to be spawned.
        /// </summary>
        int NumInactive { get; }

        /// <summary>
        /// Gets the <see cref="Type"/> of items managed by this pool.
        /// </summary>
        Type ItemType { get; }

        /// <summary>
        /// Adjusts the number of inactive items in the pool to match the desired size.
        /// If the current count is less than the desired size, new items are created.
        /// If the current count is greater, excess items are destroyed.
        /// </summary>
        /// <param name="desiredPoolSize">The target number of inactive items.</param>
        void Resize(int desiredPoolSize);

        /// <summary>
        /// Destroys all items in the pool, both active and inactive. The pool becomes empty.
        /// </summary>
        void Clear();

        /// <summary>
        /// Increases the number of inactive items in the pool by a specific amount.
        /// </summary>
        /// <param name="numToAdd">The number of new items to create and add to the pool.</param>
        void ExpandBy(int numToAdd);

        /// <summary>
        /// Decreases the number of inactive items in the pool by a specific amount.
        /// </summary>
        /// <param name="numToRemove">The number of inactive items to destroy.</param>
        void ShrinkBy(int numToRemove);

        /// <summary>
        /// Returns an item to the pool. The item must be of the correct type for the pool.
        /// </summary>
        /// <param name="obj">The object to return to the pool.</param>
        void Despawn(object obj);
    }

    /// <summary>
    /// Extends the base memory pool with a strongly-typed Despawn method.
    /// </summary>
    /// <typeparam name="TValue">The type of item to despawn.</typeparam>
    public interface IDespawnableMemoryPool<in TValue> : IMemoryPool
    {
        /// <summary>
        /// Returns a strongly-typed item to the pool.
        /// </summary>
        /// <param name="item">The item to return.</param>
        void Despawn(TValue item);
    }

    /// <summary>
    /// Defines a memory pool that can spawn items without parameters.
    /// </summary>
    /// <typeparam name="TValue">The type of item to spawn.</typeparam>
    public interface IMemoryPool<TValue> : IDespawnableMemoryPool<TValue>
    {
        /// <summary>
        /// Retrieves an item from the pool, creating a new one if necessary.
        /// </summary>
        /// <returns>A spawned item.</returns>
        TValue Spawn();
    }

    /// <summary>
    /// Defines a memory pool that can spawn items using a parameter.
    /// </summary>
    /// <typeparam name="TParam1">The parameter type for spawning.</typeparam>
    /// <typeparam name="TValue">The type of item to spawn.</typeparam>
    public interface IMemoryPool<in TParam1, TValue> : IDespawnableMemoryPool<TValue>
    {
        /// <summary>
        /// Retrieves an item from the pool, creating a new one if necessary, and initializes it with the given parameter.
        /// </summary>
        /// <param name="param">The parameter to use for initialization.</param>
        /// <returns>A spawned and initialized item.</returns>
        TValue Spawn(TParam1 param);
    }
}