#if VCONTAINER_PRESENT
using System;
using VContainer;

namespace CycloneGames.UIFramework.Runtime.Integrations
{
    public sealed class VContainerWindowBinder : IUIWindowBinder, IDisposable
    {
        private readonly IObjectResolver _resolver;
        private readonly Func<Type, object> _serviceResolver;
        private readonly Func<Type, IUIPresenter> _presenterResolver;
        private bool _disposed;

        public VContainerWindowBinder(IObjectResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            
            _serviceResolver = ResolveService;
            _presenterResolver = ResolvePresenter;
            
            UIPresenterFactory.CustomFactory = _presenterResolver;
            UIServiceLocator.PushResolver(_serviceResolver);
        }

        private IUIPresenter ResolvePresenter(Type presenterType)
        {
            try
            {
                return (IUIPresenter)_resolver.Resolve(presenterType);
            }
            catch (VContainerException)
            {
                return null;
            }
        }

        private object ResolveService(Type serviceType)
        {
            try
            {
                return _resolver.Resolve(serviceType);
            }
            catch (VContainerException)
            {
                return null;
            }
        }

        public void OnWindowCreated(UIWindow window)
        {
            if (window == null) return;
            _resolver.Inject(window);
        }

        public void OnWindowDestroying(UIWindow window)
        {
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UIServiceLocator.PopResolver();
            
            if (UIPresenterFactory.CustomFactory == _presenterResolver)
            {
                UIPresenterFactory.CustomFactory = null;
            }
        }
    }
}
#endif
