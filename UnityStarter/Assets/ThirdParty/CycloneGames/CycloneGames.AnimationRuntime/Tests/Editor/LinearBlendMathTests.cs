using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;

namespace CycloneGames.AnimationRuntime.Tests
{
    public sealed class LinearBlendMathTests : LoggedTestBase
    {
        private static readonly float[] Gait = { 0f, 2f, 5f, 8f };

        private static float Sum(float[] weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                total += weights[i];
            }

            return total;
        }

        [Test]
        public void SingleChild_AlwaysFullWeight()
        {
            var thresholds = new[] { 3f };
            var weights = new float[1];

            int count = LinearBlendMath.Evaluate(thresholds, 0f, weights);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1f, weights[0], 1e-6f);
        }

        [Test]
        public void BelowFirstThreshold_ClampsToFirst()
        {
            var weights = new float[4];

            LinearBlendMath.Evaluate(Gait, -10f, weights);

            Assert.AreEqual(1f, weights[0], 1e-6f);
            Assert.AreEqual(0f, weights[1], 1e-6f);
        }

        [Test]
        public void AboveLastThreshold_ClampsToLast()
        {
            var weights = new float[4];

            LinearBlendMath.Evaluate(Gait, 99f, weights);

            Assert.AreEqual(1f, weights[3], 1e-6f);
            Assert.AreEqual(1f, Sum(weights), 1e-6f);
        }

        [Test]
        public void ExactThreshold_IsPure()
        {
            var weights = new float[4];

            LinearBlendMath.Evaluate(Gait, 5f, weights);

            Assert.AreEqual(1f, weights[2], 1e-6f);
            Assert.AreEqual(0f, weights[1], 1e-6f);
            Assert.AreEqual(0f, weights[3], 1e-6f);
        }

        [Test]
        public void Midpoint_SplitsEvenly()
        {
            var weights = new float[4];

            LinearBlendMath.Evaluate(Gait, 1f, weights);

            Assert.AreEqual(0.5f, weights[0], 1e-6f);
            Assert.AreEqual(0.5f, weights[1], 1e-6f);
        }

        [Test]
        public void WeightsAlwaysSumToOne()
        {
            var weights = new float[4];
            for (float parameter = -1f; parameter <= 9f; parameter += 0.25f)
            {
                LinearBlendMath.Evaluate(Gait, parameter, weights);
                Assert.AreEqual(1f, Sum(weights), 1e-5f, "parameter " + parameter);
            }
        }

        [Test]
        public void FindSegment_ReturnsLowerBound()
        {
            Assert.AreEqual(0, LinearBlendMath.FindSegment(Gait, 1.9f));
            Assert.AreEqual(1, LinearBlendMath.FindSegment(Gait, 2f));
            Assert.AreEqual(2, LinearBlendMath.FindSegment(Gait, 7.9f));
        }

        [Test]
        public void EmptyOrTightBuffer_ReturnsZero()
        {
            var weights = new float[2];

            Assert.AreEqual(0, LinearBlendMath.Evaluate(System.Array.Empty<float>(), 0f, weights));
            Assert.AreEqual(0, LinearBlendMath.Evaluate(Gait, 0f, new float[2]));
        }
    }
}
