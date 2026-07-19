using System;
using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    public static class DataTableGeneratedTableCollector
    {
        /// <summary>Explicit AOT-safe descriptor for one generated table-set property.</summary>
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

        /// <summary>Builds an immutable catalog without reflection or runtime type discovery.</summary>
        public static DataTableCatalog CreateCatalog<TTableSet>(
            TTableSet tableSet,
            IReadOnlyList<TableDescriptor<TTableSet>> descriptors)
        {
            ValidateArguments(tableSet, descriptors);
            DataTableCatalogBuilder builder = new DataTableCatalogBuilder(descriptors.Count);
            for (int i = 0; i < descriptors.Count; i++)
            {
                TableDescriptor<TTableSet> descriptor = descriptors[i];
                if (descriptor.TryGetTable(tableSet, out object table))
                {
                    builder.Add(descriptor.TableType, table);
                }
            }

            return builder.Build();
        }

        private static void ValidateArguments<TTableSet>(
            TTableSet tableSet,
            IReadOnlyList<TableDescriptor<TTableSet>> descriptors)
        {
            if (tableSet is null)
            {
                throw new ArgumentNullException(nameof(tableSet));
            }

            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }
        }
    }
}
