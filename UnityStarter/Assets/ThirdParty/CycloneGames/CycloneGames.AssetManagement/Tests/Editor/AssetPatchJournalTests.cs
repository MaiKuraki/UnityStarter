using System;
using System.IO;
using System.Threading.Tasks;

using Cysharp.Threading.Tasks;
using NUnit.Framework;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetPatchJournalTests
    {
        [Test]
        public void Codec_RoundTrips_Record_With_Escaped_Fields()
        {
            var record = new AssetPatchJournalRecord(
                sequence: 7L,
                packageName: "Main\"Package",
                packageVersion: "manifest-2026-07-09",
                rollbackVersion: "manifest-2026-07-08",
                stage: PatchWorkflowState.Download,
                status: AssetPatchJournalStatus.InProgress,
                totalDownloadCount: 3,
                totalDownloadBytes: 4096L,
                contentTrustEnabled: true,
                trustFailureCount: 1,
                contentTrustManifestFingerprint: 123456789UL,
                startedUtcTicks: 100L,
                updatedUtcTicks: 200L,
                error: "network\ninterrupted");

            string json = AssetPatchJournalCodec.ToJson(in record);
            bool parsed = AssetPatchJournalCodec.TryFromJson(json, out AssetPatchJournalRecord restored, out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(record.SchemaVersion, restored.SchemaVersion);
            Assert.AreEqual(record.Sequence, restored.Sequence);
            Assert.AreEqual(record.PackageName, restored.PackageName);
            Assert.AreEqual(record.PackageVersion, restored.PackageVersion);
            Assert.AreEqual(record.RollbackVersion, restored.RollbackVersion);
            Assert.AreEqual(record.Stage, restored.Stage);
            Assert.AreEqual(record.Status, restored.Status);
            Assert.AreEqual(record.TotalDownloadCount, restored.TotalDownloadCount);
            Assert.AreEqual(record.TotalDownloadBytes, restored.TotalDownloadBytes);
            Assert.AreEqual(record.ContentTrustEnabled, restored.ContentTrustEnabled);
            Assert.AreEqual(record.TrustFailureCount, restored.TrustFailureCount);
            Assert.AreEqual(record.ContentTrustManifestFingerprint, restored.ContentTrustManifestFingerprint);
            Assert.AreEqual(record.StartedUtcTicks, restored.StartedUtcTicks);
            Assert.AreEqual(record.UpdatedUtcTicks, restored.UpdatedUtcTicks);
            Assert.AreEqual(record.Error, restored.Error);
        }

        [Test]
        public void FileStore_WriteReadClear_Persists_Record()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.JournalTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var store = new FileAssetPatchJournalStore(journalPath);
                var record = new AssetPatchJournalRecord(
                    sequence: 3L,
                    packageName: "Main",
                    packageVersion: "manifest-current",
                    rollbackVersion: null,
                    stage: PatchWorkflowState.WaitingForDownload,
                    status: AssetPatchJournalStatus.PendingDownload,
                    totalDownloadCount: 2,
                    totalDownloadBytes: 1024L,
                    contentTrustEnabled: false,
                    trustFailureCount: 0,
                    contentTrustManifestFingerprint: 0UL,
                    startedUtcTicks: 10L,
                    updatedUtcTicks: 20L,
                    error: null);

                store.Write(in record);
                bool read = store.TryRead(out AssetPatchJournalRecord restored, out string error);

                Assert.IsTrue(read, error);
                Assert.AreEqual(record.PackageName, restored.PackageName);
                Assert.AreEqual(record.Status, restored.Status);

                store.Clear();
                Assert.IsFalse(File.Exists(journalPath));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void Recovery_Analyzes_Interrupted_Download_As_ResumeOrRestart()
        {
            var record = new AssetPatchJournalRecord(
                sequence: 5L,
                packageName: "Main",
                packageVersion: "manifest-current",
                rollbackVersion: null,
                stage: PatchWorkflowState.Download,
                status: AssetPatchJournalStatus.InProgress,
                totalDownloadCount: 10,
                totalDownloadBytes: 2048L,
                contentTrustEnabled: true,
                trustFailureCount: 0,
                contentTrustManifestFingerprint: 1UL,
                startedUtcTicks: 10L,
                updatedUtcTicks: 20L,
                error: null);

            AssetPatchRecoveryRecommendation recommendation = AssetPatchJournalRecovery.Analyze(in record);

            Assert.AreEqual(AssetPatchRecoveryAction.ResumeOrRestartDownload, recommendation.Action);
            Assert.IsTrue(recommendation.HasActiveWork);
            Assert.IsTrue(recommendation.RequiresProviderReconciliation);
        }

        [Test]
        public async Task RecoveryService_Without_Journal_Returns_NoJournal()
        {
            string directory = CreateTempDirectory();
            try
            {
                var package = new RecordingAssetPackage { NameValue = "Main" };
                var store = new FileAssetPatchJournalStore(Path.Combine(directory, "missing.json"));
                var service = new AssetPatchRecoveryService(package, store);

                AssetPatchRecoveryResult result = await service.RecoverAsync();

                Assert.AreEqual(AssetPatchRecoveryStatus.NoJournal, result.Status);
                Assert.IsFalse(result.JournalRead);
                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual(0, package.UpdatedPackageVersions.Count);
                Assert.AreEqual(0, package.ClearCacheFilesCallCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RecoveryService_With_Succeeded_Journal_Clears_When_Policy_Allows()
        {
            string directory = CreateTempDirectory();
            try
            {
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var store = new FileAssetPatchJournalStore(journalPath);
                var record = CreateRecord(PatchWorkflowState.Done, AssetPatchJournalStatus.Succeeded);
                store.Write(in record);
                var package = new RecordingAssetPackage { NameValue = "Main" };
                var service = new AssetPatchRecoveryService(package, store, AssetPatchRecoveryPolicy.ClearTerminalJournals);

                AssetPatchRecoveryResult result = await service.RecoverAsync();

                Assert.AreEqual(AssetPatchRecoveryStatus.NoActionRequired, result.Status);
                Assert.IsTrue(result.JournalCleared);
                Assert.IsFalse(File.Exists(journalPath));
                Assert.AreEqual(0, package.UpdatedPackageVersions.Count);
                Assert.AreEqual(0, package.ClearCacheFilesCallCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RecoveryService_With_Failed_Journal_Rolls_Back_And_Clears_Cache_When_Policy_Allows()
        {
            string directory = CreateTempDirectory();
            try
            {
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var store = new FileAssetPatchJournalStore(journalPath);
                var record = CreateRecord(
                    PatchWorkflowState.Failed,
                    AssetPatchJournalStatus.Failed,
                    rollbackVersion: "manifest-rollback",
                    error: "trust failed");
                store.Write(in record);
                var package = new RecordingAssetPackage { NameValue = "Main" };
                var policy = new AssetPatchRecoveryPolicy(
                    rollbackFailedJournalWithVersion: true,
                    clearUnusedCacheAfterRollback: true,
                    clearJournalAfterSuccessfulRecovery: true);
                var service = new AssetPatchRecoveryService(package, store, policy);

                AssetPatchRecoveryResult result = await service.RecoverAsync();

                Assert.AreEqual(AssetPatchRecoveryStatus.RollbackCompleted, result.Status);
                Assert.AreEqual(AssetPatchRecoveryAction.RollbackManifest, result.Action);
                Assert.IsTrue(result.ManifestRolledBack);
                Assert.IsTrue(result.CacheCleared);
                Assert.IsTrue(result.JournalCleared);
                Assert.AreEqual(1, package.UpdatedPackageVersions.Count);
                Assert.AreEqual("manifest-rollback", package.UpdatedPackageVersions[0]);
                Assert.AreEqual(1, package.ClearCacheFilesCallCount);
                Assert.AreEqual(ClearCacheMode.Unused, package.LastClearCacheMode);
                Assert.IsFalse(File.Exists(journalPath));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RecoveryService_With_Interrupted_Download_DefaultPolicy_Requires_Owner_Action()
        {
            string directory = CreateTempDirectory();
            try
            {
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var store = new FileAssetPatchJournalStore(journalPath);
                var record = CreateRecord(PatchWorkflowState.Download, AssetPatchJournalStatus.InProgress);
                store.Write(in record);
                var package = new RecordingAssetPackage { NameValue = "Main" };
                var service = new AssetPatchRecoveryService(package, store);

                AssetPatchRecoveryResult result = await service.RecoverAsync();

                Assert.AreEqual(AssetPatchRecoveryStatus.RequiresOwnerAction, result.Status);
                Assert.AreEqual(AssetPatchRecoveryAction.ResumeOrRestartDownload, result.Action);
                Assert.IsFalse(result.JournalCleared);
                Assert.AreEqual(0, package.UpdatedPackageVersions.Count);
                Assert.AreEqual(0, package.ClearCacheFilesCallCount);
                Assert.IsTrue(File.Exists(journalPath));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RecoveryService_With_Provider_Reconciler_Returns_Provider_Result()
        {
            string directory = CreateTempDirectory();
            try
            {
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var store = new FileAssetPatchJournalStore(journalPath);
                var record = CreateRecord(PatchWorkflowState.Download, AssetPatchJournalStatus.InProgress);
                store.Write(in record);
                var package = new RecordingAssetPackage { NameValue = "Main" };
                AssetPatchProviderReconciliationCapabilities capabilities = CreateProviderCapabilities();
                var reconciler = new RecordingPatchProviderReconciler(
                    capabilities,
                    AssetPatchProviderReconciliationResult.ReadyToRestartPatch(
                        capabilities,
                        "Provider is ready for owner-controlled restart."));
                var service = new AssetPatchRecoveryService(package, store, providerReconciler: reconciler);

                AssetPatchRecoveryResult result = await service.RecoverAsync();

                Assert.AreEqual(AssetPatchRecoveryStatus.RequiresOwnerAction, result.Status);
                Assert.AreEqual(1, reconciler.ReconcileCallCount);
                Assert.AreEqual(AssetPatchProviderReconciliationStatus.ReadyToRestartPatch, result.ProviderReconciliation.Status);
                Assert.AreEqual("Provider is ready for owner-controlled restart.", result.ProviderReconciliation.Message);
                Assert.AreEqual(0, package.ClearCacheFilesCallCount);
                Assert.AreEqual(0, package.UpdatedPackageVersions.Count);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RecoveryService_Does_Not_Rollback_When_Provider_Lacks_Versioned_Manifest_Update()
        {
            string directory = CreateTempDirectory();
            try
            {
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var store = new FileAssetPatchJournalStore(journalPath);
                var record = CreateRecord(
                    PatchWorkflowState.Failed,
                    AssetPatchJournalStatus.Failed,
                    rollbackVersion: "manifest-rollback",
                    error: "failed");
                store.Write(in record);
                var package = new RecordingAssetPackage { NameValue = "Main" };
                AssetPatchProviderReconciliationCapabilities capabilities = CreateProviderCapabilities(
                    supportsVersionedManifestUpdate: false);
                var reconciler = new RecordingPatchProviderReconciler(
                    capabilities,
                    AssetPatchProviderReconciliationResult.RequiresOwnerAction(
                        capabilities,
                        "Provider cannot roll back to a historical manifest."));
                var policy = new AssetPatchRecoveryPolicy(rollbackFailedJournalWithVersion: true);
                var service = new AssetPatchRecoveryService(package, store, policy, reconciler);

                AssetPatchRecoveryResult result = await service.RecoverAsync();

                Assert.AreEqual(AssetPatchRecoveryStatus.RequiresOwnerAction, result.Status);
                Assert.AreEqual(1, reconciler.ReconcileCallCount);
                Assert.AreEqual(AssetPatchProviderReconciliationStatus.RequiresOwnerAction, result.ProviderReconciliation.Status);
                Assert.AreEqual(0, package.UpdatedPackageVersions.Count);
                Assert.AreEqual(0, package.ClearCacheFilesCallCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RecoveryService_Does_Not_Clear_Unused_Cache_When_Provider_Lacks_Unused_Cleanup()
        {
            string directory = CreateTempDirectory();
            try
            {
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var store = new FileAssetPatchJournalStore(journalPath);
                var record = CreateRecord(PatchWorkflowState.Download, AssetPatchJournalStatus.InProgress);
                store.Write(in record);
                var package = new RecordingAssetPackage { NameValue = "Main" };
                AssetPatchProviderReconciliationCapabilities capabilities = CreateProviderCapabilities(
                    supportsExplicitCacheCleanup: true,
                    supportsUnusedCacheCleanup: false);
                var reconciler = new RecordingPatchProviderReconciler(
                    capabilities,
                    AssetPatchProviderReconciliationResult.RequiresOwnerAction(
                        capabilities,
                        "Provider cannot clear unused cache safely."));
                var policy = new AssetPatchRecoveryPolicy(clearCacheForInterruptedDownload: true);
                var service = new AssetPatchRecoveryService(package, store, policy, reconciler);

                AssetPatchRecoveryResult result = await service.RecoverAsync();

                Assert.AreEqual(AssetPatchRecoveryStatus.RequiresOwnerAction, result.Status);
                Assert.AreEqual(1, reconciler.ReconcileCallCount);
                Assert.AreEqual(AssetPatchProviderReconciliationStatus.RequiresOwnerAction, result.ProviderReconciliation.Status);
                Assert.AreEqual(0, package.ClearCacheFilesCallCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task RecoveryService_With_Interrupted_Download_Clears_Cache_When_Policy_Allows()
        {
            string directory = CreateTempDirectory();
            try
            {
                string journalPath = Path.Combine(directory, "patch-journal.json");
                var store = new FileAssetPatchJournalStore(journalPath);
                var record = CreateRecord(PatchWorkflowState.Download, AssetPatchJournalStatus.InProgress);
                store.Write(in record);
                var package = new RecordingAssetPackage { NameValue = "Main" };
                var policy = new AssetPatchRecoveryPolicy(
                    clearCacheForInterruptedDownload: true,
                    interruptedDownloadClearMode: ClearCacheMode.Unused,
                    clearJournalAfterSuccessfulRecovery: true);
                var service = new AssetPatchRecoveryService(package, store, policy);

                AssetPatchRecoveryResult result = await service.RecoverAsync();

                Assert.AreEqual(AssetPatchRecoveryStatus.CacheCleanupCompleted, result.Status);
                Assert.IsTrue(result.CacheCleared);
                Assert.IsTrue(result.JournalCleared);
                Assert.AreEqual(1, package.ClearCacheFilesCallCount);
                Assert.AreEqual(ClearCacheMode.Unused, package.LastClearCacheMode);
                Assert.IsFalse(File.Exists(journalPath));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static AssetPatchJournalRecord CreateRecord(
            PatchWorkflowState stage,
            AssetPatchJournalStatus status,
            string rollbackVersion = null,
            string error = null)
        {
            return new AssetPatchJournalRecord(
                sequence: 1L,
                packageName: "Main",
                packageVersion: "manifest-current",
                rollbackVersion: rollbackVersion,
                stage: stage,
                status: status,
                totalDownloadCount: 2,
                totalDownloadBytes: 1024L,
                contentTrustEnabled: true,
                trustFailureCount: 0,
                contentTrustManifestFingerprint: 1UL,
                startedUtcTicks: 10L,
                updatedUtcTicks: 20L,
                error: error);
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.JournalTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static AssetPatchProviderReconciliationCapabilities CreateProviderCapabilities(
            bool supportsVersionedManifestUpdate = true,
            bool supportsExplicitCacheCleanup = true,
            bool supportsUnusedCacheCleanup = true,
            bool supportsTagScopedCacheCleanup = true)
        {
            return new AssetPatchProviderReconciliationCapabilities(
                "TestProvider",
                supportsVersionedManifestUpdate,
                supportsExplicitCacheCleanup,
                supportsUnusedCacheCleanup,
                supportsTagScopedCacheCleanup,
                supportsProviderManagedDownloadCache: true,
                supportsIsolatedVersionPreDownload: false,
                requiresMainThreadAccess: false);
        }

        private sealed class RecordingPatchProviderReconciler : IAssetPatchProviderReconciler
        {
            private readonly AssetPatchProviderReconciliationResult _result;

            public int ReconcileCallCount;
            public AssetPatchJournalRecord LastRecord;
            public AssetPatchRecoveryRecommendation LastRecommendation;
            public AssetPatchProviderReconciliationCapabilities Capabilities { get; }

            public RecordingPatchProviderReconciler(
                AssetPatchProviderReconciliationCapabilities capabilities,
                AssetPatchProviderReconciliationResult result)
            {
                Capabilities = capabilities;
                _result = result;
            }

            public UniTask<AssetPatchProviderReconciliationResult> ReconcileAsync(
                AssetPatchJournalRecord record,
                AssetPatchRecoveryRecommendation recommendation,
                System.Threading.CancellationToken cancellationToken = default)
            {
                ReconcileCallCount++;
                LastRecord = record;
                LastRecommendation = recommendation;
                return UniTask.FromResult(_result);
            }
        }
    }
}
