using System;
using CycloneGames.Networking.Maintenance;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    /// <summary>Session-scoped teardown for MovementNetworkInputHistory. Container state is keyed by entity/actor id, not connection id.</summary>
    public sealed class MovementHistoryTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly MovementNetworkInputHistory _container;

        public MovementHistoryTeardownTarget(MovementNetworkInputHistory container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public string MaintenanceId => "movement.history.teardown";

        public void TeardownSession()
        {
            _container.Clear();
        }
    }
}
