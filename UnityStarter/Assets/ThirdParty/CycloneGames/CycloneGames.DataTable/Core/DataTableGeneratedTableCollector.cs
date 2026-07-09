using System;
using System.Collections.Generic;
using System.Reflection;

namespace CycloneGames.DataTable
{
    public static class DataTableGeneratedTableCollector
    {
        public readonly struct TableDescriptor<TTableSet>
        {
            private readonly Func<TTableSet, object> _getter;

            public Type TableType { get; }

            public TableDescriptor(Type tableType, Func<TTableSet, object> getter)
            {
                TableType = tableType ?? throw new ArgumentNullException(nameof(tableType));
                _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            }

            public bool TryGetTable(TTableSet tableSet, out object table)
            {
                table = _getter(tableSet);
                return table != null;
            }
        }

        public static Dictionary<Type, object> CreateTableMap<TTableSet>(
            TTableSet tableSet,
            TableDescriptor<TTableSet>[] descriptors)
        {
            if (tableSet == null)
            {
                throw new ArgumentNullException(nameof(tableSet));
            }

            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            Dictionary<Type, object> tableMap = new Dictionary<Type, object>(descriptors.Length);
            for (int i = 0; i < descriptors.Length; i++)
            {
                TableDescriptor<TTableSet> descriptor = descriptors[i];
                if (descriptor.TryGetTable(tableSet, out object table))
                {
                    tableMap[descriptor.TableType] = table;
                }
            }

            return tableMap;
        }

        public static Dictionary<Type, object> CreateTableMap(object tableSet)
        {
            if (tableSet == null)
            {
                throw new ArgumentNullException(nameof(tableSet));
            }

            PropertyInfo[] properties = tableSet.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            Dictionary<Type, object> tableMap = new Dictionary<Type, object>(properties.Length);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object table = property.GetValue(tableSet);
                if (table == null)
                {
                    continue;
                }

                tableMap[property.PropertyType] = table;
            }

            return tableMap;
        }
    }
}
