using UnityEngine;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// <see cref="ObjectPool{TParam1, TValue}"/> for <see cref="Object"/> items whose instances can be
    /// destroyed by another owner; <see cref="Component"/> is the common case.
    /// </summary>
    /// <remarks>
    /// The core pool cannot see Unity's fake-null: a destroyed instance is still a live reference there.
    /// This adapter answers through <see cref="Object"/>'s operator, so a destroyed instance is quarantined
    /// on the next spawn or despawn instead of being handed back or reset. Main-thread only.
    /// </remarks>
    public sealed class MonoObjectPool<TParam1, TValue> : ObjectPool<TParam1, TValue>
        where TValue : Object, IPoolable<TParam1, TValue>
    {
        public MonoObjectPool(IFactory<TValue> factory, int initialCapacity = 0, int maxCapacity = -1)
            : base(factory, initialCapacity, maxCapacity)
        {
        }

        public MonoObjectPool(
            IFactory<TValue> factory,
            PoolCapacitySettings capacitySettings,
            bool validateUniqueOwnership = true)
            : base(factory, capacitySettings, validateUniqueOwnership)
        {
        }

        protected override bool IsValid(TValue item)
        {
            // UnityEngine.Object's operator reports a destroyed instance as equal to null.
            return item != null;
        }
    }
}
