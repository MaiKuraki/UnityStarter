using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

using NUnit.Framework;

namespace CycloneGames.DataTable.Tests.Editor
{
    public sealed class DataTableCoreContractTests
    {
        [SetUp]
        public void SetUp()
        {
            DataTableRegistry.Reset();
            DataTableLogger.LogInfo = _ => { };
            DataTableLogger.LogWarning = _ => { };
            DataTableLogger.LogError = _ => { };
        }

        [TearDown]
        public void TearDown()
        {
            DataTableRegistry.Reset();
            DataTableLogger.ResetToDefaults();
        }

        [Test]
        public void Constructor_CopiesArrayAndExposesNonArrayReadOnlyView()
        {
            var original = new TestRow { Id = 1, Value = "original" };
            var replacement = new TestRow { Id = 2, Value = "replacement" };
            var source = new[] { original };

            var table = new DataTable<TestRow>(source);
            source[0] = replacement;

            Assert.AreSame(original, table.Get(1));
            Assert.AreEqual(1, table.Count);
            Assert.IsFalse(table.All is TestRow[]);
            Assert.Throws<NotSupportedException>(
                () => ((IList<TestRow>)table.All)[0] = replacement);
        }

        [Test]
        public void FromOwnedArray_IndexesTheTransferredArrayWithoutExposingItThroughAll()
        {
            var row = new TestRow { Id = 7 };
            var source = new[] { row };

            DataTable<TestRow> table = DataTable<TestRow>.FromOwnedArray(source);

            Assert.AreSame(row, table.Get(7));
            Assert.IsFalse(table.All is TestRow[]);
        }

        [Test]
        public void Constructor_RejectsNullRowsAndDuplicateKeys()
        {
            Assert.Throws<ArgumentException>(() => new DataTable<TestRow>(new TestRow[] { null }));
            Assert.Throws<ArgumentException>(() => new DataTable<TestRow>(new[]
            {
                new TestRow { Id = 1 },
                new TestRow { Id = 1 },
            }));
        }

        [Test]
        public void GenericTable_AcceptsGeneratedRowsWithoutFrameworkInterface()
        {
            var first = new ExternalGeneratedRow { Code = "ITEM_A" };
            var table = new DataTable<string, ExternalGeneratedRow>(
                new[] { first },
                row => row.Code,
                StringComparer.Ordinal);

            Assert.AreSame(first, table.Get("ITEM_A"));
            Assert.IsTrue(table.TryGet("ITEM_A", out ExternalGeneratedRow found));
            Assert.AreSame(first, found);
            Assert.IsNull(table.GetOrDefault("MISSING"));
            Assert.Throws<KeyNotFoundException>(() => table.Get("MISSING"));
        }

        [Test]
        public void ValueTypeRows_AreReadByKeyWithoutChangingSourceOrder()
        {
            var rows = new[]
            {
                new WideValueRow(7, 70),
                new WideValueRow(3, 30),
            };

            var table = new DataTable<WideValueRow>(rows);

            Assert.AreEqual(30, table.Get(3).Value);
            Assert.IsTrue(table.TryGet(7, out WideValueRow found));
            Assert.AreEqual(70, found.Value);
            Assert.IsFalse(table.TryGet(99, out WideValueRow missing));
            Assert.AreEqual(default(WideValueRow), missing);
            Assert.AreEqual(7, table.All[0].Id);
            Assert.AreEqual(3, table.All[1].Id);
        }

        [Test]
        public void Constructor_EnforcesConfiguredRowLimit()
        {
            var limits = new DataTableLoadLimits(2, 16, 32, maxRowsPerTable: 1, maxTableNameLength: 16);
            Assert.Throws<InvalidOperationException>(() => new DataTable<TestRow>(
                new[] { new TestRow { Id = 1 }, new TestRow { Id = 2 } },
                limits: limits));
        }

        [Test]
        public void FromEnumerable_StopsAtRowBudgetBeforeUnboundedMaterialization()
        {
            var limits = new DataTableLoadLimits(4, 128, 256, maxRowsPerTable: 3, maxTableNameLength: 32);
            var rows = new UnboundedRows();

            Assert.Throws<InvalidOperationException>(
                () => DataTable<TestRow>.FromEnumerable(rows, limits));
            Assert.AreEqual(
                4,
                rows.MoveNextCount,
                "Enumeration must stop immediately after observing the first row beyond the configured limit.");
        }

        [Test]
        public void Lookup_10000Rows_DoesNotAllocateAfterWarmup()
        {
            const int rowCount = 10_000;
            var rows = new TestRow[rowCount];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new TestRow { Id = i };
            }

