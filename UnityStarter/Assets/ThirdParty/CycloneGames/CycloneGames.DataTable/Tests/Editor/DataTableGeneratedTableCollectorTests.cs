using System;
using System.Collections.Generic;
using System.Threading;

using NUnit.Framework;

namespace CycloneGames.DataTable.Tests.Editor
{
    public sealed class DataTableGeneratedTableCollectorTests
    {
        [Test]
        public void CreateCatalog_WithDescriptors_CollectsTablesWithoutReflection()
        {
            var rowTable = new DataTable<TestRow>(new[]
            {
                new TestRow { Id = 1 }
            });
            var tableSet = new TestTableSet(rowTable);
            var descriptors = new[]
            {
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set => set.Rows)
            };

            DataTableCatalog catalog = DataTableGeneratedTableCollector.CreateCatalog(
                tableSet,
                descriptors,
                DataTableLoadLimits.Default);

            Assert.AreSame(rowTable, catalog.Get<DataTable<TestRow>>());
        }

        [Test]
        public void CreateCatalog_WithRequiredNullTable_FailsBeforePublication()
        {
            var tableSet = new TestTableSet(null);
            var descriptors = new[]
            {
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set => set.Rows)
            };

            Assert.Throws<InvalidOperationException>(
                () => DataTableGeneratedTableCollector.CreateCatalog(
                    tableSet,
                    descriptors,
                    DataTableLoadLimits.Default));
        }

        [Test]
        public void CreateCatalog_WithOptionalNullTable_SkipsItExplicitly()
        {
            var tableSet = new TestTableSet(null);
            var descriptors = new[]
            {
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set => set.Rows,
                    required: false)
            };

            DataTableCatalog catalog = DataTableGeneratedTableCollector.CreateCatalog(
                tableSet,
                descriptors,
                DataTableLoadLimits.Default);

            Assert.AreEqual(0, catalog.Count);
        }

        [Test]
        public void CreateCatalog_WithDuplicateDescriptors_FailsBeforePublication()
        {
            var rowTable = new DataTable<TestRow>(new[] { new TestRow { Id = 1 } });
            var tableSet = new TestTableSet(rowTable);
            var descriptors = new[]
            {
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set => set.Rows),
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set => set.Rows),
            };

            Assert.Throws<ArgumentException>(
                () => DataTableGeneratedTableCollector.CreateCatalog(
                    tableSet,
                    descriptors,
                    DataTableLoadLimits.Default));
        }

        [Test]
        public void CreateCatalog_WithDuplicateOptionalNullDescriptors_FailsBeforeInvokingGetters()
        {
            int getterCallCount = 0;
            var tableSet = new TestTableSet(null);
            var descriptors = new[]
            {
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    _ =>
                    {
                        getterCallCount++;
                        return null;
                    },
                    required: false),
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    _ =>
                    {
                        getterCallCount++;
                        return null;
                    },
                    required: false),
            };

            Assert.Throws<ArgumentException>(
                () => DataTableGeneratedTableCollector.CreateCatalog(
                    tableSet,
                    descriptors,
                    DataTableLoadLimits.Default));
            Assert.AreEqual(0, getterCallCount);
        }

        [Test]
        public void CreateCatalog_WithCanceledBuildContext_DoesNotInvokeGetters()
        {
            int getterCallCount = 0;
            var cancellation = new CancellationToken(canceled: true);
            var context = new DataTableBuildContext(
                DataTableLoadLimits.Default,
                cancellation,
                cancellationCheckInterval: 1);
            var descriptors = new[]
            {
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set =>
                    {
                        getterCallCount++;
                        return set.Rows;
                    })
            };

            Assert.Throws<OperationCanceledException>(
                () => DataTableGeneratedTableCollector.CreateCatalog(
                    new TestTableSet(null),
                    descriptors,
                    context));
            Assert.AreEqual(0, getterCallCount);
        }

        [Test]
        public void CreateCatalog_GetterMutationCannotChangeValidatedDescriptorTopology()
        {
            var rows = new DataTable<TestRow>(new[] { new TestRow { Id = 7 } });
            var secondary = new SecondaryTable();
            var tableSet = new TestTableSet(rows);
            var descriptors = new List<
                DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>>();
            descriptors.Add(
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set =>
                    {
                        descriptors[1] = default;
                        return set.Rows;
                    }));
            descriptors.Add(
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(SecondaryTable),
                    _ => secondary));

            DataTableCatalog catalog = DataTableGeneratedTableCollector.CreateCatalog(
                tableSet,
                descriptors,
                DataTableLoadLimits.Default);

            Assert.AreSame(rows, catalog.Get<DataTable<TestRow>>());
            Assert.AreSame(secondary, catalog.Get<SecondaryTable>());
        }

        [Test]
        public void CreateCatalog_RejectsDescriptorCountBeyondProductLimit()
        {
            var tableSet = new TestTableSet(null);
            var descriptors = new[]
            {
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set => set.Rows,
                    required: false),
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(IDataTable<TestRow>),
                    set => set.Rows,
                    required: false),
            };
            var limits = new DataTableLoadLimits(
                maxTableCount: 1,
                maxBytesPerTable: 1,
                maxTotalBytes: 1,
                maxRowsPerTable: 1,
                maxTableNameLength: 1);

            Assert.Throws<InvalidOperationException>(
                () => DataTableGeneratedTableCollector.CreateCatalog(tableSet, descriptors, limits));
        }

        private sealed class TestTableSet
        {
            public TestTableSet(DataTable<TestRow> rows)
            {
                Rows = rows;
            }

            public DataTable<TestRow> Rows { get; }
        }

        private sealed class TestRow : IDataRow
        {
            public int Id { get; set; }
        }

        private sealed class SecondaryTable
        {
        }
    }
}
