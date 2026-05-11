namespace CycloneGames.DataTable
{
    /// <summary>
    /// Marker interface for a single row in a data table.
    /// Every generated config row class must implement this.
    /// </summary>
    public interface IDataRow
    {
        /// <summary>Primary key — unique within the table.</summary>
        int Id { get; }
    }
}
