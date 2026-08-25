using System;
using System.IO;
using Build.Pipeline.Editor;
using Build.Pipeline.Editor.Integrations.YooAsset3Core;
using NUnit.Framework;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    /// <summary>
    /// Core-assembly recovery tests. These fixtures build the durable journal and
    /// ownership markers directly from core types, so they run without the YooAsset
    /// package and prove that publication recovery still works after the package is
    /// uninstalled or upgraded.
    /// </summary>
    public sealed class YooAsset3PublicationRecoveryTests
    {
        private const string InvocationId = "yooasset-recovery";
        private string projectRoot;
        private string testRoot;
        private string buildOutputRoot;
        private string bundledFileRoot;

        [SetUp]
        public void SetUp()
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            // Keep the fixture root short. The recovery journal path budget enforces the
            // Win32 MAX_PATH limit (259 characters) on the "work" root, and the repository
            // checkout path is already long. A deeply nested
            // Temp/BuildPipelineTests/YooAsset3Recovery/<guid>/Project tree plus the fixed
            // .buildpipeline/transactions/yooasset3/<invocation>/work/<transactionId> suffix
            // overflows that budget, so use a single short directory as the project root.
            testRoot = Path.Combine(
                unityProjectRoot,
                "Temp",
                "Y3R" + Guid.NewGuid().ToString("N").Substring(0, 8));
            projectRoot = testRoot;
            buildOutputRoot = Path.Combine(projectRoot, "BuildOutput");
            bundledFileRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "YooAsset");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }

        [Test]
        public void RecoverPending_AfterInstalledCrash_RestoresOriginalTarget()
        {
            const string packageName = "PackageOne";
            const string packageVersion = "1.0.0";
            string originalTransactionId = Guid.NewGuid().ToString("N");
            string transactionId = Guid.NewGuid().ToString("N");
            string parent = Path.Combine(buildOutputRoot, packageName);
            string target = Path.Combine(parent, packageVersion);
            string stage = Path.Combine(parent, ".yoo-stage-" + transactionId + "-000");
            string backup = Path.Combine(parent, ".yoo-backup-" + transactionId + "-000");

            WriteFile(target, "payload.txt", "old");
            YooAsset3PublicationOwnership.PublicationSnapshot original = SealOwned(
                target,
                packageName,
                packageVersion,
                originalTransactionId);
            Directory.Move(target, backup);

            WriteFile(stage, "payload.txt", "new");
            YooAsset3PublicationOwnership.PublicationSnapshot installed = SealOwned(
                stage,
                packageName,
                packageVersion,
                transactionId);
            Directory.Move(stage, target);

            string stateRoot = YooAsset3PublicationPaths.GetStateRoot(projectRoot, InvocationId);
            string journalPath = Path.Combine(stateRoot, "active.json");
            var operation = new YooAsset3PublicationJournalOperation
            {
                kind = YooAsset3PublicationOwnership.PackageOutputKind,
                packageName = packageName,
                packageVersion = packageVersion,
                cryptographyAdapterId = YooAssetCryptographyIdentity.NoneAdapterId,
                runtimeDecryptContractId = YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId,
                approvedRoot = buildOutputRoot,
                target = target,
                stage = stage,
                backup = backup,
                targetInitiallyExisted = true,
                originalWasOwned = true,
                originalTransactionId = originalTransactionId,
                originalPackageVersion = packageVersion,
                originalCryptographyAdapterId = YooAssetCryptographyIdentity.NoneAdapterId,
                originalRuntimeDecryptContractId = YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId,
                originalContentIdentity = original.ContentIdentity,
                originalEntryCount = original.EntryCount,
                installedContentIdentity = installed.ContentIdentity,
                installedEntryCount = installed.EntryCount,
                managesSiblingMeta = false,
                state = YooAsset3PublicationConstants.InstalledState
            };
            var journal = new Journal
            {
                documentType = YooAsset3PublicationConstants.JournalDocumentType,
                invocationId = InvocationId,
                transactionId = transactionId,
                phase = YooAsset3PublicationConstants.CommittingPhase,
                projectRoot = Path.GetFullPath(projectRoot),
                buildOutputRoot = Path.GetFullPath(buildOutputRoot),
                bundledFileRoot = Path.GetFullPath(bundledFileRoot),
                workRoot = Path.GetFullPath(Path.Combine(stateRoot, "work", transactionId)),
                operations = new[] { operation }
            };
            YooAsset3PublicationRecovery.WriteJournal(journal, journalPath, createNew: true);

            YooAsset3PublicationRecovery.RecoverPending(projectRoot, NoOp);

            Assert.That(ReadFile(target, "payload.txt"), Is.EqualTo("old"));
            Assert.That(Directory.Exists(backup), Is.False);
            Assert.That(Directory.Exists(stage), Is.False);
            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(Directory.Exists(stateRoot), Is.False);
        }

        [Test]
        public void RecoverPending_AfterPreparedCrash_DiscardsStageWithoutChangingTarget()
        {
            const string packageName = "PackageOne";
            const string packageVersion = "1.0.0";
            string originalTransactionId = Guid.NewGuid().ToString("N");
            string transactionId = Guid.NewGuid().ToString("N");
            string parent = Path.Combine(buildOutputRoot, packageName);
            string target = Path.Combine(parent, packageVersion);
            string stage = Path.Combine(parent, ".yoo-stage-" + transactionId + "-000");
            string backup = Path.Combine(parent, ".yoo-backup-" + transactionId + "-000");

            WriteFile(target, "payload.txt", "old");
            YooAsset3PublicationOwnership.PublicationSnapshot original = SealOwned(
                target,
                packageName,
                packageVersion,
                originalTransactionId);

            WriteFile(stage, "payload.txt", "new");
            YooAsset3PublicationOwnership.PublicationSnapshot installed = SealOwned(
                stage,
                packageName,
                packageVersion,
                transactionId);

            string stateRoot = YooAsset3PublicationPaths.GetStateRoot(projectRoot, InvocationId);
            string journalPath = Path.Combine(stateRoot, "active.json");
            var operation = new YooAsset3PublicationJournalOperation
            {
                kind = YooAsset3PublicationOwnership.PackageOutputKind,
                packageName = packageName,
                packageVersion = packageVersion,
                cryptographyAdapterId = YooAssetCryptographyIdentity.NoneAdapterId,
                runtimeDecryptContractId = YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId,
                approvedRoot = buildOutputRoot,
                target = target,
                stage = stage,
                backup = backup,
                targetInitiallyExisted = true,
                originalWasOwned = true,
                originalTransactionId = originalTransactionId,
                originalPackageVersion = packageVersion,
                originalCryptographyAdapterId = YooAssetCryptographyIdentity.NoneAdapterId,
                originalRuntimeDecryptContractId = YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId,
                originalContentIdentity = original.ContentIdentity,
                originalEntryCount = original.EntryCount,
                installedContentIdentity = installed.ContentIdentity,
                installedEntryCount = installed.EntryCount,
                managesSiblingMeta = false,
                state = YooAsset3PublicationConstants.PreparedState
            };
            var journal = new Journal
            {
                documentType = YooAsset3PublicationConstants.JournalDocumentType,
                invocationId = InvocationId,
                transactionId = transactionId,
                phase = YooAsset3PublicationConstants.PreparedPhase,
                projectRoot = Path.GetFullPath(projectRoot),
                buildOutputRoot = Path.GetFullPath(buildOutputRoot),
                bundledFileRoot = Path.GetFullPath(bundledFileRoot),
                workRoot = Path.GetFullPath(Path.Combine(stateRoot, "work", transactionId)),
                operations = new[] { operation }
            };
            YooAsset3PublicationRecovery.WriteJournal(journal, journalPath, createNew: true);

            YooAsset3PublicationRecovery.RecoverPending(projectRoot, NoOp);

            Assert.That(ReadFile(target, "payload.txt"), Is.EqualTo("old"));
            Assert.That(Directory.Exists(stage), Is.False);
            Assert.That(File.Exists(journalPath), Is.False);
        }

        [Test]
        public void CaptureExisting_WhenDirectoryIsNonEmptyAndUnowned_FailsClosed()
        {
            string directory = Path.Combine(buildOutputRoot, "Unknown");
            WriteFile(directory, "authored.txt", "not-owned");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                YooAsset3PublicationOwnership.CaptureExisting(
                    projectRoot,
                    directory,
                    YooAsset3PublicationOwnership.PackageOutputKind,
                    "PackageOne"));

            StringAssert.Contains("not a Build-owned", exception.Message);
            Assert.That(ReadFile(directory, "authored.txt"), Is.EqualTo("not-owned"));
        }

        private YooAsset3PublicationOwnership.PublicationSnapshot SealOwned(
            string directory,
            string packageName,
            string packageVersion,
            string transactionId)
        {
            return YooAsset3PublicationOwnership.Seal(
                projectRoot,
                directory,
                YooAsset3PublicationOwnership.PackageOutputKind,
                packageName,
                packageVersion,
                YooAssetCryptographyIdentity.NoneAdapterId,
                YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId,
                transactionId);
        }

        private static void WriteFile(string directory, string fileName, string content)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }

        private static string ReadFile(string directory, string fileName)
        {
            return File.ReadAllText(Path.Combine(directory, fileName));
        }

        private static void NoOp()
        {
        }
    }
}
