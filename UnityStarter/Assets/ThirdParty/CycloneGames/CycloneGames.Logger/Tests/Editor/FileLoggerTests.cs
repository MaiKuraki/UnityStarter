using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class FileLoggerTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CycloneGames.Logger.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
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
                LogMessage message = LogMessagePool.Get();
                message.Initialize(
                    new DateTime(2026, 5, 20, 1, 2, 3, 4),
                    LogLevel.Error,
                    "disk failure",
                    null,
                    "Storage",
                    "C:\\Project\\FileLoggerTests.cs",
                    25,
                    nameof(Log_ErrorMessage_FlushesImmediately));

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

        private static string ReadAllTextShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
