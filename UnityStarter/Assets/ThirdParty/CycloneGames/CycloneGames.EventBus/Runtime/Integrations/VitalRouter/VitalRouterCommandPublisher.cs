using System;
using System.Threading;
using System.Threading.Tasks;
using VitalRouter;
using CycloneGames.EventBus.Core;

namespace CycloneGames.EventBus.Runtime.Integrations.VitalRouter
{
    /// <summary>
    /// Adapts Core command semantics to a VitalRouter <see cref="Router"/>. VitalRouter requires its
    /// commands to implement <c>VitalRouter.ICommand</c>, which is a stricter constraint than the
    /// Core <see cref="ICommandPublisher"/> port's struct-only constraint. That mismatch means this
    /// adapter is a VitalRouter-typed API rather than a drop-in <see cref="ICommandPublisher"/>
    /// implementation; the struct-only Core port is served by
    /// <see cref="InProcessCommandPublisher"/>.
    ///
    /// The adapter is single-thread-confined like the rest of the package; VitalRouter's own async
    /// ordering (Sequential/Drop/Switch) is preserved by delegating directly to the router.
    /// </summary>
    public sealed class VitalRouterCommandPublisher : IDisposable
    {
        private readonly Router _router;
        private readonly bool _ownsRouter;

        public VitalRouterCommandPublisher(Router router = null)
        {
            _ownsRouter = router == null;
            _router = router ?? new Router();
        }

        public Router Router => _router;

        public ValueTask PublishAsync<TCommand>(
            in TCommand command,
            CancellationToken cancellationToken = default)
            where TCommand : struct, ICommand
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Direct forwarding: no wrapper state machine, and the token reaches the router so
            // its interceptors and subscribers observe mid-dispatch cancellation.
            return _router.PublishAsync(command, cancellationToken);
        }

        public void Dispose()
        {
            if (_ownsRouter && _router is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
