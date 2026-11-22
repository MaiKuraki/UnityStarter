using System;

namespace CycloneGames.Networking.Maintenance
{
    /// <summary>
    /// Session-scoped reclamation target for containers keyed by entity or actor
    /// id rather than connection id. TeardownSession clears the whole container
    /// at session shutdown; it is not a per-connection operation.
    /// </summary>
    public interface INetworkSessionTeardownTarget
    {
        string MaintenanceId { get; }
        void TeardownSession();
    }
}
