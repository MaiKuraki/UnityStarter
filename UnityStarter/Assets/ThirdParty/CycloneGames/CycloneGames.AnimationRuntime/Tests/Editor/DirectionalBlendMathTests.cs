using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;

namespace CycloneGames.AnimationRuntime.Tests
{
    public sealed class DirectionalBlendMathTests : LoggedTestBase
    {
        private static readonly float[] Strafe = { 0f, 90f, 180f, 270f };

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
        public void ExactDirection_IsPure()
        {
            var weights = new float[4];

            int count = DirectionalBlendMath.Evaluate(Strafe, 0, 90f, 1f, 0.1f, weights);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1f, weights[1], 1e-6f);
        }

        [Test]
        public void BetweenTwoChildren_SplitsEvenly()
        {
            var weights = new float[4];

            DirectionalBlendMath.Evaluate(Strafe, 0, 45f, 1f, 0.1f, weights);

            Assert.AreEqual(0.5f, weights[0], 1e-6f);
            Assert.AreEqual(0.5f, weights[1], 1e-6f);
        }

        [Test]
        public void WrapAround_BlendsLastAndFirst()
        {
            var weights = new float[4];

            DirectionalBlendMath.Evaluate(Strafe, 0, 350f, 1f, 0.1f, weights);

            Assert.AreEqual(0.111f, weights[3], 1e-3f);
            Assert.AreEqual(0.888f, weights[0], 1e-3f);
            Assert.AreEqual(1f, Sum(weights), 1e-5f);
        }

        [Test]
        public void TenDegrees_BlendsTheFirstPair()
        {
            var weights = new float[4];

            // 10 degrees sits inside [0, 90], so it blends child 0 and child 1. It is not a wrap case.
            DirectionalBlendMath.Evaluate(Strafe, 0, 10f, 1f, 0.1f, weights);

            Assert.AreEqual(0.889f, weights[0], 1e-3f);
            Assert.AreEqual(0.111f, weights[1], 1e-3f);
        }

        [Test]
        public void BelowFirstAngle_WrapsToTheLastChild()
        {
            var sparse = new[] { 20f, 110f, 200f, 290f };
            var weights = new float[4];

            // 10 degrees is below the first child at 20, so the bracketing pair straddles the seam: (290, 20).
            DirectionalBlendMath.Evaluate(sparse, 0, 10f, 1f, 0.1f, weights);

            Assert.AreEqual(0.111f, weights[3], 1e-3f);
            Assert.AreEqual(0.889f, weights[0], 1e-3f);
        }

        [Test]
        public void DeadZone_PrefersCenterChild()
        {
            var weights = new float[4];

            int count = DirectionalBlendMath.Evaluate(Strafe, 2, 123f, 0.05f, 0.1f, weights);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1f, weights[2], 1e-6f);
        }

        [Test]
        public void NoCenter_StillBlendsByDirection()
        {
            var weights = new float[4];

            DirectionalBlendMath.Evaluate(Strafe, -1, 45f, 0f, 0.1f, weights);

            Assert.AreEqual(0.5f, weights[0], 1e-6f);
            Assert.AreEqual(0.5f, weights[1], 1e-6f);
        }

        [Test]
        public void AllDirections_SumToOne()
        {
            var weights = new float[4];
            for (float angle = 0f; angle < 360f; angle += 7.5f)
            {
                DirectionalBlendMath.Evaluate(Strafe, 0, angle, 1f, 0.1f, weights);
                Assert.AreEqual(1f, Sum(weights), 1e-5f, "angle " + angle);
            }
        }

        [Test]
        public void DeltaAngle_TakesShortestPath()
        {
            Assert.AreEqual(20f, DirectionalBlendMath.DeltaAngle(350f, 10f), 1e-4f);
            Assert.AreEqual(-20f, DirectionalBlendMath.DeltaAngle(10f, 350f), 1e-4f);
            Assert.AreEqual(90f, DirectionalBlendMath.DeltaAngle(0f, 90f), 1e-4f);
        }

        [Test]
        public void NormalizeAngle_WrapsIntoRange()
        {
            Assert.AreEqual(350f, DirectionalBlendMath.NormalizeAngle(-10f), 1e-4f);
            Assert.AreEqual(10f, DirectionalBlendMath.NormalizeAngle(370f), 1e-4f);
            Assert.AreEqual(0f, DirectionalBlendMath.NormalizeAngle(float.NaN), 1e-4f);
        }
    }
}
