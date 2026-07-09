using System;
using System.Collections.Generic;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Default MVP Window Binder. When registered with UIManager, this binder
    /// automatically watches for window creation and destruction.
    /// 
    /// Usage:
    /// uiService.RegisterWindowBinder(new UIPresenterBinder());
    /// 
    /// Presenters are resolved from explicit bindings registered by generated code,
    /// composition roots, or manual calls to RegisterMapping.
    /// </summary>
    public class UIPresenterBinder : IUIWindowBinder
    {
        private static readonly Dictionary<string, Type> _globalPresenterMap = new Dictionary<string, Type>(32);
        private static readonly object _globalMapLock = new object();

        private readonly Dictionary<string, Type> _presenterMap;
        private readonly Dictionary<UIWindow, IUIPresenter> _activePresenters = new Dictionary<UIWindow, IUIPresenter>(16);
        private IUIService _uiService;

        public UIPresenterBinder()
        {
            lock (_globalMapLock)
            {
                _presenterMap = new Dictionary<string, Type>(_globalPresenterMap);
            }
        }

        public bool LogMissingPresenterMappings { get; set; }

        public static void RegisterGlobalMapping<TPresenter>(string windowName) where TPresenter : class, IUIPresenter
        {
            RegisterGlobalMapping(windowName, typeof(TPresenter));
        }

        public static void RegisterGlobalMapping(string windowName, Type presenterType)
        {
            ValidateMapping(windowName, presenterType);

            lock (_globalMapLock)
            {
                _globalPresenterMap[windowName] = presenterType;
            }
        }

        public static bool UnregisterGlobalMapping(string windowName)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                return false;
            }

            lock (_globalMapLock)
            {
                return _globalPresenterMap.Remove(windowName);
            }
        }

        public static void ClearGlobalMappings()
        {
            lock (_globalMapLock)
            {
                _globalPresenterMap.Clear();
            }
        }

        /// <summary>
        /// Provides the IUIService reference to presenters so they can use NavigateTo / NavigateBack.
        /// Call this once after UIService is initialized.
        /// </summary>
        public void SetUIService(IUIService uiService)
        {
            _uiService = uiService;
        }

        /// <summary>
        /// Explicitly add a mapping without reflection if needed for zero-allocation strictness.
        /// </summary>
        public void RegisterMapping<TPresenter>(string windowName) where TPresenter : class, IUIPresenter
        {
            RegisterMapping(windowName, typeof(TPresenter));
        }

        public void RegisterMapping(string windowName, Type presenterType)
        {
            ValidateMapping(windowName, presenterType);

            _presenterMap[windowName] = presenterType;
        }

        private static void ValidateMapping(string windowName, Type presenterType)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                throw new ArgumentException("Window name cannot be null or empty.", nameof(windowName));
            }

            if (presenterType == null)
            {
                throw new ArgumentNullException(nameof(presenterType));
            }

            if (!typeof(IUIPresenter).IsAssignableFrom(presenterType))
            {
                throw new ArgumentException("Presenter type must implement IUIPresenter.", nameof(presenterType));
            }
        }

        public void OnWindowCreated(UIWindow window)
        {
            if (window == null)
            {
                return;
            }
            
            if (TryGetPresenterType(window.WindowName, out Type presenterType))
            {
                // Create the Presenter via UIPresenterFactory which handles DI and explicit registrations.
                IUIPresenter presenter = UIPresenterFactory.Create(presenterType);
                if (presenter != null)
                {
                    _activePresenters[window] = presenter;
                    
                    // Bind the view
                    presenter.SetView(window);
                    presenter.SetUIService(_uiService);
                    
                    // Hook into lifecycle manually or passively if UIWindow exposes events.
                    // For now, UIWindow doesn't expose public events for opening/closing, 
                    // To maintain true decoupling, UIWindow needs a way to signal its state to the Binder, 
                    // OR the binder can just rely on the existing Virtual methods being invoked by linking to the window.
                    // IMPORTANT: Currently we must inject into UIWindow's lifecycle or let UIWindow notify us.
                }
                else
                {
                    CLogger.LogError($"[UIPresenterBinder] Failed to create Presenter of type {presenterType.Name} for window {window.WindowName}.");
                }
            }
            else if (LogMissingPresenterMappings)
            {
                CLogger.LogInfo($"[UIPresenterBinder] Window '{window.WindowName}' created without a registered Presenter. If MVP was expected, check generated or manual presenter registration.");
            }
        }

        private bool TryGetPresenterType(string windowName, out Type presenterType)
        {
            if (_presenterMap.TryGetValue(windowName, out presenterType))
            {
                return true;
            }

            lock (_globalMapLock)
            {
                if (!_globalPresenterMap.TryGetValue(windowName, out presenterType))
                {
                    return false;
                }
            }

            _presenterMap[windowName] = presenterType;
            return true;
        }

        public void OnWindowDestroying(UIWindow window)
        {
            if (window == null)
            {
                return;
            }
            
            if (_activePresenters.TryGetValue(window, out IUIPresenter presenter))
            {
                presenter.Dispose();
                _activePresenters.Remove(window);
            }
        }

        /// <summary>
        /// Forwards UIWindow lifecycle calls to the Presenter.
        /// This method must be called by the framework when window state changes.
        /// </summary>
        public void OnWindowStateChanged(UIWindow window, WindowStateCallbackType state)
        {
            if (_activePresenters.TryGetValue(window, out IUIPresenter presenter))
            {
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
                }
            }
        }
    }
}
