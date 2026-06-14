using System;
using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    public static class DataTableTableMapBuilder
    {
        public static Dictionary<Type, object> Create(params (Type type, object table)[] entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            Dictionary<Type, object> tableMap = new Dictionary<Type, object>(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                Add(tableMap, entries[i].type, entries[i].table);
            }

            return tableMap;
        }

        public static void Add(
            Dictionary<Type, object> tableMap,
            Type tableType,
            object table)
        {
            if (tableMap == null)
            {
                throw new ArgumentNullException(nameof(tableMap));
            }

            if (tableType == null)
            {
                throw new ArgumentNullException(nameof(tableType));
            }

            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (!tableType.IsInstanceOfType(table))
            {
                throw new ArgumentException(
                    $"Table instance type {table.GetType().FullName} is not assignable to {tableType.FullName}.",
                    nameof(table));
            }

            tableMap.Add(tableType, table);
        }
    }
}
