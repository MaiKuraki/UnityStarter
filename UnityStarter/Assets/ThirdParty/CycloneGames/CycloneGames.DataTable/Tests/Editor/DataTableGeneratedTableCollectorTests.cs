using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace CycloneGames.DataTable.Tests.Editor
{
    public sealed class DataTableGeneratedTableCollectorTests
    {
        [Test]
        public void CreateTableMap_WithDescriptors_CollectsTablesWithoutReflection()
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

            Dictionary<Type, object> tableMap = DataTableGeneratedTableCollector.CreateTableMap(tableSet, descriptors);

            Assert.AreSame(rowTable, tableMap[typeof(DataTable<TestRow>)]);
        }

        [Test]
        public void CreateTableMap_WithDescriptors_SkipsNullTables()
        {
            var tableSet = new TestTableSet(null);
            var descriptors = new[]
            {
                new DataTableGeneratedTableCollector.TableDescriptor<TestTableSet>(
                    typeof(DataTable<TestRow>),
                    set => set.Rows)
            };

            Dictionary<Type, object> tableMap = DataTableGeneratedTableCollector.CreateTableMap(tableSet, descriptors);

            Assert.AreEqual(0, tableMap.Count);
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
