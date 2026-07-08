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

            public TestDownloader(int totalDownloadCount, long totalDownloadBytes, Action onBegin = null)
            {
                TotalDownloadCount = totalDownloadCount;
                TotalDownloadBytes = totalDownloadBytes;
                _onBegin = onBegin;
            }

            public bool BeginCalled { get; private set; }
            public bool IsDone => BeginCalled;
            public bool Succeed => BeginCalled;
            public float Progress => BeginCalled ? 1f : 0f;
            public int TotalDownloadCount { get; }
            public int CurrentDownloadCount => BeginCalled ? TotalDownloadCount : 0;
            public long TotalDownloadBytes { get; }
            public long CurrentDownloadBytes => BeginCalled ? TotalDownloadBytes : 0L;
            public string Error => string.Empty;

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
            }

            public void Combine(IDownloader other)
            {
            }
        }
    }
}
