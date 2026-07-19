#if CYCLONEGAMES_HAS_VCONTAINER
using System;
using VContainer;

namespace CycloneGames.UIFramework.Runtime.Integrations
{
    /// <summary>
    /// Injects dependencies into each window through one explicit VContainer scope.
    /// </summary>
    public sealed class VContainerWindowBinder : IUIWindowBinder
    {
        private sealed class InjectedWindowBinding : IUIWindowBinding
        {
            public static readonly InjectedWindowBinding Instance = new InjectedWindowBinding();

            private InjectedWindowBinding()
            {
            }

            public void OnWindowStateChanged(WindowStateCallbackType state)
            {
            }

            public void Dispose()
            {
            }
        }

        private readonly IObjectResolver _resolver;

        public VContainerWindowBinder(IObjectResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public IUIWindowBinding Bind(UIWindowBindingContext context)
        {
            if (context.Window == null)
            {
                throw new ArgumentException("The binding context must contain a live window.", nameof(context));
            }

            if (context.UIService == null)
            {
                throw new ArgumentException("The binding context must contain a UI service.", nameof(context));
            }

            _resolver.Inject(context.Window);
            return InjectedWindowBinding.Instance;
        }
    }
}
#endif
