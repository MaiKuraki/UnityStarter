#if VCONTAINER_PRESENT
using System;
using VContainer;

namespace CycloneGames.UIFramework.Runtime.Integrations
{
    /// <summary>
    /// VContainer integration for UIFramework. Automatically injects dependencies
    /// into UIWindow instances and resolves Presenters from the container.
    /// </summary>
    public sealed class VContainerWindowBinder : IUIWindowBinder
    {
        private readonly IObjectResolver _resolver;
        
        public VContainerWindowBinder(IObjectResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            
            // Override factory to use VContainer for Presenter creation
            UIPresenterFactory.CustomFactory = ResolvePresenter;
        }
        
        private IUIPresenter ResolvePresenter(Type presenterType)
        {
            try
            {
                return (IUIPresenter)_resolver.Resolve(presenterType);
            }
            catch (VContainerException)
            {
                // Fallback: type not registered, create manually
                return null;
            }
        }
        
        /// <summary>
        /// Injects dependencies into the window using VContainer.
        /// </summary>
        public void OnWindowCreated(UIWindow window)
        {
            if (window == null) return;
            _resolver.Inject(window);
        }
        
        /// <summary>
        /// Called when window is being destroyed. Override for custom cleanup.
        /// </summary>
        public void OnWindowDestroying(UIWindow window)
        {
            // VContainer handles disposal automatically for scoped registrations
        }
        
        /// <summary>
        /// Clears the custom factory. Call on dispose to prevent memory leaks.
        /// </summary>
        public void Dispose()
        {
            if (UIPresenterFactory.CustomFactory == ResolvePresenter)
            {
                UIPresenterFactory.CustomFactory = null;
            }
        }
    }
}
#endif
