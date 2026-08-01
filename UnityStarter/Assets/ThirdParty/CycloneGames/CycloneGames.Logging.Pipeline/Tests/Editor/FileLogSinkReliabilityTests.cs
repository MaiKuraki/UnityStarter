using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace CycloneGames.Logging.Pipeline.Tests.Editor
{
    public sealed class FileLogSinkReliabilityTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.Logging.ReliabilityTests",
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
            FileLogSinkOptions first = FileLogSinkOptions.Default;
            FileLogSinkOptions second = FileLogSinkOptions.Default;

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

            var options = new FileLogSinkOptions
            {
                MaxFileBytes = 128,
                MaxArchiveFiles = 1,
                FlushBatchSize = 1
            };

            using (new FileLogSink(logPath, options))
            {
            }

            Assert.That(File.Exists(markerButInvalid), Is.True);
            Assert.That(File.Exists(legacyName), Is.True);
            Assert.That(File.Exists(ownedOld), Is.False);
            Assert.That(File.Exists(ownedNewer), Is.False);
            Assert.That(CountStrictOwnedArchives("game.cyclone-v2-", ".log"), Is.EqualTo(1));
        }

        [Test]
        public void Recovery_RecreatesParentDirectoryDeletedAfterWriterFailure()
        {
            string nestedDirectory = Path.Combine(_tempDirectory, "deleted-parent");
            string logPath = Path.Combine(nestedDirectory, "recovered.log");
            var options = new FileLogSinkOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1,
                RecoveryRetryIntervalMs = 0
            };

            using (var sink = new FileLogSink(logPath, options))
            {
                CloseWriterForRecoveryTest(sink);
                Directory.Delete(nestedDirectory, true);

                sink.PerformMaintenance();
                LogEvent message = CreateMessage(
                    LogSeverity.Info,
                    "recovered after parent deletion",
                    "Reliability",
                    "FileLogSinkReliabilityTests.cs");
                sink.Emit(message);
                LogEventPool.Return(message);

                Assert.That(sink.TryFlush(LogFlushMode.Buffered), Is.True);
                Assert.That(sink.Statistics.RecoveryCount, Is.EqualTo(1));
            }

            Assert.That(Directory.Exists(nestedDirectory), Is.True);
            Assert.That(File.ReadAllText(logPath), Does.Contain("recovered after parent deletion"));
        }

        [Test]
        public void Maintenance_RestoresActivePathUnlinkedWhileWriterRemainsOpen()
        {
            if (Path.DirectorySeparatorChar != '/')
            {
                Assert.Ignore("Open-file unlink semantics are validated on Unix-like filesystems.");
            }

            string logPath = Path.Combine(_tempDirectory, "unlinked.log");
            var options = new FileLogSinkOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1,
                RecoveryRetryIntervalMs = 0
            };

            using (var sink = new FileLogSink(logPath, options))
            {
                LogEvent before = CreateMessage(
                    LogSeverity.Info,
                    "before unlink",
                    "Reliability",
                    "FileLogSinkReliabilityTests.cs");
                sink.Emit(before);
                LogEventPool.Return(before);
                Assert.That(sink.TryFlush(LogFlushMode.Buffered), Is.True);

                try
                {
                    File.Delete(logPath);
                }
                catch (IOException exception)
                {
                    Assert.Ignore("The current filesystem does not permit unlinking an open file: " + exception.Message);
                }
                catch (UnauthorizedAccessException exception)
                {
                    Assert.Ignore("The current filesystem does not permit unlinking an open file: " + exception.Message);
                }

                Assert.That(File.Exists(logPath), Is.False);
                sink.PerformMaintenance();

                Assert.That(File.Exists(logPath), Is.True);
                Assert.That(sink.Statistics.RecoveryCount, Is.EqualTo(1));
                Assert.That(sink.Statistics.LastFailure, Is.EqualTo(FileLogSinkFailureKind.Recovery));

                LogEvent after = CreateMessage(
                    LogSeverity.Info,
                    "after unlink recovery",
                    "Reliability",
                    "FileLogSinkReliabilityTests.cs");
                sink.Emit(after);
                LogEventPool.Return(after);
                Assert.That(sink.TryFlush(LogFlushMode.Buffered), Is.True);
            }

            Assert.That(File.ReadAllText(logPath), Does.Contain("after unlink recovery"));
        }

        [Test]
        public void Maintenance_LargeCandidateSetIsBudgetedAndEventuallyConverges()
        {
            const int OwnedArchiveCount = 257;
            const int NonOwnedCandidateCount = 257;
            const int UnrelatedEntryCount = 257;
            const int MaxArchiveFiles = 3;
            const string Prefix = "bounded.cyclone-v2-";
            const string Extension = ".log";

            string logPath = Path.Combine(_tempDirectory, "bounded.log");
            for (int i = 1; i <= OwnedArchiveCount; i++)
            {
                File.WriteAllText(
                    Path.Combine(_tempDirectory, Prefix + i.ToString("D19", CultureInfo.InvariantCulture) + Extension),
                    "owned");
            }

            for (int i = 0; i < NonOwnedCandidateCount; i++)
            {
                File.WriteAllText(
                    Path.Combine(_tempDirectory, Prefix + "invalid-" + i.ToString("D4", CultureInfo.InvariantCulture) + Extension),
                    "not owned");
            }

            for (int i = 0; i < UnrelatedEntryCount; i++)
            {
                File.WriteAllText(
                    Path.Combine(
                        _tempDirectory,
                        "unrelated-" + i.ToString("D4", CultureInfo.InvariantCulture) + ".tmp"),
                    "unrelated");
            }

            var options = new FileLogSinkOptions
            {
                MaxFileBytes = 1024,
                MaxArchiveFiles = MaxArchiveFiles,
                FlushBatchSize = 1
            };

            using (var sink = new FileLogSink(logPath, options))
            {
                long inspectedBefore = sink.ArchiveEntriesInspected;
                int ownedBefore = CountStrictOwnedArchives(Prefix, Extension);
                sink.PerformMaintenance();
                long inspectedByOneCall = sink.ArchiveEntriesInspected - inspectedBefore;
                int deletedByOneCall = ownedBefore - CountStrictOwnedArchives(Prefix, Extension);

                Assert.That(inspectedByOneCall, Is.GreaterThan(0));
                Assert.That(inspectedByOneCall, Is.LessThanOrEqualTo(FileLogSink.ArchiveScanEntryBudget));
                Assert.That(deletedByOneCall, Is.InRange(0, FileLogSink.ArchiveDeletionBudget));

                for (int i = 0; i < 512; i++)
                {
                    sink.PerformMaintenance();
                }

                FileLogSinkStatistics statistics = sink.Statistics;
                Assert.That(statistics.ArchiveEntriesInspected, Is.GreaterThan(0));
                Assert.That(statistics.ArchiveFilesDeleted, Is.GreaterThan(0));
                Assert.That(statistics.ArchiveCleanupPending, Is.False);
            }

            Assert.That(CountStrictOwnedArchives(Prefix, Extension), Is.EqualTo(MaxArchiveFiles));
            Assert.That(
                File.Exists(Path.Combine(_tempDirectory, Prefix + (1).ToString("D19", CultureInfo.InvariantCulture) + Extension)),
                Is.False);
            for (int i = OwnedArchiveCount - MaxArchiveFiles + 1; i <= OwnedArchiveCount; i++)
            {
                Assert.That(
                    File.Exists(Path.Combine(_tempDirectory, Prefix + i.ToString("D19", CultureInfo.InvariantCulture) + Extension)),
                    Is.True,
                    "The newest strictly owned archives must be retained.");
            }

            for (int i = 0; i < NonOwnedCandidateCount; i++)
            {
                Assert.That(
                    File.Exists(Path.Combine(_tempDirectory, Prefix + "invalid-" + i.ToString("D4", CultureInfo.InvariantCulture) + Extension)),
                    Is.True,
                    "A matching but non-owned candidate must never be deleted.");
            }

            Assert.That(
                Directory.GetFiles(_tempDirectory, "unrelated-*.tmp").Length,
                Is.EqualTo(UnrelatedEntryCount));
        }

        [Test]
        public void ContinuousRotation_DoesNotRestartActiveArchiveScanAndEventuallyConverges()
        {
            int initialArchiveCount = (FileLogSink.ArchiveScanEntryBudget * 2) + 1;
            const int MaxArchiveFiles = 3;
            const string Prefix = "rotating.cyclone-v2-";
            const string Extension = ".log";
            string logPath = Path.Combine(_tempDirectory, "rotating.log");

            for (int i = 1; i <= initialArchiveCount; i++)
            {
                File.WriteAllText(
                    Path.Combine(
                        _tempDirectory,
                        Prefix + i.ToString("D19", CultureInfo.InvariantCulture) + Extension),
                    "owned");
            }

            var options = new FileLogSinkOptions
            {
                MaxFileBytes = 32,
                MaxArchiveFiles = MaxArchiveFiles,
                FlushBatchSize = 1
            };

            using (var sink = new FileLogSink(logPath, options))
            {
                for (int i = 0; i < 6; i++)
                {
                    LogEvent message = CreateMessage(
                        LogSeverity.Info,
                        "rotation-" + i + "-abcdefghijklmnopqrstuvwxyz",
                        "Reliability",
                        "FileLogSinkReliabilityTests.cs");
                    sink.Emit(message);
                    LogEventPool.Return(message);
                }

                FileLogSinkStatistics afterRotation = sink.Statistics;
                Assert.That(afterRotation.RotationCount, Is.GreaterThanOrEqualTo(3));
                Assert.That(
                    CountStrictOwnedArchives(Prefix, Extension),
                    Is.LessThan(initialArchiveCount + afterRotation.RotationCount),
                    "At least one completed scan must clean archives while rotations continue.");

                for (int i = 0; i < 512; i++)
                {
                    sink.PerformMaintenance();
                }
            }

            Assert.That(CountStrictOwnedArchives(Prefix, Extension), Is.EqualTo(MaxArchiveFiles));
        }

        [Test]
        public void LogAndFlush_RotateBeforeProjectedUtf8LimitAndExposeStatistics()
        {
            string logPath = Path.Combine(_tempDirectory, "bounded.log");
            var options = new FileLogSinkOptions
            {
                MaxFileBytes = 220,
                MaxArchiveFiles = 2,
                FlushBatchSize = 1024,
                FlushIntervalMs = 60000
            };

            FileLogSinkStatistics statistics;
            using (var logger = new FileLogSink(logPath, options))
            {
                for (int i = 0; i < 8; i++)
                {
                    LogEvent message = CreateMessage(
                        LogSeverity.Info,
                        "entry-" + i + "-\u4E2D\u6587-abcdefghijklmnopqrstuvwxyz",
                        "Reliability\r\nInjected",
                        "C:\\private\\source\\FileLogSinkReliabilityTests.cs");
                    logger.Emit(message);
                    LogEventPool.Return(message);
                }

                Assert.That(logger.TryFlush(LogFlushMode.Buffered), Is.True);
                statistics = logger.Statistics;
                Assert.That(logger.LogFilePath, Is.EqualTo(Path.GetFullPath(logPath)));
                Assert.That(logger.Health, Is.Not.EqualTo(FileLogSinkHealth.Faulted));
            }

            Assert.That(statistics.AttemptedEntries, Is.EqualTo(8));
            Assert.That(statistics.WrittenEntries, Is.EqualTo(8));
            Assert.That(statistics.DroppedEntries, Is.Zero);
            Assert.That(statistics.RotationCount, Is.GreaterThan(0));
            Assert.That(new FileInfo(logPath).Length, Is.LessThanOrEqualTo(options.MaxFileBytes));
            Assert.That(Directory.GetFiles(_tempDirectory, "bounded.cyclone-v2-*.log").Length, Is.LessThanOrEqualTo(2));

            string activeContent = File.ReadAllText(logPath);
            Assert.That(activeContent, Does.Not.Contain("C:/private/source"));
            Assert.That(activeContent, Does.Contain("FileLogSinkReliabilityTests.cs"));
            Assert.That(activeContent, Does.Contain("Reliability\\r\\nInjected"));
            Assert.That(activeContent, Does.Not.Contain("Reliability\r\nInjected"));
        }

        [Test]
        public void IdleMaintenance_FlushesWithoutAnotherLogEntry()
        {
            string logPath = Path.Combine(_tempDirectory, "idle-flush.log");
            var options = new FileLogSinkOptions
            {
                MaintenanceMode = FileMaintenanceMode.None,
                FlushBatchSize = 1024,
                FlushIntervalMs = 10
            };

            using (var logger = new FileLogSink(logPath, options))
            {
                LogEvent message = CreateMessage(LogSeverity.Info, "idle flush", "Reliability", "FileLogSinkReliabilityTests.cs");
                logger.Emit(message);
                LogEventPool.Return(message);
                Thread.Sleep(25);
                logger.PerformMaintenance();

                Assert.That(ReadAllTextShared(logPath), Does.Contain("idle flush"));
            }
        }

        private static string ReadAllTextShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }

        private static void CloseWriterForRecoveryTest(FileLogSink sink)
        {
            MethodInfo method = typeof(FileLogSink).GetMethod(
                "CloseWriterUnderLock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object[] arguments = { false, null };
            bool closed = (bool)method.Invoke(sink, arguments);
            Assert.That(closed, Is.True);
            Assert.That(arguments[1], Is.Null);
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

        private static LogEvent CreateMessage(LogSeverity level, string text, string category, string sourcePath)
        {
            LogEvent message = LogEventPool.Get();
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
