using System;
using CycloneGames.Networking.Maintenance;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>Session-scoped teardown for GASNetworkStateBuffer. Container state is keyed by entity/actor id, not connection id.</summary>
    public sealed class GASStateTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly GASNetworkStateBuffer _container;

        public GASStateTeardownTarget(GASNetworkStateBuffer container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public string MaintenanceId => "gas.state.teardown";

        public void TeardownSession()
        {
            _container.Clear();
        }
    }
}
