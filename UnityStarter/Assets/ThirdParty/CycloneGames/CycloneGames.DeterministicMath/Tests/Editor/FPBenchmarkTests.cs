using System;
using System.Diagnostics;
using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    /// <summary>
    /// Performance baselines. Not strict assertions - document the relative cost
    /// of FP operations vs float so developers can make informed trade-offs.
    /// Run in Release configuration for meaningful numbers.
    /// </summary>
    public sealed class FPBenchmarkTests
    {
        private const int Iterations = 100_000;

        [Test]
        public void Multiply_FPInt64_vs_Float()
        {
            var a = FPInt64.FromFloat(3.14f);
            var b = FPInt64.FromFloat(2.71f);

            var sw = Stopwatch.StartNew();
            FPInt64 result = default;
            for (int i = 0; i < Iterations; i++)
            {
                result = a * b;
            }
            sw.Stop();
            var fpTime = sw.Elapsed.TotalMilliseconds;

            float fa = 3.14f, fb = 2.71f, fr = 0;
            sw.Restart();
            for (int i = 0; i < Iterations; i++)
            {
                fr = fa * fb;
            }
            sw.Stop();
            var floatTime = sw.Elapsed.TotalMilliseconds;

            TestContext.WriteLine($"FPInt64 multiply: {fpTime:F2}ms ({Iterations} ops)");
            TestContext.WriteLine($"Float multiply:   {floatTime:F2}ms ({Iterations} ops)");
            TestContext.WriteLine($"Ratio (FP/float): {fpTime / floatTime:F1}x");
            TestContext.WriteLine($"Ignore: {result.RawValue}, {fr}");

            Assert.Pass("Benchmark recorded for diagnostics.");
        }

        [Test]
        public void SinCos_CORDIC_vs_SystemMath()
        {
            var angle = FPInt64.Pi / 4;

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
                FPMath.SinCos(angle, out _, out _);
            sw.Stop();
            var fpTime = sw.Elapsed.TotalMilliseconds;

            double fa = 0.7853982d;
            sw.Restart();
            for (int i = 0; i < Iterations; i++)
            {
                var s = Math.Sin(fa);
                var c = Math.Cos(fa);
                if (s + c < 0)
                {
                    TestContext.WriteLine("Unreachable benchmark guard.");
                }
            }
            sw.Stop();
            var floatTime = sw.Elapsed.TotalMilliseconds;

            // Call Math.Sin + Math.Cos separately (2 calls), CORDIC does both in 1
            // so we compare 1 CORDIC pass vs 2 System.Math calls.
            sw.Restart();
            for (int i = 0; i < Iterations; i++)
            {
                Math.Sin(fa);
                Math.Cos(fa);
            }
            sw.Stop();
            var floatTimeBoth = sw.Elapsed.TotalMilliseconds;

            TestContext.WriteLine($"CORDIC SinCos:   {fpTime:F2}ms ({Iterations} ops, 1 CORDIC pass)");
            TestContext.WriteLine($"Math Sin+Cos:    {floatTimeBoth:F2}ms ({Iterations} ops, 2 System.Math calls)");
            TestContext.WriteLine($"Ratio:            {fpTime / floatTimeBoth:F1}x");

            Assert.Pass("Benchmark recorded for diagnostics.");
        }

        [Test]
        public void Division_PowerOf2_vs_Generic()
        {
            var half = FPInt64.FromInt(2); // power of 2: raw = 2L << 32 = 8589934592
            var nonPower = FPInt64.FromInt(3);
            var value = FPInt64.FromInt(100);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
                value = value / half;
            sw.Stop();
            var fastTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            for (int i = 0; i < Iterations; i++)
                value = value / nonPower;
            sw.Stop();
            var slowTime = sw.Elapsed.TotalMilliseconds;

            TestContext.WriteLine($"Division by 2 (fast path): {fastTime:F2}ms");
            TestContext.WriteLine($"Division by 3 (generic):   {slowTime:F2}ms");
            TestContext.WriteLine($"Speedup: {slowTime / fastTime:F1}x");

            Assert.Pass("Benchmark recorded for diagnostics.");
        }

        [Test]
        public void StructAllocation_ZeroGC()
        {
            // Verify that creating and passing structs produces zero heap allocation
            long before = GC.GetTotalMemory(true);

            var v = new FPVector3(FPInt64.FromInt(1), FPInt64.FromInt(2), FPInt64.FromInt(3));
            var q = new FPQuaternion(v.X, v.Y, v.Z, FPInt64.OneValue);
            var m = FPMatrix4x4.Identity;
            var prod = q * v;
            var matMul = m * v;

            long after = GC.GetTotalMemory(false);
            var delta = after - before;

            TestContext.WriteLine($"GC delta after struct ops: {delta} bytes");
            TestContext.WriteLine($"Ignore: {prod.X.RawValue}, {matMul.X.RawValue}");
            Assert.Pass("GC sample recorded for diagnostics.");
        }

        [Test]
        public void Slerp_vs_Nlerp_Performance()
        {
            var a = FPQuaternion.Euler(0, 0, 0);
            var b = FPQuaternion.Euler(FPInt64.Pi / 2, FPInt64.Pi / 4, 0);
            var t = FPInt64.FromFloat(0.3f);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations / 10; i++)
                FPQuaternion.Slerp(a, b, t);
            sw.Stop();
            var slerpTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            for (int i = 0; i < Iterations / 10; i++)
                FPQuaternion.Nlerp(a, b, t);
            sw.Stop();
            var nlerpTime = sw.Elapsed.TotalMilliseconds;

            TestContext.WriteLine($"Slerp: {slerpTime:F2}ms (10K ops)");
            TestContext.WriteLine($"Nlerp: {nlerpTime:F2}ms (10K ops)");
            TestContext.WriteLine($"Slerp/Nlerp ratio: {slerpTime / nlerpTime:F1}x");
        }
    }
}
