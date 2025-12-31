using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Factory for creating Presenter instances. Supports DI framework override
    /// via CustomFactory delegate. 
    /// </summary>
    public static class UIPresenterFactory
    {
        /// <summary>
        /// Custom factory delegate. Set this to integrate with DI frameworks.
        /// When set, bypasses default creation and uses DI container resolution.
        /// </summary>
        public static Func<Type, IUIPresenter> CustomFactory { get; set; }

        // Cache for injectable properties to avoid repeated reflection
        private static readonly Dictionary<Type, PropertyInfo[]> _injectablePropertiesCache = new Dictionary<Type, PropertyInfo[]>(32);
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Creates a presenter instance of the specified type.
        /// Uses CustomFactory if set, otherwise creates via Activator and auto-injects services.
        /// </summary>
        public static TPresenter Create<TPresenter>() where TPresenter : class, IUIPresenter
        {
            return (TPresenter)Create(typeof(TPresenter));
        }

        /// <summary>
        /// Creates a presenter instance of the specified type.
        /// </summary>
        public static IUIPresenter Create(Type presenterType)
        {
            if (presenterType == null)
            {
                CLogger.LogError("[UIPresenterFactory] Cannot create presenter: type is null");
                return null;
            }

            if (CustomFactory != null)
            {
                try
                {
                    var customPresenter = CustomFactory(presenterType);
                    if (customPresenter != null)
                    {
                        return customPresenter;
                    }
                }
                catch (Exception ex)
                {
                    CLogger.LogError($"[UIPresenterFactory] CustomFactory failed for {presenterType.Name}: {ex.Message}");
                    return null;
                }
            }

            IUIPresenter presenter;
            try
            {
                presenter = (IUIPresenter)Activator.CreateInstance(presenterType);
            }
            catch (Exception ex)
            {
                CLogger.LogError($"[UIPresenterFactory] Failed to create {presenterType.Name}. Ensure it has a parameterless constructor. Error: {ex.Message}");
                return null;
            }

            AutoInjectServices(presenter);

            return presenter;
        }

        /// <summary>
        /// Injects services into properties marked with [UIInject] attribute.
        /// </summary>
        private static void AutoInjectServices(object target)
        {
            if (target == null) return;

            var type = target.GetType();
            PropertyInfo[] injectableProperties;

            // Check cache first
            lock (_cacheLock)
            {
                if (!_injectablePropertiesCache.TryGetValue(type, out injectableProperties))
                {
                    // Build cache for this type
                    var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var injectableList = new List<PropertyInfo>(4);

                    for (int i = 0; i < props.Length; i++)
                    {
                        var prop = props[i];
                        if (prop.CanWrite && prop.GetCustomAttribute<UIInjectAttribute>() != null)
                        {
                            injectableList.Add(prop);
                        }
                    }

                    injectableProperties = injectableList.ToArray();
                    _injectablePropertiesCache[type] = injectableProperties;
                }
            }

            // Inject services from UIServiceLocator
            for (int i = 0; i < injectableProperties.Length; i++)
            {
                var prop = injectableProperties[i];
                var service = UIServiceLocator.Get(prop.PropertyType);
                if (service != null)
                {
                    try
                    {
                        prop.SetValue(target, service);
                    }
                    catch (Exception ex)
                    {
                        CLogger.LogWarning($"[UIPresenterFactory] Failed to inject {prop.PropertyType.Name} into {type.Name}.{prop.Name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Clears the reflection cache. Call if types are reloaded at runtime.
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _injectablePropertiesCache.Clear();
            }
        }
    }
}
