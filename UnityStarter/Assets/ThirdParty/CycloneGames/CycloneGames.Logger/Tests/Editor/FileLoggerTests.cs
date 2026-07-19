using System;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class FileLoggerTests
    {
        private string _tempDirectory;
        private string _sourceFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CycloneGames.Logger.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _sourceFilePath = Path.Combine(_tempDirectory, "FileLoggerTests.cs");
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
            var options = new FileLoggerOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1024,
                FlushIntervalMs = 60000
            };

            using (var logger = new FileLogger(logPath, options))
            {
                LogMessage message = CreateMessage(LogLevel.Error, "disk failure", "Storage", 25);
                logger.Log(message);
                LogMessagePool.Return(message);

                string content = ReadAllTextShared(logPath);
                Assert.That(content, Does.Contain("[ERROR]"));
                Assert.That(content, Does.Contain("[Storage] disk failure"));
                Assert.That(content, Does.Contain("(at FileLoggerTests.cs:25)"));
            }
        }

        [Test]
        public void Log_BuilderMessage_WritesBuilderContent()
        {
            string logPath = Path.Combine(_tempDirectory, "builder.log");
            var options = new FileLoggerOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1,
                FlushIntervalMs = 60000
            };

            using (var logger = new FileLogger(logPath, options))
            {
                LogMessage message = LogMessagePool.Get();
                var builder = new StringBuilder(64);
                builder.Append("value=").Append(99);

                message.Initialize(
                    new DateTime(2026, 5, 20, 1, 2, 3, 4),
                    LogLevel.Info,
                    null,
                    builder,
                    "Builder",
                    string.Empty,
                    0,
                    nameof(Log_BuilderMessage_WritesBuilderContent));

                logger.Log(message);
                LogMessagePool.Return(message);
            }

            string content = File.ReadAllText(logPath);
            Assert.That(content, Does.Contain("[INFO]"));
            Assert.That(content, Does.Contain("[Builder] value=99"));
        }

        [Test]
        public void Constructor_InvalidOptions_FailsFast()
        {
            string logPath = Path.Combine(_tempDirectory, "invalid.log");
            var options = new FileLoggerOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 0
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => new FileLogger(logPath, options));
        }

        [Test]
        public void Log_NewFile_DoesNotWriteUtf8Bom()
        {
            string logPath = Path.Combine(_tempDirectory, "encoding.log");
            var options = new FileLoggerOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1,
                FlushIntervalMs = 60000
            };

            using (var logger = new FileLogger(logPath, options))
            {
                LogMessage message = CreateMessage(LogLevel.Info, "utf8", "Encoding", 12);
                logger.Log(message);
                LogMessagePool.Return(message);
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
                LogMessage consoleMessage = CreateMessage(LogLevel.Info, "console", "Culture", -123);
                using (var consoleLogger = new ConsoleLogger())
                {
                    consoleLogger.Log(consoleMessage);
                }
                LogMessagePool.Return(consoleMessage);

                var options = new FileLoggerOptions
                {
                    MaintenanceMode = FileMaintenanceMode.None,
                    FlushBatchSize = 1,
                    FlushIntervalMs = 60000
                };
                using (var fileLogger = new FileLogger(logPath, options))
                {
                    LogMessage fileMessage = CreateMessage(LogLevel.Info, "file", "Culture", -123);
                    fileLogger.Log(fileMessage);
                    LogMessagePool.Return(fileMessage);
                }
            }
            finally
            {
                Console.SetOut(previousOut);
                CultureInfo.CurrentCulture = previousCulture;
            }

            string consoleText = consoleOutput.ToString();
            string fileText = File.ReadAllText(logPath);
            Assert.That(consoleText, Does.Contain("FileLoggerTests.cs:-123"));
            Assert.That(fileText, Does.Contain("FileLoggerTests.cs:-123"));
            Assert.That(consoleText, Does.Not.Contain(customCulture.NumberFormat.NegativeSign));
            Assert.That(fileText, Does.Not.Contain(customCulture.NumberFormat.NegativeSign));
        }

        [Test]
        public void Constructor_RotatesOversizedFileToVersionedArchive()
        {
            string logPath = Path.Combine(_tempDirectory, "rotate.log");

            File.WriteAllText(logPath, new string('a', 128));

            var options = new FileLoggerOptions
            {
                MaintenanceMode = FileMaintenanceMode.Rotate,
                MaxFileBytes = 1,
                MaxArchiveFiles = 8,
                FlushBatchSize = 1
            };

            using (new FileLogger(logPath, options))
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

        private LogMessage CreateMessage(LogLevel level, string messageText, string category, int lineNumber)
        {
            LogMessage message = LogMessagePool.Get();
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
