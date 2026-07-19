using System;
using System.IO;

using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.IO.Tests.Performance
{
    public sealed class SystemIOPerformanceTests
    {
        private const int PAYLOAD_SIZE = 4 * 1024 * 1024;
        private const int WARMUP_COUNT = 3;
        private const int MEASUREMENT_COUNT = 10;

        private static bool ComparisonSink;
        private static byte HashSink;

        private string _directory;
        private string _filePath;
        private byte[] _first;
        private byte[] _second;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.IO.Performance",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _filePath = Path.Combine(_directory, "payload.bin");
            _first = CreatePayload(PAYLOAD_SIZE);
            _second = (byte[])_first.Clone();
            File.WriteAllBytes(_filePath, _first);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test, Performance]
        public void ExactByteComparison_FourMiB()
        {
            Measure.Method(() =>
                {
                    ComparisonSink ^= BinaryContentComparer.AreEqual(_first, _second);
                })
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(8)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void Sha256FileHash_FourMiB()
        {
            byte[] hash = new byte[ContentHasher.GetHashSize(FileHashAlgorithm.Sha256)];
            Measure.Method(() =>
                {
                    FileHasher.WriteHash(_filePath, FileHashAlgorithm.Sha256, hash);
                    HashSink ^= hash[0];
                })
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(2)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void XxHash64FileHash_FourMiB()
        {
            byte[] hash = new byte[ContentHasher.GetHashSize(FileHashAlgorithm.XxHash64)];
            Measure.Method(() =>
                {
                    FileHasher.WriteHash(_filePath, FileHashAlgorithm.XxHash64, hash);
                    HashSink ^= hash[0];
                })
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(4)
                .GC()
                .Run();
        }

        private static byte[] CreatePayload(int length)
        {
            var bytes = new byte[length];
            uint state = 0x9E3779B9U;
            for (int i = 0; i < bytes.Length; i++)
            {
                state = unchecked((state * 1664525U) + 1013904223U);
                bytes[i] = (byte)(state >> 24);
            }

            return bytes;
        }
    }
}
