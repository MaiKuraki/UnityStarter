using System;
using CycloneGames.Networking.Maintenance;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    /// <summary>Session-scoped teardown for InteractionRequestReplayWindow. Container state is keyed by entity/actor id, not connection id.</summary>
    public sealed class InteractionReplayTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly InteractionRequestReplayWindow _container;

        public InteractionReplayTeardownTarget(InteractionRequestReplayWindow container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public string MaintenanceId => "interaction.replay.teardown";

        public void TeardownSession()
        {
            _container.Clear();
        }
    }
}
