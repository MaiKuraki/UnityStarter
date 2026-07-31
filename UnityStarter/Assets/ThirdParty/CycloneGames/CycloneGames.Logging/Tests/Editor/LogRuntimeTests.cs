using System;
using System.Text;
using NUnit.Framework;

namespace CycloneGames.Logging.Tests
{
    public sealed class LogRuntimeTests
    {
        [SetUp]
        public void SetUp()
        {
            LogRuntime.ResetWriter();
        }

        [TearDown]
        public void TearDown()
        {
            LogRuntime.ResetWriter();
        }

        [Test]
        public void Channel_DefaultsToNullWriter()
        {
            LogChannel channel = LogChannel.Create("CycloneGames.Tests");

            Assert.IsFalse(channel.IsEnabled(LogSeverity.Info));
            Assert.DoesNotThrow(() => channel.Info("ignored"));
        }

        [Test]
        public void Channel_ObservesAtomicRuntimeReplacement()
        {
            LogChannel channel = LogChannel.Create("CycloneGames.Tests");
            var first = new RecordingWriter();
            var second = new RecordingWriter();

            Assert.IsTrue(LogRuntime.TryInstallWriter(first));
            channel.Info("first");
            Assert.AreEqual("first", first.Message);

            Assert.AreSame(first, LogRuntime.ReplaceWriter(second));
            channel.Warning("second");
            Assert.AreEqual("second", second.Message);
            Assert.AreEqual(LogSeverity.Warning, second.Severity);
        }

        [Test]
        public void ExplicitChannel_DoesNotFollowRuntimeWriter()
        {
            var explicitWriter = new RecordingWriter();
            var runtimeWriter = new RecordingWriter();
            LogChannel channel = LogChannel.Create("CycloneGames.Tests", explicitWriter);
            LogRuntime.ReplaceWriter(runtimeWriter);

            channel.Error("explicit");

            Assert.AreEqual("explicit", explicitWriter.Message);
            Assert.IsNull(runtimeWriter.Message);
        }

        [Test]
        public void Exception_PreservesStructuredInput()
        {
            var writer = new RecordingWriter();
            LogChannel channel = LogChannel.Create("CycloneGames.Tests", writer);
            var exception = new InvalidOperationException("failure");

            channel.Error(exception, "operation failed");

            Assert.AreSame(exception, writer.Exception);
            Assert.AreEqual("operation failed", writer.Message);
        }

        [Test]
        public void ExceptionOverloads_MapEverySeverity()
        {
            var writer = new RecordingWriter();
            LogChannel channel = LogChannel.Create("CycloneGames.Tests", writer);
            var exception = new InvalidOperationException("failure");

            AssertExceptionWrite(() => channel.Trace(exception), writer, LogSeverity.Trace, exception);
            AssertExceptionWrite(() => channel.Debug(exception), writer, LogSeverity.Debug, exception);
            AssertExceptionWrite(() => channel.Info(exception), writer, LogSeverity.Info, exception);
            AssertExceptionWrite(() => channel.Warning(exception), writer, LogSeverity.Warning, exception);
            AssertExceptionWrite(() => channel.Error(exception), writer, LogSeverity.Error, exception);
            AssertExceptionWrite(() => channel.Fatal(exception), writer, LogSeverity.Fatal, exception);
        }

        private static void AssertExceptionWrite(
            Action write,
            RecordingWriter writer,
            LogSeverity expectedSeverity,
            Exception expectedException)
        {
            write();

            Assert.AreEqual(expectedSeverity, writer.Severity);
            Assert.AreSame(expectedException, writer.Exception);
        }

        private sealed class RecordingWriter : ILogWriter
        {
            public LogSeverity Severity { get; private set; }
            public string Category { get; private set; }
            public string Message { get; private set; }
            public Exception Exception { get; private set; }

            public bool IsEnabled(LogSeverity severity, string category) => true;

            public void Write(LogSeverity severity, string category, string message, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                Severity = severity;
                Category = category;
                Message = message;
            }

            public void Write(LogSeverity severity, string category, Action<StringBuilder> messageBuilder, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                var builder = new StringBuilder();
                messageBuilder?.Invoke(builder);
                Write(severity, category, builder.ToString(), filePath, lineNumber, memberName);
            }

            public void Write<TState>(LogSeverity severity, string category, TState state, Action<TState, StringBuilder> messageBuilder, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                var builder = new StringBuilder();
                messageBuilder?.Invoke(state, builder);
                Write(severity, category, builder.ToString(), filePath, lineNumber, memberName);
            }

            public void WriteException(LogSeverity severity, string category, Exception exception, string message = null, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                Exception = exception;
                Write(severity, category, message, filePath, lineNumber, memberName);
            }
        }
    }
}
