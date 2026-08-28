using System;
using CycloneGames.EventBus.Core;

namespace CycloneGames.EventBus.Runtime
{
    /// <summary>
    /// Builds a ready-to-use <see cref="EventBusContext"/> from an <see cref="EventBusConfiguration"/>.
    /// The VitalRouter backend is only constructed by the VitalRouter integration assembly; here, the
    /// builder resolves the backend through an injected factory so Core/Runtime never reference
    /// VitalRouter directly.
    /// </summary>
    public sealed class EventBusBuilder
    {
        private EventBusConfiguration _configuration = EventBusConfiguration.Default;
        private Func<EventBusConfiguration, ICommandPublisher> _commandPublisherFactory;

        public EventBusBuilder WithConfiguration(EventBusConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            return this;
        }

        /// <summary>
        /// Installs a custom command-publisher factory that returns an <see cref="ICommandPublisher"/>
        /// implementation (for example a DI-container-backed publisher). If none is set, the builder
        /// falls back to <see cref="InProcessCommandPublisher"/>.
        /// </summary>
        public EventBusBuilder WithCommandPublisherFactory(
            Func<EventBusConfiguration, ICommandPublisher> factory)
        {
            _commandPublisherFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public EventBusContext Build()
        {
            ICommandPublisher commandPublisher;
            if (_commandPublisherFactory != null)
            {
                commandPublisher = _commandPublisherFactory(_configuration);
            }
            else if (_configuration.CommandBackend == CommandBackend.VitalRouter)
            {
                // The VitalRouter adapter cannot satisfy the struct-only ICommandPublisher port
                // (it requires VitalRouter.ICommand), so it is not routable through
                // EventBusContext.Commands. Fail loudly instead of silently building the in-process
                // backend while the config claims VitalRouter.
                throw new InvalidOperationException(
                    "CommandBackend.VitalRouter is not wired through EventBusContext.Commands: the "
                    + "VitalRouter adapter requires commands to implement VitalRouter.ICommand, so it "
                    + "cannot satisfy the struct-only ICommandPublisher port. Use "
                    + "VitalRouterCommandPublisher directly instead of configuring it as the command "
                    + "backend.");
            }
            else
            {
                commandPublisher = new InProcessCommandPublisher(
                    _configuration.CommandQueueCapacity,
                    _configuration.CommandOverflowPolicy);
            }

            // Construction of the context never fails after resources are created, so no rollback is
            // needed beyond disposing the publisher if an unexpected error occurs.
            try
            {
                return new EventBusContext(_configuration, commandPublisher);
            }
            catch
            {
                if (commandPublisher is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                throw;
            }
        }
    }
}
