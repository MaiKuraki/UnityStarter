using System;
using CycloneGames.Networking.Maintenance;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public sealed class MovementHistoryTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly MovementNetworkInputHistory _container;

        public MovementHistoryTeardownTarget(MovementNetworkInputHistory container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public string MaintenanceId { get { return "movement.history.teardown"; } }

        public void TeardownSession()
        {
            _container.Clear();
        }
    }

    public readonly struct MovementSessionTeardownSet
    {
        public readonly INetworkSessionTeardownTarget History;
        public readonly INetworkSessionTeardownTarget Audit;

        public MovementSessionTeardownSet(
            INetworkSessionTeardownTarget history,
            INetworkSessionTeardownTarget audit)
        {
            History = history;
            Audit = audit;
        }

        public void TeardownSession()
        {
            if (History != null) History.TeardownSession();
            if (Audit != null) Audit.TeardownSession();
        }
    }
}
