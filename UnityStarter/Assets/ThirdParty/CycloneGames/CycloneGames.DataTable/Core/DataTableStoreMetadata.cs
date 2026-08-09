namespace CycloneGames.DataTable
{
    /// <summary>Allocation-free atomic metadata view of one store state.</summary>
    public readonly struct DataTableStoreMetadata
    {
        internal DataTableStoreMetadata(
            bool isInitialized,
            long generation,
            DataTableRevision revision,
            long revisionSequenceHighWatermark,
            int activeReaderCount)
        {
            IsInitialized = isInitialized;
            Generation = generation;
            Revision = revision;
            RevisionSequenceHighWatermark = revisionSequenceHighWatermark;
            ActiveReaderCount = activeReaderCount;
        }

        public bool IsInitialized { get; }

        public long Generation { get; }

        public DataTableRevision Revision { get; }

        /// <summary>
        /// Highest revision sequence accepted by this store, including before an explicit reset.
        /// </summary>
        public long RevisionSequenceHighWatermark { get; }

        public int ActiveReaderCount { get; }
    }
}
