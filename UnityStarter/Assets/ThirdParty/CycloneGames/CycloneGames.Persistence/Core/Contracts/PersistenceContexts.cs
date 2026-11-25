using System.Threading;

namespace CycloneGames.Persistence
{
    public readonly struct PersistenceWriteContext
    {
        internal PersistenceWriteContext(
            PersistenceLimits limits,
            CancellationToken cancellationToken)
        {
            Limits = limits;
            CancellationToken = cancellationToken;
        }

        public PersistenceLimits Limits { get; }

        public CancellationToken CancellationToken { get; }
    }

    public readonly struct PersistenceReadContext
    {
        internal PersistenceReadContext(
            PersistenceLimits limits,
            CancellationToken cancellationToken)
        {
            Limits = limits;
            CancellationToken = cancellationToken;
        }

        public PersistenceLimits Limits { get; }

        public CancellationToken CancellationToken { get; }
    }
}
