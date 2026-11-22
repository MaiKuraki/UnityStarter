using System;
using CycloneGames.Networking.Maintenance;

namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>Session-scoped teardown for DamageCooldownTracker. Container state is keyed by entity/actor id, not connection id.</summary>
    public sealed class GameplayFrameworkDamageTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly DamageCooldownTracker _container;

        public GameplayFrameworkDamageTeardownTarget(DamageCooldownTracker container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public string MaintenanceId => "gf.damage.teardown";

        public void TeardownSession()
        {
            _container.Clear();
        }
    }
}
