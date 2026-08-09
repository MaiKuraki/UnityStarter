using System;
using System.Threading;
using NUnit.Framework;

namespace CycloneGames.DataTable.Tests.Editor
{
    public sealed class DataTableBuildContextTests
    {
        [Test]
        public void Constructor_RejectsNonPowerOfTwoCancellationInterval()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DataTableBuildContext(
                    DataTableLoadLimits.Default,
                    cancellationCheckInterval: 1000));
        }

        [Test]
        public void FromOwnedArray_PreCancelled_DoesNotConsumeCallerArray()
        {
            var rows = new[]
            {
                new TestRow(1),
                new TestRow(2),
            };
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var context = new DataTableBuildContext(
                DataTableLoadLimits.Default,
                cancellation.Token,
                cancellationCheckInterval: 1);

            Assert.Throws<OperationCanceledException>(() =>
                DataTable<TestRow>.FromOwnedArray(rows, context));
            Assert.AreEqual(1, rows[0].Id);
            Assert.AreEqual(2, rows[1].Id);
        }

        [Test]
        public void Constructor_ChecksCancellationDuringIndexBuild()
        {
            var rows = new TestRow[4096];
            using var cancellation = new CancellationTokenSource();
            int selectedRows = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new TestRow(i);
            }

            var context = new DataTableBuildContext(
                DataTableLoadLimits.Default,
                cancellation.Token,
                cancellationCheckInterval: 1);

            Assert.Throws<OperationCanceledException>(() =>
                new DataTable<int, TestRow>(
                    rows,
                    row =>
                    {
                        selectedRows++;
                        if (selectedRows == 128)
                        {
                            cancellation.Cancel();
                        }

                        return row.Id;
                    },
                    context));
            Assert.Less(selectedRows, rows.Length);
        }

        private sealed class TestRow : IDataRow
        {
            public TestRow(int id)
            {
                Id = id;
            }

            public int Id { get; }
        }
    }
}
