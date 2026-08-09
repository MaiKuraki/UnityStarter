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

            public bool Required { get; }

            public TableDescriptor(
                Type tableType,
                Func<TTableSet, object> getter,
                bool required = true)
            {
                TableType = tableType ?? throw new ArgumentNullException(nameof(tableType));
                if (tableType.IsValueType)
                {
                    throw new ArgumentException(
                        "A generated table contract must be a reference type.",
                        nameof(tableType));
                }

                _getter = getter ?? throw new ArgumentNullException(nameof(getter));
                Required = required;
            }

            internal bool IsConfigured => TableType != null && _getter != null;

            public object GetTable(TTableSet tableSet)
            {
                object table = _getter(tableSet);
                if (table == null && Required)
                {
                    throw new InvalidOperationException(
                        $"Required generated table '{TableType.FullName}' is null.");
                }

                return table;
            }
        }

        /// <summary>Builds an immutable catalog without reflection or runtime type discovery.</summary>
        public static DataTableCatalog CreateCatalog<TTableSet>(
            TTableSet tableSet,
            IReadOnlyList<TableDescriptor<TTableSet>> descriptors,
            DataTableLoadLimits limits)
        {
            return CreateCatalog(
                tableSet,
                descriptors,
                new DataTableBuildContext(limits));
        }

        /// <summary>
        /// Builds an immutable catalog with bounded cooperative cancellation between generated
        /// table getters. Descriptor topology is validated before any getter is invoked.
        /// </summary>
        public static DataTableCatalog CreateCatalog<TTableSet>(
            TTableSet tableSet,
            IReadOnlyList<TableDescriptor<TTableSet>> descriptors,
            DataTableBuildContext context)
        {
            ValidateArguments(tableSet, descriptors);
            context.EnsureValid(nameof(context));
            DataTableLoadLimits limits = context.Limits;
            int descriptorCount = descriptors.Count;
            limits.ValidateTableCount(descriptorCount);
            context.ThrowIfCancellationRequested(0);

            // Freeze the caller-owned descriptor list before invoking any getter. A getter is
            // arbitrary product code and may re-enter or mutate the source list; executing from
            // that mutable list would invalidate the topology that was just checked.
            var descriptorSnapshot = descriptorCount == 0
                ? Array.Empty<TableDescriptor<TTableSet>>()
                : new TableDescriptor<TTableSet>[descriptorCount];
            for (int i = 0; i < descriptorSnapshot.Length; i++)
            {
                context.ThrowIfCancellationRequested(i);
                descriptorSnapshot[i] = descriptors[i];
            }

            // Validate the complete descriptor topology before executing generated getters. A
            // HashSet keeps this cold-path validation O(N) and detects duplicates even when both
            // descriptors are optional and currently return null.
            var tableTypes = new HashSet<Type>();
            for (int i = 0; i < descriptorSnapshot.Length; i++)
            {
                TableDescriptor<TTableSet> descriptor = descriptorSnapshot[i];
                if (!descriptor.IsConfigured)
                {
                    throw new ArgumentException(
                        $"Generated table descriptor at index {i} is not initialized.",
                        nameof(descriptors));
                }

                if (!tableTypes.Add(descriptor.TableType))
                {
                    throw new ArgumentException(
                        $"Generated table descriptors contain duplicate contract '{descriptor.TableType.FullName}'.",
                        nameof(descriptors));
                }
            }

            DataTableCatalogBuilder builder = new DataTableCatalogBuilder(limits, descriptorSnapshot.Length);
            for (int i = 0; i < descriptorSnapshot.Length; i++)
            {
                context.ThrowIfCancellationRequested(i);
                TableDescriptor<TTableSet> descriptor = descriptorSnapshot[i];
                object table = descriptor.GetTable(tableSet);
                if (table != null)
                {
                    builder.Add(descriptor.TableType, table);
                }
            }

            context.CancellationToken.ThrowIfCancellationRequested();
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
