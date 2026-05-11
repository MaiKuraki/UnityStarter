using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class DeterministicRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = DeterministicRandom.Create(12345UL);
            var b = DeterministicRandom.Create(12345UL);

            for (int i = 0; i < 16; i++)
            {
                Assert.That(a.NextULong(), Is.EqualTo(b.NextULong()));
            }
        }

        [Test]
        public void SaveRestore_ResumesSequence()
        {
            var random = DeterministicRandom.Create(99UL);
            random.NextULong();

            var state = random.SaveState();
            var first = random.NextULong();
            random.NextULong();
            random.RestoreState(state);

            Assert.That(random.NextULong(), Is.EqualTo(first));
        }

        [Test]
        public void NextInt_StaysInsideRange()
        {
            var random = DeterministicRandom.Create(7UL);

            for (int i = 0; i < 128; i++)
            {
                int value = random.NextInt(3, 11);
                Assert.That(value, Is.GreaterThanOrEqualTo(3));
                Assert.That(value, Is.LessThan(11));
            }
        }
    }
}
