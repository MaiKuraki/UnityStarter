using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Default MVP Window Binder. When registered with UIManager, this binder
    /// automatically watches for window creation and destruction.
    /// 
    /// Usage:
    /// uIManager.RegisterWindowBinder(new UIPresenterBinder());
    /// 
    /// It scans all loaded assemblies for Presenters decorated with [UIPresenterBind("WindowName")]
    /// and automatically creates/destroys the presenter linked to that window instance.
    /// </summary>
    public class UIPresenterBinder : IUIWindowBinder
    {
        private readonly Dictionary<string, Type> _presenterMap = new Dictionary<string, Type>(32);
        private readonly Dictionary<UIWindow, IUIPresenter> _activePresenters = new Dictionary<UIWindow, IUIPresenter>(16);
        private IUIService _uiService;

        public UIPresenterBinder()
        {
            InitializeMap();
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
            _presenterMap[windowName] = typeof(TPresenter);
        }

        private void InitializeMap()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in assemblies)
                {
                    // Skip generic system assemblies to speed up loading
                    if (assembly.FullName.StartsWith("System.") || assembly.FullName.StartsWith("mscorlib") || assembly.FullName.StartsWith("UnityEngine")) continue;
                    
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        var attrs = type.GetCustomAttributes(typeof(UIPresenterBindAttribute), false);
                        if (attrs != null && attrs.Length > 0)
                        {
                            if (!typeof(IUIPresenter).IsAssignableFrom(type))
                            {
                                CLogger.LogWarning($"[UIPresenterBinder] Type {type.Name} has UIPresenterBindAttribute but does not implement IUIPresenter.");
                                continue;
                            }

                            foreach (UIPresenterBindAttribute attr in attrs)
                            {
                                if (_presenterMap.ContainsKey(attr.WindowName))
                                {
                                    CLogger.LogWarning($"[UIPresenterBinder] Multiple Presenters bound to window: {attr.WindowName}. Overwriting with {type.Name}.");
                                }
                                _presenterMap[attr.WindowName] = type;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CLogger.LogError($"[UIPresenterBinder] Failed to reflect assemblies for Presenter bindings: {ex.Message}");
            }
        }

        public void OnWindowCreated(UIWindow window)
        {
            if (window == null) return;
            
            if (_presenterMap.TryGetValue(window.WindowName, out Type presenterType))
            {
                // Create the Presenter via UIPresenterFactory which handles DI and attributes
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
            else
            {
                // While it is perfectly fine for a window to NOT have a presenter (Non-MVP approach),
                // if a user typed [UIPresenterBind("WrongName")], this is the only place we can hint at it.
                // We log it as Info so it doesn't pollute the console for intentionally non-MVP windows.
                CLogger.LogInfo($"[UIPresenterBinder] Documented Window '{window.WindowName}' created without a registered Presenter. (If MVP was expected, check [UIPresenterBind] spelling).");
            }
        }

        public void OnWindowDestroying(UIWindow window)
        {
            if (window == null) return;
            
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
