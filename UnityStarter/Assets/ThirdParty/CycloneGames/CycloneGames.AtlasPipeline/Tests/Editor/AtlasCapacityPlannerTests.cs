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
    }
}
