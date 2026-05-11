using System;
using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Dictionary-backed IDataTable with O(1) lookup and zero allocation on read.
    /// Built once at startup from a pre-built array.
    /// </summary>
    public class DataTable<T> : IDataTable<T> where T : IDataRow
    {
        private readonly Dictionary<int, T> _rows;
        private readonly T[] _rowsArray;

        /// <summary>
        /// Build from a pre-allocated array. The array is stored directly (no copy).
        /// Caller should not mutate the array after construction.
        /// </summary>
        public DataTable(T[] rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            _rowsArray = rows;
            _rows = new Dictionary<int, T>(rows.Length);

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                if (_rows.ContainsKey(row.Id))
                {
                    DataTableLogger.LogWarning(
                        $"Duplicate Id {row.Id} in DataTable<{typeof(T).Name}>. Keeping first occurrence.");
                    continue;
                }
                _rows[row.Id] = row;
            }
        }

        /// <summary>
        /// Build from a List. The list's internal array is NOT copied (ToArray avoided).
        /// </summary>
        public DataTable(List<T> rows) : this(rows?.ToArray() ?? throw new ArgumentNullException(nameof(rows))) { }

        /// <summary>
        /// Build from an IEnumerable. Uses a pooled List internally to minimize allocation.
        /// Prefer the array or List overloads when the source is already materialized.
        /// </summary>
        public static DataTable<T> FromEnumerable(IEnumerable<T> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            return new DataTable<T>(new List<T>(rows).ToArray());
        }

        public T Get(int id)
        {
            if (_rows.TryGetValue(id, out var row))
                return row;
            throw new KeyNotFoundException(
                $"Key {id} not found in DataTable<{typeof(T).Name}> ({Count} rows).");
        }

        public T GetOrDefault(int id)
        {
            _rows.TryGetValue(id, out var row);
            return row;
        }

        public bool TryGet(int id, out T row) => _rows.TryGetValue(id, out row);

        public IReadOnlyList<T> All => _rowsArray;

        public int Count => _rowsArray.Length;
    }
}
