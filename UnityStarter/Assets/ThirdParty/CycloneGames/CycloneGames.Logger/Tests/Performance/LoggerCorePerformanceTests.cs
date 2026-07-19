using System;
using System.Text;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.Logger.Tests.Performance
{
    public sealed class LoggerCorePerformanceTests
    {
        private const int WarmupCount = 10;
        private const int MeasurementCount = 20;
        private const int IterationsPerMeasurement = 1000;
        private const int AllocationIterations = 10000;

        private static readonly Action<int, StringBuilder> AppendValueCallback = AppendValue;

        private CLogger _logger;
        private CountingSink _sink;

        [TearDown]
        public void TearDown()
        {
            _logger?.Dispose();
            _logger = null;
            _sink = null;
        }

        [Test, Performance]
        public void FilteredGenericBuilder_ProducerCost()
        {
            _logger = CLoggerFactory.CreateSingleThreaded(CreateOptions());
            _sink = new CountingSink();
            _logger.AddLogger(_sink);
            _logger.SetLogLevel(LogLevel.Error);

            Measure.Method(LogFiltered)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(IterationsPerMeasurement)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void AcceptedGenericBuilder_WithSynchronousDispatch()
        {
            _logger = CLoggerFactory.CreateSingleThreaded(CreateOptions());
            _sink = new CountingSink();
            _logger.AddLogger(_sink);

            Measure.Method(LogAndPump)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(IterationsPerMeasurement)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void AcceptedShortString_WithSynchronousDispatch()
        {
            _logger = CLoggerFactory.CreateSingleThreaded(CreateOptions());
            _sink = new CountingSink();
            _logger.AddLogger(_sink);

            Measure.Method(LogStringAndPump)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(IterationsPerMeasurement)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void DropOldestAtHead_OverloadProducerCost()
        {
            LoggerProcessingOptions options = CreateOptions();
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            options.ReservedCriticalMessages = 0;
            options.ReservedCriticalCharacters = 0;
            _logger = CLoggerFactory.CreateSingleThreaded(options);
            _sink = new CountingSink();
            _logger.AddLogger(_sink);
            for (int i = 0; i < options.MaxQueuedMessages; i++)
            {
                _logger.Log(LogLevel.Info, "queued", filePath: string.Empty, memberName: string.Empty);
            }

            Measure.Method(LogOverloadedDropOldest)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(IterationsPerMeasurement)
                .GC()
                .Run();
        }

        [Test]
        public void FilteredCachedBuilder_SteadyStateAllocatesZeroBytes()
        {
            _logger = CLoggerFactory.CreateSingleThreaded(CreateOptions());
            _sink = new CountingSink();
            _logger.AddLogger(_sink);
            _logger.SetLogLevel(LogLevel.Error);
            LogFiltered();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < AllocationIterations; i++)
            {
                LogFiltered();
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Test]
        public void AcceptedCachedBuilder_SteadyStateAllocatesZeroBytes()
        {
            _logger = CLoggerFactory.CreateSingleThreaded(CreateOptions());
            _sink = new CountingSink();
            _logger.AddLogger(_sink);
            for (int i = 0; i < 512; i++)
            {
                LogAndPump();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < AllocationIterations; i++)
            {
                LogAndPump();
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Test]
        public void AcceptedShortString_SteadyStateAllocatesZeroBytes()
        {
            _logger = CLoggerFactory.CreateSingleThreaded(CreateOptions());
            _sink = new CountingSink();
            _logger.AddLogger(_sink);
            for (int i = 0; i < 512; i++)
            {
                LogStringAndPump();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < AllocationIterations; i++)
            {
                LogStringAndPump();
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Test]
        public void DropOldestAtHead_SteadyStateAllocatesZeroBytes()
        {
            LoggerProcessingOptions options = CreateOptions();
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            options.ReservedCriticalMessages = 0;
            options.ReservedCriticalCharacters = 0;
            _logger = CLoggerFactory.CreateSingleThreaded(options);
            _sink = new CountingSink();
            _logger.AddLogger(_sink);
            for (int i = 0; i < options.MaxQueuedMessages; i++)
            {
                _logger.Log(LogLevel.Info, "queued", filePath: string.Empty, memberName: string.Empty);
            }

            LogOverloadedDropOldest();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < AllocationIterations; i++)
            {
                LogOverloadedDropOldest();
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        private void LogFiltered()
        {
            _logger.Log(LogLevel.Info, 42, AppendValueCallback, "Performance", string.Empty, 0, string.Empty);
        }

        private void LogAndPump()
        {
            _logger.Log(LogLevel.Info, 42, AppendValueCallback, "Performance", string.Empty, 0, string.Empty);
            _logger.Pump(1);
        }

        private void LogStringAndPump()
        {
            _logger.Log(LogLevel.Info, "short message", "Performance", string.Empty, 0, string.Empty);
            _logger.Pump(1);
        }

        private void LogOverloadedDropOldest()
        {
            _logger.Log(LogLevel.Info, "replacement", filePath: string.Empty, memberName: string.Empty);
        }

        private static void AppendValue(int value, StringBuilder builder)
        {
            builder.Append("value=");
            builder.Append(value);
        }

        private static LoggerProcessingOptions CreateOptions()
        {
            return new LoggerProcessingOptions
            {
                MaxQueuedMessages = 256,
                MaxQueuedCharacters = 64 * 1024,
                MaxMessageCharacters = 1024,
                ReservedCriticalMessages = 16,
                ReservedCriticalCharacters = 4096,
                CriticalLevel = LogLevel.Error
            };
        }

        private sealed class CountingSink : ILogger
        {
            private int _count;
            public void Log(LogMessage logMessage) => _count++;
            public void Dispose() { }
        }
    }
}
