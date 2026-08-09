using System;
using System.Text;

using CycloneGames.Logging;
using NUnit.Framework;

namespace CycloneGames.DataTable.Tests.Editor.Integrations.Logging
{
    public sealed class DataTableLogWriterAdapterTests
    {
        [TestCase(DataTableDiagnosticLevel.Trace, LogSeverity.Trace)]
        [TestCase(DataTableDiagnosticLevel.Debug, LogSeverity.Debug)]
        [TestCase(DataTableDiagnosticLevel.Info, LogSeverity.Info)]
        [TestCase(DataTableDiagnosticLevel.Warning, LogSeverity.Warning)]
        [TestCase(DataTableDiagnosticLevel.Error, LogSeverity.Error)]
        [TestCase(DataTableDiagnosticLevel.Fatal, LogSeverity.Fatal)]
        public void MapsEveryOutputLevelExactly(
            DataTableDiagnosticLevel level,
            LogSeverity expectedSeverity)
        {
            var writer = new ProbeLogWriter();
            var adapter = new DataTableLogWriterAdapter(writer);

            adapter.Write(level, DataTableDiagnosticCategories.Root, "message");

            Assert.AreEqual(1, writer.CallCount);
            Assert.AreEqual(expectedSeverity, writer.LastSeverity);
        }

        [TestCase(DataTableDiagnosticLevel.None)]
        [TestCase((DataTableDiagnosticLevel)byte.MaxValue)]
        public void DropsNonOutputAndUnknownLevels(DataTableDiagnosticLevel level)
        {
            var writer = new ProbeLogWriter();
            var adapter = new DataTableLogWriterAdapter(writer);

            Assert.IsFalse(adapter.IsEnabled(level, DataTableDiagnosticCategories.Root));
            Assert.DoesNotThrow(() =>
                adapter.Write(level, DataTableDiagnosticCategories.Root, "message"));
            Assert.DoesNotThrow(() =>
                adapter.WriteException(
                    level,
                    DataTableDiagnosticCategories.Root,
                    new InvalidOperationException("diagnostic")));
            Assert.AreEqual(0, writer.CallCount);
        }

        [Test]
        public void IsolatesOrdinaryWriterFailures()
        {
            var writer = new ProbeLogWriter(throwOnCall: true);
            var adapter = new DataTableLogWriterAdapter(writer);

            Assert.IsFalse(adapter.IsEnabled(
                DataTableDiagnosticLevel.Info,
                DataTableDiagnosticCategories.Root));
            Assert.DoesNotThrow(() =>
                adapter.Write(
                    DataTableDiagnosticLevel.Info,
                    DataTableDiagnosticCategories.Root,
                    "message"));
            Assert.DoesNotThrow(() =>
                adapter.WriteException(
                    DataTableDiagnosticLevel.Error,
                    DataTableDiagnosticCategories.Root,
                    new InvalidOperationException("diagnostic")));
            Assert.AreEqual(3, writer.CallCount);
        }

        private sealed class ProbeLogWriter : ILogWriter
        {
            private readonly bool _throwOnCall;

            public ProbeLogWriter(bool throwOnCall = false)
            {
                _throwOnCall = throwOnCall;
            }

            public int CallCount { get; private set; }

            public LogSeverity LastSeverity { get; private set; }

            public bool IsEnabled(LogSeverity severity, string category)
            {
                Record(severity);
                return true;
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Record(severity);

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Record(severity);

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Record(severity);

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Record(severity);

            private void Record(LogSeverity severity)
            {
                CallCount++;
                LastSeverity = severity;
                if (_throwOnCall)
                {
                    throw new InvalidOperationException("Expected writer failure.");
                }
            }
        }
    }
}
