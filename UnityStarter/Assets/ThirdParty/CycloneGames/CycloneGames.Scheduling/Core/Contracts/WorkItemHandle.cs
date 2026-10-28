namespace CycloneGames.Scheduling.Core
{
    /// <summary>
    /// Identifies one accepted registration in the scheduler that created it.
    /// Handles are not transferable between scheduler instances.
    /// </summary>
    public readonly struct WorkItemHandle
    {
        public int Slot { get; }
        public uint Generation { get; }
        public bool IsDefault => Generation == 0;

        internal WorkItemHandle(int slot, uint generation)
        {
            Slot = slot;
            Generation = generation;
        }
    }
}
