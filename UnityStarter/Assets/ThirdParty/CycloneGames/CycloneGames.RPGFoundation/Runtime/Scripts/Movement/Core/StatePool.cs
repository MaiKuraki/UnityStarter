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
            if (StateInstanceCache<T>.Instance == null)
            {
                StateInstanceCache<T>.CreateInstance();
            }
            return StateInstanceCache<T>.Instance;
        }

        public static void Clear()
        {
            foreach (var action in _clearActions)
            {
                action?.Invoke();
            }
        }

        private static class StateInstanceCache<T> where T : TState, new()
        {
            public static T Instance;
            private static bool _clearActionRegistered = false;

            static StateInstanceCache()
            {
                CreateInstance();
            }

            public static void CreateInstance()
            {
                Instance = new T();
                if (!_clearActionRegistered)
                {
                    StatePool<TState>.RegisterClearAction(() => Instance = default(T));
                    _clearActionRegistered = true;
                }
            }
        }

        private static void RegisterClearAction(Action clearAction)
        {
            _clearActions.Add(clearAction);
        }
    }
}