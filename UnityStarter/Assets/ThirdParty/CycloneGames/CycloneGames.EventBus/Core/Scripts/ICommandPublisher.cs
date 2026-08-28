using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Directed, possibly asynchronous command port. Unlike <see cref="EventBus{T}"/> this is a
    /// narrow capability that stays BCL-only; a VitalRouter adapter implements it against a real
    /// router, and <see cref="InProcessCommandPublisher"/> is the no-dependency fallback.
    /// </summary>
    public interface ICommandPublisher
    {
        ValueTask PublishAsync<TCommand>(
            in TCommand command,
            CancellationToken cancellationToken = default)
            where TCommand : struct;
    }
}
