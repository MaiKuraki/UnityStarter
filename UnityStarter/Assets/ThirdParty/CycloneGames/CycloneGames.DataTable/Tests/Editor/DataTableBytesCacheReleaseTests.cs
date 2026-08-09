using System;

using NUnit.Framework;

namespace CycloneGames.DataTable.Tests.Editor
{
    public sealed class DataTableBytesCacheReleaseTests
    {
        [Test]
        public void Inventory_UsesCompactConstantTimeIndexesAcrossSwapBackRemoval()
        {
            using var cache = CreateCache(maxTables: 4, maxBytesPerTable: 8, maxTotalBytes: 32);
            cache.AddOwned("First", new byte[] { 1 });
            cache.AddOwned("Second", new byte[] { 2 });
            cache.AddOwned("Third", new byte[] { 3 });

            IDataTableBytesInventory inventory = cache;
            Assert.That(inventory.Count, Is.EqualTo(3));
            Assert.That(inventory.GetTableName(0), Is.EqualTo("First"));
            Assert.That(inventory.GetTableName(1), Is.EqualTo("Second"));
            Assert.That(inventory.GetTableName(2), Is.EqualTo("Third"));

            Assert.That(cache.Remove("Second"), Is.True);
            Assert.That(inventory.Count, Is.EqualTo(2));
            Assert.That(inventory.GetTableName(0), Is.EqualTo("First"));
            Assert.That(inventory.GetTableName(1), Is.EqualTo("Third"));
            Assert.That(cache.GetBytes("Third").Span[0], Is.EqualTo(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.GetTableName(2));
        }

        [Test]
        public void Close_InitializesForwardOnlyCursorAndVisitsEachPayloadOnceWithoutClearing()
        {
            const int payloadCount = 257;
            var cache = CreateCache(payloadCount, 1, payloadCount);
            for (int index = 0; index < payloadCount; index++)
            {
                cache.AddOwned("Table" + index, new byte[] { 1 });
            }

            cache.Close();
            Assert.That(cache.IsClosed, Is.True);
            Assert.That(cache.GetMemorySnapshot().PayloadCount, Is.EqualTo(payloadCount));

            var budget = new DataTableBytesCacheReleaseBudget(7, maxBytesToClear: 0);
            int calls = 0;
            int totalProcessed = 0;
            int totalReleased = 0;
            while (!cache.IsReleaseComplete)
            {
                DataTableBytesCacheReleaseResult result = cache.ReleaseStep(in budget);
                Assert.That(result.ProcessedPayloads, Is.InRange(1, 7));
                Assert.That(result.ReleasedPayloads, Is.EqualTo(result.ProcessedPayloads));
                Assert.That(result.ClearedBytes, Is.Zero);
                totalProcessed += result.ProcessedPayloads;
                totalReleased += result.ReleasedPayloads;
                calls++;
            }

            Assert.That(calls, Is.EqualTo((payloadCount + 6) / 7));
            Assert.That(totalProcessed, Is.EqualTo(payloadCount));
            Assert.That(totalReleased, Is.EqualTo(payloadCount));
            Assert.That(cache.GetMemorySnapshot().ReleasedBytes, Is.EqualTo(payloadCount));
            Assert.That(cache.GetMemorySnapshot().TotalBytes, Is.Zero);
            Assert.DoesNotThrow(cache.Dispose);
        }

        [Test]
        public void ReleaseStep_EnforcesPayloadAndClearByteBudgetsSimultaneously()
        {
            var first = FilledBytes(2);
            var second = FilledBytes(5);
            var third = FilledBytes(3);
            var cache = new DataTableBytesCache(
                new DataTableLoadLimits(3, 5, 10),
                capacity: 3,
                clearBytesOnRelease: true);
            cache.AddOwned("First", first);
            cache.AddOwned("Second", second);
            cache.AddOwned("Third", third);
            cache.Close();

            var budget = new DataTableBytesCacheReleaseBudget(2, maxBytesToClear: 4);
            DataTableBytesCacheReleaseResult firstStep = cache.ReleaseStep(in budget);
            Assert.That(firstStep.ProcessedPayloads, Is.EqualTo(2));
            Assert.That(firstStep.ClearedBytes, Is.EqualTo(4));
            Assert.That(firstStep.ReleasedPayloads, Is.EqualTo(1));
            Assert.That(firstStep.ReleasedBytes, Is.EqualTo(2));
            Assert.That(firstStep.RemainingPayloads, Is.EqualTo(2));
            Assert.That(firstStep.RemainingBytes, Is.EqualTo(8));
            CollectionAssert.AreEqual(new byte[] { 0, 0 }, first);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 1, 1, 1 }, second);
            CollectionAssert.AreEqual(new byte[] { 1, 1, 1 }, third);

