using System;
using NUnit.Framework;
using UnityEngine;
using CycloneGames.UIFramework.DynamicAtlas;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class DynamicAtlasConfigTests
    {
        [Test]
        public void DefaultConfiguration_PrefersGpuAndProvidesBoundedReadableCpuFallback()
        {
            var config = new DynamicAtlasConfig();

            Assert.That(config.pageSize, Is.EqualTo(1024));
            Assert.That(config.maxPages, Is.EqualTo(2));
            Assert.That(config.minRetainedPages, Is.Zero);
            Assert.That(config.maxEntries, Is.EqualTo(512));
            Assert.That(config.maxEntriesPerPage, Is.EqualTo(384));
            Assert.That(config.memoryBudgetBytes, Is.EqualTo(16L * 1024L * 1024L));
            Assert.That(config.copyFallback, Is.EqualTo(DynamicAtlasCopyFallback.AllowCpuRawCopy));
            Assert.That(config.Validate(out string error), Is.True, error);
        }

        [Test]
        public void CreateForTier_MobileLowAllowsTwoCpuBackedPagesWithinItsBudget()
        {
            DynamicAtlasConfig config = DynamicAtlasConfig.CreateForTier(
                DynamicAtlasConfig.PlatformTier.MobileLowEnd);
            long cpuBackedPageBytes = TextureFormatHelper.EstimatePageBytes(
                config.pageSize,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);

            Assert.That(config.copyFallback, Is.EqualTo(DynamicAtlasCopyFallback.AllowCpuRawCopy));
            Assert.That(config.memoryBudgetBytes, Is.GreaterThanOrEqualTo(cpuBackedPageBytes * config.maxPages));
            Assert.That(config.Validate(out string error), Is.True, error);
        }

        [Test]
        public void CreateForTier_WebGL_IsBounded()
        {
            DynamicAtlasConfig config = DynamicAtlasConfig.CreateForTier(DynamicAtlasConfig.PlatformTier.WebGL);

            Assert.That(config.pageSize, Is.EqualTo(1024));
            Assert.That(config.maxPages, Is.EqualTo(2));
            Assert.That(config.maxEntries, Is.EqualTo(512));
            Assert.That(config.memoryBudgetBytes, Is.EqualTo(20L * 1024L * 1024L));
            Assert.That(config.copyFallback, Is.EqualTo(DynamicAtlasCopyFallback.AllowCpuRawCopy));
            Assert.That(config.Validate(out _), Is.True);
        }

        [TestCase(DynamicAtlasConfig.PlatformTier.DesktopHighEnd)]
        [TestCase(DynamicAtlasConfig.PlatformTier.MobileHighEnd)]
        [TestCase(DynamicAtlasConfig.PlatformTier.MobileLowEnd)]
        [TestCase(DynamicAtlasConfig.PlatformTier.WebGL)]
        public void CreateForTier_AlwaysProducesAValidSafeDefault(
            DynamicAtlasConfig.PlatformTier tier)
        {
            DynamicAtlasConfig config = DynamicAtlasConfig.CreateForTier(tier);

            Assert.That(config.oversizePolicy, Is.EqualTo(DynamicAtlasOversizePolicy.Reject));
            Assert.That(
                config.copyFallback,
                Is.Not.EqualTo(DynamicAtlasCopyFallback.AllowSynchronousReadback));
            Assert.That(config.Validate(out string error), Is.True, error);
        }

        [Test]
        public void ManagerConvenienceConfigure_ProducesAValidPlatformConfiguration()
        {
            var gameObject = new GameObject("DynamicAtlasManagerTest");
            gameObject.SetActive(false);
            try
            {
                DynamicAtlasManager manager = gameObject.AddComponent<DynamicAtlasManager>();

                manager.Configure(null, null, autoScaleLargeTextures: true);

                DynamicAtlasConfig config = manager.Configuration;
                Assert.That(config.Validate(out string error), Is.True, error);
                Assert.That(
                    config.oversizePolicy,
                    Is.EqualTo(
                        config.copyFallback == DynamicAtlasCopyFallback.AllowSynchronousReadback
                            ? DynamicAtlasOversizePolicy.ScaleDown
                            : DynamicAtlasOversizePolicy.Reject));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ManagerConfigure_RejectsInvalidConfigurationBeforeInitialization()
        {
            var gameObject = new GameObject("DynamicAtlasManagerTest");
            gameObject.SetActive(false);
            try
            {
                DynamicAtlasManager manager = gameObject.AddComponent<DynamicAtlasManager>();
                DynamicAtlasConfig config = CreateSmallConfig();
                config.maxPages = 0;

                Assert.Throws<ArgumentException>(() => manager.Configure(config));
                Assert.That(manager.IsInitialized, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ManagerServiceRead_DoesNotCreateHiddenOwnership()
        {
            var gameObject = new GameObject("DynamicAtlasManagerTest");
            gameObject.SetActive(false);
            try
            {
                DynamicAtlasManager manager = gameObject.AddComponent<DynamicAtlasManager>();

                Assert.That(manager.Service, Is.Null);
                Assert.That(manager.IsInitialized, Is.False);
                Assert.Throws<InvalidOperationException>(() => manager.GetStats());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Validate_RejectsUnboundedAndContradictoryConfiguration()
        {
            DynamicAtlasConfig config = CreateSmallConfig();
            config.maxPages = 0;
            Assert.That(config.Validate(out string pageError), Is.False);
            Assert.That(pageError, Does.Contain("Max pages"));

            config = CreateSmallConfig();
            config.enableBleed = true;
            config.padding = 0;
            Assert.That(config.Validate(out string bleedError), Is.False);
            Assert.That(bleedError, Does.Contain("bleed").IgnoreCase);

            config = CreateSmallConfig();
            config.oversizePolicy = DynamicAtlasOversizePolicy.ScaleDown;
            config.copyFallback = DynamicAtlasCopyFallback.GpuOnly;
            Assert.That(config.Validate(out string scaleError), Is.False);
            Assert.That(scaleError, Does.Contain("ScaleDown"));
        }

        [Test]
        public void Validate_RequiresCustomLoaderOwnershipPair()
        {
            DynamicAtlasConfig config = CreateSmallConfig();
            config.loadFunc = _ => null;

            Assert.That(config.Validate(out string error), Is.False);
            Assert.That(error, Does.Contain("ownership pair"));
        }

        [Test]
        public void Validate_RejectsCorruptedEnumValues()
        {
            DynamicAtlasConfig config = CreateSmallConfig();
            config.filterMode = (FilterMode)999;
            Assert.That(config.Validate(out _), Is.False);

            config = CreateSmallConfig();
            config.retentionPolicy = (DynamicAtlasRetentionPolicy)999;
            Assert.That(config.Validate(out _), Is.False);

            config = CreateSmallConfig();
            config.oversizePolicy = (DynamicAtlasOversizePolicy)999;
            Assert.That(config.Validate(out _), Is.False);

            config = CreateSmallConfig();
            config.copyFallback = (DynamicAtlasCopyFallback)999;
            Assert.That(config.Validate(out _), Is.False);
        }

        [Test]
        public void EstimatePageBytes_IncludesCpuBackingOnlyWhenEnabled()
        {
            long gpuOnly = TextureFormatHelper.EstimatePageBytes(
                1024,
                DynamicAtlasCopyFallback.GpuOnly);
            long cpuBacked = TextureFormatHelper.EstimatePageBytes(
                1024,
                DynamicAtlasCopyFallback.AllowSynchronousReadback);
            long rawCpuBacked = TextureFormatHelper.EstimatePageBytes(
                1024,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);

            Assert.That(gpuOnly, Is.EqualTo(4L * 1024L * 1024L));
            Assert.That(cpuBacked, Is.EqualTo(gpuOnly * 2L));
            Assert.That(rawCpuBacked, Is.EqualTo(gpuOnly * 2L));
        }

        [Test]
        public void EstimatePageBytes_InvalidOrOverflowingInputsSaturate()
        {
            Assert.That(
                TextureFormatHelper.EstimatePageBytes(int.MaxValue, DynamicAtlasCopyFallback.GpuOnly),
                Is.EqualTo(long.MaxValue));
            Assert.That(
                TextureFormatHelper.EstimatePageBytes(1024, (DynamicAtlasCopyFallback)999),
                Is.EqualTo(long.MaxValue));
        }

        internal static DynamicAtlasConfig CreateSmallConfig()
        {
            return new DynamicAtlasConfig
            {
                pageSize = 64,
                maxPages = 2,
                minRetainedPages = 0,
                maxEntries = 16,
                maxEntriesPerPage = 8,
                maxKeyLength = 64,
                memoryBudgetBytes = 1024L * 1024L,
                padding = 1,
                enableBleed = false,
                retentionPolicy = DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = DynamicAtlasCopyFallback.AllowSynchronousReadback,
            };
        }
    }
}
