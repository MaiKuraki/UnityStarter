using System;

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

            DataTableCatalog catalog = DataTableGeneratedTableCollector.CreateCatalog(tableSet, descriptors);

            Assert.AreSame(rowTable, catalog.Get<DataTable<TestRow>>());
        }

        [Test]
        public void CreateCatalog_WithDescriptors_SkipsNullTables()
        {
            var tableSet = new TestTableSet(null);
            var descriptors = new[]
            {
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set => set.Rows)
            };

            DataTableCatalog catalog = DataTableGeneratedTableCollector.CreateCatalog(tableSet, descriptors);

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
                () => DataTableGeneratedTableCollector.CreateCatalog(tableSet, descriptors));
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
    }
}
