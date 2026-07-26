using System;
using System.Collections;
using System.Reflection;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class ExternalAudioClipMaintenanceTests
    {
        private static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public;

        [SetUp]
        public void SetUp()
        {
            AudioClipResolver.ClearExternalCache();
            ResetInjectedCache();
        }

        [TearDown]
        public void TearDown()
        {
            AudioClipResolver.ClearExternalCache();
            ResetInjectedCache();
        }

        [Test]
        public void BoundedMaintenance_RejectsWorkAboveTheHardCeiling()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ExternalAudioClipHandle.EvictExpiredEntriesBounded(
                    0f,
                    0L,
                    ExternalAudioClipHandle.MaximumEntriesToScanPerCall + 1,
                    1));
        }

        [Test]
        public void BoundedMaintenance_RejectsAnEvictionBudgetAboveTheScanBudget()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ExternalAudioClipHandle.EvictExpiredEntriesBounded(0f, 0L, 1, 2));
        }

        [Test]
        public void EmptyCache_ConsumesNoMaintenanceWork()
        {
            ExternalAudioClipEvictionResult result =
                ExternalAudioClipHandle.EvictExpiredEntriesBounded(0f, 0L, 1, 1);

            Assert.That(result.EntriesScanned, Is.Zero);
            Assert.That(result.EntriesEvicted, Is.Zero);
        }

        [Test]
        public void ExternalCacheCapacity_IsClampedWithoutInvalidatingTheConfigurationOwner()
        {
            int original = AudioManager.ExternalClipMaximumCacheEntryCount;
            try
            {
                AudioManager.ExternalClipMaximumCacheEntryCount = 0;
                Assert.That(AudioManager.ExternalClipMaximumCacheEntryCount, Is.EqualTo(1));

                AudioManager.ExternalClipMaximumCacheEntryCount = int.MaxValue;
                Assert.That(AudioManager.ExternalClipMaximumCacheEntryCount, Is.EqualTo(16384));
            }
            finally
            {
                AudioManager.ExternalClipMaximumCacheEntryCount = original;
            }
        }

        [Test]
        public void CacheStatsAndEviction_KeepLegacyClrOverloads()
        {
            Type[] legacyStatsParameters =
            {
                typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
                typeof(int), typeof(int), typeof(int), typeof(int)
            };
            Type[] legacyEvictionParameters = { typeof(float), typeof(long) };
            Type[] boundedEvictionParameters = { typeof(float), typeof(long), typeof(int) };

            Assert.That(
                typeof(ExternalAudioClipCacheStats).GetConstructor(legacyStatsParameters),
                Is.Not.Null);
            Assert.That(
                typeof(ExternalAudioClipHandle).GetMethod(
                    "EvictExpiredEntries",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    legacyEvictionParameters,
                    null),
                Is.Not.Null);
            Assert.That(
                typeof(ExternalAudioClipHandle).GetMethod(
                    "EvictExpiredEntries",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    boundedEvictionParameters,
                    null),
                Is.Not.Null);
        }

        [Test]
        public void LegacyMaintenance_PreservesFullCacheScanAndEviction()
        {
            int seededCount = ExternalAudioClipHandle.MaximumEntriesToScanPerCall + 2;
            SeedLoadedEntries(seededCount, activeEntryCount: 0, bytesPerEntry: 1L);

            int evicted = ExternalAudioClipHandle.EvictExpiredEntries(0f, 0L);
            ExternalAudioClipCacheStats stats = ExternalAudioClipHandle.GetCacheStats();

            Assert.That(evicted, Is.EqualTo(seededCount));
            Assert.That(stats.EntryCount, Is.Zero);
            Assert.That(stats.LoadedCount, Is.Zero);
            Assert.That(stats.EvictionScanCount, Is.EqualTo(seededCount));
        }

        [Test]
        public void LegacyMaintenance_StopsAfterRestoringPositiveMemoryBudget()
        {
            SeedLoadedEntries(entryCount: 3, activeEntryCount: 0, bytesPerEntry: 10L);

            int evicted = ExternalAudioClipHandle.EvictExpiredEntries(
                float.MaxValue,
                memoryBudgetBytes: 20L);
            ExternalAudioClipCacheStats stats = ExternalAudioClipHandle.GetCacheStats();

            Assert.That(evicted, Is.EqualTo(1));
            Assert.That(stats.EntryCount, Is.EqualTo(2));
            Assert.That(stats.EvictionScanCount, Is.EqualTo(3));
            Assert.That(ExternalAudioClipHandle.GetTotalCachedMemoryBytes(), Is.EqualTo(20L));
        }

        [Test]
        public void BoundedMaintenance_AdvancesAcrossBatchesWithoutExceedingTheCeiling()
        {
            int seededCount = ExternalAudioClipHandle.MaximumEntriesToScanPerCall + 2;
            SeedLoadedEntries(seededCount, activeEntryCount: 0, bytesPerEntry: 1L);

            int firstEvicted = ExternalAudioClipHandle.EvictExpiredEntries(
                0f,
                0L,
                ExternalAudioClipHandle.MaximumEntriesToScanPerCall);
            ExternalAudioClipCacheStats firstStats = ExternalAudioClipHandle.GetCacheStats();

            Assert.That(firstEvicted, Is.EqualTo(ExternalAudioClipHandle.MaximumEntriesToScanPerCall));
            Assert.That(firstStats.EntryCount, Is.EqualTo(2));
            Assert.That(firstStats.EvictionScanCount, Is.EqualTo(ExternalAudioClipHandle.MaximumEntriesToScanPerCall));

            int secondEvicted = ExternalAudioClipHandle.EvictExpiredEntries(
                0f,
                0L,
                ExternalAudioClipHandle.MaximumEntriesToScanPerCall);
            ExternalAudioClipCacheStats secondStats = ExternalAudioClipHandle.GetCacheStats();

            Assert.That(secondEvicted, Is.EqualTo(2));
            Assert.That(secondStats.EntryCount, Is.Zero);
            Assert.That(secondStats.EvictionScanCount, Is.EqualTo(seededCount));
        }

        [Test]
        public void BoundedMaintenance_StopsAtBudgetAndProtectsActiveReferences()
        {
            SeedLoadedEntries(entryCount: 3, activeEntryCount: 1, bytesPerEntry: 10L);

            ExternalAudioClipEvictionResult result =
                ExternalAudioClipHandle.EvictExpiredEntriesBounded(
                    float.MaxValue,
                    memoryBudgetBytes: 20L,
                    maximumEntriesToScan: 3,
                    maximumEntriesToEvict: 3);
            ExternalAudioClipCacheStats stats = ExternalAudioClipHandle.GetCacheStats();

            Assert.That(result.EntriesScanned, Is.EqualTo(3));
            Assert.That(result.EntriesEvicted, Is.EqualTo(1));
            Assert.That(stats.EntryCount, Is.EqualTo(2));
            Assert.That(stats.LoadedCount, Is.EqualTo(2));
            Assert.That(stats.TotalRefCount, Is.EqualTo(1));
            Assert.That(ExternalAudioClipHandle.GetTotalCachedMemoryBytes(), Is.EqualTo(20L));
        }

        [Test]
        public void CacheAdmission_RejectsBeforeAllocatingPastConfiguredCapacity()
        {
            int originalMaximum = AudioManager.ExternalClipMaximumCacheEntryCount;
            AudioClipReference reference = null;
            try
            {
                AudioManager.ExternalClipMaximumCacheEntryCount = 1;
                SeedLoadedEntries(entryCount: 1, activeEntryCount: 0, bytesPerEntry: 1L);
                reference = AudioClipReference.CreateRuntime(AudioLocationKind.Url, "https://example.invalid/rejected.ogg");
                var key = new AudioClipCacheKey(reference);
                MethodInfo acquire = typeof(ExternalAudioClipHandle).GetMethod("AcquireEntry", StaticPrivate);

                object result = acquire.Invoke(null, new object[] { key, reference.Location });
                ExternalAudioClipCacheStats stats = ExternalAudioClipHandle.GetCacheStats();

                Assert.That(result, Is.Null);
                Assert.That(stats.EntryCount, Is.EqualTo(1));
                Assert.That(stats.AdmissionRejectionCount, Is.EqualTo(1));
            }
            finally
            {
                if (reference != null)
                {
                    UnityEngine.Object.DestroyImmediate(reference);
                }
                AudioManager.ExternalClipMaximumCacheEntryCount = originalMaximum;
            }
        }

        [Test]
        public void PopulatedCacheStats_AreZeroAllocationAfterWarmup()
        {
            const int entryCount = 1024;
            const int iterations = 64;
            SeedLoadedEntries(entryCount, activeEntryCount: 8, bytesPerEntry: 4L);
            ExternalAudioClipHandle.GetCacheStats();

            int observedEntries = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < iterations; index++)
            {
                observedEntries += ExternalAudioClipHandle.GetCacheStats().EntryCount;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(observedEntries, Is.EqualTo(entryCount * iterations));
            Assert.That(after - before, Is.Zero);
        }

        [Test]
        public void MaintenanceCeiling_HasOneConfigurationSource()
        {
            Assert.That(
                ExternalAudioClipHandle.MaximumEntriesToScanPerCall,
                Is.EqualTo(AudioManager.MaximumIdleTrimItemsPerCall));
        }

        [Test]
        public void IdleTrim_SourceSlotAndExternalCacheShareOneWorkBudget()
        {
            GameObject managerObject = null;
            AudioManager manager = null;
            try
            {
                managerObject = new GameObject("ExternalAudioClipMaintenanceTests.AudioManager");
                managerObject.SetActive(false);
                managerObject.AddComponent<AudioListener>();
                manager = managerObject.AddComponent<AudioManager>();
                typeof(AudioManager).GetField("customPoolSize", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, 1);
                managerObject.SetActive(true);
                AudioManager.SetInstance(manager);

                SeedLoadedEntries(entryCount: 1, activeEntryCount: 0, bytesPerEntry: 1L);
                AudioSource source = null;
                foreach (AudioSource candidate in AudioManager.AvailableSources)
                {
                    source = candidate;
                    break;
                }

                Assert.That(source, Is.Not.Null);
                UnityEngine.Object.DestroyImmediate(source.gameObject);
                SetStaticField(typeof(AudioManager), "initialPoolSize", 0);
                typeof(AudioManager).GetField("trimExternalCacheFirst", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, false);

                AudioIdleTrimResult result = manager.TrimIdleMemory(1);

                Assert.That(result.TotalWorkCount, Is.EqualTo(1));
                Assert.That(result.IdleSourceScanCount, Is.EqualTo(1));
                Assert.That(result.IdleSourceCount, Is.Zero);
                Assert.That(result.ExternalClipScanCount, Is.Zero);
                Assert.That(ExternalAudioClipHandle.GetCacheStats().EntryCount, Is.EqualTo(1));
            }
            finally
            {
                if (manager != null)
                {
                    AudioManager.ReleaseInstance(manager);
                }

                if (managerObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(managerObject);
                }
            }
        }

        private static void SeedLoadedEntries(int entryCount, int activeEntryCount, long bytesPerEntry)
        {
            Type ownerType = typeof(ExternalAudioClipHandle);
            Type entryType = ownerType.GetNestedType("CacheEntry", BindingFlags.NonPublic);
            ConstructorInfo constructor = entryType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(AudioClipCacheKey), typeof(string) },
                null);
            var cache = (IDictionary)ownerType.GetField("cache", StaticPrivate).GetValue(null);
            var cacheEntries = (IList)ownerType.GetField("cacheEntries", StaticPrivate).GetValue(null);
            AudioClipReference reference = AudioClipReference.CreateRuntime(AudioLocationKind.Url, string.Empty);
            try
            {
                for (int index = 0; index < entryCount; index++)
                {
                    string location = "https://example.invalid/cache-" + index + ".ogg";
                    reference.SetLocation(location);
                    var key = new AudioClipCacheKey(reference);
                    object entry = constructor.Invoke(new object[] { key, location });
                    SetEntryField(entryType, entry, "IsDone", true);
                    SetEntryField(entryType, entry, "IsSuccess", true);
                    SetEntryField(entryType, entry, "RefCount", index < activeEntryCount ? 1 : 0);
                    SetEntryField(entryType, entry, "EstimatedMemoryBytes", bytesPerEntry);
                    SetEntryField(entryType, entry, "IsMemoryAccounted", true);
                    SetEntryField(entryType, entry, "CacheIndex", index);
                    cache.Add(key, entry);
                    cacheEntries.Add(entry);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(reference);
            }

            SetStaticField(ownerType, "loadingEntryCount", 0);
            SetStaticField(ownerType, "loadedEntryCount", entryCount);
            SetStaticField(ownerType, "failedEntryCount", 0);
            SetStaticField(ownerType, "totalReferenceCount", activeEntryCount);
            SetStaticField(ownerType, "totalCachedMemoryBytes", bytesPerEntry * entryCount);
            SetStaticField(ownerType, "evictionScanCursor", 0);
        }

        private static void ResetInjectedCache()
        {
            Type ownerType = typeof(ExternalAudioClipHandle);
            ((IDictionary)ownerType.GetField("cache", StaticPrivate).GetValue(null)).Clear();
            ((IList)ownerType.GetField("cacheEntries", StaticPrivate).GetValue(null)).Clear();
            string[] integerFields =
            {
                "loadingEntryCount",
                "loadedEntryCount",
                "failedEntryCount",
                "totalReferenceCount",
                "totalLoadRequests",
                "cacheHitCount",
                "cacheMissCount",
                "totalFailureCount",
                "admissionRejectionCount",
                "evictionScanCursor"
            };
            for (int index = 0; index < integerFields.Length; index++)
            {
                SetStaticField(ownerType, integerFields[index], 0);
            }

            SetStaticField(ownerType, "totalCachedMemoryBytes", 0L);
            SetStaticField(ownerType, "evictionScanCount", 0L);
            SetStaticField(ownerType, "evictionCount", 0L);
        }

        private static void SetEntryField(Type entryType, object entry, string name, object value)
        {
            entryType.GetField(name, InstanceFields).SetValue(entry, value);
        }

        private static void SetStaticField(Type ownerType, string name, object value)
        {
            ownerType.GetField(name, StaticPrivate).SetValue(null, value);
        }
    }
}
