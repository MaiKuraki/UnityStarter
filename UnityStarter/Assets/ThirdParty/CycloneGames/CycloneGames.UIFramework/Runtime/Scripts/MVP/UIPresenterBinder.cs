using System;
using System.Collections.Generic;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Creates explicitly registered MVP presenter bindings for selected windows.
    /// </summary>
    /// <remarks>
    /// Registrations belong to this binder instance. Register them at the composition
    /// root before opening windows. The binder does not perform reflection discovery and
    /// does not assume whether a presenter is owned by a DI container or by the caller.
    /// The release delegate defines that ownership policy explicitly.
    /// </remarks>
    public sealed class UIPresenterBinder : IUIWindowBinder
    {
        private readonly struct PresenterRegistration
        {
            public PresenterRegistration(
                Func<UIWindowBindingContext, IUIPresenter> factory,
                Action<IUIPresenter> release)
            {
                Factory = factory;
                Release = release;
            }

            public Func<UIWindowBindingContext, IUIPresenter> Factory { get; }

            public Action<IUIPresenter> Release { get; }
        }

        private sealed class PresenterBinding : IUIWindowBinding
        {
            private IUIPresenter _presenter;
            private Action<IUIPresenter> _release;

            public PresenterBinding(IUIPresenter presenter, Action<IUIPresenter> release)
            {
                _presenter = presenter;
                _release = release;
            }

            public void OnWindowStateChanged(WindowStateCallbackType state)
            {
                IUIPresenter presenter = _presenter;
                if (presenter == null)
                {
                    return;
                }

                switch (state)
                {
                    case WindowStateCallbackType.OnStartOpen:
                        presenter.OnViewOpening();
                        break;
                    case WindowStateCallbackType.OnFinishedOpen:
                        presenter.OnViewOpened();
                        break;
                    case WindowStateCallbackType.OnStartClose:
                        presenter.OnViewClosing();
                        break;
                    case WindowStateCallbackType.OnFinishedClose:
                        presenter.OnViewClosed();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown window state callback.");
                }
            }

            public void Dispose()
            {
                IUIPresenter presenter = _presenter;
                Action<IUIPresenter> release = _release;
                if (presenter == null)
                {
                    return;
                }

                _presenter = null;
                _release = null;
                release(presenter);
            }
        }

        private readonly Dictionary<string, PresenterRegistration> _registrations;

        public UIPresenterBinder(int initialCapacity = 16)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _registrations = new Dictionary<string, PresenterRegistration>(initialCapacity, StringComparer.Ordinal);
        }

        public bool LogMissingPresenterMappings { get; set; }

        public void Register(
            string windowName,
            Func<IUIPresenter> factory,
            Action<IUIPresenter> release)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterCore(windowName, _ => factory(), release);
        }

        public void Register(string windowName, Func<IUIPresenter> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterCore(windowName, _ => factory(), presenter => presenter.Dispose());
        }

        public void RegisterContextual(
            string windowName,
            Func<UIWindowBindingContext, IUIPresenter> factory,
            Action<IUIPresenter> release)
        {
            RegisterCore(windowName, factory, release);
        }

        public void RegisterContextual(
            string windowName,
            Func<UIWindowBindingContext, IUIPresenter> factory)
        {
            RegisterCore(windowName, factory, presenter => presenter.Dispose());
        }

        public void Register<TPresenter>(
            string windowName,
            Func<TPresenter> factory,
            Action<TPresenter> release)
            where TPresenter : class, IUIPresenter
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            RegisterCore(
                windowName,
                _ => factory(),
                presenter => release((TPresenter)presenter));
        }

        public void Register<TPresenter>(string windowName, Func<TPresenter> factory)
            where TPresenter : class, IUIPresenter
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterCore(
                windowName,
                _ => factory(),
                presenter => ((TPresenter)presenter).Dispose());
        }

        public void RegisterContextual<TPresenter>(
            string windowName,
            Func<UIWindowBindingContext, TPresenter> factory,
            Action<TPresenter> release)
            where TPresenter : class, IUIPresenter
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            RegisterCore(
                windowName,
                context => factory(context),
                presenter => release((TPresenter)presenter));
        }

        public void RegisterContextual<TPresenter>(
            string windowName,
            Func<UIWindowBindingContext, TPresenter> factory)
            where TPresenter : class, IUIPresenter
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterCore(
                windowName,
                context => factory(context),
                presenter => ((TPresenter)presenter).Dispose());
        }

        public void Register<TPresenter>(string windowName)
            where TPresenter : class, IUIPresenter, new()
        {
            RegisterCore(
                windowName,
                _ => new TPresenter(),
                presenter => ((TPresenter)presenter).Dispose());
        }

        public bool Unregister(string windowName)
        {
            return !string.IsNullOrEmpty(windowName) && _registrations.Remove(windowName);
        }

        public void ClearRegistrations()
        {
            _registrations.Clear();
        }

        public IUIWindowBinding Bind(UIWindowBindingContext context)
        {
            UIWindow window = context.Window;
            if (window == null)
            {
                throw new ArgumentException("The binding context must contain a live window.", nameof(context));
            }

            if (context.UIService == null)
            {
                throw new ArgumentException("The binding context must contain a UI service.", nameof(context));
            }

            if (!_registrations.TryGetValue(window.WindowId, out PresenterRegistration registration))
            {
                if (LogMissingPresenterMappings)
                {
                    CLogger.LogWarning($"[UIPresenterBinder] No presenter registration exists for window '{window.WindowId}'.");
                }

                return null;
            }

            IUIPresenter presenter = registration.Factory(context);
            if (presenter == null)
            {
                throw new InvalidOperationException(
                    $"The presenter factory for window '{window.WindowId}' returned null.");
            }

            try
            {
                presenter.SetUIService(context.UIService);
                presenter.SetView(window);
                return new PresenterBinding(presenter, registration.Release);
            }
            catch (Exception bindingException)
            {
                try
                {
                    registration.Release(presenter);
                }
                catch (Exception releaseException)
                {
                    throw new AggregateException(
                        $"Presenter binding and rollback both failed for window '{window.WindowId}'.",
                        bindingException,
                        releaseException);
                }

                throw;
            }
        }

        private void RegisterCore(
            string windowName,
            Func<UIWindowBindingContext, IUIPresenter> factory,
            Action<IUIPresenter> release)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                throw new ArgumentException("Window name cannot be null or empty.", nameof(windowName));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            _registrations.Add(windowName, new PresenterRegistration(factory, release));
        }
    }
}