            DataTableBytesCacheReleaseResult secondStep = cache.ReleaseStep(in budget);
            Assert.That(secondStep.ProcessedPayloads, Is.EqualTo(2));
            Assert.That(secondStep.ClearedBytes, Is.EqualTo(4));
            Assert.That(secondStep.ReleasedPayloads, Is.EqualTo(1));
            Assert.That(secondStep.ReleasedBytes, Is.EqualTo(5));
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0, 0 }, second);
            CollectionAssert.AreEqual(new byte[] { 0, 1, 1 }, third);

            DataTableBytesCacheReleaseResult finalStep = cache.ReleaseStep(in budget);
            Assert.That(finalStep.ProcessedPayloads, Is.EqualTo(1));
            Assert.That(finalStep.ClearedBytes, Is.EqualTo(2));
            Assert.That(finalStep.ReleasedPayloads, Is.EqualTo(1));
            Assert.That(finalStep.ReleasedBytes, Is.EqualTo(3));
            Assert.That(finalStep.IsComplete, Is.True);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, third);

            DataTableBytesCacheMemorySnapshot snapshot = cache.GetMemorySnapshot();
            Assert.That(snapshot.ReleasedPayloadCount, Is.EqualTo(3));
            Assert.That(snapshot.ReleasedBytes, Is.EqualTo(10));
            Assert.That(snapshot.ClearedBytes, Is.EqualTo(10));
            Assert.That(snapshot.IsReleaseComplete, Is.True);
        }

        [Test]
        public void LargePayload_IsClearedInSegmentsAndRetainedUntilFullyCleared()
        {
            var owned = FilledBytes(11);
            var cache = new DataTableBytesCache(
                new DataTableLoadLimits(1, 11, 11),
                capacity: 1,
                clearBytesOnRelease: true);
            cache.AddOwned("Large", owned);
            cache.Close();
            var budget = new DataTableBytesCacheReleaseBudget(1, maxBytesToClear: 3);

            for (int step = 0; step < 3; step++)
            {
                DataTableBytesCacheReleaseResult result = cache.ReleaseStep(in budget);
                Assert.That(result.ProcessedPayloads, Is.EqualTo(1));
                Assert.That(result.ClearedBytes, Is.EqualTo(3));
                Assert.That(result.ReleasedPayloads, Is.Zero);
                Assert.That(result.ReleasedBytes, Is.Zero);
                Assert.That(result.RemainingPayloads, Is.EqualTo(1));
                Assert.That(result.RemainingBytes, Is.EqualTo(11));
            }

            DataTableBytesCacheReleaseResult final = cache.ReleaseStep(in budget);
            Assert.That(final.ClearedBytes, Is.EqualTo(2));
            Assert.That(final.ReleasedPayloads, Is.EqualTo(1));
            Assert.That(final.ReleasedBytes, Is.EqualTo(11));
            Assert.That(final.IsComplete, Is.True);
            CollectionAssert.AreEqual(new byte[11], owned);
        }

        [Test]
        public void ZeroBudgetsRepeatedCallsAndInvalidLifecycle_AreDeterministic()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DataTableBytesCacheReleaseBudget(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DataTableBytesCacheReleaseBudget(1, -1));

            var cache = new DataTableBytesCache(
                new DataTableLoadLimits(1, 4, 4),
                capacity: 1,
                clearBytesOnRelease: true);
            cache.AddOwned("Only", FilledBytes(4));
            Assert.Throws<InvalidOperationException>(
                () => cache.ReleaseStep(new DataTableBytesCacheReleaseBudget(1, 1)));

            cache.Close();
            Assert.DoesNotThrow(cache.Close);

            DataTableBytesCacheReleaseResult noPayloadBudget = cache.ReleaseStep(
                new DataTableBytesCacheReleaseBudget(0, 4));
            AssertNoProgress(noPayloadBudget, expectedRemainingBytes: 4);

            DataTableBytesCacheReleaseResult noByteBudget = cache.ReleaseStep(
                new DataTableBytesCacheReleaseBudget(1, 0));
            AssertNoProgress(noByteBudget, expectedRemainingBytes: 4);

            cache.Dispose();
            Assert.That(cache.IsReleaseComplete, Is.True);
            Assert.DoesNotThrow(cache.Dispose);
            DataTableBytesCacheReleaseResult repeated = cache.ReleaseStep(
                DataTableBytesCacheReleaseBudget.Unlimited);
            Assert.That(repeated.IsComplete, Is.True);
            Assert.That(repeated.ProcessedPayloads, Is.Zero);
            Assert.That(repeated.ReleasedPayloads, Is.Zero);
        }

        [Test]
        public void Dispose_AfterPartialClearCompletesSynchronousReleaseAndIsIdempotent()
        {
            var owned = FilledBytes(16);
            var cache = new DataTableBytesCache(
                new DataTableLoadLimits(1, 16, 16),
                capacity: 1,
                clearBytesOnRelease: true);
            cache.AddOwned("Only", owned);
            cache.Close();
            cache.ReleaseStep(new DataTableBytesCacheReleaseBudget(1, 3));

            cache.Dispose();
            Assert.That(cache.IsReleaseComplete, Is.True);
            Assert.That(cache.GetMemorySnapshot().ClearedBytes, Is.EqualTo(16));
            CollectionAssert.AreEqual(new byte[16], owned);
            Assert.DoesNotThrow(cache.Dispose);
        }

        [Test]
        public void WarmedInventoryAndReleaseSteps_DoNotAllocateOnCurrentThread()
        {
            using (var warmCloseCache = CreateCache(1, 1, 1))
            {
                warmCloseCache.Close();
            }

            var closeCache = CreateCache(1, 1, 1);
            closeCache.AddOwned("Only", new byte[] { 1 });
            long closeBefore = GC.GetAllocatedBytesForCurrentThread();
            closeCache.Close();
            long closeAllocated = GC.GetAllocatedBytesForCurrentThread() - closeBefore;
            closeCache.Dispose();
            Assert.That(closeAllocated, Is.Zero);

            using (var inventoryCache = CreateCache(2, 1, 2))
            {
                inventoryCache.AddOwned("First", new byte[] { 1 });
                inventoryCache.AddOwned("Second", new byte[] { 2 });
                string sink = inventoryCache.GetTableName(0);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 10_000; iteration++)
                {
                    sink = inventoryCache.GetTableName(iteration & 1);
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                GC.KeepAlive(sink);
                Assert.That(allocated, Is.Zero);
            }

            const int payloadCount = 256;
            var releaseCache = CreateCache(payloadCount, 1, payloadCount);
            for (int index = 0; index < payloadCount; index++)
            {
                releaseCache.AddOwned("Payload" + index, new byte[] { 1 });
            }

            releaseCache.Close();
            var budget = new DataTableBytesCacheReleaseBudget(1, 0);
            releaseCache.ReleaseStep(in budget);
            long releaseBefore = GC.GetAllocatedBytesForCurrentThread();
            while (!releaseCache.IsReleaseComplete)
            {
                releaseCache.ReleaseStep(in budget);
            }

            long releaseAllocated = GC.GetAllocatedBytesForCurrentThread() - releaseBefore;
            Assert.That(releaseAllocated, Is.Zero);
        }

        private static DataTableBytesCache CreateCache(
            int maxTables,
            int maxBytesPerTable,
            long maxTotalBytes)
        {
            return new DataTableBytesCache(
                new DataTableLoadLimits(maxTables, maxBytesPerTable, maxTotalBytes),
                capacity: maxTables);
        }

        private static byte[] FilledBytes(int count)
        {
            var bytes = new byte[count];
            Array.Fill(bytes, (byte)1);
            return bytes;
        }

        private static void AssertNoProgress(
            DataTableBytesCacheReleaseResult result,
            long expectedRemainingBytes)
        {
            Assert.That(result.ProcessedPayloads, Is.Zero);
            Assert.That(result.ClearedBytes, Is.Zero);
            Assert.That(result.ReleasedPayloads, Is.Zero);
            Assert.That(result.ReleasedBytes, Is.Zero);
            Assert.That(result.RemainingPayloads, Is.EqualTo(1));
            Assert.That(result.RemainingBytes, Is.EqualTo(expectedRemainingBytes));
            Assert.That(result.IsComplete, Is.False);
        }
    }
}
