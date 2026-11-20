using CycloneGames.DynamicAtlas.Editor;
using NUnit.Framework;

namespace CycloneGames.DynamicAtlas.Tests.Editor
{
    [TestFixture]
    public sealed class DynamicAtlasExtractionContractTests
    {
        [TestCase(false, false, true, false, "FastPathCandidate")]
        [TestCase(false, false, true, true, "FastPathWithCpuFallback")]
        [TestCase(false, false, false, false, "ReadbackRequired")]
        [TestCase(false, false, false, true, "ReadbackRequired")]
        [TestCase(true, false, true, false, "UnsupportedPacking")]
        [TestCase(true, false, false, true, "UnsupportedPacking")]
        [TestCase(false, true, true, false, "UnsupportedPacking")]
        [TestCase(false, true, false, true, "UnsupportedPacking")]
        [TestCase(true, true, true, true, "UnsupportedPacking")]
        [TestCase(true, true, false, false, "UnsupportedPacking")]
        public void ClassifyCompatibility_MatchesThreeFieldContract(
            bool rotation,
            bool tight,
            bool isExplicitRgba32,
            bool readable,
            string expected)
        {
            Assert.That(
                SpriteAtlasFormatValidator.ClassifyCompatibility(
                    rotation, tight, isExplicitRgba32, readable).ToString(),
                Is.EqualTo(expected));
        }

        [Test]
        public void ConsumerConsumableMatchesProducerEffectiveAcrossFullGrid()
        {
            foreach (bool rotation in new[] { false, true })
            {
                foreach (bool tight in new[] { false, true })
                {
                    foreach (bool rgba32 in new[] { false, true })
                    {
                        foreach (bool readable in new[] { false, true })
                        {
                            bool producerEffective = !rotation && !tight && rgba32;
                            SpriteAtlasFormatValidator.Compatibility classification =
                                SpriteAtlasFormatValidator.ClassifyCompatibility(
                                    rotation, tight, rgba32, readable);
                            bool consumable = classification ==
                                              SpriteAtlasFormatValidator.Compatibility.FastPathCandidate ||
                                              classification ==
                                              SpriteAtlasFormatValidator.Compatibility.FastPathWithCpuFallback;

                            string context = string.Format(
                                "contract drift: rotation={0} tight={1} rgba32={2} readable={3}",
                                rotation, tight, rgba32, readable);
                            Assert.That(consumable, Is.EqualTo(producerEffective), context);
                        }
                    }
                }
            }
        }
    }
}
