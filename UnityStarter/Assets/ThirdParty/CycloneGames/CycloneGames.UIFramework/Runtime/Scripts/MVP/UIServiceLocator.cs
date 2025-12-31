using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.UIFramework.Runtime
{
    public static class UIServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new(32);
        private static readonly Dictionary<Type, Func<object>> _factories = new(16);

        // Resolver stack for hierarchical DI scopes (root at bottom, current scene at top)
        private static readonly List<ResolverEntry> _resolverStack = new(4);

        private static readonly object _lock = new();

        // Note: struct with Dictionary reference - modifying Cache works because Dictionary is reference type.
        // Even though the struct is copied, the Cache field points to the same Dictionary instance.
        private struct ResolverEntry
        {
            public Func<Type, object> Resolver;
            public Dictionary<Type, object> Cache;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Register<T>(T instance) where T : class
        {
            if (instance == null) return;
            lock (_lock)
            {
                _services[typeof(T)] = instance;
            }
        }

        public static void RegisterFactory<T>(Func<T> factory) where T : class
        {
            if (factory == null) return;
            lock (_lock)
            {
                _factories[typeof(T)] = () => factory();
            }
        }

        public static void PushResolver(Func<Type, object> resolver)
        {
            if (resolver == null) return;
            lock (_lock)
            {
                _resolverStack.Add(new ResolverEntry
                {
                    Resolver = resolver,
                    Cache = new Dictionary<Type, object>(16)
                });
            }
        }

        public static void PopResolver()
        {
            lock (_lock)
            {
                int count = _resolverStack.Count;
                if (count > 0)
                {
                    var entry = _resolverStack[count - 1];
                    entry.Cache.Clear();
                    _resolverStack.RemoveAt(count - 1);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Get<T>() where T : class
        {
            return Get(typeof(T)) as T;
        }

        public static object Get(Type type)
        {
            if (type == null) return null;

            lock (_lock)
            {
                // Fast path: local registration
                if (_services.TryGetValue(type, out var service))
                    return service;

                // Resolver stack: iterate from top (most recent scope) to bottom
                for (int i = _resolverStack.Count - 1; i >= 0; i--)
                {
                    var entry = _resolverStack[i];

                    if (entry.Cache.TryGetValue(type, out var cached))
                        return cached;

                    try
                    {
                        var resolved = entry.Resolver(type);
                        if (resolved != null)
                        {
                            entry.Cache[type] = resolved;
                            return resolved;
                        }
                    }
                    catch
                    {
                        // Resolution failed, try next resolver
                    }
                }

                // Factory fallback
                if (_factories.TryGetValue(type, out var factory))
                {
                    var instance = factory();
                    _services[type] = instance;
                    return instance;
                }
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRegistered<T>() where T : class
        {
            return IsRegistered(typeof(T));
        }

        public static bool IsRegistered(Type type)
        {
            lock (_lock)
            {
                if (_services.ContainsKey(type) || _factories.ContainsKey(type))
                    return true;

                for (int i = _resolverStack.Count - 1; i >= 0; i--)
                {
                    if (_resolverStack[i].Cache.ContainsKey(type))
                        return true;
                }
            }
            return false;
        }

        public static void Unregister<T>() where T : class
        {
            lock (_lock)
            {
                _services.Remove(typeof(T));
                _factories.Remove(typeof(T));
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _services.Clear();
                _factories.Clear();
                for (int i = 0; i < _resolverStack.Count; i++)
                {
                    _resolverStack[i].Cache.Clear();
                }
                _resolverStack.Clear();
            }
        }

        public static void ClearResolverCaches()
        {
            lock (_lock)
            {
                for (int i = 0; i < _resolverStack.Count; i++)
                {
                    _resolverStack[i].Cache.Clear();
                }
            }
        }
    }
}
