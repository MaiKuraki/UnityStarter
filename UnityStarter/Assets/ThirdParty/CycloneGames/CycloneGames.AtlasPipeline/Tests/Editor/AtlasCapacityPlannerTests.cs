using NUnit.Framework;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// A SpriteAtlas is packed into one texture of the configured max size, and Unity drops whatever
    /// does not fit without reporting an error. The sprites then render as white quads at runtime, far
    /// from the cause, so capacity has to be evaluated up front.
    /// </summary>
    [TestFixture]
    public sealed class AtlasCapacityPlannerTests
    {
        [Test]
        public void ComputePaddedArea_AddsPaddingOnBothSides()
        {
            // Padding separates neighbours inside the atlas, so each sprite effectively reserves
            // (w + 2 * padding) x (h + 2 * padding).
            Assert.AreEqual(64L, AtlasCapacityPlanner.ComputePaddedArea(8, 8, 0));
            Assert.AreEqual(144L, AtlasCapacityPlanner.ComputePaddedArea(8, 8, 2));
            Assert.AreEqual(400L, AtlasCapacityPlanner.ComputePaddedArea(10, 10, 5));
        }

        [Test]
        public void ComputePaddedArea_IsZeroForDegenerateInput()
        {
            Assert.AreEqual(0L, AtlasCapacityPlanner.ComputePaddedArea(0, 8, 2));
            Assert.AreEqual(0L, AtlasCapacityPlanner.ComputePaddedArea(8, 0, 2));
            Assert.AreEqual(0L, AtlasCapacityPlanner.ComputePaddedArea(-1, -1, 2));
        }

        [Test]
        public void IsSpriteTooLarge_AccountsForPadding()
        {
            Assert.IsFalse(AtlasCapacityPlanner.IsSpriteTooLarge(2040, 2040, 2048, 0));
            Assert.IsTrue(AtlasCapacityPlanner.IsSpriteTooLarge(2048, 2048, 2048, 4));
            Assert.IsTrue(AtlasCapacityPlanner.IsSpriteTooLarge(2041, 16, 2048, 4));
            Assert.IsFalse(AtlasCapacityPlanner.IsSpriteTooLarge(16, 16, 2048, 4));
        }

        [Test]
        public void Evaluate_ReportsOnePageWhenEverythingFits()
        {
            AtlasCapacityReport report = AtlasCapacityPlanner.Evaluate(
                new AtlasCapacityRequest(10, 1000L, 2048, 4));

            Assert.AreEqual(1, report.PageCount);
            Assert.IsFalse(report.RequiresSplitting);
            Assert.IsFalse(report.IsEmpty);
            Assert.Greater(report.UsableAreaPerPage, report.RequiredArea);
        }

        [Test]
        public void Evaluate_SplitsWhenTheContentOverflows()
        {
            long pageCapacity = (long)(2048 * 2048 * AtlasCapacityPlanner.DefaultPackingEfficiency);

            AtlasCapacityReport report = AtlasCapacityPlanner.Evaluate(
                new AtlasCapacityRequest(500, pageCapacity * 2L, 2048, 4));

            Assert.AreEqual(2, report.PageCount);
            Assert.IsTrue(report.RequiresSplitting);
        }

        [Test]
        public void Evaluate_IsEmptyForNoContent()
        {
            Assert.IsTrue(
                AtlasCapacityPlanner.Evaluate(new AtlasCapacityRequest(0, 0L, 2048, 4)).IsEmpty);
            Assert.AreEqual(
                0,
                AtlasCapacityPlanner.Evaluate(new AtlasCapacityRequest(0, 0L, 2048, 4)).PageCount);
        }

        [Test]
        public void Request_ClampsInvalidInput()
        {
            var request = new AtlasCapacityRequest(-5, -5L, -1, -1, -1f);

            Assert.AreEqual(0, request.SpriteCount);
            Assert.AreEqual(0L, request.RequiredArea);
            Assert.AreEqual(2048, request.MaxTextureSize, "falls back to a usable atlas size");
            Assert.AreEqual(0, request.Padding);
            Assert.AreEqual(AtlasCapacityPlanner.DefaultPackingEfficiency, request.PackingEfficiency);
        }

        [Test]
        public void Request_ClampsEfficiencyToOne()
        {
            Assert.AreEqual(1f, new AtlasCapacityRequest(1, 1L, 512, 0, 5f).PackingEfficiency);
        }

        /// <summary>
        /// Page assignment has to cover every member exactly once, with no gaps and no overlaps, or
        /// sprites would be silently dropped or duplicated when splitting is added.
        /// </summary>
        [Test]
        public void AssignPageRange_CoversEveryMemberExactlyOnce()
        {
            for (int pageCount = 1; pageCount <= 7; pageCount++)
            {
                for (int total = 0; total <= 30; total++)
                {
                    int covered = 0;
                    for (int page = 0; page < pageCount; page++)
                    {
                        AtlasCapacityPlanner.AssignPageRange(
                            total,
                            pageCount,
                            page,
                            out int start,
                            out int count);

                        Assert.AreEqual(covered, start, "pages must be contiguous");
                        Assert.GreaterOrEqual(count, 0);
                        covered += count;
                    }

                    Assert.AreEqual(total, covered, "total=" + total + " pages=" + pageCount);
                }
            }
        }

        [Test]
        public void AssignPageRange_SpreadsTheRemainderToLowPages()
        {
            AtlasCapacityPlanner.AssignPageRange(10, 3, 0, out int start0, out int count0);
            AtlasCapacityPlanner.AssignPageRange(10, 3, 1, out int start1, out int count1);
            AtlasCapacityPlanner.AssignPageRange(10, 3, 2, out int start2, out int count2);

            Assert.AreEqual(0, start0);
            Assert.AreEqual(4, count0);
            Assert.AreEqual(4, start1);
            Assert.AreEqual(3, count1);
            Assert.AreEqual(7, start2);
            Assert.AreEqual(3, count2);
        }

        [Test]
        public void AssignPageRange_RejectsOutOfRangePages()
        {
            AtlasCapacityPlanner.AssignPageRange(10, 3, 3, out int start, out int count);
            Assert.AreEqual(0, start);
            Assert.AreEqual(0, count);

            AtlasCapacityPlanner.AssignPageRange(10, 3, -1, out start, out count);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void EstimateBytes_IsZeroForEmptyInput()
        {
            Assert.AreEqual(0L, AtlasCapacityPlanner.EstimateBytes(0L, 0.5d));
            Assert.AreEqual(0L, AtlasCapacityPlanner.EstimateBytes(100L, 0d));
            Assert.AreEqual(50L, AtlasCapacityPlanner.EstimateBytes(100L, 0.5d));
        }

        // ----------------------------------------------------------------
        // Paging
        // ----------------------------------------------------------------

        /// <summary>
        /// Pages are shared across platforms — the same packable list must produce the same page files
        /// everywhere, or the output would not be reproducible — so the page count is the worst case
        /// over platforms.
        /// </summary>
        [Test]
        public void ComputePageCount_TakesTheWorstCaseAcrossPlatforms()
        {
            // Computed from the same truncated integers the planner uses, so the division is exact
            // instead of landing one pixel over a page boundary.
            long androidUsable = (long)(1024 * 1024 * AtlasCapacityPlanner.DefaultPackingEfficiency);
            long requiredArea = androidUsable * 3L + 1L;

            // Android at 1024px needs four pages; iOS at 2048px fits in one. The pages are shared,
            // so the answer is four for both.
            int pages = AtlasCapacityPlanner.ComputePageCount(
                500,
                requiredArea,
                new[] { 1024, 2048, 2048, 2048 },
                4,
                pagingEnabled: true);

            Assert.AreEqual(4, pages);
        }

        [Test]
        public void ComputePageCount_DisabledPagingAlwaysAnswersOne()
        {
            long pageCapacity = (long)(2048 * 2048 * AtlasCapacityPlanner.DefaultPackingEfficiency);
            Assert.AreEqual(
                1,
                AtlasCapacityPlanner.ComputePageCount(
                    500,
                    pageCapacity * 10L,
                    new[] { 2048 },
                    4,
                    pagingEnabled: false));
        }

        [Test]
        public void ComputePageCount_EmptyContentAnswersOne()
        {
            Assert.AreEqual(
                1,
                AtlasCapacityPlanner.ComputePageCount(0, 0L, new[] { 2048 }, 4, true));
            Assert.AreEqual(
                1,
                AtlasCapacityPlanner.ComputePageCount(10, 0L, new[] { 2048 }, 4, true));
            Assert.AreEqual(
                1,
                AtlasCapacityPlanner.ComputePageCount(10, 1000L, null, 4, true));
            Assert.AreEqual(
                1,
                AtlasCapacityPlanner.ComputePageCount(10, 1000L, new int[0], 4, true));
        }

        /// <summary>
        /// A single-page atlas keeps the plain key. This is what makes paging safe to enable on an
        /// existing project: every atlas that already fits keeps its exact current output file.
        /// </summary>
        [Test]
        public void BuildPageKey_SinglePageKeepsThePlainKey()
        {
            Assert.AreEqual("ui", AtlasCapacityPlanner.BuildPageKey("ui", 0, 1));
            Assert.IsNull(AtlasCapacityPlanner.BuildPageKey(null, 0, 1));
        }

        [Test]
        public void BuildPageKey_UsesFixedWidthZeroPadding()
        {
            Assert.AreEqual("ui__p000", AtlasCapacityPlanner.BuildPageKey("ui", 0, 3));
            Assert.AreEqual("ui__p001", AtlasCapacityPlanner.BuildPageKey("ui", 1, 3));
            Assert.AreEqual("ui__p002", AtlasCapacityPlanner.BuildPageKey("ui", 2, 3));
        }

        /// <summary>
        /// Beyond 999 pages the width widens instead of truncating, so a page can never receive
        /// another page's name.
        /// </summary>
        [Test]
        public void BuildPageKey_WidensBeyondThreeDigits()
        {
            Assert.AreEqual(
                "ui__p1000",
                AtlasCapacityPlanner.BuildPageKey("ui", 1000, 1001));
            Assert.AreEqual(
                4,
                AtlasCapacityPlanner.BuildPageKey("ui", 0, 1001).Length - "ui__p".Length);
        }

        [Test]
        public void BuildPageKey_IsDeterministic()
        {
            Assert.AreEqual(
                AtlasCapacityPlanner.BuildPageKey("ui", 7, 12),
                AtlasCapacityPlanner.BuildPageKey("ui", 7, 12));
        }

        [Test]
        public void StripPageSuffix_RemovesOnlyRealPageSuffixes()
        {
            Assert.AreEqual("ui", AtlasCapacityPlanner.StripPageSuffix("ui__p000"));
            Assert.AreEqual("ui", AtlasCapacityPlanner.StripPageSuffix("ui__p12"));
            Assert.AreEqual("ui__p", AtlasCapacityPlanner.StripPageSuffix("ui__p"));
            Assert.AreEqual("ui__px", AtlasCapacityPlanner.StripPageSuffix("ui__px"));
            Assert.AreEqual("ui__p0x", AtlasCapacityPlanner.StripPageSuffix("ui__p0x"));
            Assert.AreEqual("ui", AtlasCapacityPlanner.StripPageSuffix("ui"));
            Assert.IsNull(AtlasCapacityPlanner.StripPageSuffix(null));
        }

        /// <summary>
        /// A rule group can legitimately contain the "__p" spelling. Stripping must not turn such a
        /// key into a page of some other atlas.
        /// </summary>
        [Test]
        public void StripPageSuffix_KeepsTrailingMarkerWithoutDigits()
        {
            Assert.AreEqual("map__p", AtlasCapacityPlanner.StripPageSuffix("map__p"));
            Assert.AreEqual("map__player", AtlasCapacityPlanner.StripPageSuffix("map__player"));
        }

        [Test]
        public void IsPageOf_RecognizesPagesOfABaseKey()
        {
            Assert.IsTrue(AtlasCapacityPlanner.IsPageOf("ui__p000", "ui"));
            Assert.IsTrue(AtlasCapacityPlanner.IsPageOf("ui__p017", "ui"));
            Assert.IsFalse(AtlasCapacityPlanner.IsPageOf("ui", "ui"));
            Assert.IsFalse(AtlasCapacityPlanner.IsPageOf("other__p000", "ui"));
            Assert.IsFalse(AtlasCapacityPlanner.IsPageOf(null, "ui"));
            Assert.IsFalse(AtlasCapacityPlanner.IsPageOf("ui__p000", null));
        }

        /// <summary>
        /// Slicing and naming have to agree: the pages of an atlas must cover the member list exactly
        /// once, and the page keys must be derivable from the base key alone.
        /// </summary>
        [Test]
        public void PagePlan_CoversEveryMemberExactlyOnce()
        {
            const int memberCount = 257;
            for (int pageCount = 1; pageCount <= 5; pageCount++)
            {
                int covered = 0;
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    AtlasCapacityPlanner.AssignPageRange(
                        memberCount,
                        pageCount,
                        pageIndex,
                        out int start,
                        out int count);
                    Assert.AreEqual(covered, start);
                    covered += count;

                    string pageKey = AtlasCapacityPlanner.BuildPageKey(
                        "ui",
                        pageIndex,
                        pageCount);
                    Assert.IsTrue(
                        AtlasCapacityPlanner.IsPageOf(pageKey, "ui") || pageCount == 1,
                        "page key must strip back to the base key");
                }

                Assert.AreEqual(memberCount, covered, "pageCount=" + pageCount);
            }
        }
    }
}
