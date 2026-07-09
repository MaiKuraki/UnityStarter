using System;
using System.Collections.Generic;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Factory for creating Presenter instances from explicit registrations or a DI-backed custom factory.
    /// </summary>
    public static class UIPresenterFactory
    {
        private readonly struct PresenterRegistration
        {
            public readonly Func<IUIPresenter> Factory;
            public readonly Action<IUIPresenter> Injector;

            public PresenterRegistration(
                Func<IUIPresenter> factory,
                Action<IUIPresenter> injector)
            {
                Factory = factory;
                Injector = injector;
            }
        }

        /// <summary>
        /// Custom factory delegate. Set this to integrate with DI frameworks.
        /// When set, it gets the first chance to resolve a Presenter; returning null falls back to explicit registrations.
        /// </summary>
        public static Func<Type, IUIPresenter> CustomFactory { get; set; }

        private static readonly Dictionary<Type, PresenterRegistration> _presenterRegistrations = new Dictionary<Type, PresenterRegistration>(32);
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Registers an explicit presenter factory.
        /// </summary>
        public static void Register<TPresenter>(
            Func<TPresenter> factory,
            Action<TPresenter> injector = null)
            where TPresenter : class, IUIPresenter
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            Func<IUIPresenter> wrappedFactory = () => factory();
            Action<IUIPresenter> wrappedInjector = injector == null
                ? null
                : presenter => injector((TPresenter)presenter);

            lock (_cacheLock)
            {
                _presenterRegistrations[typeof(TPresenter)] = new PresenterRegistration(
                    wrappedFactory,
                    wrappedInjector);
            }
        }

        public static void Register<TPresenter>(
            Action<TPresenter> injector = null)
            where TPresenter : class, IUIPresenter, new()
        {
            Register(() => new TPresenter(), injector);
        }

        public static bool Unregister(Type presenterType)
        {
            if (presenterType == null)
            {
                return false;
            }

            lock (_cacheLock)
            {
                return _presenterRegistrations.Remove(presenterType);
            }
        }

        public static void ClearRegistrations()
        {
            lock (_cacheLock)
            {
                _presenterRegistrations.Clear();
            }
        }

        /// <summary>
        /// Creates a presenter instance of the specified type.
        /// Uses CustomFactory first, then explicit registrations. Missing registrations fail deterministically.
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

            if (TryCreateRegisteredPresenter(presenterType, out IUIPresenter registeredPresenter))
            {
                return registeredPresenter;
            }

            CLogger.LogError($"[UIPresenterFactory] No Presenter registration found for {presenterType.Name}. Register it through UIPresenterFactory.Register or configure CustomFactory.");
            return null;
        }

        private static bool TryCreateRegisteredPresenter(Type presenterType, out IUIPresenter presenter)
        {
            PresenterRegistration registration;
            lock (_cacheLock)
            {
                if (!_presenterRegistrations.TryGetValue(presenterType, out registration))
                {
                    presenter = null;
                    return false;
                }
            }

            try
            {
                presenter = registration.Factory();
                if (presenter == null)
                {
                    CLogger.LogError($"[UIPresenterFactory] Registered factory returned null for {presenterType.Name}.");
                    return true;
                }

                registration.Injector?.Invoke(presenter);

                return true;
            }
            catch (Exception ex)
            {
                CLogger.LogError($"[UIPresenterFactory] Registered factory failed for {presenterType.Name}: {ex.Message}");
                presenter = null;
                return true;
            }
        }
    }
}
