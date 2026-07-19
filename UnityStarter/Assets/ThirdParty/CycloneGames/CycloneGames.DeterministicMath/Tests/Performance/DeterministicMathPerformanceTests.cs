using System;

using NUnit.Framework;

using Unity.PerformanceTesting;

namespace CycloneGames.DeterministicMath.Tests.Performance
{
    public sealed class DeterministicMathPerformanceTests
    {
        private const int OPERAND_COUNT = 10_240;
        private const int WARMUP_COUNT = 5;
        private const int MEASUREMENT_COUNT = 20;
        private const int ITERATIONS_PER_MEASUREMENT = 1;
        private const int ALLOCATION_ITERATIONS = 3;

        private static readonly FPInt64[] LeftOperands = CreateOperands(0x9E3779B97F4A7C15UL, false);
        private static readonly FPInt64[] RightOperands = CreateOperands(0xD1B54A32D192ED03UL, true);

        private static long ResultSink;

        [Test, Performance]
        public void Multiply_VaryingQ32Operands()
        {
            Measure.Method(MultiplyBatch)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void Divide_VaryingNonPowerOfTwoOperands()
        {
            Measure.Method(DivideBatch)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void Sqrt_VaryingPositiveOperands()
        {
            Measure.Method(SqrtBatch)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void SinCos_VaryingAngles()
        {
            Measure.Method(SinCosBatch)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void Normalize_VaryingVectors()
        {
            Measure.Method(NormalizeBatch)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test]
        public void CoreArithmetic_SteadyStateAllocatesZeroBytes()
        {
            CoreHotPathBatch();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < ALLOCATION_ITERATIONS; i++)
            {
                CoreHotPathBatch();
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocatedBytes, Is.EqualTo(0));
        }

        private static void CoreHotPathBatch()
        {
            MultiplyBatch();
            DivideBatch();
            SqrtBatch();
            SinCosBatch();
            NormalizeBatch();
        }

        private static void MultiplyBatch()
        {
            long sink = ResultSink;
            for (int i = 0; i < OPERAND_COUNT; i++)
            {
                sink ^= (LeftOperands[i] * RightOperands[i]).RawValue;
            }

            ResultSink = sink;
        }

        private static void DivideBatch()
        {
            long sink = ResultSink;
            for (int i = 0; i < OPERAND_COUNT; i++)
            {
                sink ^= (LeftOperands[i] / RightOperands[i]).RawValue;
            }

            ResultSink = sink;
        }

        private static void SqrtBatch()
        {
            long sink = ResultSink;
            for (int i = 0; i < OPERAND_COUNT; i++)
            {
                FPInt64 value = FPInt64.FromRaw(LeftOperands[i].RawValue & long.MaxValue);
                sink ^= FPInt64.Sqrt(value).RawValue;
            }

            ResultSink = sink;
        }

        private static void SinCosBatch()
        {
            long sink = ResultSink;
            for (int i = 0; i < OPERAND_COUNT; i++)
            {
                FPMath.SinCos(LeftOperands[i], out FPInt64 sin, out FPInt64 cos);
                sink ^= sin.RawValue ^ cos.RawValue;
            }

            ResultSink = sink;
        }

        private static void NormalizeBatch()
        {
            long sink = ResultSink;
            for (int i = 0; i < OPERAND_COUNT - 2; i++)
            {
                FPVector3 value = new FPVector3(LeftOperands[i], LeftOperands[i + 1], LeftOperands[i + 2]);
                FPVector3 normalized = value.Normalized;
                sink ^= normalized.X.RawValue ^ normalized.Y.RawValue ^ normalized.Z.RawValue;
            }

            ResultSink = sink;
        }

        private static FPInt64[] CreateOperands(ulong seed, bool positiveNonPowerOfTwo)
        {
            var values = new FPInt64[OPERAND_COUNT];
            ulong state = seed;

            for (int i = 0; i < values.Length; i++)
            {
                state = unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);
                long whole = positiveNonPowerOfTwo
                    ? 3L + (long)(state % 61UL)
                    : (long)(state % 4095UL) - 2047L;
                uint fractional = (uint)(state >> 32);
                long raw = unchecked((whole << FPInt64.FractionalBits) + fractional);
                values[i] = FPInt64.FromRaw(raw);
            }

            return values;
        }
    }
}
