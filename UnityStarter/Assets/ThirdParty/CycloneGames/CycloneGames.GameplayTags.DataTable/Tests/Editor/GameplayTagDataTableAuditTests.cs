using System;
using System.Collections.Generic;
using System.Diagnostics;

using CycloneGames.DataTable;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Integrations.DataTable;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.DataTable.Tests.Editor
{
    /// <summary>
    /// Build-time audit for the DataTable tag source adapters:
    /// 1. Dual independent builds from identical rows produce identical registry indices (determinism).
    /// 2. A large row set builds within a measured budget, resolves every tag, and stays stable
    ///    across a rebuild (epoch contract).
    /// </summary>
    public sealed class GameplayTagDataTableAuditTests
    {
        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagHost.Use(new GameplayTagDataTableTestPlatform());
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagHost.ClearRegisteredProjectTagSources();
        }

        [Test]
        public void DataTableSource_TwoIndependentBuilds_ProduceIdenticalIndices()
        {
            const int rowCount = 512;
            DataTable<AuditTagRow> table = BuildTable(rowCount);

            int[] firstBuild = BuildAndCaptureIndices(table, rowCount);

            // Fully reset, then rebuild from the same rows.
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagHost.ClearRegisteredProjectTagSources();
            GameplayTagHost.Use(new GameplayTagDataTableTestPlatform());

            int[] secondBuild = BuildAndCaptureIndices(table, rowCount);

            for (int i = 0; i < rowCount; i++)
            {
                Assert.That(secondBuild[i], Is.EqualTo(firstBuild[i]),
                    $"Index for row {i} diverged between two independent builds.");
            }

            System.Console.WriteLine($"[TagsAudit][DataTable] PASS: two independent builds of {rowCount} rows produced identical indices.");
        }

        [Test]
        public void DataTableSource_LargeRowSet_BuildScale_MeasureAndVerify()
        {
            const int rowCount = 5000;
            DataTable<AuditTagRow> table = BuildTable(rowCount);

            long before = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            RegisterTableSource(table);
            GameplayTagManager.InitializeIfNeeded();
            long buildTicks = Stopwatch.GetTimestamp() - t0;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            double buildMs = buildTicks * 1000.0 / Stopwatch.Frequency;
            double perRowUs = buildMs * 1000.0 / rowCount;

            // Generous upper bound so the build budget is part of pass/fail evidence, not just a log line.
            Assert.That(buildMs, Is.LessThan(10_000.0),
                $"Building {rowCount} rows must stay well within budget; measured {buildMs:F1}ms.");

            // Every row must resolve to a valid tag with the expected description.
            for (int i = 0; i < rowCount; i++)
            {
                Assert.That(GameplayTagManager.TryRequest(RowName(i), out GameplayTag tag), Is.True,
                    $"Row {i} failed to resolve.");
                Assert.That(tag.Description, Is.EqualTo(RowDescription(i)));
            }

            // Rebuild: indices must be stable (epoch contract).
            int probeIndexBefore = GameplayTagManager.Request(RowName(rowCount / 2)).RuntimeIndex;
            GameplayTagManager.RegisterDynamicTags(new List<string> { RowName(0) }); // triggers a rebuild
            int probeIndexAfter = GameplayTagManager.Request(RowName(rowCount / 2)).RuntimeIndex;
            Assert.That(probeIndexAfter, Is.EqualTo(probeIndexBefore),
                "Registry rebuild must preserve indices for DataTable-sourced tags.");

            System.Console.WriteLine(
                $"[TagsAudit][DataTable] rows={rowCount}: build={buildMs:F1}ms ({perRowUs:F1}us/row), " +
                $"buildAllocatedBytes={allocated}, TagCount={GameplayTagManager.TagCount}, rebuild stable=true");
        }

        private static DataTable<AuditTagRow> BuildTable(int rowCount)
        {
            var rows = new AuditTagRow[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                rows[i] = new AuditTagRow(i, RowName(i), RowDescription(i), true);
            }
            return new DataTable<AuditTagRow>(rows);
        }

        private static void RegisterTableSource(DataTable<AuditTagRow> table)
        {
            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableSource<AuditTagRow>(
                "Audit.GameplayTags",
                table,
                static row => row.Name,
                static row => row.Description,
                static _ => GameplayTagFlags.None,
                static row => row.Enabled));
        }

        private static int[] BuildAndCaptureIndices(DataTable<AuditTagRow> table, int rowCount)
        {
            RegisterTableSource(table);
            GameplayTagManager.InitializeIfNeeded();
            var indices = new int[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                Assert.That(GameplayTagManager.TryRequest(RowName(i), out GameplayTag tag), Is.True);
                indices[i] = tag.RuntimeIndex;
            }
            return indices;
        }

        private static string RowName(int i) => $"DataTableAudit.Tag{i:0000}";
        private static string RowDescription(int i) => $"Audit row {i}";

        private sealed class AuditTagRow : IDataRow
        {
            public AuditTagRow(int id, string name, string description, bool enabled)
            {
                Id = id;
                Name = name;
                Description = description;
                Enabled = enabled;
            }

            public int Id { get; }
            public string Name { get; }
            public string Description { get; }
            public bool Enabled { get; }
        }
    }
}
