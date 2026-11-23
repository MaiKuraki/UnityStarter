using System;
using CycloneGames.Networking.Maintenance;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
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