            var table = DataTable<TestRow>.FromOwnedArray(rows);
            Assert.AreEqual(999, table.Get(999).Id);
            int checksum = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100_000; i++)
            {
                checksum += table.Get(i % rowCount).Id;
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(checksum);
            Assert.AreEqual(0, allocatedBytes);
        }

        [Test]
        public void CatalogBuilder_IsStrictAndOneShot()
        {
            var first = new PairLeft(1);
            var builder = new DataTableCatalogBuilder(1);
            builder.Add(first);

            Assert.Throws<ArgumentException>(() => builder.Add(new PairLeft(2)));
            Assert.Throws<ArgumentException>(() => builder.Add(typeof(PairLeft), new PairRight(1)));
            Assert.Throws<ArgumentException>(
                () => new DataTableCatalogBuilder().Add(typeof(int), 1));

            DataTableCatalog catalog = builder.Build();
            Assert.AreSame(first, catalog.Get<PairLeft>());
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Throws<InvalidOperationException>(() => _ = builder.Count);
        }

        [Test]
        public void CatalogBuilder_EnforcesConfiguredTableLimitBeforeAddingEntry()
        {
            var limits = new DataTableLoadLimits(
                maxTableCount: 1,
                maxBytesPerTable: 16,
                maxTotalBytes: 16,
                maxRowsPerTable: 4,
                maxTableNameLength: 32);
            var builder = new DataTableCatalogBuilder(limits, capacity: 1);
            builder.Add(new PairLeft(1));

            Assert.Throws<InvalidOperationException>(() => builder.Add(new PairRight(1)));
            Assert.AreEqual(1, builder.Count);
        }

        [Test]
        public void Registry_PublishesWholeCatalogSnapshotsToConcurrentReaders()
        {
            DataTableCatalog first = CreatePairCatalog(1);
            DataTableCatalog second = CreatePairCatalog(2);
            DataTableRegistry.Publish(first);
            Exception readerFailure = null;

            var reader = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 100_000; i++)
                    {
                        DataTableCatalog snapshot = DataTableRegistry.Current;
                        int left = snapshot.Get<PairLeft>().Version;
                        int right = snapshot.Get<PairRight>().Version;
                        if (left != right)
                        {
                            throw new InvalidOperationException($"Observed mixed catalog versions: {left}/{right}.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    readerFailure = exception;
                }
            });

            reader.Start();
            for (int i = 0; i < 2_000; i++)
            {
                DataTableRegistry.Publish((i & 1) == 0 ? second : first);
            }

