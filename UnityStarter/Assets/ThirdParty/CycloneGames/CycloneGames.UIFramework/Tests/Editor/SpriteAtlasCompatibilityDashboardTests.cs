using System.Collections.Generic;
using CycloneGames.UIFramework.Editor.DynamicAtlas;
using NUnit.Framework;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class SpriteAtlasCompatibilityDashboardTests
    {
        [SetUp]
        public void SetUp()
        {
            SpriteAtlasCompatibilityCache.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            SpriteAtlasCompatibilityCache.ResetForTests();
        }

        [Test]
        public void RequestScan_FirstRequestSchedules_SubsequentRequestsMergeIntoOnePendingScan()
        {
            Assert.That(SpriteAtlasCompatibilityCache.RequestScan(10.0d), Is.True);
            Assert.That(SpriteAtlasCompatibilityCache.RequestScan(10.1d), Is.False);
            Assert.That(SpriteAtlasCompatibilityCache.RequestScan(10.2d), Is.False);
            Assert.That(SpriteAtlasCompatibilityCache.HasPendingScan, Is.True);
        }

        [Test]
        public void TryConsumePendingScan_RunsOnceOnlyAfterTheThrottleDelayElapses()
        {
            SpriteAtlasCompatibilityCache.RequestScan(10.0d);

            Assert.That(SpriteAtlasCompatibilityCache.TryConsumePendingScan(10.2d), Is.False);
            Assert.That(SpriteAtlasCompatibilityCache.HasPendingScan, Is.True);
            Assert.That(SpriteAtlasCompatibilityCache.TryConsumePendingScan(10.6d), Is.True);
            Assert.That(SpriteAtlasCompatibilityCache.HasPendingScan, Is.False);
            Assert.That(SpriteAtlasCompatibilityCache.TryConsumePendingScan(10.7d), Is.False);
        }

        [Test]
        public void AutoScanDisabled_DropsPendingScanAndRejectsNewRequests()
        {
            SpriteAtlasCompatibilityCache.RequestScan(10.0d);

            SpriteAtlasCompatibilityCache.AutoScanEnabled = false;

            Assert.That(SpriteAtlasCompatibilityCache.TryConsumePendingScan(11.0d), Is.False);
            Assert.That(SpriteAtlasCompatibilityCache.HasPendingScan, Is.False);
            Assert.That(SpriteAtlasCompatibilityCache.RequestScan(12.0d), Is.False);
            Assert.That(SpriteAtlasCompatibilityCache.HasPendingScan, Is.False);
        }

        [Test]
        public void ReplaceAll_PrunesStaleEntries_AndLookupKeysAreOrdinal()
        {
            var results = new List<SpriteAtlasCompatibilityCache.Entry>
            {
                CreateEntry("Assets/UI/Common.spriteatlas"),
                CreateEntry("Assets/UI/Hud.spriteatlas")
            };
            SpriteAtlasCompatibilityCache.ReplaceAll(results);

            Assert.That(SpriteAtlasCompatibilityCache.Count, Is.EqualTo(2));
            Assert.That(
                SpriteAtlasCompatibilityCache.TryGet("Assets/UI/Common.spriteatlas", out _),
                Is.True);

            results.RemoveAt(0);
            SpriteAtlasCompatibilityCache.ReplaceAll(results);

            Assert.That(SpriteAtlasCompatibilityCache.Count, Is.EqualTo(1));
            Assert.That(
                SpriteAtlasCompatibilityCache.TryGet("Assets/UI/Common.spriteatlas", out _),
                Is.False);
            Assert.That(
                SpriteAtlasCompatibilityCache.TryGet("assets/ui/hud.spriteatlas", out _),
                Is.False,
                "Cache keys must use Ordinal comparison.");
        }

        [Test]
        public void CopyEntries_SortsUnsupportedFirstThenPathOrdinal()
        {
            var results = new List<SpriteAtlasCompatibilityCache.Entry>
            {
                CreateEntry("Assets/UI/ZFast.spriteatlas"),
                CreateEntry("Assets/UI/AUnsupported.spriteatlas", rotationEnabled: true),
                CreateEntry("Assets/UI/BReadback.spriteatlas", explicitRgba32: false, readable: true)
            };
            SpriteAtlasCompatibilityCache.ReplaceAll(results);

            var sorted = new List<SpriteAtlasCompatibilityCache.Entry>(4);
            SpriteAtlasCompatibilityCache.CopyEntries(sorted);

            Assert.That(sorted.Count, Is.EqualTo(3));
            Assert.That(
                sorted[0].Compatibility,
                Is.EqualTo(SpriteAtlasFormatValidator.Compatibility.UnsupportedPacking));
            Assert.That(
                sorted[1].Compatibility,
                Is.EqualTo(SpriteAtlasFormatValidator.Compatibility.ReadbackRequired));
            Assert.That(
                sorted[2].Compatibility,
                Is.EqualTo(SpriteAtlasFormatValidator.Compatibility.FastPathCandidate));
        }

        [Test]
        public void CopyVisible_TruncatesAtTheCapAndReportsTheOverflow()
        {
            var sorted = new List<SpriteAtlasCompatibilityCache.Entry>(8);
            for (int i = 0; i < 5; i++)
            {
                sorted.Add(CreateEntry($"Assets/UI/Atlas{i}.spriteatlas"));
            }

            var visible = new List<SpriteAtlasCompatibilityCache.Entry>(8);
            int overflow = SpriteAtlasCompatibilityCache.CopyVisible(sorted, visible, 3);

            Assert.That(visible.Count, Is.EqualTo(3));
            Assert.That(overflow, Is.EqualTo(2));

            overflow = SpriteAtlasCompatibilityCache.CopyVisible(sorted, visible, 8);
            Assert.That(visible.Count, Is.EqualTo(5));
            Assert.That(overflow, Is.Zero);
        }

        [Test]
        public void BuildFailureReasons_ListsEachIssue_AndStaysEmptyForGpuCandidates()
        {
            SpriteAtlasCompatibilityCache.Entry packed = CreateEntry(
                "Assets/UI/Packed.spriteatlas",
                rotationEnabled: true,
                tightPackingEnabled: true);
            string packedReasons = SpriteAtlasCompatibilityCache.BuildFailureReasons(packed);
            Assert.That(packedReasons, Does.Contain("rotation enabled"));
            Assert.That(packedReasons, Does.Contain("tight packing"));

            SpriteAtlasCompatibilityCache.Entry readback = CreateEntry(
                "Assets/UI/Compressed.spriteatlas",
                explicitRgba32: false,
                readable: false);
            string readbackReasons = SpriteAtlasCompatibilityCache.BuildFailureReasons(readback);
            Assert.That(readbackReasons, Does.Contain("compressed format"));
            Assert.That(readbackReasons, Does.Contain("not readable"));

            SpriteAtlasCompatibilityCache.Entry fastPath = CreateEntry("Assets/UI/Fast.spriteatlas");
            Assert.That(
                SpriteAtlasCompatibilityCache.BuildFailureReasons(fastPath),
                Is.EqualTo(string.Empty));
        }

        private static SpriteAtlasCompatibilityCache.Entry CreateEntry(
            string path,
            bool explicitRgba32 = true,
            bool readable = false,
            bool rotationEnabled = false,
            bool tightPackingEnabled = false)
        {
            SpriteAtlasFormatValidator.Compatibility compatibility =
                SpriteAtlasFormatValidator.ClassifyCompatibility(
                    rotationEnabled,
                    tightPackingEnabled,
                    explicitRgba32,
                    readable);
            return new SpriteAtlasCompatibilityCache.Entry(
                path,
                path,
                explicitRgba32 ? "RGBA32" : "DXT5",
                explicitRgba32,
                readable,
                rotationEnabled,
                tightPackingEnabled,
                compatibility);
        }
    }
}
