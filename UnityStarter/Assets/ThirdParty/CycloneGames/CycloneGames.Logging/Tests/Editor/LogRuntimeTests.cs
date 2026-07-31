using System;
using System.Text;
using NUnit.Framework;

namespace CycloneGames.Logging.Tests
{
    public sealed class LogRuntimeTests
    {
        private const string TestCategory = "CycloneGames.Tests";

        [Test]
        public void Channel_DefaultsToNullWriter()
        {
            LogChannel channel = LogChannel.Create(TestCategory);

            using (new RuntimeWriterScope(NullLogWriter.Instance))
            {
                Assert.IsFalse(channel.IsEnabled(LogSeverity.Info));
                Assert.DoesNotThrow(() => channel.Info("ignored"));
            }
        }

        [Test]
        public void DefaultChannel_IsAlwaysSafeAndSilent()
        {
            var writer = new CountingWriter();
            LogChannel channel = default(LogChannel);

            using (new RuntimeWriterScope(writer))
            {
                Assert.IsFalse(channel.IsEnabled(LogSeverity.Info));
                Assert.DoesNotThrow(() => channel.Info("ignored"));
                Assert.DoesNotThrow(() => channel.Info((Action<StringBuilder>)null));
                Assert.DoesNotThrow(() => channel.Info<int>(1, null));
                Assert.DoesNotThrow(() => channel.Error((Exception)null));
            }

            Assert.AreEqual(0, writer.CallCount);
        }

        [Test]
        public void NullWriter_DoesNotInvokeDeferredBuilders()
        {
            LogChannel channel = LogChannel.Create(TestCategory, NullLogWriter.Instance);
            bool deferredInvoked = false;
            bool stateInvoked = false;

            channel.Info(builder => deferredInvoked = true);
            channel.Info(7, (state, builder) => stateInvoked = true);

            Assert.IsFalse(deferredInvoked);
            Assert.IsFalse(stateInvoked);
        }

        [Test]
        public void Channel_ObservesAtomicRuntimeReplacement()
        {
            LogChannel channel = LogChannel.Create(TestCategory);
            var first = new RecordingWriter();
            var second = new RecordingWriter();
            ILogWriter previous = LogRuntime.ReplaceWriter(NullLogWriter.Instance);
            ILogWriter installed = NullLogWriter.Instance;

            try
            {
                Assert.IsTrue(LogRuntime.TryInstallWriter(first));
                installed = first;
                channel.Info("first");
                Assert.AreEqual("first", first.Message);

                Assert.AreSame(first, LogRuntime.ReplaceWriter(second));
                installed = second;
                channel.Warning("second");
                Assert.AreEqual("second", second.Message);
                Assert.AreEqual(LogSeverity.Warning, second.Severity);
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(installed, previous));
            }
        }

        [Test]
        public void ExplicitChannel_DoesNotFollowRuntimeWriter()
        {
            var explicitWriter = new RecordingWriter();
            var runtimeWriter = new RecordingWriter();
            LogChannel channel = LogChannel.Create(TestCategory, explicitWriter);

            using (new RuntimeWriterScope(runtimeWriter))
            {
                channel.Error("explicit");
            }

            Assert.AreEqual("explicit", explicitWriter.Message);
            Assert.IsNull(runtimeWriter.Message);
        }

