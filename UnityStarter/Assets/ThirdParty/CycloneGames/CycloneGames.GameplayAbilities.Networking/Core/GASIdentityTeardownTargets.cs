using System;
using CycloneGames.Networking.Maintenance;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Session-scoped epoch rollover for the authority identity map. Unlike a
    /// plain Clear(), ResetEpoch requires a new non-zero stream epoch; the
    /// composition root supplies it at construction time.
    /// </summary>
    public sealed class GASAuthorityIdentityTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly GASAuthorityIdentityMap _map;
        private readonly uint _newStreamEpoch;

        public GASAuthorityIdentityTeardownTarget(GASAuthorityIdentityMap map, uint newStreamEpoch)
        {
            if (newStreamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));

            _map = map ?? throw new ArgumentNullException(nameof(map));
            _newStreamEpoch = newStreamEpoch;
        }

        public string MaintenanceId => "gas.identity.teardown";

        public void TeardownSession()
        {
            _map.ResetEpoch(_newStreamEpoch);
        }
    }

    /// <summary>Session-scoped epoch rollover for the replica identity map.</summary>
    internal sealed class GASReplicaIdentityTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly GASReplicaIdentityMap _map;
        private readonly uint _newStreamEpoch;

        public GASReplicaIdentityTeardownTarget(GASReplicaIdentityMap map, uint newStreamEpoch)
        {
            if (newStreamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));

            _map = map ?? throw new ArgumentNullException(nameof(map));
            _newStreamEpoch = newStreamEpoch;
        }

        public string MaintenanceId => "gas.replica.teardown";

        public void TeardownSession()
        {
            _map.ResetEpoch(_newStreamEpoch);
        }
    }
}
