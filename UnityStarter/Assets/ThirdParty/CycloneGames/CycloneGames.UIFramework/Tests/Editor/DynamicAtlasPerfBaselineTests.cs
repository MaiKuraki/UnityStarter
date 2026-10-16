// M-3: raw texture memory is not guaranteed zero-initialized; page initialization must zero explicitly.
using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.UIFramework.DynamicAtlas;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    /// <summary>
    /// Evidence tests and perf baselines for the dynamic atlas defects M-2 (released-slot
    /// coalescing + churn), M-3 (page zero-fill cost), L-11 (rejection warning dedup /
    /// unbounded cache) and L-12 (wrap guards that never fire). BENCH tests only emit timing lines.
    /// </summary>
    public sealed class DynamicAtlasPerfBaselineTests
    {
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(8);

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_owned[i]);
                }
            }

            _owned.Clear();
        }

        // M-2 perf baseline: 2000 acquire+dispose churn cycles over a 400-key pool on a
        // small page. BENCH only: emits a timing line, no threshold assertion.
        [Test]
        public void Bench_Atlas_ReleasedSlotChurn()
        {
            const int iterations = 2000;
            const int keyPool = 400;

            DynamicAtlasConfig config = CreatePageConfig(pageSize: 256, maxPages: 8);
            // Keep one page resident so the churn exercises the released-slot reserve/merge
            // path instead of tearing the page down after every dispose.
            config.minRetainedPages = 1;
            Texture2D source = Track(CreateTexture(64, 64, TextureFormat.RGBA32, Color.white));
            using var service = new DynamicAtlasService(config);

            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            for (int i = 0; i < iterations; i++)
            {
                string key = "churn/" + (i % keyPool);
                // Vary the width so freed slots fragment and the linear released-slot scan grows.
                int width = 8 + (i % 16);
                DynamicAtlasInsertStatus status = service.TryAcquireRegion(
                    key,
                    source,
                    new RectInt(0, 0, width, 8),
                    out DynamicAtlasSpriteLease lease);
                Assert.That(
                    status == DynamicAtlasInsertStatus.Success ||
                    status == DynamicAtlasInsertStatus.CacheHit,
                    Is.True,
                    $"Churn acquire {i} failed with {status}.");
                lease.Dispose();
            }

            stopwatch.Stop();
            UnityEngine.Debug.Log(
                $"BENCH:Atlas_Churn2000={stopwatch.Elapsed.TotalMilliseconds:F3}");
        }

        // M-2 correctness: releasing an adjacent block immediately after a swap-remove
        // merge must land in the already-combined slot; the current loop skips it and
        // leaves two separable free blocks -> RED.
        [Test]
        public void ReleasedSlots_AdjacentFreeBlocks_AreFullyCoalesced()
        {
            DynamicAtlasConfig config = CreatePageConfig(pageSize: 256, maxPages: 1);
            config.minRetainedPages = 1;
            Texture2D source = Track(CreateTexture(256, 256, TextureFormat.RGBA32, Color.white));
            using var service = new DynamicAtlasService(config);
            var pages = new List<DynamicAtlasPageSnapshot>(1);

            // Row 0: Q [0,0,2,4] then X [2,0,130,4]; new row: R [0,4,132,4]; anchor trails.
            AssertSuccessful(service.TryAcquireRegion(
                "merge/q", source, new RectInt(0, 0, 2, 4), out DynamicAtlasSpriteLease q));
            AssertSuccessful(service.TryAcquireRegion(
                "merge/x", source, new RectInt(0, 0, 130, 4), out DynamicAtlasSpriteLease x));
            AssertSuccessful(service.TryAcquireRegion(
                "merge/r", source, new RectInt(0, 0, 132, 4), out DynamicAtlasSpriteLease r));
            AssertSuccessful(service.TryAcquireRegion(
                "merge/anchor", source, new RectInt(0, 0, 16, 4), out DynamicAtlasSpriteLease anchor));

            // Release order [Q, R] then X: Q and X coalesce into a full-width block, but the
            // swap-remove loop has already visited R and never re-tests it against the merged
            // block, although R is vertically adjacent to it.
            q.Dispose();
            r.Dispose();
            x.Dispose();

            service.CopyPageSnapshots(pages);
            Assert.That(pages, Has.Count.EqualTo(1));
            Assert.That(
                pages[0].ReleasedSlotCount,
                Is.EqualTo(1),
                "Adjacent released slots must coalesce into a single free block.");

            anchor.Dispose();
        }

        // M-3 perf baseline: page creation for a 2048 CPU-backed page (includes the explicit
        // zero-fill). BENCH only: emits a timing line.
        [Test]
        public void Bench_Atlas_PageCreate2048()
        {
            DynamicAtlasConfig config = CreateLargePageConfig();
            using var service = new DynamicAtlasService(config);

            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            int created = service.PrewarmPages(1, DynamicAtlasPageMode.CpuBacked);
            stopwatch.Stop();

            UnityEngine.Debug.Log(
                $"BENCH:Atlas_PageCreate2048={stopwatch.Elapsed.TotalMilliseconds:F3}");
            Assert.That(created, Is.EqualTo(1));
        }

        // M-3: a freshly created CPU-backed page texture must read back as zeros. The page
        // initializer zeroes it explicitly because raw texture memory is not guaranteed zero.
        [Test]
        public void NewAtlasPage_TextureStartsZeroInitialized()
        {
            DynamicAtlasConfig config = CreateLargePageConfig();
            using var service = new DynamicAtlasService(config);

            Assert.That(service.PrewarmPages(1, DynamicAtlasPageMode.CpuBacked), Is.EqualTo(1));

            var pages = new List<DynamicAtlasPageSnapshot>(1);
            service.CopyPageSnapshots(pages);
            Assert.That(pages, Has.Count.EqualTo(1));

            Texture2D texture = pages[0].Texture;
            Assert.That(texture, Is.Not.Null);
            Assert.That(
                texture.isReadable,
                Is.True,
                "CPU-backed page textures must remain readable for raw inspection.");
            AssertSamplesZero(texture);
        }

        // L-11a: rejection warnings must dedupe by (key, reason); the current cache keys on
        // the window id alone, so a different reason for the same key is swallowed -> RED.
        [Test]
        public void RejectionWarnings_AreDeduplicatedByKeyAndReason()
        {
            Texture2D incompatible = Track(CreateTexture(8, 8, TextureFormat.ARGB32, Color.white));
            var recorder = new RecordingLogWriter();
            ILogWriter previousWriter = InstallRecorder(recorder);

            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.copyFallback = DynamicAtlasCopyFallback.GpuOnly;
            using var service = new DynamicAtlasService(config);

            try
            {
                // EditMode cannot fabricate a packed sprite, so the packing warning is
                // invoked directly with the same (key, reason) the packing branch passes.
                TestReflection.Invoke(
                    service,
                    "WarnRejectionOnce",
                    "shout/mixed",
                    "the source atlas uses tight packing",
                    "Disable rotation and tight packing on the source SpriteAtlas.");

                // Same key, different reason, driven through the real format-rejection path.
                DynamicAtlasInsertStatus status =
                    service.TryAcquire("shout/mixed", incompatible, out _);
                Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.CopyUnsupported));

                Assert.That(
                    recorder.Warnings.Count,
                    Is.EqualTo(2),
                    "Distinct rejection reasons for the same key must each emit a warning.");
            }
            finally
            {
                RestoreRecorder(recorder, previousWriter);
            }
        }

        // L-11b: the rejection-warning dedup cache must be bounded; today it grows without
        // limit -> RED.
        [Test]
        public void RejectionWarningKeyCache_DoesNotGrowWithoutBound()
        {
            const int cap = 512;
            var recorder = new RecordingLogWriter();
            ILogWriter previousWriter = InstallRecorder(recorder);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                for (int i = 0; i < 600; i++)
                {
                    TestReflection.Invoke(
                        service,
                        "WarnRejectionOnce",
                        "overflow/" + i,
                        "reason",
                        "guidance");
                }

                var keys = (HashSet<string>)TestReflection.GetField(
                    service,
                    "_rejectionWarningKeys");
                Assert.That(keys, Is.Not.Null);
                Assert.That(
                    keys.Count,
                    Is.LessThanOrEqualTo(cap),
                    "The rejection-warning dedup cache must be bounded.");
            }
            finally
            {
                RestoreRecorder(recorder, previousWriter);
            }
        }

        // L-12a: the rental-id wrap guard (`== 0`) never fires because long.MaxValue + 1 is
        // long.MinValue -> RED.
        [Test]
        public void RentalIdOverflow_ResetsToSmallPositiveValue()
        {
            Texture2D source = Track(CreateTexture(8, 8, TextureFormat.RGBA32, Color.white));
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            TestReflection.SetField(service, "_nextRentalId", long.MaxValue);

            DynamicAtlasInsertStatus status =
                service.TryAcquire("rollover/rental", source, out DynamicAtlasSpriteLease lease);
            AssertSuccessful(status);

            long wrapValue = (long)TestReflection.GetField(service, "_nextRentalId");
            Assert.That(
                wrapValue,
                Is.GreaterThan(0L),
                "The rental-id wrap guard must reset to a small positive value instead of overflowing to long.MinValue.");

            lease.Dispose();
        }

        // L-12b: same misplaced guard on the entry generation counter -> RED.
        [Test]
        public void EntryGenerationOverflow_ResetsToSmallPositiveValue()
        {
            Texture2D source = Track(CreateTexture(8, 8, TextureFormat.RGBA32, Color.white));
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            TestReflection.SetField(service, "_nextEntryGeneration", long.MaxValue);

            DynamicAtlasInsertStatus status =
                service.TryAcquire("rollover/generation", source, out DynamicAtlasSpriteLease lease);
            AssertSuccessful(status);

            long wrapValue = (long)TestReflection.GetField(service, "_nextEntryGeneration");
            Assert.That(
                wrapValue,
                Is.GreaterThan(0L),
                "The entry-generation wrap guard must reset to a small positive value instead of overflowing to long.MinValue.");

            lease.Dispose();
        }

        // L-12c: the batch-epoch guard has the same defect. The counter is only advanced in
        // ClearCore, so the wrap is triggered there (BeginBatch/EndBatch do not mutate it).
        [Test]
        public void BatchEpochOverflow_ResetsToSmallPositiveValue()
        {
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            TestReflection.SetField(service, "_batchEpoch", long.MaxValue);

            service.Clear();

            long wrapValue = (long)TestReflection.GetField(service, "_batchEpoch");
            Assert.That(
                wrapValue,
                Is.GreaterThan(0L),
                "The batch-epoch wrap guard must reset to a small positive value instead of overflowing to long.MinValue.");
        }

        // L-12c (sanity): even after the unguarded wrap, a Begin/End round trip must leave
        // the batch depth balanced at zero. GREEN on both the current and fixed tree.
        [Test]
        public void BatchDepth_ReturnsToZeroAfterWrapRoundTrip()
        {
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            TestReflection.SetField(service, "_batchEpoch", long.MaxValue);

            service.Clear();

            DynamicAtlasWriteBatch batch = service.BeginBatch();
            batch.Dispose();

            int depth = (int)TestReflection.GetField(service, "_batchDepth");
            Assert.That(depth, Is.EqualTo(0));
        }

        private static DynamicAtlasConfig CreatePageConfig(int pageSize, int maxPages)
        {
            return new DynamicAtlasConfig
            {
                pageSize = pageSize,
                maxPages = maxPages,
                minRetainedPages = 0,
                maxEntries = 512,
                maxEntriesPerPage = 512,
                maxKeyLength = 256,
                memoryBudgetBytes = 32L * 1024L * 1024L,
                padding = 0,
                enableBleed = false,
                retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy,
                // Disable the per-frame miss budget so synchronous EditMode churn stays frame-independent.
                maxMissesPerFrame = 0,
            };
        }

        private static DynamicAtlasConfig CreateLargePageConfig()
        {
            return new DynamicAtlasConfig
            {
                pageSize = 2048,
                maxPages = 2,
                minRetainedPages = 0,
                maxEntries = 64,
                maxEntriesPerPage = 8,
                maxKeyLength = 256,
                memoryBudgetBytes = 256L * 1024L * 1024L,
                padding = 2,
                enableBleed = true,
                retentionPolicy = DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = DynamicAtlasCopyFallback.AllowSynchronousReadback,
                maxMissesPerFrame = 0,
            };
        }

        private static void AssertSamplesZero(Texture2D texture)
        {
            var data = texture.GetRawTextureData<byte>();
            int[] samples = { 0, 1, data.Length / 2, data.Length - 2, data.Length - 1 };
            for (int i = 0; i < samples.Length; i++)
            {
                int index = samples[i];
                if (index < 0 || index >= data.Length)
                {
                    continue;
                }

                Assert.That(
                    data[index],
                    Is.EqualTo(0),
                    $"Expected zero-initialized texture byte at index {index}.");
            }
        }

        private static void AssertSuccessful(DynamicAtlasInsertStatus status)
        {
            Assert.That(
                status == DynamicAtlasInsertStatus.Success ||
                status == DynamicAtlasInsertStatus.CacheHit,
                Is.True,
                $"Expected a successful dynamic atlas insertion, got {status}.");
        }

        private static Texture2D CreateTexture(
            int width,
            int height,
            TextureFormat format,
            Color color)
        {
            var texture = new Texture2D(width, height, format, mipChain: false);
            var pixels = new Color32[width * height];
            Color32 pixel = color;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = pixel;
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private static ILogWriter InstallRecorder(RecordingLogWriter recorder)
        {
            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.That(
                LogRuntime.TryReplaceWriter(previousWriter, recorder),
                Is.True,
                "Failed to install the recording log writer.");
            return previousWriter;
        }

        private static void RestoreRecorder(RecordingLogWriter recorder, ILogWriter previousWriter)
        {
            LogRuntime.TryReplaceWriter(recorder, previousWriter);
        }

        private Texture2D Track(Texture2D texture)
        {
            _owned.Add(texture);
            return texture;
        }

        private sealed class RecordingLogWriter : ILogWriter
        {
            internal readonly List<string> Warnings = new List<string>(4);

            public bool IsEnabled(LogSeverity severity, string category)
            {
                return true;
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (severity == LogSeverity.Warning)
                {
                    Warnings.Add(message);
                }
            }

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                RecordBuilder(severity, messageBuilder);
            }

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (severity != LogSeverity.Warning)
                {
                    return;
                }

                var builder = new StringBuilder(128);
                messageBuilder(state, builder);
                Warnings.Add(builder.ToString());
            }

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (severity == LogSeverity.Warning)
                {
                    Warnings.Add(message ?? exception.Message);
                }
            }

            private void RecordBuilder(LogSeverity severity, Action<StringBuilder> messageBuilder)
            {
                if (severity != LogSeverity.Warning)
                {
                    return;
                }

                var builder = new StringBuilder(128);
                messageBuilder(builder);
                Warnings.Add(builder.ToString());
            }
        }
    }
}
