using System;
using CycloneGames.Networking.Replication;
using CycloneGames.Networking.Security;
using CycloneGames.Networking.Session;

namespace CycloneGames.Networking.Maintenance
{
    /// <summary>
    /// Built-in maintenance targets for containers owned by Core itself.
    /// Composition roots register these instead of writing adapter glue; satellite
    /// packages register their own targets with the ids listed in
    /// NetworkMaintenanceRequirements.
    /// </summary>
    public sealed class NetworkStateCacheReleaseTarget : INetworkConnectionReleaseTarget
    {
        private readonly NetworkReplicationStateCache _cache;

        public NetworkStateCacheReleaseTarget(NetworkReplicationStateCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public string MaintenanceId => "statecache.release";

        public void ReleaseConnection(int connectionId)
        {
            _cache.RemoveConnection(connectionId);
        }
    }

    public sealed class NetworkRateLimiterReleaseTarget : INetworkConnectionReleaseTarget
    {
        private readonly RateLimiter _limiter;

        public NetworkRateLimiterReleaseTarget(RateLimiter limiter)
        {
            _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        }

        public string MaintenanceId => "ratelimiter.release";

        public void ReleaseConnection(int connectionId)
        {
            _limiter.RemoveConnection(connectionId);
        }
    }

    public sealed class NetworkSecurityReplayReleaseTarget : INetworkConnectionReleaseTarget
    {
        private readonly NetworkReplayGuard _replayGuard;

        public NetworkSecurityReplayReleaseTarget(NetworkReplayGuard replayGuard)
        {
            _replayGuard = replayGuard ?? throw new ArgumentNullException(nameof(replayGuard));
        }

        public string MaintenanceId => "security.release";

        public void ReleaseConnection(int connectionId)
        {
            _replayGuard.RemoveConnection(connectionId);
        }
    }

    public sealed class NetworkAckStoreTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly NetworkSnapshotAckStore _store;

        public NetworkAckStoreTeardownTarget(NetworkSnapshotAckStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public string MaintenanceId => "ackstore.teardown";

        public void TeardownSession()
        {
            _store.Clear();
        }
    }

    /// <summary>
    /// Clears the session directory (published session roster) at session shutdown.
    /// </summary>
    public sealed class NetworkSessionDirectoryTeardownTarget : INetworkSessionTeardownTarget
    {
        private readonly NetworkSessionDirectory _directory;

        public NetworkSessionDirectoryTeardownTarget(NetworkSessionDirectory directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public string MaintenanceId => "session.roster.teardown";

        public void TeardownSession()
        {
            _directory.Clear();
        }
    }
}
