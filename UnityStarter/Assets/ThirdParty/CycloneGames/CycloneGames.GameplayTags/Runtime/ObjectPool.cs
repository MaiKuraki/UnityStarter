using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Runtime
{
    public class CustomObjectPool<T> where T : class, new()
    {
        private readonly Stack<T> _stack = new Stack<T>();
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;

        public CustomObjectPool(Action<T> onGet = null, Action<T> onRelease = null)
        {
            _onGet = onGet;
            _onRelease = onRelease;
        }

        public T Get()
        {
            T element;
            if (_stack.Count == 0)
            {
                element = new T();
            }
            else
            {
                element = _stack.Pop();
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
            _stack.Push(element);
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