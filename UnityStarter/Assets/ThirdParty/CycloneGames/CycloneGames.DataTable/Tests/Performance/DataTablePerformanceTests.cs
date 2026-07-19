using System;

using NUnit.Framework;

using Unity.PerformanceTesting;

namespace CycloneGames.DataTable.Tests.Performance
{
    public sealed class DataTablePerformanceTests
    {
        private const int WarmupCount = 5;
        private const int MeasurementCount = 15;
        private const int LookupIterations = 100_000;
        private const int SmallTableRowCount = 10_000;
        private const int LargeTableRowCount = 100_000;
        private const int LookupPatternSize = 4_096;

        private static readonly PerfRow[] SmallRows = CreateRows(SmallTableRowCount);
        private static readonly PerfRow[] LargeRows = CreateRows(LargeTableRowCount);
        private static readonly NamedPerfRow[] NamedRows = CreateNamedRows(SmallTableRowCount);
        private static readonly string LastNamedKey = NamedRows[NamedRows.Length - 1].Key;
        private static readonly string EqualContentNamedKey = new string(LastNamedKey.ToCharArray());
        private static readonly WidePerfRow[] WideRows = CreateWideRows(LargeTableRowCount);
        private static readonly int[] DistributedKeys = CreateDistributedKeys(LargeTableRowCount, LookupPatternSize);
        private static readonly int[] MixedHitMissKeys = CreateMixedHitMissKeys(LargeTableRowCount, LookupPatternSize);

        private DataTable<PerfRow> _largeTable;
        private DataTable<string, NamedPerfRow> _namedTable;
        private DataTable<WidePerfRow> _wideTable;
        private DataTableCatalog _catalog;
        private PerfRow _rowSink;
        private NamedPerfRow _namedRowSink;
        private WidePerfRow _wideRowSink;
        private DataTable<PerfRow> _tableSink;
        private object _objectSink;
        private int _distributedCursor;
        private int _mixedCursor;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _largeTable = new DataTable<PerfRow>(LargeRows);
            _namedTable = new DataTable<string, NamedPerfRow>(NamedRows, GetNamedKey, StringComparer.Ordinal);
            _wideTable = new DataTable<WidePerfRow>(WideRows);
            _catalog = new DataTableCatalogBuilder(2)
                .Add(_largeTable)
                .Add(_namedTable)
                .Build();
            DataTableRegistry.Reset();
            DataTableRegistry.Publish(_catalog);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            DataTableRegistry.Reset();
        }

