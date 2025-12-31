#if VCONTAINER_PRESENT
using System;
using VContainer;

namespace CycloneGames.UIFramework.Runtime.Integrations
{
    /// <summary>
    /// Bridges VContainer scope to UIServiceLocator. Register in any LifetimeScope to enable [UIInject] for that scope's services.
    /// Resolver is pushed immediately on construction to ensure availability before any UI operations.
    /// </summary>
    public sealed class UIServiceLocatorBridge : IDisposable
    {
        private readonly Func<Type, object> _resolver;
        private bool _disposed;

        [Inject]
        public UIServiceLocatorBridge(IObjectResolver resolver)
        {
            _resolver = type =>
            {
                try { return resolver.Resolve(type); }
                catch { return null; }
            };
            
            // Push immediately on construction, not deferred to Start()
            UIServiceLocator.PushResolver(_resolver);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UIServiceLocator.PopResolver();
        }
    }
}
#endif
