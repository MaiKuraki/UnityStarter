using System;
using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Movement.Core
{
    public sealed class MovementStatePool<TState> where TState : class
    {
        private readonly Dictionary<Type, TState> _states;

        public MovementStatePool(int initialCapacity = 16)
        {
            _states = new Dictionary<Type, TState>(initialCapacity);
        }

        public T Get<T>() where T : TState, new()
        {
            Type key = typeof(T);
            TState state;
            if (_states.TryGetValue(key, out state))
                return (T)state;
            T created = new T();
            _states.Add(key, created);
            return created;
        }

        public int Count
        {
            get { return _states.Count; }
        }

        public void Clear()
        {
            _states.Clear();
        }
    }
}
