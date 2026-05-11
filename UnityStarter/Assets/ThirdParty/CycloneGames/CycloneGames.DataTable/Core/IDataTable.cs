using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Read-only, zero-allocation access to a typed data table.
    /// Implementations guarantee O(1) lookup by primary key.
    /// </summary>
    public interface IDataTable<T> where T : IDataRow
    {
        /// <summary>O(1) lookup. Throws if key is missing.</summary>
        T Get(int id);

        /// <summary>O(1) lookup. Returns default(T) if key is missing.</summary>
        T GetOrDefault(int id);

        /// <summary>O(1) lookup, no allocation.</summary>
        bool TryGet(int id, out T row);

        /// <summary>All rows in insertion order (usually sorted by Id).</summary>
        IReadOnlyList<T> All { get; }

        /// <summary>Number of rows.</summary>
        int Count { get; }
    }
}
