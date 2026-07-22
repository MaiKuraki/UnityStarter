using System.Threading;

namespace CycloneGames.Persistence
{
    public readonly struct PersistenceWriteContext
    {
        internal PersistenceWriteContext(
            int contentVersion,
            PersistenceLimits limits,
            CancellationToken cancellationToken)
        {
            ContentVersion = contentVersion;
            Limits = limits;
            CancellationToken = cancellationToken;
        }

        public int ContentVersion { get; }

        public PersistenceLimits Limits { get; }

        /// <summary>
        /// The caller token. A synchronous codec may inspect it at bounded work boundaries.
        /// </summary>
        public CancellationToken CancellationToken { get; }
    }

    public readonly struct PersistenceReadContext
    {
        internal PersistenceReadContext(
            int contentVersion,
            PersistenceLimits limits,
            CancellationToken cancellationToken)
        {
            ContentVersion = contentVersion;
            Limits = limits;
            CancellationToken = cancellationToken;
        }

        public int ContentVersion { get; }

        public PersistenceLimits Limits { get; }

        /// <summary>
        /// The caller token. A synchronous codec may inspect it at bounded work boundaries.
        /// </summary>
        public CancellationToken CancellationToken { get; }
    }
}
