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
    public sealed class AssetRepairServiceTests
    {
        [Test]
        public void Planner_Builds_Unique_Location_Plan_And_Counts_Unrepairable_Failures()
        {
            var failures = new[]
            {
                ContentTrustVerificationResult.Failed(ContentTrustFailure.HashMismatch, "bundle-a"),
                ContentTrustVerificationResult.Failed(ContentTrustFailure.MissingFile, "bundle-a"),
                ContentTrustVerificationResult.Failed(ContentTrustFailure.SignatureRejected, null),
                ContentTrustVerificationResult.Passed("bundle-b")
            };
            var manifest = new ContentTrustManifest("manifest", Array.Empty<ContentTrustFileEntry>());

            AssetRepairPlan plan = AssetRepairPlanner.Shared.CreatePlan("Main", manifest, failures);

            Assert.AreEqual("Main", plan.PackageName);
            Assert.AreEqual("manifest", plan.PackageVersion);
            Assert.AreEqual(3, plan.TotalFailureCount);
            Assert.AreEqual(2, plan.RepairableFailureCount);
            Assert.AreEqual(1, plan.UnrepairableFailureCount);
            Assert.AreEqual(1, plan.RepairLocationCount);
            Assert.AreEqual("bundle-a", plan.RepairLocations[0]);
        }

        [Test]
        public async Task RepairAsync_Downloads_Corrupted_Locations_And_Reverifies_Content()
        {
            string directory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(directory, "bundle.bin");
                WriteUtf8Bytes(filePath, "tampered-content");

                var manifest = new ContentTrustManifest(
                    "manifest-current",
                    new[]
                    {
                        new ContentTrustFileEntry(
                            "bundle.bin",
                            Encoding.UTF8.GetByteCount("trusted-content"),
                            ContentTrustHashAlgorithm.Sha256,
                            ComputeSha256Hex("trusted-content"))
                    });

                var failures = new List<ContentTrustVerificationResult>(4);
                int failureCount = ContentTrustVerifier.Shared.VerifyManifestFiles(directory, manifest, failures);
                Assert.AreEqual(1, failureCount);

                var package = new RecordingAssetPackage
                {
                    NameValue = "Main",
                    DownloaderForLocations = new RepairingDownloader(
                        totalDownloadCount: 1,
                        totalDownloadBytes: 15L,
                        onBegin: () => WriteUtf8Bytes(filePath, "trusted-content"))
                };
                var service = new AssetRepairService(package);
                var trustOptions = new PatchContentTrustOptions(directory, manifest, failureBuffer: failures);
                var repairOptions = new AssetRepairOptions(PatchDownloadOptions.Default, trustOptions);

                AssetRepairRunResult result = await service.RepairAsync(manifest, failures, repairOptions);

                Assert.IsTrue(result.Succeeded, result.Error);
                Assert.AreEqual(0, result.PostRepairTrustFailureCount);
                Assert.AreEqual(1, result.RepairLocationCount);
                Assert.AreEqual(1, package.ClearCacheFilesCallCount);
                Assert.AreEqual(ClearCacheMode.Unused, package.LastClearCacheMode);
                Assert.AreEqual(1, package.CreateDownloaderForLocationsCallCount);
                Assert.IsTrue(package.LastRecursiveDownload);
                Assert.AreEqual("bundle.bin", package.LastLocations[0]);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.RepairTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string ComputeSha256Hex(string value)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
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

        private sealed class RepairingDownloader : IDownloader
        {
            private readonly Action _onBegin;

            public RepairingDownloader(int totalDownloadCount, long totalDownloadBytes, Action onBegin)
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
