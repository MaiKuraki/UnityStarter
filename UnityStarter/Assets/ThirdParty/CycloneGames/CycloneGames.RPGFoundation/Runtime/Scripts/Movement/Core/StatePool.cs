using System;
using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Generic state pool that works for both 2D and 3D states.
    /// </summary>
    public static class StatePool<TState> where TState : class
    {
        private static List<Action> _clearActions = new List<Action>();

        public static T GetState<T>() where T : TState, new()
        {
            return StateInstanceCache<T>.Instance;
        }

        public static void Clear()
        {
            foreach (var action in _clearActions)
            {
                action?.Invoke();
            }
            _clearActions.Clear();
        }

        private static class StateInstanceCache<T> where T : TState, new()
        {
            public static T Instance;

            static StateInstanceCache()
            {
                CreateInstance();
            }

            public static void CreateInstance()
            {
                Instance = new T();
                StatePool<TState>.RegisterClearAction(() => Instance = default(T));
            }
        }

        private static void RegisterClearAction(Action clearAction)
        {
            _clearActions.Add(clearAction);
        }
    }
}