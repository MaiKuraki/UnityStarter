using System;
using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public static class StatePool
    {
        private static readonly Dictionary<Type, MovementStateBase> _statePool = new Dictionary<Type, MovementStateBase>();

        public static T GetState<T>() where T : MovementStateBase, new()
        {
            Type type = typeof(T);
            if (!_statePool.ContainsKey(type))
            {
                _statePool[type] = new T();
            }
            return (T)_statePool[type];
        }

        public static void Clear()
        {
            _statePool.Clear();
        }
    }
}