            Assert.IsTrue(reader.Join(5_000), "Concurrent registry reader did not finish within the test budget.");
            Assert.IsNull(readerFailure);
            Assert.IsTrue(DataTableRegistry.IsInitialized);
            Assert.Greater(DataTableRegistry.Generation, 0);
        }

        [Test]
        public void Registry_ResetDoesNotDisposePublishedResources()
        {
            var owner = new CountingDisposable();
            var scope = new DataTableSetScope(new object(), CreatePairCatalog(1), owner);
            DataTableRegistry.Publish(scope.Catalog);

            DataTableRegistry.Reset();

            Assert.AreEqual(0, owner.DisposeCount);
            Assert.IsFalse(DataTableRegistry.IsInitialized);
            scope.Dispose();
            scope.Dispose();
            Assert.AreEqual(1, owner.DisposeCount);
        }

        [Test]
        public void Registry_Publish_RemainsSuccessfulWhenInjectedLoggerThrowsAfterCommit()
        {
            DataTableCatalog catalog = CreatePairCatalog(42);
            DataTableLogger.LogInfo = _ => throw new InvalidOperationException("Injected logger failure.");

            Assert.DoesNotThrow(() => DataTableRegistry.Publish(catalog));

            Assert.IsTrue(DataTableRegistry.IsInitialized);
            Assert.AreSame(catalog, DataTableRegistry.Current);
            Assert.AreEqual(42, DataTableRegistry.Current.Get<PairLeft>().Version);
        }

        [Test]
        public void BytesCache_CopiesInputAndTracksReplacementBudgetsTransactionally()
        {
            var limits = new DataTableLoadLimits(2, 4, 6, 10, 16);
            var source = new byte[] { 1, 2, 3, 4 };
            using (var cache = new DataTableBytesCache(limits, capacity: 2))
            {
                cache.Add("a", source);
                source[0] = 99;

                Assert.AreEqual(1, cache.GetBytes("a").Span[0]);
                Assert.AreEqual(4, cache.TotalBytes);
                Assert.Throws<InvalidOperationException>(() => cache.Add("b", new byte[] { 1, 2, 3 }));
                Assert.AreEqual(1, cache.Count);
                Assert.AreEqual(4, cache.TotalBytes);

                cache.Set("a", new byte[] { 8, 9 });
                cache.Add("b", new byte[] { 1, 2, 3, 4 });
                Assert.AreEqual(6, cache.TotalBytes);
                Assert.Throws<InvalidOperationException>(() => cache.Add("c", new byte[] { 1 }));
                Assert.IsTrue(cache.Remove("b"));
                Assert.AreEqual(2, cache.TotalBytes);
                Assert.AreEqual(1, cache.Count);
                Assert.IsFalse(cache.Remove("missing"));
            }
        }

        [Test]
        public void PayloadNames_RejectCaseOnlyDuplicatesAcrossPortableFileSystems()
        {
            var limits = new DataTableLoadLimits(2, 4, 8, 10, 32);
            using (var cache = new DataTableBytesCache(limits, capacity: 2))
            {
                cache.AddOwned("Items", new byte[] { 1 });

                Assert.Throws<ArgumentException>(
                    () => cache.AddOwned("items", new byte[] { 2 }));
                Assert.AreEqual(1, cache.GetBytes("ITEMS").Span[0]);
            }

            Assert.Throws<ArgumentException>(() => new DataTableManifest(
                schemaVersion: 1,
                entries: new[]
                {
                    new DataTableManifestEntry("Items", expectedByteLength: 1),
                    new DataTableManifestEntry("items", expectedByteLength: 1),
                },
                limits));

            var manifest = new DataTableManifest(
                schemaVersion: 1,
                entries: new[] { new DataTableManifestEntry("Items", expectedByteLength: 1) },
                limits);
            Assert.IsTrue(manifest.TryGetEntry("items", out DataTableManifestEntry canonicalEntry));
            Assert.AreEqual("Items", canonicalEntry.TableName);
        }

        [Test]
        public void BytesCache_SealAndDisposeHaveExplicitOwnershipSemantics()
        {
            var owned = new byte[] { 4, 5, 6 };
            var cache = new DataTableBytesCache(
                new DataTableLoadLimits(2, 4, 8),
                capacity: 1,
                clearBytesOnDispose: true);
            cache.AddOwned("owned", owned);
            cache.Seal();

            Assert.IsTrue(cache.IsSealed);
            Assert.AreEqual(3, cache.GetBytes("owned").Length);
            Assert.Throws<InvalidOperationException>(() => cache.Set("owned", new byte[] { 7 }));
            Assert.Throws<InvalidOperationException>(() => cache.Clear());

            cache.Dispose();
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, owned);
            Assert.IsTrue(cache.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => cache.GetBytes("owned"));
        }

        [TestCase("../secret")]
        [TestCase("tables/../secret")]
        [TestCase("tables//items")]
        [TestCase("/absolute/items")]
        [TestCase("C:\\absolute\\items")]
        [TestCase("tables/items?")]
        [TestCase("tables/items/")]
        [TestCase("tables/CON.bytes")]
        [TestCase("tables/com1")]
        [TestCase("foo..bytes")]
        [TestCase("foo .bytes")]
        [TestCase("dir/.bytes")]
        [TestCase(".bytes")]
        [TestCase("")]
        [TestCase("tables/zero\u200Bwidth")]
        public void NameUtility_RejectsTraversalRootedAndNonPortableNames(string value)
        {
            Assert.Throws<ArgumentException>(() => DataTableNameUtility.NormalizeTableName(value));
        }

        [Test]
        public void NameUtility_RejectsUnpairedSurrogateCodeUnits()
        {
            string value = "tables/unpaired" + new string(new[] { '\uD800' });

            Assert.Throws<ArgumentException>(() => DataTableNameUtility.NormalizeTableName(value));
        }

        [Test]
        public void NameUtility_NormalizesPortableRelativeNamesAndLocations()
        {
            Assert.AreEqual(
                "Config/Items",
                DataTableNameUtility.NormalizeTableName("  Config\\Items.bytes  "));
            var resolver = new DataTableLocationResolver("Assets/DataTables/");
            Assert.AreEqual("Assets/DataTables/Config/Items.bytes", resolver.Resolve("Config/Items"));
            Assert.AreEqual(
                "Caf\u00E9",
                DataTableNameUtility.NormalizeTableName("Cafe\u0301.bytes"));
        }

        [TestCase(".bytes.")]
        [TestCase("..")]
        [TestCase(".data..bytes")]
        [TestCase(".byte s")]
        public void NameUtility_RejectsAmbiguousOrNonPortableDataExtensions(string extension)
        {
            Assert.Throws<ArgumentException>(() => DataTableNameUtility.NormalizeDataExtension(extension));
        }

        [Test]
        public void Manifest_DefensivelyCopiesEntriesAndEnforcesSchemaLimitsAndHash()
        {
            byte[] bytes = { 1, 2, 3 };
            string sha256 = DataTableHashUtility.ComputeSha256Hex(bytes);
            var source = new[]
            {
                new DataTableManifestEntry("items", expectedByteLength: bytes.Length, sha256Hex: sha256)
            };
            var limits = new DataTableLoadLimits(2, 4, 8, 10, 16);
            var manifest = new DataTableManifest(2, source, limits, requireKnownTables: true);
            source[0] = new DataTableManifestEntry("changed", expectedByteLength: 1);

            Assert.AreEqual("items", manifest.Entries[0].TableName);
            Assert.IsFalse(manifest.Entries is DataTableManifestEntry[]);
            manifest.ValidateBytes("items", bytes);
            Assert.Throws<InvalidOperationException>(() => manifest.ValidateBytes("items", new byte[] { 1, 2 }));
            Assert.Throws<InvalidOperationException>(() => manifest.ValidateBytes("unknown", new byte[] { 1 }));
            Assert.DoesNotThrow(() => manifest.EnsureSchemaVersionSupported(1, 2));
            Assert.Throws<NotSupportedException>(() => manifest.EnsureSchemaVersionSupported(1, 1));
        }

        [Test]
        public void HashUtility_MatchesOnlyExplicitExpectedHash()
        {
            byte[] bytes = { 1, 2, 3 };
            string sha256 = DataTableHashUtility.ComputeSha256Hex(bytes);

            Assert.IsTrue(DataTableHashUtility.Sha256Matches(bytes, sha256));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(bytes, null));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(bytes, string.Empty));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(bytes, "   "));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(bytes, new string('0', 64)));
        }

        [Test]
        public void Manifest_ValidatesRequiredPayloadPresence()
        {
            var manifest = new DataTableManifest(new DataTableManifestEntry("required"));
            using (var cache = new DataTableBytesCache())
            {
                Assert.Throws<InvalidOperationException>(() => manifest.ValidateRequiredTables(cache));
                cache.Add("required", new byte[] { 1 });
                Assert.DoesNotThrow(() => manifest.ValidateRequiredTables(cache));
            }
        }

        [Test]
        public void Logger_ResetRestoresAllDefaultDelegates()
        {
            DataTableLogger.ResetToDefaults();
            Assert.IsTrue(DataTableLogger.IsDefault);
            DataTableLogger.LogError = _ => { };
            Assert.IsFalse(DataTableLogger.IsDefault);
            DataTableLogger.ResetToDefaults();
            Assert.IsTrue(DataTableLogger.IsDefault);
            Assert.Throws<ArgumentNullException>(() => DataTableLogger.LogInfo = null);
        }

        private static DataTableCatalog CreatePairCatalog(int version)
        {
            return new DataTableCatalogBuilder(2)
                .Add(new PairLeft(version))
                .Add(new PairRight(version))
                .Build();
        }

        private sealed class TestRow : IDataRow
        {
            public int Id { get; set; }

            public string Value { get; set; }
        }

        private sealed class ExternalGeneratedRow
        {
            public string Code { get; set; }
        }

        private readonly struct WideValueRow : IDataRow, IEquatable<WideValueRow>
        {
            public WideValueRow(int id, long value)
            {
                Id = id;
                Value = value;
                Padding0 = value + 1;
                Padding1 = value + 2;
                Padding2 = value + 3;
                Padding3 = value + 4;
                Padding4 = value + 5;
                Padding5 = value + 6;
            }

            public int Id { get; }

            public long Value { get; }

            public long Padding0 { get; }

            public long Padding1 { get; }

            public long Padding2 { get; }

            public long Padding3 { get; }

            public long Padding4 { get; }

            public long Padding5 { get; }

            public bool Equals(WideValueRow other)
            {
                return Id == other.Id && Value == other.Value;
            }

            public override bool Equals(object obj)
            {
                return obj is WideValueRow other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Id * 397) ^ Value.GetHashCode();
                }
            }
        }

        private sealed class PairLeft
        {
            public PairLeft(int version)
            {
                Version = version;
            }

            public int Version { get; }
        }

        private sealed class PairRight
        {
            public PairRight(int version)
            {
                Version = version;
            }

            public int Version { get; }
        }

        private sealed class CountingDisposable : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class UnboundedRows : IEnumerable<TestRow>
        {
            public int MoveNextCount { get; private set; }

            public IEnumerator<TestRow> GetEnumerator()
            {
                int id = 0;
                while (true)
                {
                    MoveNextCount++;
                    yield return new TestRow { Id = id++, Value = "row" };
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
