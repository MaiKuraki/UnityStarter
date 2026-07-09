using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cysharp.Threading.Tasks;
using NUnit.Framework;

using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class PatchTransactionServiceTests
    {
        private const string TARGET_MANIFEST_VERSION = "manifest-2026-07-09";
        private const string ROLLBACK_MANIFEST_VERSION = "manifest-2026-07-08";

        [Test]
        public async Task RunAsync_With_Trust_Manifest_Verifies_After_Download()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);

                var package = new RecordingAssetPackage
                {
                    NameValue = "Main",
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = new TestDownloader(totalDownloadCount: 1, totalDownloadBytes: 15L)
                };
                var service = new AssetPatchService(package);

                PatchRunOptions options = CreateOptions(
                    autoDownload: true,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult result = await service.RunAsync(options);

                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual("Main", result.PackageName);
                Assert.AreEqual(TARGET_MANIFEST_VERSION, result.PackageVersion);
                Assert.IsTrue(result.ContentTrustEnabled);
                Assert.AreEqual(0, result.TrustFailureCount);
                Assert.AreEqual(1, package.CreateDownloaderForAllCallCount);
                Assert.AreEqual(10, package.LastDownloadingMaxNumber);
                Assert.AreEqual(3, package.LastFailedTryAgain);
                Assert.AreEqual(1, package.UpdatedPackageVersions.Count);
                Assert.AreEqual(TARGET_MANIFEST_VERSION, package.UpdatedPackageVersions[0]);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RunAsync_Without_Auto_Download_Returns_Pending_Then_DownloadAsync_Completes()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);

                var downloader = new TestDownloader(totalDownloadCount: 1, totalDownloadBytes: 15L);
                var package = new RecordingAssetPackage
                {
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = downloader
                };
                var service = new AssetPatchService(package);

                PatchRunOptions options = CreateOptions(
                    autoDownload: false,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult pending = await service.RunAsync(options);

                Assert.IsTrue(pending.PendingDownload);
                Assert.IsFalse(downloader.BeginCalled);

                PatchRunResult completed = await service.DownloadAsync();

                Assert.IsTrue(completed.Succeeded);
                Assert.IsTrue(downloader.BeginCalled);
                Assert.AreEqual(0, completed.TrustFailureCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RunAsync_When_RequestPackageVersion_Fails_Returns_Failed_Result()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);

                var package = new RecordingAssetPackage
                {
                    RequestPackageVersionException = new InvalidOperationException("version endpoint unavailable"),
                    DownloaderForAll = new TestDownloader(totalDownloadCount: 1, totalDownloadBytes: 15L)
                };
                var service = new AssetPatchService(package);

                PatchRunOptions options = CreateOptions(
                    autoDownload: true,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult result = await service.RunAsync(options);

                Assert.IsTrue(result.Failed);
                Assert.AreEqual(PatchRunStatus.Failed, result.Status);
                Assert.AreEqual(PatchFailureKind.PackageVersionRequestFailed, result.FailureKind);
                Assert.IsTrue(result.PackageVersionRequestFailed);
                Assert.IsNull(result.PackageVersion);
                Assert.AreEqual(0, package.UpdatedPackageVersions.Count);
                Assert.AreEqual(0, package.CreateDownloaderForAllCallCount);
                Assert.AreEqual("Failed to request package version: version endpoint unavailable", result.Error);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RunAsync_When_UpdatePackageManifest_Returns_False_Returns_Failed_Result()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);

                var package = new RecordingAssetPackage
                {
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    UpdatePackageManifestResult = false,
                    DownloaderForAll = new TestDownloader(totalDownloadCount: 1, totalDownloadBytes: 15L)
                };
                var service = new AssetPatchService(package);

                PatchRunOptions options = CreateOptions(
                    autoDownload: true,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult result = await service.RunAsync(options);

                Assert.IsTrue(result.Failed);
                Assert.AreEqual(PatchRunStatus.Failed, result.Status);
                Assert.AreEqual(PatchFailureKind.ManifestUpdateFailed, result.FailureKind);
                Assert.IsTrue(result.ManifestUpdateFailed);
                Assert.AreEqual(TARGET_MANIFEST_VERSION, result.PackageVersion);
                Assert.AreEqual(1, package.UpdatedPackageVersions.Count);
                Assert.AreEqual(0, package.CreateDownloaderForAllCallCount);
                Assert.AreEqual("Failed to update package manifest", result.Error);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RunAsync_When_Downloader_Creation_Fails_Returns_Failed_Result()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);

                var package = new RecordingAssetPackage
                {
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    CreateDownloaderForAllException = new InvalidOperationException("provider not initialized")
                };
                var service = new AssetPatchService(package);

                PatchRunOptions options = CreateOptions(
                    autoDownload: true,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult result = await service.RunAsync(options);

                Assert.IsTrue(result.Failed);
                Assert.AreEqual(PatchRunStatus.Failed, result.Status);
                Assert.AreEqual(PatchFailureKind.DownloaderCreationFailed, result.FailureKind);
                Assert.IsTrue(result.DownloaderCreationFailed);
                Assert.AreEqual(TARGET_MANIFEST_VERSION, result.PackageVersion);
                Assert.AreEqual(1, package.UpdatedPackageVersions.Count);
                Assert.AreEqual(1, package.CreateDownloaderForAllCallCount);
                Assert.AreEqual("Patch downloader was not created: provider not initialized", result.Error);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void RunAsync_When_Trust_Fails_Rolls_Back_Manifest_And_Clears_Unused_Cache()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                WriteUtf8Bytes(filePath, "tampered-content");

                var failures = new List<ContentTrustVerificationResult>(4);
                var package = new RecordingAssetPackage
                {
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = new TestDownloader(totalDownloadCount: 1, totalDownloadBytes: 16L)
                };
                var service = new AssetPatchService(package);
                PatchRunOptions options = CreateOptions(
                    autoDownload: true,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        new string('0', 64)),
                    failurePolicy: PatchTrustFailurePolicy.RollbackManifestThenFail,
                    rollbackVersion: ROLLBACK_MANIFEST_VERSION,
                    clearUnusedAfterRollback: true,
                    failures: failures);

                PatchTrustVerificationException exception = Assert.ThrowsAsync<PatchTrustVerificationException>(
                    async () => await service.RunAsync(options));

                Assert.AreEqual(TARGET_MANIFEST_VERSION, exception.PackageVersion);
                Assert.AreEqual(1, exception.FailureCount);
                Assert.AreEqual(ContentTrustFailure.HashMismatch, failures[0].Failure);
                Assert.AreEqual(2, package.UpdatedPackageVersions.Count);
                Assert.AreEqual(TARGET_MANIFEST_VERSION, package.UpdatedPackageVersions[0]);
                Assert.AreEqual(ROLLBACK_MANIFEST_VERSION, package.UpdatedPackageVersions[1]);
                Assert.AreEqual(1, package.ClearCacheFilesCallCount);
                Assert.AreEqual(ClearCacheMode.Unused, package.LastClearCacheMode);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RunAsync_When_Trust_Fails_Repairs_Locations_And_Reverifies()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "tampered-content", Encoding.UTF8);

                var failures = new List<ContentTrustVerificationResult>(4);
                var package = new RecordingAssetPackage
                {
                    NameValue = "Main",
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = new TestDownloader(totalDownloadCount: 1, totalDownloadBytes: 16L),
                    DownloaderForLocations = new TestDownloader(
                        totalDownloadCount: 1,
                        totalDownloadBytes: 15L,
                        onBegin: () => WriteUtf8Bytes(filePath, "trusted-content"))
                };
                var service = new AssetPatchService(package);
                PatchRunOptions options = CreateOptions(
                    autoDownload: true,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        Encoding.UTF8.GetByteCount("trusted-content"),
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256HexText("trusted-content")),
                    failurePolicy: PatchTrustFailurePolicy.RepairLocationsThenReverify,
                    failures: failures);

                PatchRunResult result = await service.RunAsync(options);

                Assert.IsTrue(result.Succeeded, result.Error);
                Assert.AreEqual(0, result.TrustFailureCount);
                Assert.AreEqual(1, package.CreateDownloaderForLocationsCallCount);
                Assert.AreEqual("bundle.bin", package.LastLocations[0]);
                Assert.AreEqual(1, package.ClearCacheFilesCallCount);
                Assert.AreEqual(ClearCacheMode.Unused, package.LastClearCacheMode);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task DownloadAsync_When_Cancelled_Cancels_Downloader_And_Returns_Cancelled_Result()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);

                var downloader = new TestDownloader(
                    totalDownloadCount: 1,
                    totalDownloadBytes: 15L,
                    completeOnBegin: false);
                var package = new RecordingAssetPackage
                {
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = downloader
                };
                var service = new AssetPatchService(package);

                PatchRunOptions options = CreateOptions(
                    autoDownload: false,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult pending = await service.RunAsync(options);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                PatchRunResult result = await service.DownloadAsync(cancellation.Token);

                Assert.IsTrue(pending.PendingDownload);
                Assert.IsTrue(result.Cancelled);
                Assert.AreEqual(PatchRunStatus.Cancelled, result.Status);
                Assert.AreEqual(PatchFailureKind.Cancelled, result.FailureKind);
                Assert.IsTrue(result.ExplicitlyCancelled);
                Assert.AreEqual(TARGET_MANIFEST_VERSION, result.PackageVersion);
                Assert.AreEqual(1, result.TotalDownloadCount);
                Assert.AreEqual(15L, result.TotalDownloadBytes);
                Assert.IsTrue(downloader.BeginCalled);
                Assert.IsTrue(downloader.CancelCalled);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task Cancel_Before_DownloadAsync_Returns_Cancelled_Result_Without_Begin()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);

                var downloader = new TestDownloader(
                    totalDownloadCount: 1,
                    totalDownloadBytes: 15L,
                    completeOnBegin: false);
                var package = new RecordingAssetPackage
                {
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = downloader
                };
                var service = new AssetPatchService(package);

                PatchRunOptions options = CreateOptions(
                    autoDownload: false,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult pending = await service.RunAsync(options);
                service.Cancel();

                PatchRunResult result = await service.DownloadAsync();

                Assert.IsTrue(pending.PendingDownload);
                Assert.IsTrue(result.Cancelled);
                Assert.AreEqual(PatchRunStatus.Cancelled, result.Status);
                Assert.AreEqual(PatchFailureKind.Cancelled, result.FailureKind);
                Assert.IsTrue(result.ExplicitlyCancelled);
                Assert.AreEqual(1, result.TotalDownloadCount);
                Assert.AreEqual(15L, result.TotalDownloadBytes);
                Assert.IsFalse(downloader.BeginCalled);
                Assert.IsTrue(downloader.CancelCalled);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task Dispose_With_Pending_Download_Cancels_Downloader()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);

                var downloader = new TestDownloader(
                    totalDownloadCount: 1,
                    totalDownloadBytes: 15L,
                    completeOnBegin: false);
                var package = new RecordingAssetPackage
                {
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = downloader
                };
                var service = new AssetPatchService(package);

                PatchRunOptions options = CreateOptions(
                    autoDownload: false,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult pending = await service.RunAsync(options);
                service.Dispose();

                Assert.IsTrue(pending.PendingDownload);
                Assert.IsFalse(downloader.BeginCalled);
                Assert.IsTrue(downloader.CancelCalled);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RunAsync_With_Journal_Writes_Pending_Download_Record_And_Recovery_Action()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var journalStore = new FileAssetPatchJournalStore(journalPath);

                var downloader = new TestDownloader(totalDownloadCount: 1, totalDownloadBytes: 15L);
                var package = new RecordingAssetPackage
                {
                    NameValue = "Main",
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = downloader
                };
                var service = new AssetPatchService(package, new AssetPatchJournalOptions(journalStore));

                PatchRunOptions options = CreateOptions(
                    autoDownload: false,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult pending = await service.RunAsync(options);
                bool read = journalStore.TryRead(out AssetPatchJournalRecord record, out string error);
                AssetPatchRecoveryRecommendation recovery = AssetPatchJournalRecovery.Analyze(in record);

                Assert.IsTrue(pending.PendingDownload);
                Assert.IsTrue(read, error);
                Assert.AreEqual("Main", record.PackageName);
                Assert.AreEqual(TARGET_MANIFEST_VERSION, record.PackageVersion);
                Assert.AreEqual(PatchWorkflowState.WaitingForDownload, record.Stage);
                Assert.AreEqual(AssetPatchJournalStatus.PendingDownload, record.Status);
                Assert.AreEqual(1, record.TotalDownloadCount);
                Assert.AreEqual(15L, record.TotalDownloadBytes);
                Assert.AreEqual(AssetPatchRecoveryAction.ResumeOrRestartDownload, recovery.Action);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task DownloadAsync_With_Journal_Writes_Succeeded_Record()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var journalStore = new FileAssetPatchJournalStore(journalPath);

                var package = new RecordingAssetPackage
                {
                    NameValue = "Main",
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = new TestDownloader(totalDownloadCount: 1, totalDownloadBytes: 15L)
                };
                var service = new AssetPatchService(package, new AssetPatchJournalOptions(journalStore));

                PatchRunOptions options = CreateOptions(
                    autoDownload: false,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                await service.RunAsync(options);
                PatchRunResult result = await service.DownloadAsync();
                bool read = journalStore.TryRead(out AssetPatchJournalRecord record, out string error);

                Assert.IsTrue(result.Succeeded);
                Assert.IsTrue(read, error);
                Assert.AreEqual(PatchWorkflowState.Done, record.Stage);
                Assert.AreEqual(AssetPatchJournalStatus.Succeeded, record.Status);
                Assert.AreEqual(0, record.TrustFailureCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task DownloadAsync_When_Downloader_Fails_Returns_Failed_Result_And_Keeps_Recovery_Journal()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var journalStore = new FileAssetPatchJournalStore(journalPath);

                var downloader = new TestDownloader(
                    totalDownloadCount: 2,
                    totalDownloadBytes: 4096L,
                    succeed: false,
                    error: "network timeout");
                var package = new RecordingAssetPackage
                {
                    NameValue = "Main",
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = downloader
                };
                var service = new AssetPatchService(package, new AssetPatchJournalOptions(journalStore));

                PatchRunOptions options = CreateOptions(
                    autoDownload: false,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                PatchRunResult pending = await service.RunAsync(options);
                PatchRunResult result = await service.DownloadAsync();
                bool read = journalStore.TryRead(out AssetPatchJournalRecord record, out string error);
                AssetPatchRecoveryRecommendation recovery = AssetPatchJournalRecovery.Analyze(in record);

                Assert.IsTrue(pending.PendingDownload);
                Assert.IsTrue(result.Failed);
                Assert.AreEqual(PatchRunStatus.Failed, result.Status);
                Assert.AreEqual(PatchFailureKind.ProviderDownloadFailed, result.FailureKind);
                Assert.IsTrue(result.ProviderDownloadFailed);
                Assert.AreEqual(TARGET_MANIFEST_VERSION, result.PackageVersion);
                Assert.AreEqual(2, result.TotalDownloadCount);
                Assert.AreEqual(4096L, result.TotalDownloadBytes);
                Assert.AreEqual("Download failed: network timeout", result.Error);
                Assert.IsTrue(read, error);
                Assert.AreEqual(PatchWorkflowState.Failed, record.Stage);
                Assert.AreEqual(AssetPatchJournalStatus.Failed, record.Status);
                Assert.AreEqual("Download failed: network timeout", record.Error);
                Assert.AreEqual(AssetPatchRecoveryAction.InspectTerminalFailure, recovery.Action);
                Assert.IsTrue(recovery.RequiresProviderReconciliation);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task Cancel_With_Journal_Writes_Cancelled_Record()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                File.WriteAllText(filePath, "trusted-content", Encoding.UTF8);
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var journalStore = new FileAssetPatchJournalStore(journalPath);

                var downloader = new TestDownloader(
                    totalDownloadCount: 1,
                    totalDownloadBytes: 15L,
                    completeOnBegin: false);
                var package = new RecordingAssetPackage
                {
                    NameValue = "Main",
                    RequestPackageVersionValue = TARGET_MANIFEST_VERSION,
                    DownloaderForAll = downloader
                };
                var service = new AssetPatchService(package, new AssetPatchJournalOptions(journalStore));

                PatchRunOptions options = CreateOptions(
                    autoDownload: false,
                    directory,
                    new ContentTrustFileEntry(
                        "bundle.bin",
                        new FileInfo(filePath).Length,
                        ContentTrustHashAlgorithm.Sha256,
                        ComputeSha256Hex(filePath)));

                await service.RunAsync(options);
                service.Cancel();
                PatchRunResult result = await service.DownloadAsync();
                bool read = journalStore.TryRead(out AssetPatchJournalRecord record, out string error);

                Assert.IsTrue(result.Cancelled);
                Assert.IsTrue(read, error);
                Assert.AreEqual(PatchWorkflowState.Cancelled, record.Stage);
                Assert.AreEqual(AssetPatchJournalStatus.Cancelled, record.Status);
                Assert.IsTrue(downloader.CancelCalled);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static PatchRunOptions CreateOptions(
            bool autoDownload,
            string rootDirectory,
            ContentTrustFileEntry entry,
            PatchTrustFailurePolicy failurePolicy = PatchTrustFailurePolicy.FailFast,
            string rollbackVersion = null,
            bool clearUnusedAfterRollback = false,
            List<ContentTrustVerificationResult> failures = null)
        {
            var manifest = new ContentTrustManifest(
                TARGET_MANIFEST_VERSION,
                new[] { entry },
                rollbackVersion: rollbackVersion);

            var trustOptions = new PatchContentTrustOptions(
                rootDirectory,
                manifest,
                failurePolicy: failurePolicy,
                clearUnusedCacheAfterRollback: clearUnusedAfterRollback,
                failureBuffer: failures);

            return new PatchRunOptions(
                autoDownload,
                PatchDownloadOptions.Default,
                trustOptions,
                appendTimeTicks: false);
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.PatchTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(File.ReadAllBytes(filePath));
            return ToHex(bytes);
        }

        private static string ComputeSha256HexText(string value)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            return ToHex(bytes);
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private static void WriteUtf8Bytes(string filePath, string value)
        {
            File.WriteAllBytes(filePath, Encoding.UTF8.GetBytes(value));
        }

        private sealed class TestDownloader : IDownloader
        {
            private readonly Action _onBegin;
            private readonly bool _completeOnBegin;
            private readonly bool _succeed;

            public TestDownloader(
                int totalDownloadCount,
                long totalDownloadBytes,
                Action onBegin = null,
                bool completeOnBegin = true,
                bool succeed = true,
                string error = null)
            {
                TotalDownloadCount = totalDownloadCount;
                TotalDownloadBytes = totalDownloadBytes;
                _onBegin = onBegin;
                _completeOnBegin = completeOnBegin;
                _succeed = succeed;
                Error = error ?? string.Empty;
            }

            public bool BeginCalled { get; private set; }
            public bool CancelCalled { get; private set; }
            public bool IsDone => CancelCalled || (BeginCalled && _completeOnBegin);
            public bool Succeed => BeginCalled && !CancelCalled && _succeed && _completeOnBegin;
            public float Progress => BeginCalled ? 1f : 0f;
            public int TotalDownloadCount { get; }
            public int CurrentDownloadCount => BeginCalled ? TotalDownloadCount : 0;
            public long TotalDownloadBytes { get; }
            public long CurrentDownloadBytes => BeginCalled ? TotalDownloadBytes : 0L;
            public string Error { get; }

            public void Begin()
            {
                BeginCalled = true;
                _onBegin?.Invoke();
            }

            public UniTask StartAsync(CancellationToken cancellationToken = default)
            {
                Begin();
                return UniTask.CompletedTask;
            }

            public void Pause()
            {
            }

            public void Resume()
            {
            }

            public void Cancel()
            {
                CancelCalled = true;
            }

            public void Combine(IDownloader other)
            {
            }
        }
    }
}
