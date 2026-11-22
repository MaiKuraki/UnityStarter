using System;
using CycloneGames.Networking.Maintenance;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    /// <summary>Session-scoped teardown for MovementTimeDilationAuditor. Container state is keyed by entity/actor id, not connection id.</summary>
    public sealed class MovementAuditTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly MovementTimeDilationAuditor _container;

        public MovementAuditTeardownTarget(MovementTimeDilationAuditor container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public string MaintenanceId => "movement.audit.teardown";

        public void TeardownSession()
        {
            _container.Clear();
        }
    }
}
