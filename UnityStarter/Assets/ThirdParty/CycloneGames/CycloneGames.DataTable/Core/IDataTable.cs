using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    /// <summary>Minimal read-only row view used by consumers that do not need key lookup.</summary>
    public interface IDataTableRows<out TRow>
    {
        /// <summary>Gets all rows in source order through a non-array read-only view.</summary>
        IReadOnlyList<TRow> All { get; }

        /// <summary>Gets the number of rows.</summary>
        int Count { get; }
    }

    /// <summary>
    /// Read-only access to a typed table with a caller-selected primary-key type.
    /// Implementations provide expected O(1) dictionary lookup and allocation-free reads.
    /// </summary>
    public interface IDataTable<TKey, TRow> : IDataTableRows<TRow>
    {
        /// <summary>Gets a row or throws <see cref="KeyNotFoundException"/> when the key is absent.</summary>
        TRow Get(TKey id);

        /// <summary>Gets a row or returns the default row value when the key is absent.</summary>
        TRow GetOrDefault(TKey id);

        /// <summary>Attempts to get a row without allocating.</summary>
        bool TryGet(TKey id, out TRow row);
    }

    /// <summary>Convenience contract for integer-keyed rows implementing <see cref="IDataRow"/>.</summary>
    public interface IDataTable<TRow> : IDataTable<int, TRow> where TRow : IDataRow
    {
    }
}
