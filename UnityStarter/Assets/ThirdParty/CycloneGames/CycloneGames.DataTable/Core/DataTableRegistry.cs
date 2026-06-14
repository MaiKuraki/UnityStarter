using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Central registry for all data tables. Tables are registered once at startup,
    /// then read concurrently without locks.
    /// </summary>
    public static class DataTableRegistry
    {
        private static readonly object SyncRoot = new object();

        // Volatile read ensures visibility across threads without locking.
        private static Dictionary<Type, object> _tables = new Dictionary<Type, object>();
        private static int _initialized;

        public static bool IsInitialized => Volatile.Read(ref _initialized) == 1;

        /// <summary>Register a table instance. Call once per table during startup.</summary>
        public static void Register<TTable>(TTable table) where TTable : class
        {
            Register(typeof(TTable), table);
        }

        /// <summary>Register a table instance under an explicit table type. Call once per table during startup.</summary>
        public static void Register(Type tableType, object table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (tableType == null) throw new ArgumentNullException(nameof(tableType));
            if (!tableType.IsInstanceOfType(table))
            {
                throw new ArgumentException(
                    $"Table instance type {table.GetType().FullName} is not assignable to {tableType.FullName}.",
                    nameof(table));
            }

            lock (SyncRoot)
            {
                if (IsInitialized)
                {
                    throw new InvalidOperationException(
                        "DataTableRegistry is initialized. Call Reset before registering a new table set.");
                }

                _tables[tableType] = table;
            }
        }

        /// <summary>Get a registered table. O(1). Returns null if not found.</summary>
        public static TTable Get<TTable>() where TTable : class
        {
            // Fast path: read the cached dictionary reference without lock.
            // Dictionary<TKey,TValue> reads are thread-safe when no writes are concurrent.
            // Since all writes happen at startup (before MarkInitialized), this is safe.
            var tables = Volatile.Read(ref _tables);
            tables.TryGetValue(typeof(TTable), out var obj);
            return (TTable)obj;
        }

        /// <summary>Try-get without throwing.</summary>
        public static bool TryGet<TTable>(out TTable table) where TTable : class
        {
            var tables = Volatile.Read(ref _tables);
            if (tables.TryGetValue(typeof(TTable), out var obj))
            {
                table = (TTable)obj;
                return true;
            }
            table = null;
            return false;
        }

        /// <summary>Mark initialization complete. After this, no more Register calls.</summary>
        public static void MarkInitialized()
        {
            int count;
            lock (SyncRoot)
            {
                // Publish the dictionary for lock-free reads.
                Thread.MemoryBarrier();
                Volatile.Write(ref _initialized, 1);
                count = _tables.Count;
            }

            DataTableLogger.LogInfo($"DataTableRegistry initialized ({count} tables).");
        }

        /// <summary>Reset all state (test teardown / hot-reload).</summary>
        public static void Reset()
        {
            lock (SyncRoot)
            {
                Volatile.Write(ref _tables, new Dictionary<Type, object>());
                Volatile.Write(ref _initialized, 0);
            }
        }
    }
}
