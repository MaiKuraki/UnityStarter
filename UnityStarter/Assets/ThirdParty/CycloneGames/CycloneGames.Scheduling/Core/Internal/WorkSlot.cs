namespace CycloneGames.Scheduling.Core
{
    /// <summary>
    /// 24 bytes: reference (8), three ints (12), lane byte packed into trailing padding.
    /// A slot is either on one lane queue or on the free list, so Next serves both.
    /// </summary>
    internal struct WorkSlot
    {
        public IWorkItem Item;

        /// <summary>Next slot in the lane queue, or next slot in the free list.</summary>
        public int Next;

        /// <summary>Previous slot in the lane queue. -1 when not queued.</summary>
        public int Previous;

        public uint Generation;

        public byte Lane;
    }
}