        [Test]
        public void TryReplaceWriter_RequiresExpectedIdentity()
        {
            var installedWriter = new RecordingWriter();
            var foreignWriter = new RecordingWriter();
            var replacementWriter = new RecordingWriter();
            ILogWriter previous = LogRuntime.ReplaceWriter(installedWriter);
            ILogWriter installed = installedWriter;

            try
            {
                Assert.IsFalse(LogRuntime.TryReplaceWriter(foreignWriter, replacementWriter));
                Assert.AreSame(installedWriter, LogRuntime.Writer);

                Assert.IsTrue(LogRuntime.TryReplaceWriter(installedWriter, replacementWriter));
                installed = replacementWriter;
                Assert.AreSame(replacementWriter, LogRuntime.Writer);

                Assert.IsFalse(LogRuntime.TryResetWriter(installedWriter));
                Assert.AreSame(replacementWriter, LogRuntime.Writer);
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(installed, previous));
            }
        }

        [Test]
        public void InvalidWriterMutationArguments_ThrowBeforeMutation()
        {
            ILogWriter current = LogRuntime.Writer;

            Assert.Throws<ArgumentNullException>(() => LogRuntime.TryInstallWriter(null));
            Assert.Throws<ArgumentNullException>(() => LogRuntime.ReplaceWriter(null));
            Assert.Throws<ArgumentNullException>(() => LogRuntime.TryReplaceWriter(null, NullLogWriter.Instance));
            Assert.Throws<ArgumentNullException>(() => LogRuntime.TryReplaceWriter(current, null));
            Assert.Throws<ArgumentNullException>(() => LogRuntime.TryResetWriter(null));
            Assert.AreSame(current, LogRuntime.Writer);
        }

        [Test]
        public void Channel_ContainsNonCatastrophicWriterFailures()
        {
            var failure = new InvalidOperationException("backend failure");
            LogChannel channel = LogChannel.Create(TestCategory, new ThrowingWriter(failure));

            Assert.IsFalse(channel.IsEnabled(LogSeverity.Info));
            Assert.DoesNotThrow(() => channel.Info("message"));
            Assert.DoesNotThrow(() => channel.Info(builder => builder.Append("message")));
            Assert.DoesNotThrow(() => channel.Info(1, (state, builder) => builder.Append(state)));
            Assert.DoesNotThrow(() => channel.Error(new Exception("producer failure"), "message"));
        }

        [Test]
        public void Channel_ContainsNonCatastrophicFormatterFailures()
        {
            LogChannel channel = LogChannel.Create(TestCategory, new BuilderInvokingWriter());

            Assert.DoesNotThrow(() => channel.Info(builder => throw new FormatException("formatter failure")));
            Assert.DoesNotThrow(() => channel.Info(1, (state, builder) => throw new FormatException("formatter failure")));
        }

        [Test]
        public void Channel_DoesNotContainOutOfMemoryException()
        {
            var failure = new OutOfMemoryException("fatal backend failure");
            LogChannel channel = LogChannel.Create(TestCategory, new ThrowingWriter(failure));
            LogChannel formatterChannel = LogChannel.Create(TestCategory, new BuilderInvokingWriter());

            Assert.Throws<OutOfMemoryException>(() => channel.IsEnabled(LogSeverity.Info));
            Assert.Throws<OutOfMemoryException>(() => channel.Info("message"));
            Assert.Throws<OutOfMemoryException>(() => formatterChannel.Info(builder => throw failure));
        }

        [Test]
        public void InvalidSeverity_IsSilentAndNeverReachesWriter()
        {
            var writer = new CountingWriter();
            LogChannel channel = LogChannel.Create(TestCategory, writer);

            AssertSeverityIsSilent(channel, LogSeverity.None);
            AssertSeverityIsSilent(channel, unchecked((LogSeverity)byte.MaxValue));

            Assert.AreEqual(0, writer.CallCount);
        }

        [Test]
        public void InvalidProducerArguments_ThrowBeforeWriterInvocation()
        {
            var writer = new CountingWriter();
            LogChannel channel = LogChannel.Create(TestCategory, writer);

            Assert.Throws<ArgumentException>(() => LogChannel.Create(" "));
            Assert.Throws<ArgumentNullException>(() => LogChannel.Create(TestCategory, null));
            Assert.Throws<ArgumentNullException>(() => channel.Info((Action<StringBuilder>)null));
            Assert.Throws<ArgumentNullException>(() => channel.Info<int>(1, null));
            Assert.Throws<ArgumentNullException>(() => channel.Error((Exception)null));
            Assert.AreEqual(0, writer.CallCount);
        }

        [Test]
        public void Guard_ReportsWriterCompletionWithoutClaimingDelivery()
        {
            var writer = new RecordingWriter();
            var failureWriter = new ThrowingWriter(new InvalidOperationException("failure"));

            Assert.IsTrue(LogWriterGuard.TryWrite(writer, LogSeverity.Info, TestCategory, "message"));
            Assert.AreEqual("message", writer.Message);
            Assert.IsFalse(LogWriterGuard.TryWrite(NullLogWriter.Instance, LogSeverity.Info, TestCategory, "ignored"));
            Assert.IsFalse(LogWriterGuard.TryWrite(failureWriter, LogSeverity.Info, TestCategory, "ignored"));
            Assert.IsFalse(LogWriterGuard.IsEnabled(failureWriter, LogSeverity.Info, TestCategory));
        }

        [Test]
        public void Guard_ValidatesCallerContract()
        {
            Assert.Throws<ArgumentNullException>(() =>
                LogWriterGuard.TryWrite(null, LogSeverity.Info, TestCategory, "message"));
            Assert.Throws<ArgumentException>(() =>
                LogWriterGuard.TryWrite(NullLogWriter.Instance, LogSeverity.Info, "", "message"));
            Assert.Throws<ArgumentNullException>(() =>
                LogWriterGuard.TryWrite(
                    NullLogWriter.Instance,
                    LogSeverity.Info,
                    TestCategory,
                    (Action<StringBuilder>)null));
            Assert.Throws<ArgumentNullException>(() =>
                LogWriterGuard.TryWriteException(
                    NullLogWriter.Instance,
                    LogSeverity.Info,
                    TestCategory,
                    null));
        }

        [Test]
        public void Exception_PreservesStructuredInput()
        {
            var writer = new RecordingWriter();
            LogChannel channel = LogChannel.Create(TestCategory, writer);
            var exception = new InvalidOperationException("failure");

            channel.Error(exception, "operation failed");

            Assert.AreSame(exception, writer.Exception);
            Assert.AreEqual("operation failed", writer.Message);
        }

        [Test]
        public void ExceptionOverloads_MapEverySeverity()
        {
            var writer = new RecordingWriter();
            LogChannel channel = LogChannel.Create(TestCategory, writer);
            var exception = new InvalidOperationException("failure");

            AssertExceptionWrite(() => channel.Trace(exception), writer, LogSeverity.Trace, exception);
            AssertExceptionWrite(() => channel.Debug(exception), writer, LogSeverity.Debug, exception);
            AssertExceptionWrite(() => channel.Info(exception), writer, LogSeverity.Info, exception);
            AssertExceptionWrite(() => channel.Warning(exception), writer, LogSeverity.Warning, exception);
            AssertExceptionWrite(() => channel.Error(exception), writer, LogSeverity.Error, exception);
            AssertExceptionWrite(() => channel.Fatal(exception), writer, LogSeverity.Fatal, exception);
        }

        private static void AssertSeverityIsSilent(LogChannel channel, LogSeverity severity)
        {
            Assert.IsFalse(channel.IsEnabled(severity));
            channel.Write(severity, "message");
            channel.Write(severity, builder => builder.Append("message"));
            channel.Write(severity, 1, (state, builder) => builder.Append(state));
            channel.WriteException(severity, new Exception("failure"));
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

        private sealed class RuntimeWriterScope : IDisposable
        {
            private readonly ILogWriter _installedWriter;
            private readonly ILogWriter _previousWriter;
            private bool _disposed;

            internal RuntimeWriterScope(ILogWriter installedWriter)
            {
                _installedWriter = installedWriter;
                _previousWriter = LogRuntime.ReplaceWriter(installedWriter);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Assert.IsTrue(LogRuntime.TryReplaceWriter(_installedWriter, _previousWriter));
            }
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
                messageBuilder(builder);
                Write(severity, category, builder.ToString(), filePath, lineNumber, memberName);
            }

            public void Write<TState>(LogSeverity severity, string category, TState state, Action<TState, StringBuilder> messageBuilder, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                var builder = new StringBuilder();
                messageBuilder(state, builder);
                Write(severity, category, builder.ToString(), filePath, lineNumber, memberName);
            }

            public void WriteException(LogSeverity severity, string category, Exception exception, string message = null, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                Exception = exception;
                Write(severity, category, message, filePath, lineNumber, memberName);
            }
        }

        private sealed class CountingWriter : ILogWriter
        {
            public int CallCount { get; private set; }

            public bool IsEnabled(LogSeverity severity, string category)
            {
                CallCount++;
                return true;
            }

            public void Write(LogSeverity severity, string category, string message, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                CallCount++;
            }

            public void Write(LogSeverity severity, string category, Action<StringBuilder> messageBuilder, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                CallCount++;
            }

            public void Write<TState>(LogSeverity severity, string category, TState state, Action<TState, StringBuilder> messageBuilder, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                CallCount++;
            }

            public void WriteException(LogSeverity severity, string category, Exception exception, string message = null, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                CallCount++;
            }
        }

        private sealed class ThrowingWriter : ILogWriter
        {
            private readonly Exception _failure;

            internal ThrowingWriter(Exception failure)
            {
                _failure = failure;
            }

            public bool IsEnabled(LogSeverity severity, string category) => throw _failure;

            public void Write(LogSeverity severity, string category, string message, string filePath = "", int lineNumber = 0, string memberName = "") => throw _failure;

            public void Write(LogSeverity severity, string category, Action<StringBuilder> messageBuilder, string filePath = "", int lineNumber = 0, string memberName = "") => throw _failure;

            public void Write<TState>(LogSeverity severity, string category, TState state, Action<TState, StringBuilder> messageBuilder, string filePath = "", int lineNumber = 0, string memberName = "") => throw _failure;

            public void WriteException(LogSeverity severity, string category, Exception exception, string message = null, string filePath = "", int lineNumber = 0, string memberName = "") => throw _failure;
        }

        private sealed class BuilderInvokingWriter : ILogWriter
        {
            public bool IsEnabled(LogSeverity severity, string category) => true;

            public void Write(LogSeverity severity, string category, string message, string filePath = "", int lineNumber = 0, string memberName = "")
            {
            }

            public void Write(LogSeverity severity, string category, Action<StringBuilder> messageBuilder, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                messageBuilder(new StringBuilder());
            }

            public void Write<TState>(LogSeverity severity, string category, TState state, Action<TState, StringBuilder> messageBuilder, string filePath = "", int lineNumber = 0, string memberName = "")
            {
                messageBuilder(state, new StringBuilder());
            }

            public void WriteException(LogSeverity severity, string category, Exception exception, string message = null, string filePath = "", int lineNumber = 0, string memberName = "")
            {
            }
        }
    }
}
