using System;
using System.Threading;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Optional process-wide publication point. Composition roots can instead pass a
    /// <see cref="DataTableCatalog"/> explicitly. Publication swaps one immutable snapshot, so
    /// concurrent readers observe either one complete catalog or another complete catalog.
    /// </summary>
    public static class DataTableRegistry
    {
        private sealed class RegistryState
        {
            public RegistryState(DataTableCatalog catalog, bool initialized, long generation)
            {
                Catalog = catalog;
                Initialized = initialized;
                Generation = generation;
            }

            public readonly DataTableCatalog Catalog;
            public readonly bool Initialized;
            public readonly long Generation;
        }

        private static readonly object SyncRoot = new object();
        private static RegistryState _state = new RegistryState(DataTableCatalog.Empty, initialized: false, generation: 0);
        private static long _generationCounter;

        public static DataTableCatalog Current => Volatile.Read(ref _state).Catalog;

        public static bool IsInitialized => Volatile.Read(ref _state).Initialized;

        public static long Generation => Volatile.Read(ref _state).Generation;

        /// <summary>Atomically publishes a complete immutable catalog.</summary>
        public static void Publish(DataTableCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            long generation;
            lock (SyncRoot)
            {
                generation = Interlocked.Increment(ref _generationCounter);
                Volatile.Write(
                    ref _state,
                    new RegistryState(catalog, initialized: true, generation: generation));
            }

            DataTableLogger.LogCommittedInfoNoThrow(
                $"DataTableRegistry published generation {generation} ({catalog.Count} tables).");
        }

        /// <summary>Gets a table or returns null when the published catalog has no such contract.</summary>
        public static TTable Get<TTable>() where TTable : class
        {
            return Current.GetOrDefault<TTable>();
        }

        public static bool TryGet<TTable>(out TTable table) where TTable : class
        {
            return Current.TryGet(out table);
        }

        /// <summary>
        /// Removes the published reference without disposing the previous catalog or its resources.
        /// The owning composition scope must coordinate readers and dispose resources separately.
        /// </summary>
        public static void Reset()
        {
            lock (SyncRoot)
            {
                long generation = Interlocked.Increment(ref _generationCounter);
                Volatile.Write(
                    ref _state,
                    new RegistryState(DataTableCatalog.Empty, initialized: false, generation));
            }
        }
    }
}
