using CycloneGames.DeterministicMath;

using NUnit.Framework;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    /// <summary>
    /// Verifies that the fixed-point arithmetic used by lockstep consumers produces bit-identical results when
    /// executed inside a Burst-compiled job. With Burst enabled this runs the compiled path; without Burst it runs
    /// the managed fallback, so equality is still asserted either way and the Burst domain is covered on
    /// Burst-enabled CI/Player configurations. FPMath trigonometric helpers are intentionally excluded until their
    /// static CORDIC table initialization is audited for Burst compatibility.
    /// </summary>
    public sealed class BurstDomainConsistencyTests
    {
        [BurstCompile]
        private struct FPMathOracleJob : IJob
        {
            public NativeArray<long> Output;
            public long RawA;
            public long RawB;

            public void Execute()
            {
                FPInt64 a = FPInt64.FromRaw(RawA);
                FPInt64 b = FPInt64.FromRaw(RawB);
                Output[0] = (a + b).RawValue;
                Output[1] = (a - b).RawValue;
                FPInt64.TryMultiply(a, b, out FPInt64 product);
                Output[2] = product.RawValue;
                Output[3] = (a / b).RawValue;
                Output[4] = (a % b).RawValue;
                Output[5] = a < b ? 1L : 0L;
            }
        }

        [Test]
        public void BurstJob_GoldenVector_LocksArithmeticIdentity()
        {
            // 0.5 and 0.75 in Q32.32; expected values below are exactly representable.
            const long rawA = 2147483648L;
            const long rawB = 3221225472L;

            using var output = new NativeArray<long>(6, Allocator.TempJob);
            new FPMathOracleJob { Output = output, RawA = rawA, RawB = rawB }.Run();

            Assert.That(output[0], Is.EqualTo(5368709120L));   // 0.5 + 0.75 = 1.25
            Assert.That(output[1], Is.EqualTo(-1073741824L)); // 0.5 - 0.75 = -0.25
            Assert.That(output[2], Is.EqualTo(1610612736L));  // 0.5 * 0.75 = 0.375
            Assert.That(output[5], Is.EqualTo(1L));           // 0.5 < 0.75
        }

        [Test]
        public void BurstJob_Arithmetic_MatchesManagedBitForBit_Positive()
        {
            RunOracleComparison(2147483648L, 3221225472L);   // 0.5, 0.75
        }

        [Test]
        public void BurstJob_Arithmetic_MatchesManagedBitForBit_Negative()
        {
            RunOracleComparison(-2147483648L, 1073741824L);  // -0.5, 0.25
        }

        private static void RunOracleComparison(long rawA, long rawB)
        {
            using var output = new NativeArray<long>(6, Allocator.TempJob);
            new FPMathOracleJob { Output = output, RawA = rawA, RawB = rawB }.Run();

            FPInt64 a = FPInt64.FromRaw(rawA);
            FPInt64 b = FPInt64.FromRaw(rawB);
            FPInt64.TryMultiply(a, b, out FPInt64 product);

            Assert.That(output[0], Is.EqualTo((a + b).RawValue));
            Assert.That(output[1], Is.EqualTo((a - b).RawValue));
            Assert.That(output[2], Is.EqualTo(product.RawValue));
            Assert.That(output[3], Is.EqualTo((a / b).RawValue));
            Assert.That(output[4], Is.EqualTo((a % b).RawValue));
            Assert.That(output[5], Is.EqualTo(a < b ? 1L : 0L));
        }
    }
}
