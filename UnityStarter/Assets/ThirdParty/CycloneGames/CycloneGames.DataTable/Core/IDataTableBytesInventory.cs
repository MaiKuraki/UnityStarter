namespace CycloneGames.DataTable
{
    /// <summary>
    /// Exposes a stable, allocation-free payload-name inventory for validation and tooling.
    /// </summary>
    /// <remarks>
    /// <see cref="Count"/> and <see cref="GetTableName"/> must both be O(1). Implementations must
    /// not emulate indexed access by restarting a collection enumerator. Every current payload
    /// must appear exactly once. Names must be stable and must resolve through the paired
    /// <see cref="IDataTableBytesProvider"/>. Callers must not retain an index across mutations
    /// and must coordinate inventory traversal with the provider owner.
    /// </remarks>
    public interface IDataTableBytesInventory
    {
        int Count { get; }

        string GetTableName(int index);
    }
}
