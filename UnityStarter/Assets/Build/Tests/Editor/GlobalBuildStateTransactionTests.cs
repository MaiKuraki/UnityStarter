using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace Build.Pipeline.Editor.Tests
{
    public sealed class GlobalBuildStateTransactionTests
    {
        private string projectRoot;
        private string playerSettingsPath;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "GlobalBuildStateTransactionTests-" + Guid.NewGuid().ToString("N"));
            playerSettingsPath = Path.Combine(projectRoot, "ProjectSettings", "ProjectSettings.asset");
            Directory.CreateDirectory(Path.GetDirectoryName(playerSettingsPath));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "Config"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void InterruptedGlobalMutation_IsRestoredExactlyOnNextAcquire()
        {
            byte[] original = { 1, 2, 3, 4, 5 };
            DateTime originalTime = new DateTime(637450560000000000L, DateTimeKind.Utc);
            WriteFile(playerSettingsPath, original, originalTime);
            FileAttributes originalAttributes = File.GetAttributes(playerSettingsPath);

            GlobalBuildStateTransaction first = BeginActiveTransaction();
            WriteFile(playerSettingsPath, new byte[] { 9, 8, 7 }, originalTime.AddDays(1));
            MarkGlobalMutationApplied(first);
            first.AbandonForProcessTerminationSimulation();

            GlobalBuildStateTransaction recovered = GlobalBuildStateTransaction.Acquire(projectRoot);
            try
            {
                Assert.That(recovered.HasPendingRecovery, Is.True);
                Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(original));
                Assert.That(File.GetLastWriteTimeUtc(playerSettingsPath), Is.EqualTo(originalTime));
                Assert.That(File.GetAttributes(playerSettingsPath), Is.EqualTo(originalAttributes));

                recovered.ConfirmPendingRecovery();
                Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.False);
            }
            finally
            {
                Assert.That(recovered.Release(), Is.Null);
            }
        }

        [Test]
        public void InterruptedApplying_WithUnknownPlayerSettingsChange_FailsClosedAndPreservesEvidence()
        {
            byte[] original = { 1, 2, 3, 4 };
            byte[] foreign = { 9, 8, 7, 6 };
            WriteFile(playerSettingsPath, original, StableTime());

            GlobalBuildStateTransaction first = BeginActiveTransaction();
            WriteFile(playerSettingsPath, foreign, StableTime().AddDays(1));
            first.AbandonForProcessTerminationSimulation();

            IOException exception = Assert.Throws<IOException>(
                () => GlobalBuildStateTransaction.Acquire(projectRoot));
            Assert.That(exception.Message, Does.Contain("unrecognized global-state file"));
            Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(foreign));
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);
        }

        [Test]
        public void PersistenceBarrier_WhenPlayerSettingsAlreadyChanged_FailsBeforeOwnershipCapture()
        {
            byte[] original = { 2, 3, 5, 7 };
            byte[] foreign = { 11, 13, 17 };
            WriteFile(playerSettingsPath, original, StableTime());

            GlobalBuildStateTransaction transaction = BeginActiveTransaction();
            WriteFile(playerSettingsPath, foreign, StableTime().AddHours(1));

            IOException exception = Assert.Throws<IOException>(
                transaction.EnsurePlayerSettingsUnchangedBeforePersistence);
            Assert.That(exception.Message, Does.Contain("before the pipeline persistence barrier"));
            Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(foreign));
            transaction.AbandonForProcessTerminationSimulation();
        }

        [Test]
        public void PersistenceToken_WhenFileChangesBeforeDurableMark_FailsWithoutAdoptingIt()
        {
            byte[] original = { 2, 3, 5, 7 };
            byte[] persisted = { 11, 13, 17 };
            byte[] foreign = { 19, 23, 29 };
            WriteFile(playerSettingsPath, original, StableTime());

            GlobalBuildStateTransaction transaction = BeginActiveTransaction();
            WriteFile(playerSettingsPath, persisted, StableTime().AddHours(1));
            GlobalBuildStateTransaction.PlayerSettingsPersistenceToken token =
                transaction.CapturePlayerSettingsPersistenceToken();
            WriteFile(playerSettingsPath, foreign, StableTime().AddHours(2));

            IOException exception = Assert.Throws<IOException>(
                () => transaction.MarkGlobalMutationApplied(token));
            Assert.That(exception.Message, Does.Contain("candidate post-image was not adopted"));
            Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(foreign));
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);
            transaction.AbandonForProcessTerminationSimulation();
        }

        [Test]
        public void OwnedPlayerSettings_WithSameContentAndDifferentMetadata_IsAcceptedAndRestoredExactly()
        {
            byte[] original = { 3, 1, 4, 1, 5 };
            byte[] transient = { 9, 2, 6, 5 };
            DateTime originalTime = StableTime();
            WriteFile(playerSettingsPath, original, originalTime);
            FileAttributes originalAttributes = File.GetAttributes(playerSettingsPath);

            GlobalBuildStateTransaction transaction = BeginActiveTransaction();
            WriteFile(playerSettingsPath, transient, originalTime.AddHours(1));
            MarkGlobalMutationApplied(transaction);
            WriteFile(playerSettingsPath, transient, originalTime.AddHours(2));

            Assert.DoesNotThrow(transaction.EnsurePlayerSettingsOwned);
            transaction.RestorePlayerSettingsFile();
            transaction.Complete();

            Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(original));
            Assert.That(File.GetLastWriteTimeUtc(playerSettingsPath), Is.EqualTo(originalTime));
            Assert.That(File.GetAttributes(playerSettingsPath), Is.EqualTo(originalAttributes));
            Assert.That(transaction.Release(), Is.Null);
        }

        [Test]
        public void PlayerSettingsOwnershipGuard_WithDifferentContent_FailsAndRetainsEvidence()
        {
            byte[] original = { 8, 5, 3 };
            byte[] transient = { 2, 1, 1 };
            byte[] foreign = { 99, 98, 97 };
            WriteFile(playerSettingsPath, original, StableTime());

            GlobalBuildStateTransaction transaction = BeginActiveTransaction();
            WriteFile(playerSettingsPath, transient, StableTime().AddHours(1));
            MarkGlobalMutationApplied(transaction);
            WriteFile(playerSettingsPath, foreign, StableTime().AddHours(2));

            IOException exception = Assert.Throws<IOException>(transaction.EnsurePlayerSettingsOwned);
            Assert.That(exception.Message, Does.Contain("authorized content"));
            Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(foreign));
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);
            transaction.AbandonForProcessTerminationSimulation();
        }

        [Test]
        public void RestorePlayerSettings_AcceptsOriginalContentWithChangedMetadata()
        {
            byte[] original = { 4, 6, 8, 0 };
            byte[] transient = { 1, 3, 5, 7 };
            DateTime originalTime = StableTime();
            WriteFile(playerSettingsPath, original, originalTime);

            GlobalBuildStateTransaction transaction = BeginActiveTransaction();
            WriteFile(playerSettingsPath, transient, originalTime.AddHours(1));
            MarkGlobalMutationApplied(transaction);
            WriteFile(playerSettingsPath, original, originalTime.AddHours(2));

            transaction.RestorePlayerSettingsFile();
            transaction.Complete();

            Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(original));
            Assert.That(File.GetLastWriteTimeUtc(playerSettingsPath), Is.EqualTo(originalTime));
            Assert.That(transaction.Release(), Is.Null);
        }

        [Test]
        public void PlayerSettingsSaveFilter_AllowsOnlyCanonicalPath()
        {
            string[] filtered = BuildPipelineAssetSaveFilter.FilterPathsForTests(
                new[]
                {
                    "Assets/UserData.asset",
                    "ProjectSettings\\ProjectSettings.asset",
                    "ProjectSettings/EditorBuildSettings.asset",
                    "ProjectSettings/ProjectSettings.asset",
                    "ProjectSettings/ProjectSettings.asset"
                },
                out bool foundPlayerSettings);

            Assert.That(foundPlayerSettings, Is.True);
            Assert.That(filtered, Is.EqualTo(new[] { "ProjectSettings/ProjectSettings.asset" }));
        }

        [Test]
        public void InterruptedAbsentVersionInfoInstallation_IsRemovedOnNextAcquire()
        {
            WriteFile(playerSettingsPath, new byte[] { 1, 3, 5 }, StableTime());
            GlobalBuildStateTransaction first = BeginActiveTransaction();
            MarkGlobalMutationApplied(first);
            const string target = "Assets/Config/VersionInfo.asset";
            first.PrepareVersionInfo(target);

            string stage = Path.Combine(
                projectRoot,
                first.VersionInfoStageAssetPath.Replace('/', Path.DirectorySeparatorChar));
            WriteFile(stage, new byte[] { 10, 20, 30 }, StableTime().AddMinutes(1));
            WriteFile(stage + ".meta", new byte[] { 40, 50, 60 }, StableTime().AddMinutes(2));
            first.MarkVersionStageReady();
            first.PublishStagedVersionInfo();
            first.AbandonForProcessTerminationSimulation();

            string targetPath = Path.Combine(projectRoot, target.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(targetPath), Is.True);
            Assert.That(File.Exists(targetPath + ".meta"), Is.True);

            GlobalBuildStateTransaction recovered = GlobalBuildStateTransaction.Acquire(projectRoot);
            try
            {
                Assert.That(File.Exists(targetPath), Is.False);
                Assert.That(File.Exists(targetPath + ".meta"), Is.False);
                recovered.ConfirmPendingRecovery();
            }
            finally
            {
                Assert.That(recovered.Release(), Is.Null);
            }
        }

        [Test]
        public void InterruptedOwnedVersionInfoParent_IsRemovedOnNextAcquire()
        {
            WriteFile(playerSettingsPath, new byte[] { 1, 4, 7 }, StableTime());
            GlobalBuildStateTransaction first = BeginActiveTransaction();
            MarkGlobalMutationApplied(first);
            const string target = "Assets/Generated/Nested/VersionInfo.asset";
            first.PrepareVersionInfo(target);

            string ownedRoot = Path.Combine(projectRoot, "Assets", "Generated");
            string marker = Path.Combine(ownedRoot, "BuildPipelineGlobalState.owner");
            Assert.That(Directory.Exists(Path.Combine(ownedRoot, "Nested")), Is.True);
            Assert.That(File.Exists(marker), Is.True);

            string stage = Path.Combine(
                projectRoot,
                first.VersionInfoStageAssetPath.Replace('/', Path.DirectorySeparatorChar));
            WriteFile(stage, new byte[] { 10, 11, 12 }, StableTime().AddMinutes(1));
            WriteFile(stage + ".meta", new byte[] { 20, 21, 22 }, StableTime().AddMinutes(2));
            first.MarkVersionStageReady();
            first.PublishStagedVersionInfo();

            WriteFile(ownedRoot + ".meta", new byte[] { 31, 32 }, StableTime().AddMinutes(3));
            WriteFile(Path.Combine(ownedRoot, "Nested.meta"), new byte[] { 41, 42 }, StableTime().AddMinutes(4));
            WriteFile(marker + ".meta", new byte[] { 51, 52 }, StableTime().AddMinutes(5));
            first.AbandonForProcessTerminationSimulation();

            GlobalBuildStateTransaction recovered = GlobalBuildStateTransaction.Acquire(projectRoot);
            try
            {
                Assert.That(Directory.Exists(ownedRoot), Is.False);
                Assert.That(File.Exists(ownedRoot + ".meta"), Is.False);
                Assert.That(
                    Directory.GetDirectories(Path.Combine(projectRoot, "Assets"), "__BuildPipelineParent_*"),
                    Is.Empty);
                recovered.ConfirmPendingRecovery();
            }
            finally
            {
                Assert.That(recovered.Release(), Is.Null);
            }
        }

        [Test]
        public void ExternalFileInOwnedVersionInfoParent_FailsClosedAndPreservesEvidence()
        {
            WriteFile(playerSettingsPath, new byte[] { 2, 5, 8 }, StableTime());
            GlobalBuildStateTransaction first = BeginActiveTransaction();
            MarkGlobalMutationApplied(first);
            first.PrepareVersionInfo("Assets/Generated/Nested/VersionInfo.asset");

            string ownedRoot = Path.Combine(projectRoot, "Assets", "Generated");
            string foreignFile = Path.Combine(ownedRoot, "foreign.txt");
            byte[] foreignBytes = { 90, 91, 92 };
            WriteFile(foreignFile, foreignBytes, StableTime().AddDays(1));
            first.AbandonForProcessTerminationSimulation();

            IOException exception = Assert.Throws<IOException>(
                () => GlobalBuildStateTransaction.Acquire(projectRoot));
            Assert.That(exception.Message, Does.Contain("Unrecognized file"));
            Assert.That(File.ReadAllBytes(foreignFile), Is.EqualTo(foreignBytes));
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);
        }

        [Test]
        public void InterruptedExistingVersionInfoInstallation_RestoresBytesAndMetadata()
        {
            WriteFile(playerSettingsPath, new byte[] { 2, 4, 6 }, StableTime());
            string targetPath = Path.Combine(projectRoot, "Assets", "Config", "VersionInfo.asset");
            byte[] originalAsset = { 11, 12, 13 };
            byte[] originalMeta = { 21, 22, 23 };
            DateTime assetTime = StableTime().AddHours(1);
            DateTime metaTime = StableTime().AddHours(2);
            WriteFile(targetPath, originalAsset, assetTime);
            WriteFile(targetPath + ".meta", originalMeta, metaTime);
            FileAttributes assetAttributes = File.GetAttributes(targetPath);
            FileAttributes metaAttributes = File.GetAttributes(targetPath + ".meta");

            GlobalBuildStateTransaction first = BeginActiveTransaction();
            MarkGlobalMutationApplied(first);
            first.PrepareVersionInfo("Assets/Config/VersionInfo.asset");
            string stage = Path.Combine(
                projectRoot,
                first.VersionInfoStageAssetPath.Replace('/', Path.DirectorySeparatorChar));
            WriteFile(stage, new byte[] { 99, 98, 97 }, StableTime().AddDays(1));
            WriteFile(stage + ".meta", new byte[] { 88, 87, 86 }, StableTime().AddDays(1));
            first.MarkVersionStageReady();
            first.PublishStagedVersionInfo();
            first.AbandonForProcessTerminationSimulation();

            GlobalBuildStateTransaction recovered = GlobalBuildStateTransaction.Acquire(projectRoot);
            try
            {
                Assert.That(File.ReadAllBytes(targetPath), Is.EqualTo(originalAsset));
                Assert.That(File.ReadAllBytes(targetPath + ".meta"), Is.EqualTo(originalMeta));
                Assert.That(File.GetLastWriteTimeUtc(targetPath), Is.EqualTo(assetTime));
                Assert.That(File.GetLastWriteTimeUtc(targetPath + ".meta"), Is.EqualTo(metaTime));
                Assert.That(File.GetAttributes(targetPath), Is.EqualTo(assetAttributes));
                Assert.That(File.GetAttributes(targetPath + ".meta"), Is.EqualTo(metaAttributes));
                recovered.ConfirmPendingRecovery();
            }
            finally
            {
                Assert.That(recovered.Release(), Is.Null);
            }
        }

        [Test]
        public void ExistingVersionInfoChangedBeforePublish_FailsWithoutReplacingForeignContent()
        {
            WriteFile(playerSettingsPath, new byte[] { 2, 4, 6 }, StableTime());
            string targetPath = Path.Combine(projectRoot, "Assets", "Config", "VersionInfo.asset");
            byte[] originalAsset = { 11, 12, 13 };
            byte[] originalMeta = { 21, 22, 23 };
            byte[] foreignAsset = { 31, 32, 33 };
            WriteFile(targetPath, originalAsset, StableTime().AddHours(1));
            WriteFile(targetPath + ".meta", originalMeta, StableTime().AddHours(2));

            GlobalBuildStateTransaction transaction = BeginActiveTransaction();
            MarkGlobalMutationApplied(transaction);
            transaction.PrepareVersionInfo("Assets/Config/VersionInfo.asset");
            string stage = Path.Combine(
                projectRoot,
                transaction.VersionInfoStageAssetPath.Replace('/', Path.DirectorySeparatorChar));
            WriteFile(stage, new byte[] { 99, 98, 97 }, StableTime().AddDays(1));
            WriteFile(stage + ".meta", new byte[] { 88, 87, 86 }, StableTime().AddDays(1));
            transaction.MarkVersionStageReady();
            WriteFile(targetPath, foreignAsset, StableTime().AddDays(2));

            IOException exception = Assert.Throws<IOException>(transaction.PublishStagedVersionInfo);
            Assert.That(exception.Message, Does.Contain("restoration verification failed"));
            Assert.That(File.ReadAllBytes(targetPath), Is.EqualTo(foreignAsset));
            Assert.That(
                Directory.GetFiles(Path.GetDirectoryName(targetPath), "*.globalstate-install-*.bak"),
                Is.Empty);
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);
            transaction.AbandonForProcessTerminationSimulation();
        }

        [Test]
        public void ExistingVersionInfoChangedAtAtomicReplace_RetainsCompetingBackup()
        {
            WriteFile(playerSettingsPath, new byte[] { 2, 4, 6 }, StableTime());
            string targetPath = Path.Combine(projectRoot, "Assets", "Config", "VersionInfo.asset");
            byte[] originalAsset = { 11, 12, 13 };
            byte[] originalMeta = { 21, 22, 23 };
            byte[] foreignAsset = { 41, 42, 43, 44 };
            WriteFile(targetPath, originalAsset, StableTime().AddHours(1));
            WriteFile(targetPath + ".meta", originalMeta, StableTime().AddHours(2));

            GlobalBuildStateTransaction transaction = BeginActiveTransaction();
            MarkGlobalMutationApplied(transaction);
            transaction.PrepareVersionInfo("Assets/Config/VersionInfo.asset");
            string stage = Path.Combine(
                projectRoot,
                transaction.VersionInfoStageAssetPath.Replace('/', Path.DirectorySeparatorChar));
            byte[] stagedAsset = { 99, 98, 97 };
            WriteFile(stage, stagedAsset, StableTime().AddDays(1));
            WriteFile(stage + ".meta", new byte[] { 88, 87, 86 }, StableTime().AddDays(1));
            transaction.MarkVersionStageReady();
            transaction.SetBeforeVersionInfoInstallReplaceForTests(
                () => WriteFile(targetPath, foreignAsset, StableTime().AddDays(2)));

            IOException exception = Assert.Throws<IOException>(transaction.PublishStagedVersionInfo);
            Assert.That(exception.Message, Does.Contain("captured an unrecognized competing write"));
            string[] backups = Directory.GetFiles(
                Path.GetDirectoryName(targetPath),
                "*.globalstate-install-*.bak");
            Assert.That(backups, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllBytes(backups[0]), Is.EqualTo(foreignAsset));
            Assert.That(File.ReadAllBytes(targetPath), Is.EqualTo(stagedAsset));
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);
            transaction.AbandonForProcessTerminationSimulation();

            IOException recoveryException = Assert.Throws<IOException>(
                () => GlobalBuildStateTransaction.Acquire(projectRoot));
            Assert.That(recoveryException.Message, Does.Contain("competing backup"));
            Assert.That(File.ReadAllBytes(backups[0]), Is.EqualTo(foreignAsset));
            Assert.That(File.ReadAllBytes(targetPath), Is.EqualTo(stagedAsset));
        }

        [Test]
        public void ExternallyChangedInstalledVersionInfo_FailsClosedAndRetainsJournal()
        {
            WriteFile(playerSettingsPath, new byte[] { 7, 7, 7 }, StableTime());
            GlobalBuildStateTransaction first = BeginActiveTransaction();
            MarkGlobalMutationApplied(first);
            const string target = "Assets/Config/VersionInfo.asset";
            first.PrepareVersionInfo(target);
            string stage = Path.Combine(
                projectRoot,
                first.VersionInfoStageAssetPath.Replace('/', Path.DirectorySeparatorChar));
            WriteFile(stage, new byte[] { 1, 1, 1 }, StableTime().AddMinutes(1));
            WriteFile(stage + ".meta", new byte[] { 2, 2, 2 }, StableTime().AddMinutes(2));
            first.MarkVersionStageReady();
            first.PublishStagedVersionInfo();
            first.AbandonForProcessTerminationSimulation();

            string targetPath = Path.Combine(projectRoot, target.Replace('/', Path.DirectorySeparatorChar));
            WriteFile(targetPath, new byte[] { 6, 6, 6, 6 }, StableTime().AddDays(3));

            IOException exception = Assert.Throws<IOException>(
                () => GlobalBuildStateTransaction.Acquire(projectRoot));
            Assert.That(exception.Message, Does.Contain("externally changed"));
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);
            Assert.That(File.ReadAllBytes(targetPath), Is.EqualTo(new byte[] { 6, 6, 6, 6 }));
        }

        [Test]
        public void CorruptJournal_FailsClosedAndRetainsEvidence()
        {
            WriteFile(playerSettingsPath, new byte[] { 5, 4, 3 }, StableTime());
            GlobalBuildStateTransaction first = BeginActiveTransaction();
            MarkGlobalMutationApplied(first);
            first.AbandonForProcessTerminationSimulation();

            string journalPath = GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot);
            byte[] bytes = File.ReadAllBytes(journalPath);
            bytes[bytes.Length / 2] ^= 0x01;
            File.WriteAllBytes(journalPath, bytes);

            Assert.Throws<IOException>(() => GlobalBuildStateTransaction.Acquire(projectRoot));
            Assert.That(File.Exists(journalPath), Is.True);
        }

        [Test]
        public void CorruptSnapshot_FailsClosedAndRetainsEvidence()
        {
            WriteFile(playerSettingsPath, new byte[] { 8, 6, 4 }, StableTime());
            GlobalBuildStateTransaction first = BeginActiveTransaction();
            WriteFile(playerSettingsPath, new byte[] { 1, 2, 3 }, StableTime().AddDays(2));
            MarkGlobalMutationApplied(first);
            first.AbandonForProcessTerminationSimulation();

            string stateRoot = Path.GetDirectoryName(
                GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot));
            string[] snapshots = Directory.GetFiles(
                stateRoot,
                "player-settings.snapshot",
                SearchOption.AllDirectories);
            Assert.That(snapshots, Has.Length.EqualTo(1));
            File.WriteAllBytes(snapshots[0], new byte[] { 0, 0, 0 });

            IOException exception = Assert.Throws<IOException>(
                () => GlobalBuildStateTransaction.Acquire(projectRoot));
            Assert.That(exception.Message, Does.Contain("snapshot checksum"));
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);
        }

        [Test]
        public void Complete_BeforeVersionRestoreConfirmation_FailsAndRetainsJournal()
        {
            WriteFile(playerSettingsPath, new byte[] { 1, 9, 1 }, StableTime());
            GlobalBuildStateTransaction transaction = BeginActiveTransaction();
            MarkGlobalMutationApplied(transaction);
            const string target = "Assets/Config/VersionInfo.asset";
            transaction.PrepareVersionInfo(target);
            string stage = Path.Combine(
                projectRoot,
                transaction.VersionInfoStageAssetPath.Replace('/', Path.DirectorySeparatorChar));
            WriteFile(stage, new byte[] { 3, 4, 5 }, StableTime().AddMinutes(1));
            WriteFile(stage + ".meta", new byte[] { 6, 7, 8 }, StableTime().AddMinutes(2));
            transaction.MarkVersionStageReady();
            transaction.PublishStagedVersionInfo();
            transaction.RestorePlayerSettingsFile();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(transaction.Complete);
            Assert.That(exception.Message, Does.Contain("not confirmed"));
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);

            transaction.RestoreVersionInfoFiles();
            transaction.ConfirmVersionInfoRestored();
            transaction.Complete();
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.False);
            Assert.That(transaction.Release(), Is.Null);
        }

        [Test]
        public void ConcurrentAcquire_IsRejectedUntilOwnerReleasesLock()
        {
            WriteFile(playerSettingsPath, new byte[] { 4, 4, 4 }, StableTime());
            GlobalBuildStateTransaction owner = GlobalBuildStateTransaction.Acquire(projectRoot);
            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => GlobalBuildStateTransaction.Acquire(projectRoot));
                Assert.That(exception.Message, Does.Contain("already active"));
            }
            finally
            {
                Assert.That(owner.Release(), Is.Null);
            }
        }

        [Test]
        public void ExclusiveLock_RejectsIndependentFileHandle()
        {
            WriteFile(playerSettingsPath, new byte[] { 4, 5, 4 }, StableTime());
            GlobalBuildStateTransaction owner = GlobalBuildStateTransaction.Acquire(projectRoot);
            try
            {
                string lockPath = Path.Combine(
                    projectRoot,
                    "Library",
                    "BuildPipeline",
                    "GlobalState",
                    "build.lock");
                Assert.Throws<IOException>(() =>
                {
                    using (var competing = new FileStream(
                               lockPath,
                               FileMode.Open,
                               FileAccess.ReadWrite,
                               FileShare.None))
                    {
                    }
                });
            }
            finally
            {
                Assert.That(owner.Release(), Is.Null);
            }
        }

        [Test]
        public void CaptureAndApply_WhenBatchTargetDiffersFromActive_FailsWithoutSwitchingOrMutation()
        {
            BuildTarget originalTarget = EditorUserBuildSettings.activeBuildTarget;
            string originalCompanyName = PlayerSettings.companyName;
            BuildTarget requestedTarget = originalTarget == BuildTarget.Android
                ? BuildTarget.StandaloneWindows64
                : BuildTarget.Android;
            BuildRequest request = CreateBuildRequest(requestedTarget, batchMode: true);
            var version = new BuildVersionContext(
                "1.0.0",
                "1.0.0.1",
                1,
                "commit",
                "1",
                "branch",
                "2026-01-01T00:00:00Z",
                "test");

            BuildFailedException first = Assert.Throws<BuildFailedException>(
                () => BuildGlobalStateScope.CaptureAndApply(request, version));
            Assert.That(
                first.Message,
                Does.Contain(
                    $"-buildTarget {BuildCommandLine.GetUnityBuildTargetArgument(requestedTarget)}"));
            Assert.That(EditorUserBuildSettings.activeBuildTarget, Is.EqualTo(originalTarget));
            Assert.That(PlayerSettings.companyName, Is.EqualTo(originalCompanyName));

            Assert.Throws<BuildFailedException>(
                () => BuildGlobalStateScope.CaptureAndApply(request, version));
            Assert.That(EditorUserBuildSettings.activeBuildTarget, Is.EqualTo(originalTarget));
            Assert.That(PlayerSettings.companyName, Is.EqualTo(originalCompanyName));
        }

        [Test]
        [Explicit("Mutates and restores the live project's PlayerSettings; run as an isolated project-state integration test.")]
        public void ActiveScopeGuard_RejectsUnsavedPlayerSettingsMutation_AndRestoresExactFile()
        {
            string actualProjectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string actualPlayerSettingsPath = Path.Combine(
                actualProjectRoot,
                "ProjectSettings",
                "ProjectSettings.asset");
            byte[] originalBytes = File.ReadAllBytes(actualPlayerSettingsPath);
            DateTime originalTime = File.GetLastWriteTimeUtc(actualPlayerSettingsPath);
            FileAttributes originalAttributes = File.GetAttributes(actualPlayerSettingsPath);
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            NamedBuildTarget namedTarget = BuildRequestFactory.GetNamedBuildTarget(target);
            string applicationVersion = PlayerSettings.bundleVersion;
            long buildNumber = target == BuildTarget.Android
                ? PlayerSettings.Android.bundleVersionCode
                : 1L;
            bool enableObfuz = ObfuzIntegrator.IsBaseObfuzAvailable()
                && ObfuzIntegrator.TryGetObfuzBuildPipelineEnabled(out bool persistedObfuz)
                && persistedObfuz;
            string buildRoot = Path.Combine(actualProjectRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "GlobalStateGuardTest");
            var request = new BuildRequest(
                PlayerSettings.companyName,
                PlayerSettings.productName,
                PlayerSettings.GetApplicationIdentifier(namedTarget),
                "Assets/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                null,
                target,
                namedTarget,
                PlayerSettings.GetScriptingBackend(namedTarget),
                actualProjectRoot,
                buildRoot,
                Path.Combine(outputDirectory, "GuardTest"),
                outputDirectory,
                outputIsFolder: false,
                incrementality: BuildIncrementality.Clean,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: EditorUserBuildSettings.exportAsGoogleAndroidProject,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: applicationVersion,
                assetContentProviderId: string.Empty,
                assetContentConfiguration: null,
                useHybridClr: false,
                enablePlayerObfuscation: enableObfuz,
                stepIds: new[] { BuildStepIds.Player });
            var version = new BuildVersionContext(
                applicationVersion,
                applicationVersion + ".guard",
                buildNumber,
                "guard",
                "0",
                "guard",
                "2026-01-01T00:00:00Z",
                "test");

            BuildGlobalStateScope scope = BuildGlobalStateScope.CaptureAndApply(request, version);
            try
            {
                PlayerSettings.companyName = request.CompanyName + ".ForeignMutation";
                Exception exception = Assert.Catch<Exception>(
                    BuildGlobalStateScope.EnsureCurrentPlayerSettingsOwned);
                Assert.That(
                    exception.Message,
                    Does.Contain("unsaved in-memory changes").Or.Contain("Unity rejected"));
            }
            finally
            {
                scope.Dispose();
            }

            Assert.That(File.ReadAllBytes(actualPlayerSettingsPath), Is.EqualTo(originalBytes));
            Assert.That(File.GetLastWriteTimeUtc(actualPlayerSettingsPath), Is.EqualTo(originalTime));
            Assert.That(File.GetAttributes(actualPlayerSettingsPath), Is.EqualTo(originalAttributes));
            Assert.That(
                File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(actualProjectRoot)),
                Is.False);
        }

        [Test]
        public void InterruptedRestoreReplacement_IsFinishedAndScratchIsRemoved()
        {
            byte[] original = { 4, 2, 4, 2 };
            DateTime originalTime = StableTime();
            WriteFile(playerSettingsPath, original, originalTime);
            GlobalBuildStateTransaction first = BeginActiveTransaction();
            WriteFile(playerSettingsPath, new byte[] { 9, 9, 9 }, originalTime.AddDays(1));
            MarkGlobalMutationApplied(first);
            first.AbandonForProcessTerminationSimulation();

            string stateRoot = Path.GetDirectoryName(
                GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot));
            string[] transactionDirectories = Directory.GetDirectories(
                stateRoot,
                "transaction-*",
                SearchOption.TopDirectoryOnly);
            Assert.That(transactionDirectories, Has.Length.EqualTo(1));
            string transactionId = Path.GetFileName(transactionDirectories[0]).Substring("transaction-".Length);
            string snapshotPath = Path.Combine(transactionDirectories[0], "player-settings.snapshot");
            string temporaryPath = playerSettingsPath + ".globalstate-restore-" + transactionId + ".tmp";
            string backupPath = playerSettingsPath + ".globalstate-restore-" + transactionId + ".bak";
            File.Copy(snapshotPath, temporaryPath);
            File.Replace(temporaryPath, playerSettingsPath, backupPath);
            Assert.That(File.Exists(backupPath), Is.True);

            GlobalBuildStateTransaction recovered = GlobalBuildStateTransaction.Acquire(projectRoot);
            try
            {
                Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(original));
                Assert.That(File.GetLastWriteTimeUtc(playerSettingsPath), Is.EqualTo(originalTime));
                Assert.That(File.Exists(temporaryPath), Is.False);
                Assert.That(File.Exists(backupPath), Is.False);
                recovered.ConfirmPendingRecovery();
            }
            finally
            {
                Assert.That(recovered.Release(), Is.Null);
            }
        }

        [Test]
        public void PlayerSettingsChangedAtAtomicRestore_RetainsCompetingBackup()
        {
            byte[] original = { 4, 2, 4, 2 };
            byte[] transient = { 9, 9, 9 };
            byte[] foreign = { 7, 6, 5, 4 };
            DateTime originalTime = StableTime();
            WriteFile(playerSettingsPath, original, originalTime);
            GlobalBuildStateTransaction transaction = BeginActiveTransaction();
            WriteFile(playerSettingsPath, transient, originalTime.AddDays(1));
            MarkGlobalMutationApplied(transaction);
            transaction.SetBeforePlayerSettingsRestoreReplaceForTests(
                () => WriteFile(playerSettingsPath, foreign, originalTime.AddDays(2)));

            IOException exception = Assert.Throws<IOException>(transaction.RestorePlayerSettingsFile);
            Assert.That(exception.Message, Does.Contain("captured an unrecognized competing write"));
            string[] backups = Directory.GetFiles(
                Path.GetDirectoryName(playerSettingsPath),
                "*.globalstate-restore-*.bak");
            Assert.That(backups, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllBytes(backups[0]), Is.EqualTo(foreign));
            Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(original));
            Assert.That(File.Exists(GlobalBuildStateTransaction.GetJournalPathForTests(projectRoot)), Is.True);
            transaction.AbandonForProcessTerminationSimulation();

            IOException recoveryException = Assert.Throws<IOException>(
                () => GlobalBuildStateTransaction.Acquire(projectRoot));
            Assert.That(recoveryException.Message, Does.Contain("competing backup"));
            Assert.That(File.ReadAllBytes(backups[0]), Is.EqualTo(foreign));
            Assert.That(File.ReadAllBytes(playerSettingsPath), Is.EqualTo(original));
        }

        [Test]
        public void DetachedTransactionDirectoryWithoutJournal_FailsClosed()
        {
            WriteFile(playerSettingsPath, new byte[] { 3, 3, 3 }, StableTime());
            string detached = Path.Combine(
                projectRoot,
                "Library",
                "BuildPipeline",
                "GlobalState",
                "transaction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(detached);

            IOException exception = Assert.Throws<IOException>(
                () => GlobalBuildStateTransaction.Acquire(projectRoot));
            Assert.That(exception.Message, Does.Contain("Detached"));
        }

        private GlobalBuildStateTransaction BeginActiveTransaction()
        {
            GlobalBuildStateTransaction transaction = GlobalBuildStateTransaction.Acquire(projectRoot);
            transaction.Begin(
                "ProjectSettings/ProjectSettings.asset",
                originalActiveBuildTarget: (int)BuildTarget.StandaloneWindows64,
                originalExportAndroidProject: false,
                requestedBuildTarget: (int)BuildTarget.StandaloneWindows64,
                originalScriptingBackend: 0,
                originalCompanyName: "Company",
                originalProductName: "Product",
                originalBundleVersion: "1.0",
                originalApplicationIdentifier: "com.example.product");
            transaction.BeginGlobalMutation();
            return transaction;
        }

        private static void MarkGlobalMutationApplied(GlobalBuildStateTransaction transaction)
        {
            GlobalBuildStateTransaction.PlayerSettingsPersistenceToken token =
                transaction.CapturePlayerSettingsPersistenceToken();
            transaction.MarkGlobalMutationApplied(token);
        }

        private BuildRequest CreateBuildRequest(BuildTarget target, bool batchMode)
        {
            string buildRoot = Path.Combine(projectRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, target.ToString(), "Release");
            return new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.example.test",
                "Assets/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                null,
                target,
                BuildRequestFactory.GetNamedBuildTarget(target),
                ScriptingImplementation.Mono2x,
                projectRoot,
                buildRoot,
                Path.Combine(outputDirectory, "TestProduct"),
                outputDirectory,
                outputIsFolder: false,
                incrementality: BuildIncrementality.Clean,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: batchMode,
                applicationVersion: "1.0.0",
                assetContentProviderId: string.Empty,
                assetContentConfiguration: null,
                useHybridClr: false,
                enablePlayerObfuscation: false,
                stepIds: new[] { BuildStepIds.Player });
        }

        private static void WriteFile(string path, byte[] bytes, DateTime lastWriteTimeUtc)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        }

        private static DateTime StableTime()
        {
            return new DateTime(637765920000000000L, DateTimeKind.Utc);
        }
    }
}
