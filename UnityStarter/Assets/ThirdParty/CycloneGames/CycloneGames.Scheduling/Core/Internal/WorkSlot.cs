namespace CycloneGames.Scheduling.Core
{
    internal struct WorkSlot
    {
        public IWorkItem Item;
        public int Next;
        public int Previous;
        public uint Generation;
    }
}
