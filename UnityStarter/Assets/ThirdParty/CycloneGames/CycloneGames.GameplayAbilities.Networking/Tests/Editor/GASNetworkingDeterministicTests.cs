using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GASNetworkingDeterministicTests
    {
        [Test]
        public void FixedConversion_UsesStableRawValue()
        {
            long rawA = GASNetFixed.FromFloat(12.5f);
            long rawB = GASNetFixed.FromFloat(12.5f);

            Assert.That(rawA, Is.EqualTo(rawB));
            Assert.That(GASNetFixed.ToFloat(rawA), Is.EqualTo(12.5f));
        }

        [Test]
        public void Checksum_UsesRawValues_NotFloatBits()
        {
            var attributes = new[]
            {
                new AttributeEntry
                {
                    AttributeId = 7,
                    BaseValueRaw = GASNetFixed.FromFloat(100f),
                    CurrentValueRaw = GASNetFixed.FromFloat(75.25f)
                }
            };

            uint checksumA = GASNetworkStateChecksum.Compute(
                null,
                0,
                null,
                0,
                attributes,
                attributes.Length,
                null,
                0);

            uint checksumB = GASNetworkStateChecksum.Compute(
                null,
                0,
                null,
                0,
                attributes,
                attributes.Length,
                null,
                0);

            Assert.That(checksumA, Is.Not.EqualTo(0u));
            Assert.That(checksumA, Is.EqualTo(checksumB));
        }

        [Test]
        public void Checksum_SortsTagHashes_ForContainerOrderIndependence()
        {
            int[] tagsA = { 30, 10, 20 };
            int[] tagsB = { 10, 20, 30 };

            uint checksumA = GASNetworkStateChecksum.Compute(null, 0, null, 0, null, 0, tagsA, tagsA.Length);
            uint checksumB = GASNetworkStateChecksum.Compute(null, 0, null, 0, null, 0, tagsB, tagsB.Length);

            Assert.That(checksumA, Is.EqualTo(checksumB));
        }
    }
}
