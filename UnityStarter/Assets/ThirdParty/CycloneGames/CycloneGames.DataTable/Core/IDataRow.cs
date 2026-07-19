namespace CycloneGames.DataTable
{
    /// <summary>
    /// Exposes the primary key of a row when a generated model can implement a framework interface.
    /// </summary>
    /// <typeparam name="TKey">The stable primary-key type.</typeparam>
    public interface IDataRow<out TKey>
    {
        /// <summary>Gets the primary key, which must be unique within one table.</summary>
        TKey Id { get; }
    }

    /// <summary>
    /// Convenience contract for the common integer-keyed generated-row shape.
    /// Backends that cannot implement this interface can use <see cref="DataTable{TKey, TRow}"/>
    /// with an explicit key selector.
    /// </summary>
    public interface IDataRow : IDataRow<int>
    {
    }
}
