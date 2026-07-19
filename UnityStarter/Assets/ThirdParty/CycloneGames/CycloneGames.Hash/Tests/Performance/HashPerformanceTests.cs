using System;

using CycloneGames.Hash.Core;

using NUnit.Framework;

using Unity.PerformanceTesting;

namespace CycloneGames.Hash.Tests.Performance
{
    public sealed class HashPerformanceTests
    {
        private const int LARGE_BUFFER_SIZE = 1024 * 1024;
        private const int STREAM_CHUNK_SIZE = 4 * 1024;
        private const int LARGE_ITERATIONS_PER_MEASUREMENT = 8;
        private const int SMALL_ITERATIONS_PER_MEASUREMENT = 1000;
        private const int WARMUP_COUNT = 5;
        private const int MEASUREMENT_COUNT = 15;

        private static readonly byte[] LargeBuffer = CreateData(LARGE_BUFFER_SIZE);
        private static readonly byte[] SmallBuffer = CreateData(64);

        private static ulong DigestSink;

        [Test, Performance]
        public void XxHash64_OneShot_OneMiB()
        {
            Measure.Method(HashLargeBufferWithXxHash64)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(LARGE_ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void XxHash64_Streaming_FourKiBChunks_OneMiB()
        {
            Measure.Method(HashLargeBufferWithStreamingXxHash64)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(LARGE_ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void XxHash64_OneShot_64Bytes()
        {
            Measure.Method(HashSmallBufferWithXxHash64)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(SMALL_ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void Fnv1a64_OneShot_OneMiB()
        {
            Measure.Method(HashLargeBufferWithFnv1a64)
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(LARGE_ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        private static void HashLargeBufferWithXxHash64()
        {
            DigestSink ^= XxHash64.Compute(LargeBuffer);
        }

        private static void HashLargeBufferWithStreamingXxHash64()
        {
            XxHash64 state = XxHash64.Create();
            for (int offset = 0; offset < LargeBuffer.Length; offset += STREAM_CHUNK_SIZE)
            {
                state.Append(LargeBuffer, offset, Math.Min(STREAM_CHUNK_SIZE, LargeBuffer.Length - offset));
            }

            DigestSink ^= state.GetDigest();
        }

        private static void HashSmallBufferWithXxHash64()
        {
            DigestSink ^= XxHash64.Compute(SmallBuffer);
        }

        private static void HashLargeBufferWithFnv1a64()
        {
            DigestSink ^= Fnv1a64.Compute(LargeBuffer);
        }

        private static byte[] CreateData(int length)
        {
            byte[] data = new byte[length];
            uint state = 0x9E3779B9U;
            for (int i = 0; i < data.Length; i++)
            {
                state = unchecked((state * 1664525U) + 1013904223U);
                data[i] = (byte)(state >> 24);
            }

            return data;
        }
    }
}