        [Test, Performance]
        public void BuildTenThousandRows_WithDefensiveCopy()
        {
            Measure.Method(BuildSmallTableWithDefensiveCopy)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void BuildOneHundredThousandRows_WithDefensiveCopy()
        {
            Measure.Method(BuildLargeTableWithDefensiveCopy)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void BuildOneHundredThousandWideValueRows_WithDefensiveCopy()
        {
            Measure.Method(BuildWideTableWithDefensiveCopy)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void LookupOneHundredThousandRows_ByIntKey()
        {
            Measure.Method(LookupIntTail)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(LookupIterations)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void LookupTenThousandRows_ByStringKey()
        {
            Measure.Method(LookupStringTail)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(LookupIterations)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void LookupOneHundredThousandRows_ByDistributedIntKeys()
        {
            Measure.Method(LookupDistributedInt)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(LookupIterations)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void LookupOneHundredThousandRows_WithMixedHitsAndMisses()
        {
            Measure.Method(LookupMixedInt)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(LookupIterations)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void LookupEqualContentStringFromDifferentInstance()
        {
            Measure.Method(LookupEqualContentString)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(LookupIterations)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void LookupWideValueRow()
        {
            Measure.Method(LookupWideRow)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(LookupIterations)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void ReadPublishedCatalog()
        {
            Measure.Method(ReadRegistry)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(LookupIterations)
                .GC()
                .Run();
        }

        [Test]
        public void WarmedLookupAndCatalogRead_DoNotAllocateOnCurrentEditorMonoThread()
        {
            for (int i = 0; i < 100; i++)
            {
                LookupIntTail();
                LookupStringTail();
                LookupDistributedInt();
                LookupMixedInt();
                LookupEqualContentString();
                LookupWideRow();
                ReadCatalog();
                ReadRegistry();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < LookupIterations; i++)
            {
                LookupIntTail();
                LookupStringTail();
                LookupDistributedInt();
                LookupMixedInt();
                LookupEqualContentString();
                LookupWideRow();
                ReadCatalog();
                ReadRegistry();
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(
                allocatedBytes,
                Is.Zero,
                "Warmed table lookup and immutable catalog publication reads allocated managed memory on the current Editor Mono thread.");
        }

        private void BuildSmallTableWithDefensiveCopy()
        {
            _objectSink = new DataTable<PerfRow>(SmallRows);
        }

        private void BuildLargeTableWithDefensiveCopy()
        {
            _objectSink = new DataTable<PerfRow>(LargeRows);
        }

        private void BuildWideTableWithDefensiveCopy()
        {
            _objectSink = new DataTable<WidePerfRow>(WideRows);
        }

        private void LookupIntTail()
        {
            _largeTable.TryGet(LargeTableRowCount - 1, out _rowSink);
        }

        private void LookupStringTail()
        {
            _namedTable.TryGet(LastNamedKey, out _namedRowSink);
        }

        private void LookupDistributedInt()
        {
            int index = _distributedCursor++ & (LookupPatternSize - 1);
            _largeTable.TryGet(DistributedKeys[index], out _rowSink);
        }

        private void LookupMixedInt()
        {
            int index = _mixedCursor++ & (LookupPatternSize - 1);
            _largeTable.TryGet(MixedHitMissKeys[index], out _rowSink);
        }

        private void LookupEqualContentString()
        {
            _namedTable.TryGet(EqualContentNamedKey, out _namedRowSink);
        }

        private void LookupWideRow()
        {
            _wideTable.TryGet(LargeTableRowCount - 1, out _wideRowSink);
        }

        private void ReadCatalog()
        {
            _catalog.TryGet(out _tableSink);
        }

        private void ReadRegistry()
        {
            DataTableRegistry.TryGet(out _tableSink);
        }

        private static string GetNamedKey(NamedPerfRow row)
        {
            return row.Key;
        }

        private static PerfRow[] CreateRows(int count)
        {
            var rows = new PerfRow[count];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new PerfRow(i, i * 0.25f);
            }

            return rows;
        }

        private static NamedPerfRow[] CreateNamedRows(int count)
        {
            var rows = new NamedPerfRow[count];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new NamedPerfRow("Row." + i.ToString("D5"), i);
            }

            return rows;
        }

        private static WidePerfRow[] CreateWideRows(int count)
        {
            var rows = new WidePerfRow[count];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new WidePerfRow(i, i);
            }

            return rows;
        }

        private static int[] CreateDistributedKeys(int rowCount, int count)
        {
            var keys = new int[count];
            uint state = 0x9E3779B9u;
            for (int i = 0; i < keys.Length; i++)
            {
                state = unchecked((state * 1_664_525u) + 1_013_904_223u);
                keys[i] = (int)(state % (uint)rowCount);
            }

            return keys;
        }

        private static int[] CreateMixedHitMissKeys(int rowCount, int count)
        {
            int[] keys = CreateDistributedKeys(rowCount, count);
            for (int i = 1; i < keys.Length; i += 2)
            {
                keys[i] = -1 - keys[i];
            }

            return keys;
        }

        private sealed class PerfRow : IDataRow
        {
            public PerfRow(int id, float value)
            {
                Id = id;
                Value = value;
            }

            public int Id { get; }

            public float Value { get; }
        }

        private sealed class NamedPerfRow
        {
            public NamedPerfRow(string key, int value)
            {
                Key = key;
                Value = value;
            }

            public string Key { get; }

            public int Value { get; }
        }

        private readonly struct WidePerfRow : IDataRow
        {
            public WidePerfRow(int id, long seed)
            {
                Id = id;
                Value0 = seed;
                Value1 = seed + 1;
                Value2 = seed + 2;
                Value3 = seed + 3;
                Value4 = seed + 4;
                Value5 = seed + 5;
                Value6 = seed + 6;
                Value7 = seed + 7;
            }

            public int Id { get; }

            public long Value0 { get; }

            public long Value1 { get; }

            public long Value2 { get; }

            public long Value3 { get; }

            public long Value4 { get; }

            public long Value5 { get; }

            public long Value6 { get; }

            public long Value7 { get; }
        }
    }
}
