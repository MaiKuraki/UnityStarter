using System;
using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Immutable type-indexed table snapshot. The catalog does not own table instances or their
    /// backing resources; ownership belongs to the composition scope that published the snapshot.
    /// </summary>
    public sealed class DataTableCatalog
    {
        private static readonly DataTableCatalog EmptyCatalog = new DataTableCatalog(
            new Dictionary<Type, object>(0),
            takeOwnership: true);

        private readonly Dictionary<Type, object> _tables;

        internal DataTableCatalog(Dictionary<Type, object> tables, bool takeOwnership)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            _tables = takeOwnership
                ? tables
                : new Dictionary<Type, object>(tables);
        }

        public static DataTableCatalog Empty => EmptyCatalog;

        public int Count => _tables.Count;

        public TTable Get<TTable>() where TTable : class
        {
            if (TryGet(out TTable table))
            {
                return table;
            }

            throw new KeyNotFoundException(
                $"Data-table catalog does not contain table contract: {typeof(TTable).FullName}");
        }

        public TTable GetOrDefault<TTable>() where TTable : class
        {
            TryGet(out TTable table);
            return table;
        }

        public bool TryGet<TTable>(out TTable table) where TTable : class
        {
            if (_tables.TryGetValue(typeof(TTable), out object value))
            {
                table = (TTable)value;
                return true;
            }

            table = null;
            return false;
        }

        public bool TryGet(Type tableType, out object table)
        {
            if (tableType == null)
            {
                throw new ArgumentNullException(nameof(tableType));
            }

            return _tables.TryGetValue(tableType, out table);
        }

        public bool Contains<TTable>() where TTable : class
        {
            return _tables.ContainsKey(typeof(TTable));
        }
    }

    /// <summary>
    /// Cold-path one-shot builder for <see cref="DataTableCatalog"/>. Duplicate contracts and
    /// contract/instance type mismatches fail before a snapshot can be published.
    /// </summary>
    public sealed class DataTableCatalogBuilder
    {
        private readonly DataTableLoadLimits _limits;
        private Dictionary<Type, object> _tables;

        public DataTableCatalogBuilder(int capacity = 0)
            : this(DataTableLoadLimits.Default, capacity)
        {
        }

        public DataTableCatalogBuilder(DataTableLoadLimits limits, int capacity = 0)
        {
            limits.EnsureValid(nameof(limits));
            if (capacity < 0 || capacity > limits.MaxTableCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    $"Catalog capacity must be between zero and {limits.MaxTableCount}.");
            }

            _limits = limits;
            _tables = new Dictionary<Type, object>(capacity);
        }

        public int Count
        {
            get
            {
                ThrowIfBuilt();
                return _tables.Count;
            }
        }

        public DataTableCatalogBuilder Add<TTable>(TTable table) where TTable : class
        {
            return Add(typeof(TTable), table);
        }

        public DataTableCatalogBuilder Add(Type tableType, object table)
        {
            ThrowIfBuilt();
            ValidateEntry(tableType, table);
            _limits.ValidateTableCount(checked(_tables.Count + 1));
            _tables.Add(tableType, table);
            return this;
        }

        public DataTableCatalog Build()
        {
            ThrowIfBuilt();
            Dictionary<Type, object> ownedTables = _tables;
            _tables = null;
            return ownedTables.Count == 0
                ? DataTableCatalog.Empty
                : new DataTableCatalog(ownedTables, takeOwnership: true);
        }

        internal static void ValidateEntry(Type tableType, object table)
        {
            if (tableType == null)
            {
                throw new ArgumentNullException(nameof(tableType));
            }

            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (tableType.IsValueType)
            {
                throw new ArgumentException(
                    "A table contract must be a reference type so it can be retrieved through the typed catalog API.",
                    nameof(tableType));
            }

            if (!tableType.IsInstanceOfType(table))
            {
                throw new ArgumentException(
                    $"Table instance type {table.GetType().FullName} is not assignable to {tableType.FullName}.",
                    nameof(table));
            }
        }

        private void ThrowIfBuilt()
        {
            if (_tables == null)
            {
                throw new InvalidOperationException("The data-table catalog builder has already been consumed.");
            }
        }
    }
}
