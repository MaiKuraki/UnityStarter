using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using CycloneGames.UIFramework.DynamicAtlas;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class DynamicAtlasServiceTests
    {
        [Test]
        public void TryAcquire_DuplicateKeyReusesSpriteAndLeaseCountsAreExact()
        {
            Texture2D firstSource = CreateTexture(12, 12, TextureFormat.RGBA32, Color.red);
            Texture2D secondSource = CreateTexture(12, 12, TextureFormat.RGBA32, Color.blue);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                DynamicAtlasInsertStatus firstStatus = service.TryAcquire("icon/shared", firstSource, out DynamicAtlasSpriteLease first);
                DynamicAtlasInsertStatus secondStatus = service.TryAcquire("icon/shared", secondSource, out DynamicAtlasSpriteLease second);

                Assert.That(firstStatus, Is.EqualTo(DynamicAtlasInsertStatus.Success));
                Assert.That(secondStatus, Is.EqualTo(DynamicAtlasInsertStatus.CacheHit));
                Assert.That(second.Sprite, Is.SameAs(first.Sprite));
                Assert.That(service.GetStats().ActiveReferenceCount, Is.EqualTo(2));

                first.Dispose();
                Assert.That(service.GetStats().ActiveReferenceCount, Is.EqualTo(1));
                second.Dispose();

                DynamicAtlasStats retained = service.GetStats();
                Assert.That(retained.ActiveReferenceCount, Is.Zero);
                Assert.That(retained.RetainedEntryCount, Is.EqualTo(1));
                Assert.That(service.TrimUnused(), Is.EqualTo(1));
                Assert.That(service.GetStats().EntryCount, Is.Zero);
            }
            finally
            {
                Destroy(firstSource);
                Destroy(secondSource);
            }
        }

        [Test]
        public void RemoveWhenUnused_ReleasesEntryAndPageImmediately()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused;
            config.minRetainedPages = 0;
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.green);
            using var service = new DynamicAtlasService(config);

            try
            {
                AssertSuccessful(service.TryAcquire("temporary", source, out DynamicAtlasSpriteLease lease));
                lease.Dispose();

                DynamicAtlasStats stats = service.GetStats();
                Assert.That(stats.EntryCount, Is.Zero);
                Assert.That(stats.PageCount, Is.Zero);
                Assert.That(stats.EstimatedTextureBytes, Is.Zero);
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void ReleasedSlot_IsReusedWithoutAllocatingAnotherPage()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused;
            config.minRetainedPages = 1;
            Texture2D source = CreateTexture(24, 24, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(config);

            try
            {
                AssertSuccessful(service.TryAcquire("first", source, out DynamicAtlasSpriteLease first));
                first.Dispose();
                Assert.That(service.GetStats().PageCount, Is.EqualTo(1));

                AssertSuccessful(service.TryAcquire("second", source, out DynamicAtlasSpriteLease second));
                Assert.That(service.GetStats().PageCount, Is.EqualTo(1));
                second.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void EntryCapacity_EvictsOldestUnusedBeforeRejecting()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.maxEntries = 1;
            config.maxEntriesPerPage = 1;
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(config);

            try
            {
                AssertSuccessful(service.TryAcquire("old", source, out DynamicAtlasSpriteLease oldLease));
                oldLease.Dispose();
                AssertSuccessful(service.TryAcquire("new", source, out DynamicAtlasSpriteLease newLease));

                Assert.That(service.TryGetSprite("old", out _), Is.False);
                Assert.That(service.GetStats().EvictionCount, Is.EqualTo(1));
                newLease.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void EntryCapacity_RejectsWhenEveryEntryIsActive()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.maxEntries = 1;
            config.maxEntriesPerPage = 1;
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(config);

            try
            {
                AssertSuccessful(service.TryAcquire("active", source, out DynamicAtlasSpriteLease active));
                DynamicAtlasInsertStatus status = service.TryAcquire("rejected", source, out DynamicAtlasSpriteLease rejected);

                Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.EntryCapacityReached));
                Assert.That(rejected, Is.Null);
                active.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void EntryCapacity_RejectsLocationBeforeInvokingLoaderWhenEveryEntryIsActive()
        {
            int loadCount = 0;
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.maxEntries = 1;
            config.maxEntriesPerPage = 1;
            config.loadFunc = _ =>
            {
                loadCount++;
                return source;
            };
            config.unloadFunc = (_, __) => { };
            using var service = new DynamicAtlasService(config);

            try
            {
                AssertSuccessful(service.TryAcquire("active", source, out DynamicAtlasSpriteLease active));

                DynamicAtlasInsertStatus status = service.TryAcquireLocation(
                    "loader-must-not-run",
                    out DynamicAtlasSpriteLease rejected);

                Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.EntryCapacityReached));
                Assert.That(rejected, Is.Null);
                Assert.That(loadCount, Is.Zero);
                active.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void PageCapacity_RejectsWhenActiveContentCannotFit()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.maxPages = 1;
            config.maxEntries = 2;
            config.maxEntriesPerPage = 2;
            Texture2D source = CreateTexture(40, 40, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(config);

            try
            {
                AssertSuccessful(service.TryAcquire("large/a", source, out DynamicAtlasSpriteLease first));
                DynamicAtlasInsertStatus status = service.TryAcquire("large/b", source, out DynamicAtlasSpriteLease second);

                Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.PageCapacityReached));
                Assert.That(second, Is.Null);
                first.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void TextureMemoryBudget_RejectsSecondCpuBackedPage()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.maxPages = 2;
            config.maxEntries = 2;
            config.maxEntriesPerPage = 2;
            config.memoryBudgetBytes = TextureFormatHelper.EstimatePageBytes(
                config.pageSize,
                DynamicAtlasCopyFallback.AllowSynchronousReadback);
            Texture2D source = CreateTexture(40, 40, TextureFormat.ARGB32, Color.white);
            using var service = new DynamicAtlasService(config);

            try
            {
                AssertSuccessful(service.TryAcquire("cpu/a", source, out DynamicAtlasSpriteLease first));
                DynamicAtlasInsertStatus status = service.TryAcquire("cpu/b", source, out DynamicAtlasSpriteLease second);

                Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.MemoryBudgetReached));
                Assert.That(second, Is.Null);
                Assert.That(service.GetStats().EstimatedTextureBytes, Is.EqualTo(config.memoryBudgetBytes));
                first.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void InvalidRegionAndOversizedSource_FailWithoutLeakingEntries()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            Texture2D source = CreateTexture(128, 128, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(config);

            try
            {
                DynamicAtlasInsertStatus invalidRegion = service.TryAcquireRegion(
                    "bad-region",
                    source,
                    new RectInt(120, 120, 16, 16),
                    out DynamicAtlasSpriteLease invalidLease);
                DynamicAtlasInsertStatus oversized = service.TryAcquire("oversized", source, out DynamicAtlasSpriteLease oversizedLease);

                Assert.That(invalidRegion, Is.EqualTo(DynamicAtlasInsertStatus.InvalidRegion));
                Assert.That(oversized, Is.EqualTo(DynamicAtlasInsertStatus.OversizedSource));
                Assert.That(invalidLease, Is.Null);
                Assert.That(oversizedLease, Is.Null);
                Assert.That(service.GetStats().EntryCount, Is.Zero);
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void Loader_UnloadsExactlyOnceOnSuccessAndFailure()
        {
            Texture2D source = CreateTexture(128, 128, TextureFormat.RGBA32, Color.white);
            int unloadCount = 0;
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.loadFunc = _ => source;
            config.unloadFunc = (_, __) => unloadCount++;
            using var service = new DynamicAtlasService(config);

            try
            {
                DynamicAtlasInsertStatus failure = service.TryAcquireLocation("too-large", out DynamicAtlasSpriteLease failedLease);
                Assert.That(failure, Is.EqualTo(DynamicAtlasInsertStatus.OversizedSource));
                Assert.That(failedLease, Is.Null);
                Assert.That(unloadCount, Is.EqualTo(1));

                config = DynamicAtlasConfigTests.CreateSmallConfig();
                config.loadFunc = _ => source;
                config.unloadFunc = (_, __) => unloadCount++;
                config.oversizePolicy = DynamicAtlasOversizePolicy.ScaleDown;
                using var scalingService = new DynamicAtlasService(config);
                DynamicAtlasInsertStatus success = scalingService.TryAcquireLocation("scaled", out DynamicAtlasSpriteLease scaledLease);
                AssertSuccessful(success);
                Assert.That(unloadCount, Is.EqualTo(2));
                scaledLease.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void TryAcquireLocation_RequiresAnExplicitLoaderOwnershipPair()
        {
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            DynamicAtlasInsertStatus status = service.TryAcquireLocation(
                "shared/resource",
                out DynamicAtlasSpriteLease lease);

            Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.LoaderUnavailable));
            Assert.That(lease, Is.Null);
            Assert.That(service.GetStats().EntryCount, Is.Zero);
        }

        [Test]
        public void InvalidRegionWithOverflowingCoordinates_IsRejectedBeforeAllocation()
        {
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                DynamicAtlasInsertStatus status = service.TryAcquireRegion(
                    "overflow-region",
                    source,
                    new RectInt(int.MaxValue, int.MaxValue, 1, 1),
                    out DynamicAtlasSpriteLease lease);

                Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.InvalidRegion));
                Assert.That(lease, Is.Null);
                Assert.That(service.GetStats().PageCount, Is.Zero);
                Assert.That(service.GetStats().EntryCount, Is.Zero);
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void PrewarmPages_RejectsUnknownPageMode()
        {
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                service.PrewarmPages(1, (DynamicAtlasPageMode)999));
            Assert.That(service.GetStats().PageCount, Is.Zero);
        }

        [Test]
        public void TryGetSprite_AfterWarmup_DoesNotAllocateManagedMemory()
        {
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                AssertSuccessful(service.TryAcquire("hot-query", source, out DynamicAtlasSpriteLease lease));
                Assert.That(service.TryGetSprite("hot-query", out _), Is.True);

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; i++)
                {
                    service.TryGetSprite("hot-query", out _);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero);
                lease.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void PublicOperations_RejectNonOwnerThread()
        {
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    service.GetStats();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            thread.Start();
            thread.Join();

            Assert.That(captured, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void AcquireVariantsAndLeaseDispose_FailBeforeUnityAccessOnWorkerThread()
        {
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            Sprite sourceSprite = Sprite.Create(
                source,
                new Rect(0f, 0f, 8f, 8f),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                AssertSuccessful(service.TryAcquire("lease", source, out DynamicAtlasSpriteLease lease));

                Exception textureError = CaptureWorkerException(() => service.TryAcquire("worker/texture", source, out _));
                Exception regionError = CaptureWorkerException(() => service.TryAcquireRegion(
                    "worker/region", source, new RectInt(0, 0, 4, 4), out _));
                Exception spriteError = CaptureWorkerException(() => service.TryAcquireSprite(
                    "worker/sprite", sourceSprite, out _));
                Exception leaseError = CaptureWorkerException(lease.Dispose);

                Assert.That(textureError, Is.TypeOf<InvalidOperationException>());
                Assert.That(regionError, Is.TypeOf<InvalidOperationException>());
                Assert.That(spriteError, Is.TypeOf<InvalidOperationException>());
                Assert.That(leaseError, Is.TypeOf<InvalidOperationException>());
                Assert.That(lease.IsDisposed, Is.False);
                lease.Dispose();
            }
            finally
            {
                Destroy(sourceSprite);
                Destroy(source);
            }
        }

        [Test]
        public void Constructor_RejectsWorkerThreadBeforeTouchingUnityObjects()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = new DynamicAtlasService(config);
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            thread.Start();
            thread.Join();

            Assert.That(captured, Is.TypeOf<InvalidOperationException>());
            Assert.That(captured.Message, Does.Contain("main thread"));
        }

        [Test]
        public void Clear_InvalidatesOutstandingLeaseWithoutDoubleRelease()
        {
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                AssertSuccessful(service.TryAcquire("clear", source, out DynamicAtlasSpriteLease lease));
                service.Clear();

                Assert.That(service.GetStats().EntryCount, Is.Zero);
                Assert.DoesNotThrow(() => lease.Dispose());
                Assert.That(lease.IsDisposed, Is.True);
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void SnapshotCopies_ReuseCallerOwnedLists()
        {
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            var pages = new List<DynamicAtlasPageSnapshot>(2);
            var entries = new List<DynamicAtlasEntrySnapshot>(2);

            try
            {
                AssertSuccessful(service.TryAcquire("snapshot", source, out DynamicAtlasSpriteLease lease));
                Assert.That(service.CopyPageSnapshots(pages), Is.EqualTo(1));
                Assert.That(service.CopyEntrySnapshots(entries), Is.EqualTo(1));
                Assert.That(entries[0].Key, Is.EqualTo("snapshot"));
                Assert.That(entries[0].PageId, Is.EqualTo(pages[0].PageId));
                lease.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void WriteBatch_SupportsNestedOwnershipAndFlushesAtOuterDispose()
        {
            Texture2D source = CreateTexture(8, 8, TextureFormat.ARGB32, Color.white);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                using (DynamicAtlasWriteBatch outer = service.BeginBatch())
                {
                    AssertSuccessful(service.TryAcquire("batch/a", source, out DynamicAtlasSpriteLease first));
                    using (DynamicAtlasWriteBatch inner = service.BeginBatch())
                    {
                        AssertSuccessful(service.TryAcquire("batch/b", source, out DynamicAtlasSpriteLease second));
                        second.Dispose();
                    }

                    Assert.That(service.GetStats().EntryCount, Is.EqualTo(2));
                    first.Dispose();
                }

                Assert.That(service.GetStats().EntryCount, Is.EqualTo(2));
                Assert.That(service.GetStats().SynchronousReadbackCount, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void Clear_InvalidatesOutstandingBatchWithoutThrowingOnLateDispose()
        {
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            DynamicAtlasWriteBatch staleBatch = service.BeginBatch();

            service.Clear();

            Assert.DoesNotThrow(() => staleBatch.Dispose());
        }

        [Test]
        public void StaleBatchDispose_DoesNotConsumeTheCurrentBatchDepth()
        {
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            DynamicAtlasWriteBatch staleBatch = service.BeginBatch();
            service.Clear();
            DynamicAtlasWriteBatch currentBatch = service.BeginBatch();

            Assert.DoesNotThrow(() => staleBatch.Dispose());
            Assert.DoesNotThrow(() => currentBatch.Dispose());
        }

        [Test]
        public void RetainedEntries_EvictInStableLeastRecentlyUsedOrder()
        {
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                AssertSuccessful(service.TryAcquire("lru/a", source, out DynamicAtlasSpriteLease first));
                first.Dispose();
                AssertSuccessful(service.TryAcquire("lru/b", source, out DynamicAtlasSpriteLease second));
                second.Dispose();
                Assert.That(service.TryAcquireCached("lru/a", out DynamicAtlasSpriteLease refreshed), Is.True);
                refreshed.Dispose();

                Assert.That(service.TrimUnused(1), Is.EqualTo(1));
                Assert.That(service.TryGetSprite("lru/b", out _), Is.False);
                Assert.That(service.TryGetSprite("lru/a", out _), Is.True);
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void ReleasedSlotMetadata_RemainsBoundedDuringIrregularChurn()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.maxPages = 1;
            config.minRetainedPages = 1;
            config.maxEntries = 64;
            config.maxEntriesPerPage = 2;
            config.padding = 0;
            config.retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused;
            Texture2D source = CreateTexture(32, 1, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(config);
            var pages = new List<DynamicAtlasPageSnapshot>(1);

            try
            {
                AssertSuccessful(service.TryAcquireRegion(
                    "slot/anchor",
                    source,
                    new RectInt(0, 0, 1, 1),
                    out DynamicAtlasSpriteLease anchor));

                for (int width = 2; width <= 24; width++)
                {
                    AssertSuccessful(service.TryAcquireRegion(
                        "slot/" + width,
                        source,
                        new RectInt(0, 0, width, 1),
                        out DynamicAtlasSpriteLease lease));
                    lease.Dispose();
                }

                service.CopyPageSnapshots(pages);
                Assert.That(pages, Has.Count.EqualTo(1));
                Assert.That(pages[0].ReleasedSlotCount, Is.LessThanOrEqualTo(config.maxEntriesPerPage));
                anchor.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void ReleasedSlots_CoalesceIntoReusableLargeRegionWithinMetadataLimit()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.maxPages = 1;
            config.minRetainedPages = 1;
            config.maxEntries = 16;
            config.maxEntriesPerPage = 2;
            config.padding = 0;
            config.retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused;
            Texture2D source = CreateTexture(62, 64, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(config);
            var pages = new List<DynamicAtlasPageSnapshot>(1);

            try
            {
                AssertSuccessful(service.TryAcquireRegion(
                    "slot/anchor",
                    source,
                    new RectInt(0, 0, 1, 64),
                    out DynamicAtlasSpriteLease anchor));

                int[] widths = { 2, 4, 8, 16, 32 };
                for (int i = 0; i < widths.Length; i++)
                {
                    int width = widths[i];
                    AssertSuccessful(service.TryAcquireRegion(
                        "slot/churn/" + width,
                        source,
                        new RectInt(0, 0, width, 64),
                        out DynamicAtlasSpriteLease churn));
                    churn.Dispose();
                }

                AssertSuccessful(service.TryAcquireRegion(
                    "slot/reused-large-hole",
                    source,
                    new RectInt(0, 0, 62, 64),
                    out DynamicAtlasSpriteLease reused));
                service.CopyPageSnapshots(pages);

                Assert.That(pages, Has.Count.EqualTo(1));
                Assert.That(pages[0].ReleasedSlotCount, Is.LessThanOrEqualTo(config.maxEntriesPerPage));
                reused.Dispose();
                anchor.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void EntryGenerationWrap_DoesNotLetAStaleLeaseReleaseTheReplacement()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused;
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(config);

            try
            {
                TestReflection.SetField(service, "_nextEntryGeneration", long.MaxValue - 1L);
                AssertSuccessful(service.TryAcquire("generation", source, out DynamicAtlasSpriteLease stale));
                service.Clear();
                AssertSuccessful(service.TryAcquire("generation", source, out DynamicAtlasSpriteLease current));

                stale.Dispose();

                Assert.That(service.GetStats().ActiveReferenceCount, Is.EqualTo(1));
                Assert.That(current.Sprite, Is.Not.Null);
                current.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void ScaleDown_PreservesSpriteLogicalSizeAndMetadata()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.oversizePolicy = DynamicAtlasOversizePolicy.ScaleDown;
            Texture2D sourceTexture = CreateTexture(128, 64, TextureFormat.RGBA32, Color.white);
            Sprite sourceSprite = Sprite.Create(
                sourceTexture,
                new Rect(0f, 0f, 128f, 64f),
                new Vector2(0.25f, 0.75f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(4f, 6f, 8f, 10f));
            using var service = new DynamicAtlasService(config);

            try
            {
                AssertSuccessful(service.TryAcquireSprite(
                    "scaled-sprite",
                    sourceSprite,
                    out DynamicAtlasSpriteLease lease));

                Assert.That(lease.Sprite.bounds.size.x, Is.EqualTo(sourceSprite.bounds.size.x).Within(0.001f));
                Assert.That(lease.Sprite.bounds.size.y, Is.EqualTo(sourceSprite.bounds.size.y).Within(0.001f));
                Assert.That(lease.Sprite.pivot.x / lease.Sprite.rect.width, Is.EqualTo(0.25f).Within(0.001f));
                Assert.That(lease.Sprite.pivot.y / lease.Sprite.rect.height, Is.EqualTo(0.75f).Within(0.001f));
                lease.Dispose();
            }
            finally
            {
                Destroy(sourceSprite);
                Destroy(sourceTexture);
            }
        }

        [Test]
        public void LinearRgbaSource_DoesNotUseRawCopyIntoSrgbPage()
        {
            var source = new Texture2D(8, 8, TextureFormat.RGBA32, mipChain: false, linear: true);
            source.SetPixels32(new Color32[64]);
            source.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                AssertSuccessful(service.TryAcquire("linear-source", source, out DynamicAtlasSpriteLease lease));
                Assume.That(source.graphicsFormat, Is.Not.EqualTo(lease.Sprite.texture.graphicsFormat));
                Assert.That(service.GetStats().CpuRawCopyCount, Is.Zero);
                Assert.That(service.GetStats().SynchronousReadbackCount, Is.GreaterThanOrEqualTo(1));
                lease.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void CpuRawCopyPolicy_AllowsRawCopyButDoesNotReadBackUnsupportedFormatsOrEvict()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.maxPages = 1;
            config.minRetainedPages = 1;
            config.copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy;
            Texture2D compatible = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            Texture2D incompatible = CreateTexture(8, 8, TextureFormat.ARGB32, Color.red);
            using var service = new DynamicAtlasService(config);

            try
            {
                Assert.That(service.PrewarmPages(1, DynamicAtlasPageMode.CpuBacked), Is.EqualTo(1));
                AssertSuccessful(service.TryAcquire("raw/retained", compatible, out DynamicAtlasSpriteLease retained));
                retained.Dispose();

                DynamicAtlasInsertStatus status = service.TryAcquire(
                    "raw/unsupported",
                    incompatible,
                    out DynamicAtlasSpriteLease rejected);
                DynamicAtlasStats stats = service.GetStats();

                Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.CopyUnsupported));
                Assert.That(rejected, Is.Null);
                Assert.That(service.TryGetSprite("raw/retained", out _), Is.True);
                Assert.That(stats.CpuRawCopyCount, Is.EqualTo(1));
                Assert.That(stats.SynchronousReadbackCount, Is.Zero);
                Assert.That(stats.EvictionCount, Is.Zero);
            }
            finally
            {
                Destroy(compatible);
                Destroy(incompatible);
            }
        }

        [Test]
        public void CpuRawCopy_RegionPreservesExactPixels()
        {
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.padding = 0;
            config.enableBleed = false;
            config.copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy;
            var source = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false);
            var pixels = new Color32[16];
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    pixels[(y * 4) + x] = new Color32((byte)(x * 40), (byte)(y * 50), (byte)(x + y), 255);
                }
            }

            source.SetPixels32(pixels);
            source.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            using var service = new DynamicAtlasService(config);

            try
            {
                Assert.That(service.PrewarmPages(1, DynamicAtlasPageMode.CpuBacked), Is.EqualTo(1));
                AssertSuccessful(service.TryAcquireRegion(
                    "raw/pixels",
                    source,
                    new RectInt(1, 1, 2, 2),
                    out DynamicAtlasSpriteLease lease));

                Rect atlasRect = lease.Sprite.textureRect;
                Color32[] atlasPixels = lease.Sprite.texture.GetPixels32();
                int atlasWidth = lease.Sprite.texture.width;
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        Color32 expected = pixels[((y + 1) * 4) + x + 1];
                        Color32 actual = atlasPixels[
                            (((int)atlasRect.y + y) * atlasWidth) + (int)atlasRect.x + x];
                        Assert.That(actual, Is.EqualTo(expected));
                    }
                }

                Assert.That(service.GetStats().CpuRawCopyCount, Is.EqualTo(1));
                lease.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void GeneratedPageAndSprite_AreNotPersisted()
        {
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            var pages = new List<DynamicAtlasPageSnapshot>(1);

            try
            {
                AssertSuccessful(service.TryAcquire("dont-save", source, out DynamicAtlasSpriteLease lease));
                service.CopyPageSnapshots(pages);

                Assert.That(pages, Has.Count.EqualTo(1));
                Assert.That(pages[0].Texture.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave));
                Assert.That(lease.Sprite.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave));
                lease.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void TryAcquireSprite_CacheHitDoesNotRequireAnotherSource()
        {
            Texture2D sourceTexture = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            Sprite sourceSprite = Sprite.Create(
                sourceTexture,
                new Rect(0f, 0f, 8f, 8f),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                AssertSuccessful(service.TryAcquireSprite("sprite/cache-first", sourceSprite, out DynamicAtlasSpriteLease first));
                DynamicAtlasInsertStatus status = service.TryAcquireSprite(
                    "sprite/cache-first",
                    null,
                    out DynamicAtlasSpriteLease cached);

                Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.CacheHit));
                Assert.That(cached.Sprite, Is.SameAs(first.Sprite));
                cached.Dispose();
                first.Dispose();
            }
            finally
            {
                Destroy(sourceSprite);
                Destroy(sourceTexture);
            }
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

        private static void AssertSuccessful(DynamicAtlasInsertStatus status)
        {
            Assert.That(
                status == DynamicAtlasInsertStatus.Success || status == DynamicAtlasInsertStatus.CacheHit,
                Is.True,
                $"Expected a successful dynamic atlas insertion, got {status}.");
        }

        private static Exception CaptureWorkerException(Action action)
        {
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            thread.Start();
            thread.Join();
            return captured;
        }

        private static void Destroy(UnityEngine.Object target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
