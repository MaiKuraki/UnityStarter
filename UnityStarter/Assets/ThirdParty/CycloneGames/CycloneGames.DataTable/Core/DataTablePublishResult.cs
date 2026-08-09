namespace CycloneGames.DataTable
{
    public enum DataTablePublishStatus : byte
    {
        Invalid = 0,
        Committed = 1,
        Superseded = 2,
        NonMonotonicRevision = 3
    }

    /// <summary>
    /// Allocation-free outcome of a publication attempt. Superseded and non-monotonic revisions
    /// are normal rejection results and never consume the candidate.
    /// </summary>
    public readonly struct DataTablePublishResult
    {
        internal DataTablePublishResult(
            DataTablePublishStatus status,
            long expectedGeneration,
            long observedGeneration,
            long observedRevisionSequence)
        {
            Status = status;
            ExpectedGeneration = expectedGeneration;
            ObservedGeneration = observedGeneration;
            ObservedRevisionSequence = observedRevisionSequence;
        }

        public DataTablePublishStatus Status { get; }

        public long ExpectedGeneration { get; }

        /// <summary>
        /// The committed generation on success, or the current generation that rejected the
        /// attempt.
        /// </summary>
        public long ObservedGeneration { get; }

        /// <summary>The store's revision-sequence high-water mark after the attempt.</summary>
        public long ObservedRevisionSequence { get; }

        public bool IsCommitted => Status == DataTablePublishStatus.Committed;
    }
}
