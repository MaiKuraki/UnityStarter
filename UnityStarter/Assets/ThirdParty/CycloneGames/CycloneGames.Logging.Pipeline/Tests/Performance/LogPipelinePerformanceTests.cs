using System;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.Logging.Pipeline.Tests.Performance
{
    public sealed class LogPipelinePerformanceTests
    {
        private const int WarmupCount = 10;
        private const int MeasurementCount = 20;
        private const int IterationsPerMeasurement = 1000;
        private const int AllocationIterations = 10000;

        private static readonly Action<int, StringBuilder> AppendValueCallback = AppendValue;

        private LogPipeline _pipeline;
        private ILogWriter _writer;
        private CountingSink _sink;

        [TearDown]
        public void TearDown()
        {
            _pipeline?.Dispose();
            _pipeline = null;
            _writer = null;
            _sink = null;
        }

        [Test, Performance]
        public void FilteredGenericBuilder_ProducerCost()
        {
            _pipeline = CreatePipeline(CreateOptions());
            _sink = new CountingSink();
            _pipeline.RegisterSink(_sink);
            _pipeline.MinimumSeverity = LogSeverity.Error;

            Measure.Method(WriteFilteredMessage)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(IterationsPerMeasurement)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void AcceptedGenericBuilder_WithSynchronousDispatch()
        {
            _pipeline = CreatePipeline(CreateOptions());
            _sink = new CountingSink();
            _pipeline.RegisterSink(_sink);

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
            _pipeline = CreatePipeline(CreateOptions());
            _sink = new CountingSink();
            _pipeline.RegisterSink(_sink);

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
            LogPipelineOptions options = CreateOptions();
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            options.ReservedCriticalMessages = 0;
            options.ReservedCriticalCharacters = 0;
            _pipeline = CreatePipeline(options);
            _sink = new CountingSink();
            _pipeline.RegisterSink(_sink);
            for (int i = 0; i < options.MaxQueuedMessages; i++)
            {
                _writer.Write(LogSeverity.Info, null, "queued", filePath: string.Empty, memberName: string.Empty);
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
            _pipeline = CreatePipeline(CreateOptions());
            _sink = new CountingSink();
            _pipeline.RegisterSink(_sink);
            _pipeline.MinimumSeverity = LogSeverity.Error;
            WriteFilteredMessage();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < AllocationIterations; i++)
            {
                WriteFilteredMessage();
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Test]
        public void AcceptedCachedBuilder_SteadyStateAllocatesZeroBytes()
        {
            _pipeline = CreatePipeline(CreateOptions());
            _sink = new CountingSink();
            _pipeline.RegisterSink(_sink);
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
            _pipeline = CreatePipeline(CreateOptions());
            _sink = new CountingSink();
            _pipeline.RegisterSink(_sink);
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
            LogPipelineOptions options = CreateOptions();
            options.OverflowPolicy = LogQueueOverflowPolicy.DropOldest;
            options.ReservedCriticalMessages = 0;
            options.ReservedCriticalCharacters = 0;
            _pipeline = CreatePipeline(options);
            _sink = new CountingSink();
            _pipeline.RegisterSink(_sink);
            for (int i = 0; i < options.MaxQueuedMessages; i++)
            {
                _writer.Write(LogSeverity.Info, null, "queued", filePath: string.Empty, memberName: string.Empty);
            }

            LogOverloadedDropOldest();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < AllocationIterations; i++)
            {
                LogOverloadedDropOldest();
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        private void WriteFilteredMessage()
        {
            _writer.Write(LogSeverity.Info, "Performance", 42, AppendValueCallback, string.Empty, 0, string.Empty);
        }

        private void LogAndPump()
        {
            _writer.Write(LogSeverity.Info, "Performance", 42, AppendValueCallback, string.Empty, 0, string.Empty);
            _pipeline.Pump(1);
        }

        private void LogStringAndPump()
        {
            _writer.Write(LogSeverity.Info, "Performance", "short message", string.Empty, 0, string.Empty);
            _pipeline.Pump(1);
        }

        private void LogOverloadedDropOldest()
        {
            _writer.Write(LogSeverity.Info, null, "replacement", filePath: string.Empty, memberName: string.Empty);
        }

        private LogPipeline CreatePipeline(LogPipelineOptions options)
        {
            LogPipeline pipeline = LogPipelineFactory.CreateSingleThreaded(options);
            _writer = pipeline;
            return pipeline;
        }

        private static void AppendValue(int value, StringBuilder builder)
        {
            builder.Append("value=");
            builder.Append(value);
        }

        private static LogPipelineOptions CreateOptions()
        {
            return new LogPipelineOptions
            {
                MaxQueuedMessages = 256,
                MaxQueuedCharacters = 64 * 1024,
                MaxMessageCharacters = 1024,
                ReservedCriticalMessages = 16,
                ReservedCriticalCharacters = 4096,
                CriticalSeverity = LogSeverity.Error
            };
        }

        private sealed class CountingSink : ILogSink
        {
            private int _count;
            public void Emit(LogEvent logEvent) => _count++;
            public void Dispose() { }
        }
    }
}
