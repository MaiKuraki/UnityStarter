using System;
using System.Collections.Generic;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Lightweight service locator for use when no DI framework is present.
    /// </summary>
    public static class UIServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>(16);
        private static readonly Dictionary<Type, Func<object>> _factories = new Dictionary<Type, Func<object>>(16);
        private static readonly object _lock = new object();

        /// <summary>
        /// Registers a singleton service instance.
        /// </summary>
        public static void Register<T>(T instance) where T : class
        {
            if (instance == null) return;
            lock (_lock)
            {
                _services[typeof(T)] = instance;
            }
        }

        /// <summary>
        /// Registers a factory for lazy instantiation.
        /// </summary>
        public static void RegisterFactory<T>(Func<T> factory) where T : class
        {
            if (factory == null) return;
            lock (_lock)
            {
                _factories[typeof(T)] = () => factory();
            }
        }

        /// <summary>
        /// Gets a registered service. Returns null if not found.
        /// </summary>
        public static T Get<T>() where T : class
        {
            return Get(typeof(T)) as T;
        }

        /// <summary>
        /// Gets a registered service by type. Returns null if not found.
        /// </summary>
        public static object Get(Type type)
        {
            if (type == null) return null;

            lock (_lock)
            {
                if (_services.TryGetValue(type, out var service))
                {
                    return service;
                }

                if (_factories.TryGetValue(type, out var factory))
                {
                    var instance = factory();
                    _services[type] = instance;
                    return instance;
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a service is registered.
        /// </summary>
        public static bool IsRegistered<T>() where T : class
        {
            lock (_lock)
            {
                return _services.ContainsKey(typeof(T)) || _factories.ContainsKey(typeof(T));
            }
        }

        /// <summary>
        /// Unregisters a service.
        /// </summary>
        public static void Unregister<T>() where T : class
        {
            lock (_lock)
            {
                _services.Remove(typeof(T));
                _factories.Remove(typeof(T));
            }
        }

        /// <summary>
        /// Clears all registered services. Call on scene transitions if needed.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _services.Clear();
                _factories.Clear();
            }
        }
    }
}
