using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public sealed class ProjectileEventBuffer
    {
        private readonly ProjectileHitEvent[] _events;

        public ProjectileEventBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _events = new ProjectileHitEvent[capacity];
        }

        public int Capacity
        {
            get
            {
                return _events.Length;
            }
        }

        public int Count { get; private set; }

        public ProjectileHitEvent this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _events[index];
            }
        }

        public bool TryAdd(in ProjectileHitEvent hitEvent)
        {
            if (Count >= _events.Length)
            {
                return false;
            }

            _events[Count] = hitEvent;
            Count++;
            return true;
        }

        public void Clear()
        {
            Count = 0;
        }
    }
}
