using System;
using CycloneGames.EventBus.Core;

namespace CycloneGames.EventBus.Runtime
{
    /// <summary>
    /// Builds a ready-to-use <see cref="EventBusContext"/> from an <see cref="EventBusConfiguration"/>.
    /// A command backend other than the built-in <see cref="InProcessCommandPublisher"/> is supplied
    /// through <see cref="WithCommandPublisherFactory"/>, so Core/Runtime never reference an
    /// integration assembly directly.
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
        /// Installs a command-publisher factory that returns the <see cref="ICommandPublisher"/> the
        /// context should use (for example a DI-container-backed publisher, or an adapter over a
        /// third-party router). If none is set, the builder falls back to
        /// <see cref="InProcessCommandPublisher"/>.
        /// </summary>
        public EventBusBuilder WithCommandPublisherFactory(
            Func<EventBusConfiguration, ICommandPublisher> factory)
        {
            _commandPublisherFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public EventBusContext Build()
        {
            ICommandPublisher commandPublisher = _commandPublisherFactory != null
                ? _commandPublisherFactory(_configuration)
                : new InProcessCommandPublisher(
                    _configuration.CommandQueueCapacity,
                    _configuration.CommandOverflowPolicy);

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
