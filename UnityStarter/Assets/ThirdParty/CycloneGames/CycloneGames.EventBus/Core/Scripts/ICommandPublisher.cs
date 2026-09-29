using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Directed, possibly asynchronous command port. Unlike <see cref="EventBus{T}"/> this is a
    /// narrow capability that stays BCL-only, so a third-party router can be adapted onto it from
    /// outside Core. <see cref="InProcessCommandPublisher"/> is the no-dependency fallback, and
    /// <c>EventBusBuilder.WithCommandPublisherFactory</c> is how a composition root substitutes
    /// another implementation.
    /// </summary>
    public interface ICommandPublisher
    {
        ValueTask PublishAsync<TCommand>(
            in TCommand command,
            CancellationToken cancellationToken = default)
            where TCommand : struct;
    }
}
