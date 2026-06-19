#if VCONTAINER_PRESENT
using System;
using CycloneGames.Cheat.Core;
using VContainer;
using VContainer.Unity;

namespace CycloneGames.Cheat.Runtime.Integrations.VContainer
{
    public sealed class CheatVContainerInstaller : IInstaller
    {
        private readonly Func<IObjectResolver, ICheatLogger> _loggerFactory;

        public CheatVContainerInstaller(Func<IObjectResolver, ICheatLogger> loggerFactory = null)
        {
            _loggerFactory = loggerFactory;
        }

        public void Install(IContainerBuilder builder)
        {
            builder.Register<ICheatCommandRuntime>(resolver =>
            {
                ICheatLogger logger = _loggerFactory != null
                    ? _loggerFactory(resolver)
                    : new UnityDebugCheatLogger();
                return new CheatCommandRuntime(logger);
            }, Lifetime.Singleton)
                .As<ICheatCommandRuntime>()
                .As<ICheatCommandPublisher>()
                .As<ICheatCommandControl>();

            builder.RegisterDisposeCallback(resolver =>
            {
                if (resolver.TryResolve<ICheatCommandRuntime>(out var runtime))
                {
                    runtime.Dispose();
                }
            });
        }
    }
}
#endif
