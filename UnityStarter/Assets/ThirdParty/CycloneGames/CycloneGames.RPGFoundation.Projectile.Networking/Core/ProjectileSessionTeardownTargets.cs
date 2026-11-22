using System;
using CycloneGames.Networking.Maintenance;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>Session-scoped teardown for ProjectileNetworkSnapshotHistory. Container state is keyed by entity/actor id, not connection id.</summary>
    public sealed class ProjectileHistoryTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly ProjectileNetworkSnapshotHistory _container;

        public ProjectileHistoryTeardownTarget(ProjectileNetworkSnapshotHistory container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public string MaintenanceId => "projectile.history.teardown";

        public void TeardownSession()
        {
            _container.Clear();
        }
    }
}
