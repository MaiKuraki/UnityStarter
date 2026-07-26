namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Optional owner-thread capability for bounded inactive-item maintenance.
    /// Implementations never reclaim active items.
    /// </summary>
    public interface IBoundedPoolMaintenance : IMemoryPool
    {
        int TrimInactiveStep(int targetInactiveCount, int maxWork);
    }
}
