using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Structurally immutable array-backed data table with a key-to-index lookup. Row objects are
    /// not deep-cloned and remain the content owner's responsibility. Construction is a cold-path
    /// operation; successful reads are expected O(1) and do not allocate.
    /// </summary>
    public class DataTable<TKey, TRow> : IDataTable<TKey, TRow>
    {
        private readonly Dictionary<TKey, int> _rowIndicesByKey;
        private readonly TRow[] _rows;
        private readonly ReadOnlyCollection<TRow> _rowsView;

        /// <summary>
        /// Copies <paramref name="rows"/> before indexing it. Null rows, null keys, and duplicate
        /// keys fail construction so <see cref="All"/>, <see cref="Count"/>, and key lookup always
        /// describe the same immutable table.
        /// </summary>
        public DataTable(
            TRow[] rows,
            Func<TRow, TKey> keySelector,
            IEqualityComparer<TKey> comparer = null)
            : this(rows, keySelector, DataTableLoadLimits.Default, comparer, takeOwnership: false)
        {
        }

        public DataTable(
            TRow[] rows,
            Func<TRow, TKey> keySelector,
            DataTableLoadLimits limits,
            IEqualityComparer<TKey> comparer = null)
            : this(rows, keySelector, limits, comparer, takeOwnership: false)
        {
        }

        /// <summary>Copies a list once before indexing it.</summary>
        public DataTable(
            List<TRow> rows,
            Func<TRow, TKey> keySelector,
            IEqualityComparer<TKey> comparer = null)
            : this(
                MaterializeOwned(rows, DataTableLoadLimits.Default),
                keySelector,
                DataTableLoadLimits.Default,
                comparer,
                takeOwnership: true)
        {
        }

        public DataTable(
            List<TRow> rows,
            Func<TRow, TKey> keySelector,
            DataTableLoadLimits limits,
            IEqualityComparer<TKey> comparer = null)
            : this(MaterializeOwned(rows, limits), keySelector, limits, comparer, takeOwnership: true)
        {
        }

        protected DataTable(
            TRow[] rows,
            Func<TRow, TKey> keySelector,
            DataTableLoadLimits limits,
            IEqualityComparer<TKey> comparer,
            bool takeOwnership)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }

            limits.EnsureValid(nameof(limits));
            limits.ValidateRowCount(typeof(TRow).FullName, rows.Length);

            _rows = takeOwnership ? rows : (TRow[])rows.Clone();
            _rowsView = Array.AsReadOnly(_rows);
            _rowIndicesByKey = comparer == null
                ? new Dictionary<TKey, int>(_rows.Length)
                : new Dictionary<TKey, int>(_rows.Length, comparer);

            for (int i = 0; i < _rows.Length; i++)
            {
                TRow row = _rows[i];
                if (row is null)
                {
                    throw new ArgumentException($"Row at index {i} is null.", nameof(rows));
                }

                TKey key = keySelector(row);
                if (key is null)
                {
                    throw new ArgumentException($"Row at index {i} has a null key.", nameof(rows));
                }

                if (!_rowIndicesByKey.TryAdd(key, i))
                {
                    throw new ArgumentException(
                        $"Duplicate key '{key}' at row index {i} in DataTable<{typeof(TKey).Name}, {typeof(TRow).Name}>.",
                        nameof(rows));
                }
            }
        }

        /// <summary>
        /// Takes ownership of a materialized array without copying it. The caller must relinquish
        /// every writable alias and must never mutate the array after this call succeeds.
        /// </summary>
        public static DataTable<TKey, TRow> FromOwnedArray(
            TRow[] rows,
            Func<TRow, TKey> keySelector,
            IEqualityComparer<TKey> comparer = null)
        {
            return new DataTable<TKey, TRow>(
                rows,
                keySelector,
                DataTableLoadLimits.Default,
                comparer,
                takeOwnership: true);
        }

        public static DataTable<TKey, TRow> FromOwnedArray(
            TRow[] rows,
            Func<TRow, TKey> keySelector,
            DataTableLoadLimits limits,
            IEqualityComparer<TKey> comparer = null)
        {
            return new DataTable<TKey, TRow>(rows, keySelector, limits, comparer, takeOwnership: true);
        }

        /// <summary>Materializes an enumerable once and takes ownership of the resulting array.</summary>
        public static DataTable<TKey, TRow> FromEnumerable(
            IEnumerable<TRow> rows,
            Func<TRow, TKey> keySelector,
            IEqualityComparer<TKey> comparer = null)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            TRow[] ownedRows = MaterializeOwned(rows, DataTableLoadLimits.Default);
            return FromOwnedArray(ownedRows, keySelector, comparer);
        }

        public static DataTable<TKey, TRow> FromEnumerable(
            IEnumerable<TRow> rows,
            Func<TRow, TKey> keySelector,
            DataTableLoadLimits limits,
            IEqualityComparer<TKey> comparer = null)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            TRow[] ownedRows = MaterializeOwned(rows, limits);
            return FromOwnedArray(ownedRows, keySelector, limits, comparer);
        }

        public TRow Get(TKey id)
        {
            if (_rowIndicesByKey.TryGetValue(id, out int rowIndex))
            {
                return _rows[rowIndex];
            }

            throw new KeyNotFoundException(
                $"Key '{id}' was not found in DataTable<{typeof(TKey).Name}, {typeof(TRow).Name}> ({Count} rows).");
        }

        public TRow GetOrDefault(TKey id)
        {
            return _rowIndicesByKey.TryGetValue(id, out int rowIndex)
                ? _rows[rowIndex]
                : default;
        }

        public bool TryGet(TKey id, out TRow row)
        {
            if (_rowIndicesByKey.TryGetValue(id, out int rowIndex))
            {
                row = _rows[rowIndex];
                return true;
            }

            row = default;
            return false;
        }

        public IReadOnlyList<TRow> All => _rowsView;

        public int Count => _rows.Length;

        protected static TRow[] MaterializeOwned(
            IEnumerable<TRow> rows,
            DataTableLoadLimits limits)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            limits.EnsureValid(nameof(limits));

            if (rows is TRow[] array)
            {
                limits.ValidateRowCount(typeof(TRow).FullName, array.Length);
                return array.Length == 0 ? Array.Empty<TRow>() : (TRow[])array.Clone();
            }

            if (rows is ICollection<TRow> collection)
            {
                int count = collection.Count;
                limits.ValidateRowCount(typeof(TRow).FullName, count);
                if (count == 0)
                {
                    return Array.Empty<TRow>();
                }

                var owned = new TRow[count];
                collection.CopyTo(owned, 0);
                return owned;
            }

            var materialized = new List<TRow>();
            foreach (TRow row in rows)
            {
                if (materialized.Count >= limits.MaxRowsPerTable)
                {
                    throw new InvalidOperationException(
                        $"Data-table row count exceeds the configured limit. " +
                        $"Table={typeof(TRow).FullName}, Limit={limits.MaxRowsPerTable}.");
                }

                materialized.Add(row);
            }

            return materialized.Count == 0 ? Array.Empty<TRow>() : materialized.ToArray();
        }
    }

    /// <summary>Convenience table for integer-keyed rows implementing <see cref="IDataRow"/>.</summary>
    public class DataTable<TRow> : DataTable<int, TRow>, IDataTable<TRow> where TRow : IDataRow
    {
        public DataTable(TRow[] rows, IEqualityComparer<int> comparer = null)
            : base(rows, GetId, comparer)
        {
        }

        public DataTable(
            TRow[] rows,
            DataTableLoadLimits limits,
            IEqualityComparer<int> comparer = null)
            : base(rows, GetId, limits, comparer)
        {
        }

        public DataTable(List<TRow> rows, IEqualityComparer<int> comparer = null)
            : base(rows, GetId, comparer)
        {
        }

        public DataTable(
            List<TRow> rows,
            DataTableLoadLimits limits,
            IEqualityComparer<int> comparer = null)
            : base(rows, GetId, limits, comparer)
        {
        }

        private DataTable(
            TRow[] rows,
            DataTableLoadLimits limits,
            IEqualityComparer<int> comparer,
            bool takeOwnership)
            : base(rows, GetId, limits, comparer, takeOwnership)
        {
        }

        public static DataTable<TRow> FromOwnedArray(
            TRow[] rows,
            IEqualityComparer<int> comparer = null)
        {
            return new DataTable<TRow>(
                rows,
                DataTableLoadLimits.Default,
                comparer,
                takeOwnership: true);
        }

        public static DataTable<TRow> FromOwnedArray(
            TRow[] rows,
            DataTableLoadLimits limits,
            IEqualityComparer<int> comparer = null)
        {
            return new DataTable<TRow>(rows, limits, comparer, takeOwnership: true);
        }

        public static DataTable<TRow> FromEnumerable(
            IEnumerable<TRow> rows,
            IEqualityComparer<int> comparer = null)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            TRow[] ownedRows = MaterializeOwned(rows, DataTableLoadLimits.Default);
            return FromOwnedArray(ownedRows, comparer);
        }

        public static DataTable<TRow> FromEnumerable(
            IEnumerable<TRow> rows,
            DataTableLoadLimits limits,
            IEqualityComparer<int> comparer = null)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            TRow[] ownedRows = MaterializeOwned(rows, limits);
            return FromOwnedArray(ownedRows, limits, comparer);
        }

        private static int GetId(TRow row)
        {
            return row.Id;
        }
    }
}
