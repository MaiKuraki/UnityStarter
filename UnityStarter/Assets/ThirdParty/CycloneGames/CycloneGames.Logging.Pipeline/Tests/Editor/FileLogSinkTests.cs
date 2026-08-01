using System;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace CycloneGames.Logging.Pipeline.Tests.Editor
{
    public sealed class FileLogSinkTests
    {
        private string _tempDirectory;
        private string _sourceFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CycloneGames.Logging.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _sourceFilePath = Path.Combine(_tempDirectory, "FileLogSinkTests.cs");
            File.WriteAllText(_sourceFilePath, string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Test]
        public void Log_ErrorMessage_FlushesImmediately()
        {
            string logPath = Path.Combine(_tempDirectory, "error.log");
            var options = new FileLogSinkOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1024,
                FlushIntervalMs = 60000
            };

            using (var logger = new FileLogSink(logPath, options))
            {
                LogEvent message = CreateMessage(LogSeverity.Error, "disk failure", "Storage", 25);
                logger.Emit(message);
                LogEventPool.Return(message);

                string content = ReadAllTextShared(logPath);
                Assert.That(content, Does.Contain("[ERROR]"));
                Assert.That(content, Does.Contain("[Storage] disk failure"));
                Assert.That(content, Does.Contain("(at FileLogSinkTests.cs:25)"));
            }
        }

        [Test]
        public void Log_BuilderMessage_WritesBuilderContent()
        {
            string logPath = Path.Combine(_tempDirectory, "builder.log");
            var options = new FileLogSinkOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1,
                FlushIntervalMs = 60000
            };

            using (var logger = new FileLogSink(logPath, options))
            {
                LogEvent message = LogEventPool.Get();
                var builder = new StringBuilder(64);
                builder.Append("value=").Append(99);

                message.Initialize(
                    new DateTime(2026, 5, 20, 1, 2, 3, 4),
                    LogSeverity.Info,
                    null,
                    builder,
                    "Builder",
                    string.Empty,
                    0,
                    nameof(Log_BuilderMessage_WritesBuilderContent));

                logger.Emit(message);
                LogEventPool.Return(message);
            }

            string content = File.ReadAllText(logPath);
            Assert.That(content, Does.Contain("[INFO]"));
            Assert.That(content, Does.Contain("[Builder] value=99"));
        }

        [Test]
        public void Constructor_InvalidOptions_FailsFast()
        {
            string logPath = Path.Combine(_tempDirectory, "invalid.log");
            var options = new FileLogSinkOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 0
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => new FileLogSink(logPath, options));
        }

        [Test]
        public void Log_NewFile_DoesNotWriteUtf8Bom()
        {
            string logPath = Path.Combine(_tempDirectory, "encoding.log");
            var options = new FileLogSinkOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1,
                FlushIntervalMs = 60000
            };

            using (var logger = new FileLogSink(logPath, options))
            {
                LogEvent message = CreateMessage(LogSeverity.Info, "utf8", "Encoding", 12);
                logger.Emit(message);
                LogEventPool.Return(message);
            }

            byte[] bytes = File.ReadAllBytes(logPath);
            Assert.Greater(bytes.Length, 3);
            Assert.IsFalse(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        }

        [Test]
        public void TextSinks_FormatLineNumbersIndependentlyOfCurrentCulture()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            TextWriter previousOut = Console.Out;
            var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            customCulture.NumberFormat.NegativeSign = new string('!', 1024);
            var consoleOutput = new StringWriter(CultureInfo.InvariantCulture);
            string logPath = Path.Combine(_tempDirectory, "culture.log");
            try
            {
                CultureInfo.CurrentCulture = customCulture;
                Console.SetOut(consoleOutput);
                LogEvent consoleMessage = CreateMessage(LogSeverity.Info, "console", "Culture", -123);
                using (var consoleSink = new ConsoleLogSink())
                {
                    consoleSink.Emit(consoleMessage);
                }
                LogEventPool.Return(consoleMessage);

                var options = new FileLogSinkOptions
                {
                    MaintenanceMode = FileMaintenanceMode.None,
                    FlushBatchSize = 1,
                    FlushIntervalMs = 60000
                };
                using (var fileSink = new FileLogSink(logPath, options))
                {
                    LogEvent fileMessage = CreateMessage(LogSeverity.Info, "file", "Culture", -123);
                    fileSink.Emit(fileMessage);
                    LogEventPool.Return(fileMessage);
                }
            }
            finally
            {
                Console.SetOut(previousOut);
                CultureInfo.CurrentCulture = previousCulture;
            }

            string consoleText = consoleOutput.ToString();
            string fileText = File.ReadAllText(logPath);
            Assert.That(consoleText, Does.Contain("FileLogSinkTests.cs:-123"));
            Assert.That(fileText, Does.Contain("FileLogSinkTests.cs:-123"));
            Assert.That(consoleText, Does.Not.Contain(customCulture.NumberFormat.NegativeSign));
            Assert.That(fileText, Does.Not.Contain(customCulture.NumberFormat.NegativeSign));
        }

        [Test]
        public void Constructor_RotatesOversizedFileToVersionedArchive()
        {
            string logPath = Path.Combine(_tempDirectory, "rotate.log");

            File.WriteAllText(logPath, new string('a', 128));

            var options = new FileLogSinkOptions
            {
                MaintenanceMode = FileMaintenanceMode.Rotate,
                MaxFileBytes = 1,
                MaxArchiveFiles = 8,
                FlushBatchSize = 1
            };

            using (new FileLogSink(logPath, options))
            {
            }

            Assert.AreEqual(1, Directory.GetFiles(_tempDirectory, "rotate.cyclone-v2-*.log").Length);
        }

        private static string ReadAllTextShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        private LogEvent CreateMessage(LogSeverity level, string messageText, string category, int lineNumber)
        {
            LogEvent message = LogEventPool.Get();
            message.Initialize(
                new DateTime(2026, 5, 20, 1, 2, 3, 4),
                level,
                messageText,
                null,
                category,
                _sourceFilePath,
                lineNumber,
                nameof(CreateMessage));
            return message;
        }
    }
}
