using System;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class FileLoggerReliabilityTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.Logger.ReliabilityTests",
                Guid.NewGuid().ToString("N"));
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
        public void DefaultOptions_EnableBoundedRotationAndPrivateSourcePaths()
        {
            FileLoggerOptions first = FileLoggerOptions.Default;
            FileLoggerOptions second = FileLoggerOptions.Default;

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.MaintenanceMode, Is.EqualTo(FileMaintenanceMode.Rotate));
            Assert.That(first.SourcePathMode, Is.EqualTo(LogSourcePathMode.FileName));
            Assert.That(first.MaxFileBytes, Is.GreaterThan(0L));
            Assert.That(first.MaxArchiveFiles, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void Maintenance_DeletesOnlyStrictlyOwnedArchives()
        {
            string logPath = Path.Combine(_tempDirectory, "game.log");
            string ownedOld = Path.Combine(_tempDirectory, "game.cyclone-v2-0000000000000000001.log");
            string ownedNewer = Path.Combine(_tempDirectory, "game.cyclone-v2-0000000000000000002.log");
            string markerButInvalid = Path.Combine(_tempDirectory, "game.cyclone-v2-not-owned.log");
            string legacyName = Path.Combine(_tempDirectory, "game_20200101_000000.log");

            File.WriteAllText(ownedOld, "old");
            File.WriteAllText(ownedNewer, "newer");
            File.WriteAllText(markerButInvalid, "keep");
            File.WriteAllText(legacyName, "keep");
            File.WriteAllText(logPath, new string('x', 512));
            File.SetLastWriteTimeUtc(ownedOld, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(ownedNewer, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var options = new FileLoggerOptions
            {
                MaxFileBytes = 128,
                MaxArchiveFiles = 1,
                FlushBatchSize = 1
            };

            using (new FileLogger(logPath, options))
            {
            }

            Assert.That(File.Exists(markerButInvalid), Is.True);
            Assert.That(File.Exists(legacyName), Is.True);
            Assert.That(File.Exists(ownedOld), Is.False);
            Assert.That(File.Exists(ownedNewer), Is.False);
            Assert.That(CountStrictOwnedArchives("game.cyclone-v2-", ".log"), Is.EqualTo(1));
        }

        [Test]
        public void LogAndFlush_RotateBeforeProjectedUtf8LimitAndExposeStatistics()
        {
            string logPath = Path.Combine(_tempDirectory, "bounded.log");
            var options = new FileLoggerOptions
            {
                MaxFileBytes = 220,
                MaxArchiveFiles = 2,
                FlushBatchSize = 1024,
                FlushIntervalMs = 60000
            };

            FileLoggerStatistics statistics;
            using (var logger = new FileLogger(logPath, options))
            {
                for (int i = 0; i < 8; i++)
                {
                    LogMessage message = CreateMessage(
                        LogLevel.Info,
                        "entry-" + i + "-\u4E2D\u6587-abcdefghijklmnopqrstuvwxyz",
                        "Reliability\r\nInjected",
                        "C:\\private\\source\\FileLoggerReliabilityTests.cs");
                    logger.Log(message);
                    LogMessagePool.Return(message);
                }

                Assert.That(logger.TryFlush(LogFlushMode.Buffered), Is.True);
                statistics = logger.Statistics;
                Assert.That(logger.LogFilePath, Is.EqualTo(Path.GetFullPath(logPath)));
                Assert.That(logger.Health, Is.Not.EqualTo(FileLoggerHealth.Faulted));
            }

            Assert.That(statistics.AttemptedEntries, Is.EqualTo(8));
            Assert.That(statistics.WrittenEntries, Is.EqualTo(8));
            Assert.That(statistics.DroppedEntries, Is.Zero);
            Assert.That(statistics.RotationCount, Is.GreaterThan(0));
            Assert.That(new FileInfo(logPath).Length, Is.LessThanOrEqualTo(options.MaxFileBytes));
            Assert.That(Directory.GetFiles(_tempDirectory, "bounded.cyclone-v2-*.log").Length, Is.LessThanOrEqualTo(2));

            string activeContent = File.ReadAllText(logPath);
            Assert.That(activeContent, Does.Not.Contain("C:/private/source"));
            Assert.That(activeContent, Does.Contain("FileLoggerReliabilityTests.cs"));
            Assert.That(activeContent, Does.Contain("Reliability\\r\\nInjected"));
            Assert.That(activeContent, Does.Not.Contain("Reliability\r\nInjected"));
        }

        [Test]
        public void IdleMaintenance_FlushesWithoutAnotherLogEntry()
        {
            string logPath = Path.Combine(_tempDirectory, "idle-flush.log");
            var options = new FileLoggerOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1024,
                FlushIntervalMs = 10
            };

            using (var logger = new FileLogger(logPath, options))
            {
                LogMessage message = CreateMessage(LogLevel.Info, "idle flush", "Reliability", "FileLoggerReliabilityTests.cs");
                logger.Log(message);
                LogMessagePool.Return(message);
                Thread.Sleep(25);
                ((IMaintainableLogger)logger).PerformMaintenance();

                Assert.That(ReadAllTextShared(logPath), Does.Contain("idle flush"));
            }
        }

        private static string ReadAllTextShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }

        private int CountStrictOwnedArchives(string prefix, string extension)
        {
            string[] files = Directory.GetFiles(_tempDirectory, prefix + "*" + extension);
            int count = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                string token = name.Substring(prefix.Length, name.Length - prefix.Length - extension.Length);
                int sequenceSeparator = token.LastIndexOf('-');
                string ticksToken = sequenceSeparator > 0 ? token.Substring(0, sequenceSeparator) : token;
                long ticks;
                if (ticksToken.Length == 19
                    && long.TryParse(ticksToken, out ticks)
                    && ticks >= DateTime.MinValue.Ticks
                    && ticks <= DateTime.MaxValue.Ticks)
                {
                    count++;
                }
            }

            return count;
        }

        private static LogMessage CreateMessage(LogLevel level, string text, string category, string sourcePath)
        {
            LogMessage message = LogMessagePool.Get();
            message.Initialize(
                new DateTime(2026, 7, 10, 1, 2, 3, 4, DateTimeKind.Utc),
                level,
                text,
                null,
                category,
                sourcePath,
                42,
                nameof(CreateMessage),
                4096);
            return message;
        }
    }
}
