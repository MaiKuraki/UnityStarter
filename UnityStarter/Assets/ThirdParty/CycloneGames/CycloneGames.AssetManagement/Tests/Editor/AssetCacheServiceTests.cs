using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using NUnit.Framework;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetCacheServiceTests
    {
        [Test]
        public void BuildCacheKey_Separates_Operation_Kinds()
        {
            const string location = "Assets/Test/Icon.png";

            AssetCacheKey assetKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheEntryKind.Asset);
            AssetCacheKey allAssetsKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheEntryKind.AllAssets);
            AssetCacheKey rawKey = AssetCacheService.BuildCacheKey(location, null, AssetCacheEntryKind.RawFile);

            Assert.AreNotEqual(assetKey, allAssetsKey);
            Assert.AreNotEqual(assetKey, rawKey);
            Assert.AreNotEqual(allAssetsKey, rawKey);
        }

        [Test]
        public void Cache_Rejects_Unbounded_Entry_Limits_And_Negative_Byte_Budgets()
        {
            var package = new RecordingAssetPackage();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetCacheService(package, maxTrialEntries: 131_073, maxMainEntries: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 131_073));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1, maxIdleBytes: -1L));

            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetIdleMemoryBudget(-1L));
        }

        [Test]
        public void BuildCacheKey_Preserves_Location_Type_And_Kind()
        {
            const string location = "Assets/Test/Icon.png";
            AssetCacheKey cacheKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheEntryKind.Asset);

            Assert.AreEqual(location, cacheKey.Location);
            Assert.AreEqual(typeof(Texture2D), cacheKey.AssetType);
            Assert.AreEqual(AssetCacheEntryKind.Asset, cacheKey.Kind);
        }

        [Test]
        public void Retention_Policy_Defensively_Copies_Rule_Collections()
        {
            var source = new[] { AssetCacheRetentionRules.EvictAll };
            AssetCacheRetentionPolicy policy = AssetCacheRetentionPolicy.MatchingAny(source);
            source[0] = new NeverEvictRule();
            AssetCacheEntryInfo entry = CreateCacheEntryInfo();

            Assert.IsTrue(policy.ShouldEvict(in entry));
        }

        [Test]
        public void Composite_Rule_Defensively_Copies_Rule_Collections()
        {
            var source = new[] { AssetCacheRetentionRules.EvictAll };
            IAssetCacheRetentionRule composite = AssetCacheRetentionRules.Any(source);
            source[0] = new NeverEvictRule();
            AssetCacheEntryInfo entry = CreateCacheEntryInfo();

            Assert.IsTrue(composite.ShouldEvict(in entry));
        }

        [Test]
        public void Cache_Does_Not_Return_Asset_For_AllAssets_Key()
        {
            const string location = "Assets/Test/Icon.png";
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);

            AssetCacheKey assetKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheEntryKind.Asset);
            AssetCacheKey allAssetsKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheEntryKind.AllAssets);
            var handle = new TestAssetHandle<Texture2D>();

            cache.RegisterNew(assetKey, bucket: null, tag: null, owner: null, handle);

            Assert.IsNull(cache.Get(allAssetsKey, bucket: null, tag: null, owner: null));
        }

        [Test]
        public void TrimIdle_Evicts_Only_After_Minimum_Idle_Time()
        {
            const string location = "Assets/Test/Icon.png";
            var package = new RecordingAssetPackage();
            var clock = new ManualAssetCacheClock();
            using var cache = new AssetCacheService(package, 4, 4, 64L * 1024 * 1024, clock);

            AssetCacheKey cacheKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheEntryKind.Asset);
            var handle = new TestAssetHandle<Texture2D>();
            cache.RegisterNew(cacheKey, bucket: "UI.Shop", tag: "UI", owner: "Shop", handle);
            ReleaseToIdle(cache, cacheKey, handle);

            var policy = AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(60d));

            clock.AdvanceSeconds(59d);
            Assert.AreEqual(0, cache.TrimIdle(policy));
            Assert.IsTrue(cache.Contains(cacheKey));

            clock.AdvanceSeconds(1d);
            Assert.AreEqual(1, cache.TrimIdle(policy));
            Assert.IsFalse(cache.Contains(cacheKey));
        }

        [Test]
        public void TrimIdle_Composes_Global_And_Bucket_Retention_Rules()
        {
            var package = new RecordingAssetPackage();
            var clock = new ManualAssetCacheClock();
            using var cache = new AssetCacheService(package, 8, 8, 64L * 1024 * 1024, clock);

            AssetCacheKey sceneKey = AssetCacheService.BuildCacheKey("Assets/Test/SceneIcon.png", typeof(Texture2D), AssetCacheEntryKind.Asset);
            AssetCacheKey sharedKey = AssetCacheService.BuildCacheKey("Assets/Test/SharedIcon.png", typeof(Texture2D), AssetCacheEntryKind.Asset);
            var sceneHandle = new TestAssetHandle<Texture2D>();
            var sharedHandle = new TestAssetHandle<Texture2D>();

            cache.RegisterNew(sceneKey, bucket: "Scene.Battle.UI", tag: "UI", owner: "BattleHud", sceneHandle);
            cache.RegisterNew(sharedKey, bucket: "Shared.Foundation", tag: "Shared", owner: "Bootstrap", sharedHandle);
            ReleaseToIdle(cache, sceneKey, sceneHandle);
            ReleaseToIdle(cache, sharedKey, sharedHandle);

            var policy = AssetCacheRetentionPolicy.MatchingAny(
                AssetCacheRetentionRules.IdleForAtLeast(TimeSpan.FromSeconds(120d)),
                AssetCacheRetentionRules.All(
                    AssetCacheRetentionRules.Bucket("Scene.Battle", includeChildren: true),
                    AssetCacheRetentionRules.IdleForAtLeast(TimeSpan.FromSeconds(30d))));

            clock.AdvanceSeconds(40d);
            Assert.AreEqual(1, cache.TrimIdle(policy));
            Assert.IsFalse(cache.Contains(sceneKey));
            Assert.IsTrue(cache.Contains(sharedKey));

            clock.AdvanceSeconds(90d);
            Assert.AreEqual(1, cache.TrimIdle(policy));
            Assert.IsFalse(cache.Contains(sharedKey));
        }

        [Test]
        public void RegisterNew_Duplicate_Keeps_Canonical_Handle()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 4, 4, 64L * 1024 * 1024);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Assets/Test/Icon.png", typeof(Texture2D));
            var canonical = new TestAssetHandle<Texture2D>();
            var duplicate = new TestAssetHandle<Texture2D>();

            IReferenceCounted first = cache.RegisterNew(key, null, null, null, canonical);
            IReferenceCounted second = cache.RegisterNew(key, null, null, null, duplicate);

            Assert.AreSame(canonical, first);
            Assert.AreSame(canonical, second);
            Assert.AreEqual(2, canonical.RefCount);
            Assert.AreEqual(0, duplicate.RefCount);
        }

        [Test]
        public void Retention_Rules_Match_All_Bounded_Metadata_Associations()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 4, 4, 64L * 1024 * 1024);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Assets/Test/Shared.png", typeof(Texture2D));
            var handle = new TestAssetHandle<Texture2D>();

            cache.RegisterNew(key, "UI.SceneA", "UI", "SceneA", handle);
            cache.Get(key, "UI.SceneB", "Shared", "SceneB");
            handle.Release();
            handle.Release();
            cache.OnHandleReleased(key, handle);

            int evicted = cache.TrimIdle(new AssetCacheRetentionPolicy(
                AssetCacheRetentionRules.Bucket("UI.SceneB")));

            Assert.AreEqual(1, evicted);
            Assert.IsFalse(cache.Contains(key));
        }

        [Test]
        public void ClearBucket_Removes_All_Reverse_Associations_Before_Node_Reuse()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 4, 4, 64L * 1024 * 1024);
            AssetCacheKey firstKey = AssetCacheService.BuildCacheKey("Assets/Test/First.png", typeof(Texture2D));
            AssetCacheKey secondKey = AssetCacheService.BuildCacheKey("Assets/Test/Second.png", typeof(Texture2D));
            var firstHandle = new TestAssetHandle<Texture2D>();
            var secondHandle = new TestAssetHandle<Texture2D>();

            cache.RegisterNew(firstKey, "Scene.A", null, null, firstHandle);
            cache.Get(firstKey, "Scene.B", null, null);
            firstHandle.Release();
            firstHandle.Release();
            cache.OnHandleReleased(firstKey, firstHandle);
            cache.ClearBucket("Scene.A");

            cache.RegisterNew(secondKey, "Scene.C", null, null, secondHandle);
            cache.ClearBucket("Scene.B");

            Assert.IsTrue(cache.Contains(secondKey));
            Assert.AreEqual(1, secondHandle.RefCount);
        }

        [Test]
        public void Metadata_Overflow_Bypasses_Idle_Cache()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 16, 16, 64L * 1024 * 1024);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Assets/Test/DynamicOwner.png", typeof(Texture2D));
            var handle = new TestAssetHandle<Texture2D>();

            cache.RegisterNew(key, "Shared", "UI", "Owner0", handle);
            for (int i = 1; i <= 8; i++)
            {
                cache.Get(key, "Shared", "UI", $"Owner{i}");
            }

            for (int i = 0; i <= 8; i++)
            {
                handle.Release();
            }
            cache.OnHandleReleased(key, handle);

            Assert.IsFalse(cache.Contains(key));
        }

        [Test]
        public void Protected_Overflow_Demotes_Before_Probation_Eviction()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 1, 1, 64L * 1024 * 1024);
            AssetCacheKey keyA = AssetCacheService.BuildCacheKey("A", typeof(Texture2D));
            AssetCacheKey keyB = AssetCacheService.BuildCacheKey("B", typeof(Texture2D));
            AssetCacheKey keyC = AssetCacheService.BuildCacheKey("C", typeof(Texture2D));
            var handleA = new TestAssetHandle<Texture2D>();
            var handleB = new TestAssetHandle<Texture2D>();
            var handleC = new TestAssetHandle<Texture2D>();

            cache.RegisterNew(keyA, null, null, null, handleA);
            ReleaseToIdle(cache, keyA, handleA);
            cache.Get(keyA, null, null, null);
            ReleaseToIdle(cache, keyA, handleA);

            cache.RegisterNew(keyB, null, null, null, handleB);
            ReleaseToIdle(cache, keyB, handleB);
            cache.Get(keyB, null, null, null);
            ReleaseToIdle(cache, keyB, handleB);

            Assert.IsTrue(cache.Contains(keyA));
            Assert.IsTrue(cache.Contains(keyB));

            cache.RegisterNew(keyC, null, null, null, handleC);
            ReleaseToIdle(cache, keyC, handleC);

            Assert.IsFalse(cache.Contains(keyA));
            Assert.IsTrue(cache.Contains(keyB));
            Assert.IsTrue(cache.Contains(keyC));
        }

        [Test]
        public void One_Hit_Scan_Does_Not_Displace_Protected_Working_Set()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 2, 2, 64L * 1024 * 1024);
            AssetCacheKey hotKeyA = AssetCacheService.BuildCacheKey("Hot/A", typeof(Texture2D));
            AssetCacheKey hotKeyB = AssetCacheService.BuildCacheKey("Hot/B", typeof(Texture2D));
            var hotHandleA = new ControllableCacheHandle(true, string.Empty);
            var hotHandleB = new ControllableCacheHandle(true, string.Empty);

            cache.RegisterNew(hotKeyA, null, null, null, hotHandleA);
            ReleaseToIdle(cache, hotKeyA, hotHandleA);
            cache.Get(hotKeyA, null, null, null);
            ReleaseToIdle(cache, hotKeyA, hotHandleA);

            cache.RegisterNew(hotKeyB, null, null, null, hotHandleB);
            ReleaseToIdle(cache, hotKeyB, hotHandleB);
            cache.Get(hotKeyB, null, null, null);
            ReleaseToIdle(cache, hotKeyB, hotHandleB);

            for (int i = 0; i < 32; i++)
            {
                AssetCacheKey scanKey = AssetCacheService.BuildCacheKey($"Scan/{i}", typeof(Texture2D));
                var scanHandle = new ControllableCacheHandle(true, string.Empty);
                cache.RegisterNew(scanKey, null, null, null, scanHandle);
                ReleaseToIdle(cache, scanKey, scanHandle);
            }

            Assert.IsTrue(cache.Contains(hotKeyA));
            Assert.IsTrue(cache.Contains(hotKeyB));
            Assert.AreSame(hotHandleA, cache.Get(hotKeyA, null, null, null));
            Assert.AreSame(hotHandleB, cache.Get(hotKeyB, null, null, null));
            ReleaseToIdle(cache, hotKeyA, hotHandleA);
            ReleaseToIdle(cache, hotKeyB, hotHandleB);
        }

        [Test]
        public void Oversized_Reused_Handle_Bypasses_Idle_Cache_Without_Displacing_Protected_Working_Set()
        {
            const long idleBudget = 1L * 1024 * 1024;
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 2, 2, idleBudget);
            AssetCacheKey hotKey = AssetCacheService.BuildCacheKey("Hot", typeof(Texture2D));
            AssetCacheKey oversizedKey = AssetCacheService.BuildCacheKey("Oversized", typeof(Texture2D));
            var hotHandle = new ControllableCacheHandle(true, string.Empty, estimatedBytes: idleBudget / 4L);
            var oversizedHandle = new ControllableCacheHandle(true, string.Empty, estimatedBytes: idleBudget + 1L);

            cache.RegisterNew(hotKey, null, null, null, hotHandle);
            ReleaseToIdle(cache, hotKey, hotHandle);
            cache.Get(hotKey, null, null, null);
            ReleaseToIdle(cache, hotKey, hotHandle);

            cache.RegisterNew(oversizedKey, null, null, null, oversizedHandle);
            cache.Get(oversizedKey, null, null, null);
            oversizedHandle.Release();
            ReleaseToIdle(cache, oversizedKey, oversizedHandle);

            Assert.IsTrue(cache.Contains(hotKey));
            Assert.IsFalse(cache.Contains(oversizedKey));
            Assert.IsTrue(oversizedHandle.ForceDisposed);
            Assert.AreEqual(idleBudget / 4L, cache.IdleBytesApprox);
            Assert.AreEqual(1L, cache.CreateRuntimeSnapshot("Default", "Test").OversizeRejectionCount);
        }

        [Test]
        public void Oversized_First_Use_Bypasses_Idle_Cache_Without_Displacing_Existing_Entries()
        {
            const long idleBudget = 1L * 1024 * 1024;
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 2, 2, idleBudget);
            AssetCacheKey residentKey = AssetCacheService.BuildCacheKey("Resident", typeof(Texture2D));
            AssetCacheKey oversizedKey = AssetCacheService.BuildCacheKey("OversizedFirstUse", typeof(Texture2D));
            var residentHandle = new ControllableCacheHandle(
                true,
                string.Empty,
                estimatedBytes: idleBudget / 2L);
            var oversizedHandle = new ControllableCacheHandle(
                true,
                string.Empty,
                estimatedBytes: idleBudget + 1L);

            cache.RegisterNew(residentKey, null, null, null, residentHandle);
            ReleaseToIdle(cache, residentKey, residentHandle);

            cache.RegisterNew(oversizedKey, null, null, null, oversizedHandle);
            ReleaseToIdle(cache, oversizedKey, oversizedHandle);

            Assert.IsTrue(cache.Contains(residentKey));
            Assert.IsFalse(cache.Contains(oversizedKey));
            Assert.IsFalse(residentHandle.ForceDisposed);
            Assert.IsTrue(oversizedHandle.ForceDisposed);
            Assert.AreEqual(idleBudget / 2L, cache.IdleBytesApprox);
            Assert.AreEqual(1L, cache.CreateRuntimeSnapshot("Default", "Test").OversizeRejectionCount);
        }

        [Test]
        public void Entry_Equal_To_Complete_Byte_Budget_Is_Admitted()
        {
            const long idleBudget = 1L * 1024 * 1024;
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 1, 1, idleBudget);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("ExactBudget", typeof(Texture2D));
            var handle = new ControllableCacheHandle(
                true,
                string.Empty,
                estimatedBytes: idleBudget);

            cache.RegisterNew(key, null, null, null, handle);
            ReleaseToIdle(cache, key, handle);

            Assert.IsTrue(cache.Contains(key));
            Assert.IsFalse(handle.ForceDisposed);
            Assert.AreEqual(idleBudget, cache.IdleBytesApprox);
        }

        [Test]
        public void Releasing_Unfinished_Operation_Does_Not_Admit_It_To_Idle_Cache()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Pending", typeof(Texture2D));
            var handle = new PendingCacheHandle();

            cache.RegisterNew(key, null, null, null, handle);
            handle.Release();
            cache.OnHandleReleased(key, handle);

            Assert.IsFalse(cache.Contains(key));
            Assert.IsTrue(handle.ForceDisposed);
            Assert.AreEqual(1L, cache.CreateRuntimeSnapshot("Default", "Test").FailedOperationRejectionCount);
        }

        [Test]
        public void Advancing_Generation_Disposes_Idle_And_Forces_A_Fresh_Lookup()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Versioned", typeof(Texture2D));
            var handle = new ControllableCacheHandle(isDone: true, error: string.Empty);

            cache.RegisterNew(key, null, null, null, handle);
            handle.Release();
            cache.OnHandleReleased(key, handle);
            cache.AdvanceGeneration();

            Assert.IsFalse(cache.Contains(key));
            Assert.IsTrue(handle.ForceDisposed);
            Assert.AreEqual(1L, cache.CreateRuntimeSnapshot("Default", "Test").ExplicitEvictionCount);
        }

        [Test]
        public void Advancing_Generation_Detaches_Active_Handle_Until_Final_Release()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("VersionedActive", typeof(Texture2D));
            var handle = new ControllableCacheHandle(isDone: true, error: string.Empty);

            cache.RegisterNew(key, null, null, null, handle);
            cache.AdvanceGeneration();

            Assert.IsFalse(cache.Contains(key));
            Assert.IsFalse(handle.ForceDisposed);
            Assert.AreEqual(1, cache.ActiveCount);

            handle.Release();
            cache.OnHandleReleased(key, handle);
            Assert.IsTrue(handle.ForceDisposed);
            Assert.AreEqual(1, handle.ForceDisposeCount);
            Assert.AreEqual(0, cache.ActiveCount);
        }

        [Test]
        public void Dispose_ForceDisposes_GenerationDetached_Active_Handle()
        {
            var package = new RecordingAssetPackage();
            var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("DetachedOnShutdown", typeof(Texture2D));
            var handle = new ControllableCacheHandle(isDone: true, error: string.Empty);

            cache.RegisterNew(key, null, null, null, handle);
            cache.AdvanceGeneration();
            cache.Dispose();

            Assert.IsTrue(handle.ForceDisposed);
            Assert.AreEqual(1, handle.ForceDisposeCount);
            Assert.AreEqual(0, cache.ActiveCount);
        }

        [Test]
        public void Diagnostics_Identify_GenerationDetached_Handle_As_Active()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("DetachedDiagnostic", typeof(Texture2D));
            var handle = new ControllableCacheHandle(isDone: true, error: string.Empty);
            var active = new List<AssetCacheService.CacheDiagnosticEntry>();

            cache.RegisterNew(key, "Gameplay", "World", "TestOwner", handle);
            cache.AdvanceGeneration();
            cache.GetDiagnostics(active, null, null);

            Assert.AreEqual(1, active.Count);
            Assert.IsTrue(active[0].IsGenerationDetached);
            Assert.AreEqual(1, active[0].RefCount);
            Assert.Greater(active[0].DiagnosticId, 0);
            Assert.AreEqual(key.ToDiagnosticString(), active[0].CacheKey);
        }

        [Test]
        public void Diagnostics_Expose_Exact_Tracked_Handle_Identity()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("TrackedIdentity", typeof(Texture2D));
            var handle = new ControllableCacheHandle(
                isDone: true,
                error: string.Empty,
                diagnosticHandleId: 73L);
            var active = new List<AssetCacheService.CacheDiagnosticEntry>();

            cache.RegisterNew(key, null, null, null, handle);
            cache.GetDiagnostics(active, null, null);

            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(73L, active[0].HandleId);
        }

        [Test]
        public void Editor_Diagnostics_Epoch_Uses_Weak_Survivors_And_Monotonic_Row_Identity()
        {
            var package = new RecordingAssetPackage();
            var first = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            var second = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            var instances = new List<AssetCacheService>();
            AssetCacheKey firstKey = AssetCacheService.BuildCacheKey("DiagnosticsResetA", typeof(Texture2D));
            AssetCacheKey secondKey = AssetCacheService.BuildCacheKey("DiagnosticsResetB", typeof(Texture2D));
            first.RegisterNew(firstKey, null, null, null, new ControllableCacheHandle(true, string.Empty));
            second.RegisterNew(secondKey, null, null, null, new ControllableCacheHandle(true, string.Empty));
            var active = new List<AssetCacheService.CacheDiagnosticEntry>();
            first.GetDiagnostics(active, null, null);
            long firstDiagnosticId = active[0].DiagnosticId;
            second.GetDiagnostics(active, null, null);
            long secondDiagnosticId = active[0].DiagnosticId;

            try
            {
                AssetCacheService.CopyGlobalInstancesTo(instances);
                CollectionAssert.Contains(instances, first);
                CollectionAssert.Contains(instances, second);

                int survivorCount = AssetCacheService.BeginEditorDiagnosticsEpoch();
                AssetCacheService.CopyGlobalInstancesTo(instances);

                Assert.AreEqual(2, survivorCount);
                CollectionAssert.Contains(instances, first);
                CollectionAssert.Contains(instances, second);

                using var nextEpoch = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
                AssetCacheKey nextKey = AssetCacheService.BuildCacheKey("DiagnosticsResetNext", typeof(Texture2D));
                nextEpoch.RegisterNew(nextKey, null, null, null, new ControllableCacheHandle(true, string.Empty));
                nextEpoch.GetDiagnostics(active, null, null);

                Assert.Greater(active[0].DiagnosticId, Math.Max(firstDiagnosticId, secondDiagnosticId));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void Subsystem_Reset_With_Detached_Cache_Owner_Marks_Observation_Incomplete()
        {
            var package = new RecordingAssetPackage();
            var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);

            try
            {
                AssetRuntimeGuard.ResetStatics();

                Assert.IsTrue(HandleTracker.ObservationIncomplete);
            }
            finally
            {
                cache.Dispose();
                HandleTracker.Reset();
                SceneTracker.Reset();
            }
        }

        [Test]
        public void Diagnostics_Assign_Unique_Row_Identity_To_SameKey_Detached_Generations()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("RepeatedDiagnostic", typeof(Texture2D));
            var active = new List<AssetCacheService.CacheDiagnosticEntry>();

            cache.RegisterNew(key, null, null, null, new ControllableCacheHandle(isDone: true, error: string.Empty));
            cache.AdvanceGeneration();
            cache.RegisterNew(key, null, null, null, new ControllableCacheHandle(isDone: true, error: string.Empty));
            cache.AdvanceGeneration();
            cache.GetDiagnostics(active, null, null);

            Assert.AreEqual(2, active.Count);
            Assert.IsTrue(active[0].IsGenerationDetached);
            Assert.IsTrue(active[1].IsGenerationDetached);
            Assert.AreEqual(key.ToDiagnosticString(), active[0].CacheKey);
            Assert.AreEqual(key.ToDiagnosticString(), active[1].CacheKey);
            Assert.AreNotEqual(active[0].DiagnosticId, active[1].DiagnosticId);
        }

        [Test]
        public void Diagnostics_Bounded_Capture_Reports_Exact_Total_And_Prioritizes_Detached()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey detachedKey = AssetCacheService.BuildCacheKey("Bounded/Detached", typeof(Texture2D));
            AssetCacheKey activeKey = AssetCacheService.BuildCacheKey("Bounded/Active", typeof(Texture2D));
            var active = new List<AssetCacheService.CacheDiagnosticEntry>();

            cache.RegisterNew(
                detachedKey,
                null,
                null,
                null,
                new ControllableCacheHandle(isDone: true, error: string.Empty));
            cache.AdvanceGeneration();
            cache.RegisterNew(
                activeKey,
                null,
                null,
                null,
                new ControllableCacheHandle(isDone: true, error: string.Empty));

            AssetCacheService.CacheDiagnosticCapture capture = cache.GetDiagnostics(
                active,
                null,
                null,
                maxActiveEntries: 1,
                maxProbationEntries: 0,
                maxProtectedEntries: 0);

            Assert.AreEqual(2, capture.ActiveTotal);
            Assert.AreEqual(1, capture.ActiveCaptured);
            Assert.IsTrue(capture.IsTruncated);
            Assert.AreEqual(1, active.Count);
            Assert.IsTrue(active[0].IsGenerationDetached);
        }

        [Test]
        public void Multiple_Generations_Retain_Every_Detached_Handle_Until_Shutdown()
        {
            var package = new RecordingAssetPackage();
            var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("RepeatedGeneration", typeof(Texture2D));
            var first = new ControllableCacheHandle(isDone: true, error: string.Empty);
            var second = new ControllableCacheHandle(isDone: true, error: string.Empty);

            cache.RegisterNew(key, null, null, null, first);
            cache.AdvanceGeneration();
            cache.RegisterNew(key, null, null, null, second);
            cache.AdvanceGeneration();

            Assert.AreEqual(2, cache.ActiveCount);
            cache.Dispose();

            Assert.AreEqual(1, first.ForceDisposeCount);
            Assert.AreEqual(1, second.ForceDisposeCount);
            Assert.AreEqual(0, cache.ActiveCount);
        }

        [Test]
        public void Dispose_Attempts_Every_GenerationDetached_Handle_Before_Reporting_Failure()
        {
            var package = new RecordingAssetPackage();
            var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey throwingKey = AssetCacheService.BuildCacheKey("DetachedThrowing", typeof(Texture2D));
            AssetCacheKey healthyKey = AssetCacheService.BuildCacheKey("DetachedHealthy", typeof(Texture2D));
            var throwing = new ThrowingDisposeCacheHandle();
            var healthy = new ControllableCacheHandle(isDone: true, error: string.Empty);

            cache.RegisterNew(throwingKey, null, null, null, throwing);
            cache.RegisterNew(healthyKey, null, null, null, healthy);
            cache.AdvanceGeneration();

            Assert.Throws<AggregateException>(() => cache.Dispose());

            Assert.IsTrue(throwing.DisposeAttempted);
            Assert.IsTrue(healthy.ForceDisposed);
            Assert.AreEqual(0, cache.ActiveCount);
        }

        [Test]
        public void Advancing_Generation_Detaches_All_Keys_Before_Reporting_Idle_Release_Failure()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 2, maxMainEntries: 2);
            AssetCacheKey idleKey = AssetCacheService.BuildCacheKey("ThrowingIdle", typeof(Texture2D));
            AssetCacheKey activeKey = AssetCacheService.BuildCacheKey("Active", typeof(Texture2D));
            var throwingIdle = new ThrowingDisposeCacheHandle();
            var active = new ControllableCacheHandle(isDone: true, error: string.Empty);

            cache.RegisterNew(idleKey, null, null, null, throwingIdle);
            ReleaseToIdle(cache, idleKey, throwingIdle);
            cache.RegisterNew(activeKey, null, null, null, active);

            Assert.Throws<AggregateException>(() => cache.AdvanceGeneration());

            Assert.IsTrue(throwingIdle.DisposeAttempted);
            Assert.IsFalse(cache.Contains(idleKey));
            Assert.IsFalse(cache.Contains(activeKey));
            Assert.AreEqual(1, cache.ActiveCount);
            Assert.AreEqual(0, cache.IdleCount);
            Assert.IsFalse(active.ForceDisposed);

            active.Release();
            cache.OnHandleReleased(activeKey, active);
            Assert.IsTrue(active.ForceDisposed);
            Assert.AreEqual(0, cache.ActiveCount);
        }

        [Test]
        public void Releasing_Failed_Operation_Does_Not_Negative_Cache_It()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Failed", typeof(Texture2D));
            var handle = new ControllableCacheHandle(isDone: true, error: "transient failure");

            cache.RegisterNew(key, null, null, null, handle);
            handle.Release();
            cache.OnHandleReleased(key, handle);

            Assert.IsFalse(cache.Contains(key));
            Assert.IsTrue(handle.ForceDisposed);
            Assert.AreEqual(1L, cache.CreateRuntimeSnapshot("Default", "Test").FailedOperationRejectionCount);
        }

        [Test]
        public void Releasing_Faulted_Operation_With_Empty_Error_Does_Not_Cache_It()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("FaultedWithoutDiagnostic", typeof(Texture2D));
            var handle = new ControllableCacheHandle(
                isDone: true,
                error: string.Empty,
                taskFailure: new InvalidOperationException("provider task failed"));

            cache.RegisterNew(key, null, null, null, handle);
            handle.Release();
            cache.OnHandleReleased(key, handle);

            Assert.IsFalse(cache.Contains(key));
            Assert.IsTrue(handle.ForceDisposed);
        }

        [Test]
        public void Releasing_Canceled_Operation_With_Empty_Error_Does_Not_Cache_It()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("CanceledWithoutDiagnostic", typeof(Texture2D));
            var handle = new ControllableCacheHandle(
                isDone: true,
                error: string.Empty,
                taskCanceled: true);

            cache.RegisterNew(key, null, null, null, handle);
            handle.Release();
            cache.OnHandleReleased(key, handle);

            Assert.IsFalse(cache.Contains(key));
            Assert.IsTrue(handle.ForceDisposed);
        }

        [Test]
        public void Releasing_Unknown_Footprint_Raw_File_Does_Not_Bypass_Byte_Budget()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey(
                "Raw/InMemory.bin",
                null,
                AssetCacheEntryKind.RawFile);
            var handle = new UnknownFootprintRawFileHandle();

            cache.RegisterNew(key, null, null, null, handle);
            handle.Release();
            cache.OnHandleReleased(key, handle);

            Assert.IsFalse(cache.Contains(key));
            Assert.AreEqual(0, cache.IdleCount);
            Assert.AreEqual(0L, cache.IdleBytesApprox);
            Assert.IsTrue(handle.ForceDisposed);
            Assert.AreEqual(1L, cache.CreateRuntimeSnapshot("Default", "Test").UnknownFootprintRejectionCount);
        }

        [Test]
        public void Reused_Handle_Reestimates_Memory_On_Each_Idle_Transition()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Estimated", typeof(Texture2D));
            var handle = new ControllableCacheHandle(isDone: true, error: string.Empty);

            cache.RegisterNew(key, null, null, null, handle);
            ReleaseToIdle(cache, key, handle);
            cache.Get(key, null, null, null);
            ReleaseToIdle(cache, key, handle);

            Assert.AreEqual(2, handle.EstimateCallCount);
        }

        [Test]
        public void Retention_Rule_Reentrant_Mutation_Fails_Without_Corrupting_Cache()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Reentrant", typeof(Texture2D));
            var handle = new ControllableCacheHandle(isDone: true, error: string.Empty);
            cache.RegisterNew(key, null, null, null, handle);
            ReleaseToIdle(cache, key, handle);

            var policy = new AssetCacheRetentionPolicy(new ReentrantMutationRule(cache));

            Assert.Throws<InvalidOperationException>(() => cache.TrimIdle(policy));
            Assert.IsTrue(cache.Contains(key));
        }

        [Test]
        public void Dispose_Attempts_All_Handles_And_Allows_Terminal_Cleanup_Retry()
        {
            var package = new RecordingAssetPackage();
            var cache = new AssetCacheService(package, maxTrialEntries: 2, maxMainEntries: 2);
            AssetCacheKey throwingKey = AssetCacheService.BuildCacheKey("Throwing", typeof(Texture2D));
            AssetCacheKey healthyKey = AssetCacheService.BuildCacheKey("Healthy", typeof(Texture2D));
            var throwingHandle = new ThrowingDisposeCacheHandle();
            var healthyHandle = new ControllableCacheHandle(isDone: true, error: string.Empty);
            cache.RegisterNew(throwingKey, null, null, null, throwingHandle);
            cache.RegisterNew(healthyKey, null, null, null, healthyHandle);

            Assert.Throws<AggregateException>(() => cache.Dispose());

            Assert.IsTrue(throwingHandle.DisposeAttempted);
            Assert.IsTrue(healthyHandle.ForceDisposed);
            Assert.AreEqual(0, cache.ActiveCount);
            Assert.AreEqual(0, cache.IdleCount);
            Assert.DoesNotThrow(() => cache.Dispose());
        }

        [Test]
        public void ClearAll_Attempts_Every_Idle_Handle_Before_Reporting_Release_Failure()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 2, maxMainEntries: 2);
            AssetCacheKey throwingKey = AssetCacheService.BuildCacheKey("ThrowingIdle", typeof(Texture2D));
            AssetCacheKey healthyKey = AssetCacheService.BuildCacheKey("HealthyIdle", typeof(Texture2D));
            var throwingHandle = new ThrowingDisposeCacheHandle();
            var healthyHandle = new ControllableCacheHandle(isDone: true, error: string.Empty);

            cache.RegisterNew(throwingKey, null, null, null, throwingHandle);
            ReleaseToIdle(cache, throwingKey, throwingHandle);
            cache.RegisterNew(healthyKey, null, null, null, healthyHandle);
            ReleaseToIdle(cache, healthyKey, healthyHandle);

            Assert.Throws<AggregateException>(() => cache.ClearAll());

            Assert.IsTrue(throwingHandle.DisposeAttempted);
            Assert.IsTrue(healthyHandle.ForceDisposed);
            Assert.IsFalse(cache.Contains(throwingKey));
            Assert.IsFalse(cache.Contains(healthyKey));
            Assert.AreEqual(0, cache.IdleCount);
        }

        [Test]
        public void TrimIdle_Attempts_Every_Matching_Handle_Before_Reporting_Release_Failure()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 2, maxMainEntries: 2);
            AssetCacheKey throwingKey = AssetCacheService.BuildCacheKey("TrimThrowing", typeof(Texture2D));
            AssetCacheKey healthyKey = AssetCacheService.BuildCacheKey("TrimHealthy", typeof(Texture2D));
            var throwingHandle = new ThrowingDisposeCacheHandle();
            var healthyHandle = new ControllableCacheHandle(true, string.Empty);

            cache.RegisterNew(throwingKey, null, null, null, throwingHandle);
            ReleaseToIdle(cache, throwingKey, throwingHandle);
            cache.RegisterNew(healthyKey, null, null, null, healthyHandle);
            ReleaseToIdle(cache, healthyKey, healthyHandle);

            Assert.Throws<AggregateException>(() => cache.TrimIdle(AssetCacheRetentionPolicy.EvictAllIdle));

            Assert.IsTrue(throwingHandle.DisposeAttempted);
            Assert.IsTrue(healthyHandle.ForceDisposed);
            Assert.AreEqual(0, cache.IdleCount);
        }

        [Test]
        public void ClearBucket_Attempts_Every_Matching_Handle_Before_Reporting_Release_Failure()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 2, maxMainEntries: 2);
            AssetCacheKey throwingKey = AssetCacheService.BuildCacheKey("BucketThrowing", typeof(Texture2D));
            AssetCacheKey healthyKey = AssetCacheService.BuildCacheKey("BucketHealthy", typeof(Texture2D));
            var throwingHandle = new ThrowingDisposeCacheHandle();
            var healthyHandle = new ControllableCacheHandle(true, string.Empty);

            cache.RegisterNew(throwingKey, "Shared", null, null, throwingHandle);
            ReleaseToIdle(cache, throwingKey, throwingHandle);
            cache.RegisterNew(healthyKey, "Shared", null, null, healthyHandle);
            ReleaseToIdle(cache, healthyKey, healthyHandle);

            Assert.Throws<AggregateException>(() => cache.ClearBucket("Shared"));

            Assert.IsTrue(throwingHandle.DisposeAttempted);
            Assert.IsTrue(healthyHandle.ForceDisposed);
            Assert.AreEqual(0, cache.IdleCount);
        }

        [Test]
        public void Budget_Eviction_Continues_After_Provider_Release_Failure()
        {
            const long handleBytes = 600L * 1024;
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 4, 4, 4L * 1024 * 1024);
            AssetCacheKey throwingKey = AssetCacheService.BuildCacheKey("BudgetThrowing", typeof(Texture2D));
            AssetCacheKey healthyVictimKey = AssetCacheService.BuildCacheKey("BudgetHealthyVictim", typeof(Texture2D));
            AssetCacheKey survivorKey = AssetCacheService.BuildCacheKey("BudgetSurvivor", typeof(Texture2D));
            var throwingHandle = new ThrowingDisposeCacheHandle(handleBytes);
            var healthyVictim = new ControllableCacheHandle(true, string.Empty, estimatedBytes: handleBytes);
            var survivor = new ControllableCacheHandle(true, string.Empty, estimatedBytes: handleBytes);

            cache.RegisterNew(throwingKey, null, null, null, throwingHandle);
            ReleaseToIdle(cache, throwingKey, throwingHandle);
            cache.RegisterNew(healthyVictimKey, null, null, null, healthyVictim);
            ReleaseToIdle(cache, healthyVictimKey, healthyVictim);
            cache.RegisterNew(survivorKey, null, null, null, survivor);
            ReleaseToIdle(cache, survivorKey, survivor);

            Assert.Throws<AggregateException>(() => cache.SetIdleMemoryBudget(1L * 1024 * 1024));

            Assert.IsTrue(throwingHandle.DisposeAttempted);
            Assert.IsTrue(healthyVictim.ForceDisposed);
            Assert.IsTrue(cache.Contains(survivorKey));
            Assert.AreEqual(handleBytes, cache.IdleBytesApprox);
        }

        [Test]
        public void Aggregate_Memory_Estimate_Fails_Closed_When_Any_Member_Is_Unknown()
        {
            long total = 128L;

            Assert.IsFalse(AssetMemoryEstimator.TryAddToAggregate(null, ref total));
            Assert.AreEqual(0L, total);
        }

        [Test]
        public void Cache_Reactivation_Starts_A_New_Tracked_Active_Epoch()
        {
            DateTime nowUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long nowTimestamp = System.Diagnostics.Stopwatch.Frequency;
            HandleTracker.Reset();
            HandleTracker.UtcNowProvider = () => nowUtc;
            HandleTracker.MonotonicTimestampProvider = () => nowTimestamp;
            HandleTracker.Enabled = true;
            HandleTracker.Register(73L, "default", "Assets/Reactivated.asset");

            try
            {
                var package = new RecordingAssetPackage();
                using var cache = new AssetCacheService(package, 2, 2);
                AssetCacheKey key = AssetCacheService.BuildCacheKey("Reactivated", typeof(Texture2D));
                var handle = new ControllableCacheHandle(
                    isDone: true,
                    error: string.Empty,
                    diagnosticHandleId: 73L);
                cache.RegisterNew(key, null, null, null, handle);
                ReleaseToIdle(cache, key, handle);

                nowUtc = nowUtc.AddMinutes(10d);
                nowTimestamp += System.Diagnostics.Stopwatch.Frequency * 600L;
                IReferenceCounted reacquired = cache.Get(key, null, null, null);

                Assert.AreSame(handle, reacquired);
                HandleTracker.HandleInfo info = HandleTracker.GetActiveHandles()[0];
                Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), info.RegistrationTime);
                Assert.AreEqual(nowUtc, info.ActiveSince);
                Assert.AreEqual(nowTimestamp, info.ActiveSinceTimestamp);

                nowUtc = nowUtc.AddDays(-2d);
                nowTimestamp += System.Diagnostics.Stopwatch.Frequency * 360L;
                info = HandleTracker.GetActiveHandles()[0];
                Assert.AreEqual(
                    new DateTime(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc),
                    info.ActiveSince);
                Assert.AreEqual(360d, HandleTracker.GetActiveDurationSeconds(in info, nowTimestamp), 0.001d);

                ReleaseToIdle(cache, key, reacquired);
            }
            finally
            {
                HandleTracker.Reset();
            }
        }

        private static void ReleaseToIdle(AssetCacheService cache, AssetCacheKey cacheKey, IReferenceCounted handle)
        {
            handle.Release();
            cache.OnHandleReleased(cacheKey, handle);
        }

        private static AssetCacheEntryInfo CreateCacheEntryInfo()
        {
            return new AssetCacheEntryInfo(
                AssetCacheService.BuildCacheKey("Retention", typeof(Texture2D)),
                null,
                null,
                null,
                null,
                null,
                null,
                accessCount: 1,
                estimatedBytes: 1L,
                idleTime: TimeSpan.Zero,
                tier: AssetCacheIdleTier.Probation);
        }

        private sealed class NeverEvictRule : IAssetCacheRetentionRule
        {
            public bool ShouldEvict(in AssetCacheEntryInfo entry)
            {
                return false;
            }
        }

        private sealed class ManualAssetCacheClock : IAssetCacheClock
        {
            private double _seconds;

            public long Timestamp => (long)(_seconds * 1000d);

            public void AdvanceSeconds(double seconds)
            {
                _seconds += seconds;
            }

            public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
            {
                long delta = endTimestamp - startTimestamp;
                return delta <= 0L ? TimeSpan.Zero : TimeSpan.FromSeconds(delta / 1000d);
            }
        }

        private sealed class PendingCacheHandle : IAssetHandle<Texture2D>, IReferenceCounted, IInternalCacheable
        {
            private readonly UniTaskCompletionSource _completion = new UniTaskCompletionSource();

            public Texture2D Asset => null;
            public UnityEngine.Object AssetObject => null;
            public bool IsDone => false;
            public float Progress => 0f;
            public string Error => string.Empty;
            public UniTask Task => _completion.Task;
            public int RefCount { get; private set; } = 1;
            public bool ForceDisposed { get; private set; }

            public void Retain()
            {
                RefCount++;
            }

            public void Release()
            {
                RefCount--;
            }

            public void Dispose()
            {
                Release();
            }

            public void WaitForAsyncComplete()
            {
            }

            void IInternalCacheable.ForceDispose()
            {
                ForceDisposed = true;
            }
        }

        private sealed class ControllableCacheHandle : IAssetHandle<Texture2D>, IReferenceCounted,
            IInternalCacheable, IAssetMemoryFootprint, ITrackedAssetHandle
        {
            private readonly bool _isDone;
            private readonly string _error;
            private readonly UniTask _task;
            private readonly long _estimatedBytes;
            private readonly long _diagnosticHandleId;

            public ControllableCacheHandle(
                bool isDone,
                string error,
                Exception taskFailure = null,
                bool taskCanceled = false,
                long estimatedBytes = 64L,
                long diagnosticHandleId = 0L)
            {
                _isDone = isDone;
                _error = error;
                _estimatedBytes = estimatedBytes;
                _diagnosticHandleId = diagnosticHandleId;
                if (taskCanceled)
                {
                    _task = UniTask.FromCanceled(new System.Threading.CancellationToken(canceled: true));
                }
                else if (taskFailure != null || !string.IsNullOrEmpty(error))
                {
                    _task = UniTask.FromException(
                        taskFailure ?? new InvalidOperationException(error));
                }
                else if (isDone)
                {
                    _task = UniTask.CompletedTask;
                }
                else
                {
                    var completion = new UniTaskCompletionSource();
                    _task = completion.Task;
                }
            }

            public Texture2D Asset => null;
            public UnityEngine.Object AssetObject => null;
            public bool IsDone => _isDone;
            public float Progress => _isDone ? 1f : 0f;
            public string Error => _error;
            public UniTask Task => _task;
            public int RefCount { get; private set; } = 1;
            public bool ForceDisposed { get; private set; }
            public int ForceDisposeCount { get; private set; }
            public int EstimateCallCount { get; private set; }
            long ITrackedAssetHandle.DiagnosticHandleId => _diagnosticHandleId;

            public void Retain()
            {
                RefCount++;
            }

            public void Release()
            {
                RefCount--;
            }

            public void Dispose()
            {
                Release();
            }

            public void WaitForAsyncComplete()
            {
            }

            void IInternalCacheable.ForceDispose()
            {
                ForceDisposed = true;
                ForceDisposeCount++;
            }

            long IAssetMemoryFootprint.EstimateRuntimeBytes()
            {
                EstimateCallCount++;
                return _estimatedBytes;
            }
        }

        private sealed class UnknownFootprintRawFileHandle : IRawFileHandle, IReferenceCounted,
            IInternalCacheable
        {
            public string FilePath => string.Empty;
            public bool IsDone => true;
            public float Progress => 1f;
            public string Error => string.Empty;
            public UniTask Task => UniTask.CompletedTask;
            public int RefCount { get; private set; } = 1;
            public bool ForceDisposed { get; private set; }

            public string ReadText() => string.Empty;
            public byte[] ReadBytes() => System.Array.Empty<byte>();
            public void Retain() => RefCount++;
            public void Release() => RefCount--;
            public void Dispose() => Release();
            public void WaitForAsyncComplete() { }

            void IInternalCacheable.ForceDispose()
            {
                ForceDisposed = true;
            }
        }

        private sealed class ThrowingDisposeCacheHandle : IAssetHandle<Texture2D>, IReferenceCounted,
            IInternalCacheable, IAssetMemoryFootprint
        {
            private readonly long _estimatedBytes;

            public ThrowingDisposeCacheHandle(long estimatedBytes = 1L)
            {
                _estimatedBytes = estimatedBytes;
            }

            public Texture2D Asset => null;
            public UnityEngine.Object AssetObject => null;
            public bool IsDone => true;
            public float Progress => 1f;
            public string Error => string.Empty;
            public UniTask Task => UniTask.CompletedTask;
            public int RefCount { get; private set; } = 1;
            public bool DisposeAttempted { get; private set; }

            public void Retain() => RefCount++;
            public void Release() => RefCount--;
            public void Dispose() => Release();
            public void WaitForAsyncComplete() { }
            long IAssetMemoryFootprint.EstimateRuntimeBytes() => _estimatedBytes;

            void IInternalCacheable.ForceDispose()
            {
                DisposeAttempted = true;
                throw new InvalidOperationException("Synthetic provider release failure.");
            }
        }

        private sealed class ReentrantMutationRule : IAssetCacheRetentionRule
        {
            private readonly AssetCacheService _cache;

            public ReentrantMutationRule(AssetCacheService cache)
            {
                _cache = cache;
            }

            public bool ShouldEvict(in AssetCacheEntryInfo entry)
            {
                _cache.ClearAll();
                return true;
            }
        }
    }
}
