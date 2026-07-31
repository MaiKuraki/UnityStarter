#if VCONTAINER_PRESENT
using System;
using CycloneGames.Cheat.Core;
using CycloneGames.Logging;
using VContainer;
using VContainer.Unity;

namespace CycloneGames.Cheat.Runtime.Integrations.VContainer
{
    public sealed class CheatVContainerInstaller : IInstaller
    {
        private readonly Func<IObjectResolver, ILogWriter> _logWriterFactory;

        public CheatVContainerInstaller()
        {
        }

        public CheatVContainerInstaller(Func<IObjectResolver, ILogWriter> logWriterFactory)
        {
            _logWriterFactory = logWriterFactory ?? throw new ArgumentNullException(nameof(logWriterFactory));
        }

        public void Install(IContainerBuilder builder)
        {
            builder.Register<CheatCommandRuntime>(resolver =>
            {
                if (_logWriterFactory != null)
                {
                    ILogWriter writer = _logWriterFactory(resolver);
                    return new CheatCommandRuntime(
                        CheatCommandRuntime.DefaultMaximumConcurrentCommandCount,
                        writer);
                }

                return new CheatCommandRuntime();
            }, Lifetime.Singleton)
                .As<ICheatCommandRuntime>()
                .As<ICheatCommandPublisher>()
                .As<ICheatCommandControl>()
                .As<ICheatCommandAdmissionPublisher>()
                .As<ICheatLogWriterConfigurable>();

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
