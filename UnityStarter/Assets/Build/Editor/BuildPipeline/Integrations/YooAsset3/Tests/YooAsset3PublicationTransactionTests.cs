using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3.Tests
{
    public sealed class YooAsset3PublicationTransactionTests
    {
        private string projectRoot;
        private string testRoot;
        private string buildOutputRoot;
        private string bundledFileRoot;

        [SetUp]
        public void SetUp()
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            testRoot = Path.Combine(
                unityProjectRoot,
                "Temp",
                "BuildPipelineTests",
                "YooAsset3Publication",
                Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(testRoot, "Project");
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
        public void Commit_WhenSecondPackageStageIsMissing_RestoresEveryOriginalTarget()
        {
            YooAsset3BuildPlan plan = CreatePlan(
                CreatePackage("PackageOne", EBundledCopyOption.None),
                CreatePackage("PackageTwo", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old-one");
            WriteOwnedPublication(plan.Packages[1], false, "payload.txt", "old-two");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            WriteFile(transaction.Packages[0].OutputOperation.stage, "payload.txt", "new-one");
            WriteFile(transaction.Packages[1].OutputOperation.stage, "payload.txt", "new-two");
            transaction.SealReadyDirectories();
            Directory.Delete(transaction.Packages[1].OutputOperation.stage, true);

            Assert.Throws<DirectoryNotFoundException>(() => transaction.Commit(null, () => { }));

            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old-one"));
            Assert.That(ReadFile(plan.Packages[1].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old-two"));
            Assert.That(
                File.Exists(Path.Combine(
                    YooAsset3PublicationTransaction.GetStateRoot(projectRoot),
                    "active.json")),
                Is.False);
        }

        [Test]
        public void Commit_WhenEveryStageIsValid_PublishesAllPackagesAndRemovesRecoveryState()
        {
            YooAsset3BuildPlan plan = CreatePlan(
                CreatePackage("PackageOne", EBundledCopyOption.None),
                CreatePackage("PackageTwo", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old-one");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            WriteFile(transaction.Packages[0].OutputOperation.stage, "payload.txt", "new-one");
            WriteFile(transaction.Packages[1].OutputOperation.stage, "payload.txt", "new-two");
            transaction.SealReadyDirectories();

            transaction.Commit(() =>
            {
                Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("new-one"));
                Assert.That(ReadFile(plan.Packages[1].OutputPackageDirectory, "payload.txt"), Is.EqualTo("new-two"));
            }, () => { });

            Assert.That(
                File.Exists(Path.Combine(
                    YooAsset3PublicationTransaction.GetStateRoot(projectRoot),
                    "active.json")),
                Is.False);
        }

        [Test]
        public void RecoverPending_AfterPreparedCrash_DiscardsStagesWithoutChangingFinalTarget()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            string stage = transaction.Packages[0].OutputOperation.stage;
            WriteFile(stage, "payload.txt", "new");

            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { });

            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old"));
            Assert.That(Directory.Exists(stage), Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    YooAsset3PublicationTransaction.GetStateRoot(projectRoot),
                    "active.json")),
                Is.False);
        }

        [Test]
        public void RecoverPending_WhenConfiguredRootsChanged_UsesRootsRecordedByCentralJournal()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            string stage = transaction.Packages[0].OutputOperation.stage;
            WriteFile(stage, "payload.txt", "new");
            string originalTarget = plan.Packages[0].OutputPackageDirectory;
            string journalPath = GetJournalPath();

            buildOutputRoot = Path.Combine(projectRoot, "ChangedBuildOutput");
            bundledFileRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "ChangedYooAsset");
            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { });

            Assert.That(ReadFile(originalTarget, "payload.txt"), Is.EqualTo("old"));
            Assert.That(Directory.Exists(stage), Is.False);
            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(
                YooAsset3BuildSafety.IsStrictDescendant(
                    Path.Combine(projectRoot, ".buildpipeline"),
                    YooAsset3PublicationTransaction.GetStateRoot(projectRoot)),
                Is.True);
        }

        [TestCase(EBundledCopyOption.OnlyCopyAll)]
        [TestCase(EBundledCopyOption.OnlyCopyByTags)]
        public void Prepare_ForOnlyCopyModes_SeedsBundledWorkFromCurrentSnapshot(EBundledCopyOption option)
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", option));
            WriteOwnedPublication(plan.Packages[0], true, "preserved.bundle", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);

            transaction.Prepare();

            Assert.That(
                ReadFile(transaction.Packages[0].BundledWorkDirectory, "preserved.bundle"),
                Is.EqualTo("old-bundle"));
            transaction.Abort();
        }

        [Test]
        public void RecoverPending_WhenJournalChecksumIsCorrupt_FailsClosedAndRetainsState()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            string journalPath = Path.Combine(
                YooAsset3PublicationTransaction.GetStateRoot(projectRoot),
                "active.json");
            string journal = File.ReadAllText(journalPath);
            const string ChecksumMarker = "\"checksum\": \"";
            int checksumIndex = journal.IndexOf(ChecksumMarker, StringComparison.Ordinal);
            Assert.That(checksumIndex, Is.GreaterThanOrEqualTo(0));
            checksumIndex += ChecksumMarker.Length;
            char replacement = journal[checksumIndex] == '0' ? '1' : '0';
            journal = journal.Substring(0, checksumIndex) + replacement + journal.Substring(checksumIndex + 1);
            File.WriteAllText(journalPath, journal);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { }));

            StringAssert.Contains("journal", exception.Message.ToLowerInvariant());
            Assert.That(File.Exists(journalPath), Is.True);
        }

        [Test]
        public void CreateExecutionPlan_PreservesConcretePipelineTypeAndRedirectsOnlyPublicationPaths()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            var request = new AssetContentBuildRequest(
                BuildTarget.StandaloneWindows64,
                "1.0.0",
                projectRoot,
                null,
                BuildIncrementality.Incremental,
                true);

            YooAsset3PackageBuildPlan execution = transaction.CreateExecutionPlan(
                request,
                transaction.Packages[0]);

            Assert.That(execution.Parameters, Is.InstanceOf<RawFileBuildParameters>());
            Assert.That(
                YooAsset3BuildSafety.PathsEqual(
                    execution.OutputPackageDirectory,
                    transaction.Packages[0].OutputOperation.stage),
                Is.True);
            Assert.That(
                YooAsset3BuildSafety.PathsEqual(
                    execution.BundledPackageDirectory,
                    transaction.Packages[0].BundledWorkDirectory),
                Is.True);
            Assert.That(
                YooAsset3BuildSafety.PathsEqual(
                    execution.Parameters.GetPipelineOutputDirectory(),
                    plan.Packages[0].Parameters.GetPipelineOutputDirectory()),
                Is.True);
            transaction.Abort();
        }

        [Test]
        public void BuildLock_WhenBuildRootsDifferButBundledRootMatches_RejectsConcurrentPublication()
        {
            string firstBuildRoot = Path.Combine(testRoot, "BuildOne");
            string secondBuildRoot = Path.Combine(testRoot, "BuildTwo");
            using (YooAsset3BuildLock.Acquire(projectRoot, firstBuildRoot, bundledFileRoot))
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                {
                    using (YooAsset3BuildLock.Acquire(projectRoot, secondBuildRoot, bundledFileRoot))
                    {
                    }
                });

                StringAssert.Contains("publication roots", exception.Message);
            }

            using (YooAsset3BuildLock.Acquire(projectRoot, secondBuildRoot, bundledFileRoot))
            {
            }
        }

        [Test]
        public void BuildLock_WhenAllPublicationRootsDiffer_StillSerializesTheProjectJournal()
        {
            string firstBuildRoot = Path.Combine(projectRoot, "BuildOne");
            string secondBuildRoot = Path.Combine(projectRoot, "BuildTwo");
            string firstBundledRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "First");
            string secondBundledRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "Second");
            using (YooAsset3BuildLock.Acquire(projectRoot, firstBuildRoot, firstBundledRoot))
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                {
                    using (YooAsset3BuildLock.Acquire(projectRoot, secondBuildRoot, secondBundledRoot))
                    {
                    }
                });

                StringAssert.Contains("publication roots", exception.Message);
            }
        }

        [Test]
        public void Prepare_WhenExistingTargetContainsUnknownAuthoredFiles_FailsClosedWithoutJournal()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteFile(plan.Packages[0].OutputPackageDirectory, "authored.txt", "not-owned");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => transaction.Prepare());

            StringAssert.Contains("not a Build-owned", exception.Message);
            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "authored.txt"), Is.EqualTo("not-owned"));
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void Prepare_WhenBundledTargetIsAbsentButRootMetaExists_FailsClosedWithoutDeletingMeta()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            string targetMeta = plan.Packages[0].BundledPackageDirectory + ".meta";
            Directory.CreateDirectory(Path.GetDirectoryName(targetMeta));
            File.WriteAllText(
                targetMeta,
                "fileFormatVersion: 2\nguid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nfolderAsset: yes\n");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => transaction.Prepare());

            StringAssert.Contains("both exist or both be absent", exception.Message);
            StringAssert.Contains("aaaaaaaaaaaaaaaa", File.ReadAllText(targetMeta));
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void Commit_WhenOriginalPublicationChangesBeforeCommit_RejectsWithoutReplacingIt()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            WriteFile(transaction.Packages[0].OutputOperation.stage, "payload.txt", "new");
            transaction.SealReadyDirectories();
            WriteFile(plan.Packages[0].OutputPackageDirectory, "external.txt", "external-change");

            AggregateException exception = Assert.Throws<AggregateException>(() =>
                transaction.Commit(null, () => { }));

            StringAssert.Contains("rollback", exception.Message.ToLowerInvariant());
            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old"));
            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "external.txt"), Is.EqualTo("external-change"));
            Assert.That(File.Exists(GetJournalPath()), Is.True);
        }

        [Test]
        public void Rollback_WhenInstalledTargetWasExternallyReplaced_PreservesReplacementAndBackup()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation operation = transaction.Packages[0].OutputOperation;
            WriteFile(operation.stage, "payload.txt", "new");
            transaction.SealReadyDirectories();

            Assert.Throws<AggregateException>(() => transaction.Commit(() =>
            {
                Directory.Delete(operation.target, true);
                WriteFile(operation.target, "external.txt", "replacement");
                throw new InvalidOperationException("force rollback after external replacement");
            }, () => { }));

            Assert.That(ReadFile(operation.target, "external.txt"), Is.EqualTo("replacement"));
            Assert.That(ReadFile(operation.backup, "payload.txt"), Is.EqualTo("old"));
            Assert.That(File.Exists(GetJournalPath()), Is.True);
        }

        [Test]
        public void Commit_WhenRefreshFails_RetainsCommittedJournalUntilRecoveryRefreshSucceeds()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation operation = transaction.Packages[0].OutputOperation;
            WriteFile(operation.stage, "payload.txt", "new");
            transaction.SealReadyDirectories();

            YooAsset3CommittedPublicationException exception = Assert.Throws<YooAsset3CommittedPublicationException>(() =>
                transaction.Commit(null, () => throw new InvalidOperationException("refresh failed")));

            StringAssert.Contains("committed", exception.Message.ToLowerInvariant());
            Assert.That(ReadFile(operation.target, "payload.txt"), Is.EqualTo("new"));
            Assert.That(ReadFile(operation.backup, "payload.txt"), Is.EqualTo("old"));
            Assert.That(File.Exists(GetJournalPath()), Is.True);

            bool refreshed = false;
            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => refreshed = true);

            Assert.That(refreshed, Is.True);
            Assert.That(ReadFile(operation.target, "payload.txt"), Is.EqualTo("new"));
            Assert.That(Directory.Exists(operation.backup), Is.False);
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void RecoverPending_WhenBundledTargetWasAbsentDuringCrash_RestoresOriginalDirectoryMetaIdentity()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation operation = transaction.Packages[0].BundledOperation;
            string originalMeta = File.ReadAllText(operation.targetMeta);

            File.Copy(operation.targetMeta, operation.protectedMeta);
            Directory.Move(operation.target, operation.backup);
            File.Delete(operation.targetMeta);

            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { });

            Assert.That(ReadFile(operation.target, "payload.txt"), Is.EqualTo("old-bundle"));
            Assert.That(File.ReadAllText(operation.targetMeta), Is.EqualTo(originalMeta));
            Assert.That(Directory.Exists(operation.backup), Is.False);
            Assert.That(File.Exists(operation.protectedMeta), Is.False);
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void RecoverPending_WhenBundledTargetMetaWasExternallyReplaced_FailsClosed()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation operation = transaction.Packages[0].BundledOperation;

            File.Copy(operation.targetMeta, operation.protectedMeta);
            Directory.Move(operation.target, operation.backup);
            File.WriteAllText(
                operation.targetMeta,
                "fileFormatVersion: 2\nguid: fedcba9876543210fedcba9876543210\nfolderAsset: yes\n");

            Assert.Throws<AggregateException>(() =>
                YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { }));

            Assert.That(Directory.Exists(operation.target), Is.False);
            Assert.That(Directory.Exists(operation.backup), Is.True);
            StringAssert.Contains("fedcba9876543210", File.ReadAllText(operation.targetMeta));
            Assert.That(File.Exists(operation.protectedMeta), Is.True);
            Assert.That(File.Exists(GetJournalPath()), Is.True);
        }

        [Test]
        public void Commit_WhenRefreshFailsAfterGeneratingInitialBundledMeta_RecoveryCapturesItBeforeCleanup()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
            YooAsset3PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
            WriteFile(output.stage, "payload.txt", "new-output");
            WriteFile(bundled.stage, "payload.txt", "new-bundle");
            transaction.SealReadyDirectories();

            const string GeneratedMeta =
                "fileFormatVersion: 2\nguid: 11111111111111111111111111111111\nfolderAsset: yes\n";
            Assert.Throws<YooAsset3CommittedPublicationException>(() => transaction.Commit(null, () =>
            {
                File.WriteAllText(bundled.targetMeta, GeneratedMeta);
                throw new InvalidOperationException("refresh failed after generating meta");
            }));

            Assert.That(File.Exists(bundled.targetMeta), Is.True);
            Assert.That(File.Exists(GetJournalPath()), Is.True);
            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { });

            Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("new-bundle"));
            StringAssert.Contains("1111111111111111", File.ReadAllText(bundled.targetMeta));
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void BuildLock_WhenLockDirectoryIsReparsePoint_FailsClosed()
        {
            string fakeProjectRoot = Path.Combine(testRoot, "FakeProject");
            Directory.CreateDirectory(Path.Combine(fakeProjectRoot, "Assets"));
            string redirectedTarget = Path.Combine(fakeProjectRoot, "RedirectedLocks");
            Directory.CreateDirectory(redirectedTarget);
            string lockRoot = YooAsset3BuildLock.GetLockRoot(fakeProjectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(lockRoot));
            CreateDirectoryLink(lockRoot, redirectedTarget);
            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                {
                    using (YooAsset3BuildLock.Acquire(
                               fakeProjectRoot,
                               Path.Combine(fakeProjectRoot, "BuildOutput"),
                               Path.Combine(fakeProjectRoot, "Assets", "StreamingAssets")))
                    {
                    }
                });

                StringAssert.Contains("reparse point", exception.Message);
            }
            finally
            {
                DeleteDirectoryLink(lockRoot);
            }
        }

        [Test]
        public void Prepare_WhenTransactionStateDirectoryIsReparsePoint_FailsClosed()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            Directory.CreateDirectory(buildOutputRoot);
            string redirectedTarget = Path.Combine(testRoot, "RedirectedState");
            Directory.CreateDirectory(redirectedTarget);
            string stateRoot = YooAsset3PublicationTransaction.GetStateRoot(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(stateRoot));
            CreateDirectoryLink(stateRoot, redirectedTarget);
            try
            {
                YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => transaction.Prepare());

                StringAssert.Contains("reparse point", exception.Message);
            }
            finally
            {
                DeleteDirectoryLink(stateRoot);
            }
        }

        [Test]
        public void Registry_WhenYooAsset305IsInstalled_ResolvesTypedAdapterByRegistrationMetadata()
        {
            IAssetContentBuildAdapter adapter = BuildPipelineRegistry.ResolveContentAdapter(
                AssetContentProviderIds.YooAsset);

            Assert.That(adapter, Is.InstanceOf<YooAsset3BuildAdapter>());
            Assert.That(adapter.Priority, Is.EqualTo(100));
        }

        private YooAsset3BuildPlan CreatePlan(params YooAsset3PackageBuildPlan[] packages)
        {
            return new YooAsset3BuildPlan(
                projectRoot,
                buildOutputRoot,
                bundledFileRoot,
                packages,
                Array.Empty<string>());
        }

        private YooAsset3PackageBuildPlan CreatePackage(string packageName, EBundledCopyOption bundledCopyOption)
        {
            var profile = new YooAssetPackageProfile
            {
                packageName = packageName,
                buildPipeline = YooAssetBuildPipelineKind.RawFile,
                bundledCopyOption = ToProfileOption(bundledCopyOption),
                versionCollisionPolicy = YooAssetVersionCollisionPolicy.ReplaceExactVersion
            };
            var parameters = new RawFileBuildParameters
            {
                BuildOutputRoot = buildOutputRoot,
                BundledFileRoot = bundledFileRoot,
                BuildPipeline = EBuildPipeline.RawFileBuildPipeline.ToString(),
                BuildBundleType = (int)EBundleType.RawBundle,
                BuildTarget = BuildTarget.StandaloneWindows64,
                PackageName = packageName,
                PackageVersion = "1.0.0",
                PackageNote = "transaction-test",
                BundledCopyOption = bundledCopyOption
            };
            return new YooAsset3PackageBuildPlan(profile, parameters, new UnusedBuildPipeline(), string.Empty);
        }

        private static YooAssetBundledCopyOption ToProfileOption(EBundledCopyOption option)
        {
            switch (option)
            {
                case EBundledCopyOption.None:
                    return YooAssetBundledCopyOption.None;
                case EBundledCopyOption.OnlyCopyAll:
                    return YooAssetBundledCopyOption.OnlyCopyAll;
                case EBundledCopyOption.OnlyCopyByTags:
                    return YooAssetBundledCopyOption.OnlyCopyByTags;
                default:
                    throw new ArgumentOutOfRangeException(nameof(option), option, null);
            }
        }

        private static void WriteFile(string directory, string fileName, string content)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }

        private void WriteOwnedPublication(
            YooAsset3PackageBuildPlan package,
            bool bundled,
            string fileName,
            string content)
        {
            string directory = bundled ? package.BundledPackageDirectory : package.OutputPackageDirectory;
            string kind = bundled
                ? YooAsset3PublicationOwnership.BundledPackageKind
                : YooAsset3PublicationOwnership.PackageOutputKind;
            WriteFile(directory, fileName, content);
            if (bundled)
            {
                File.WriteAllText(
                    directory + ".meta",
                    "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nfolderAsset: yes\n");
            }
            YooAsset3PublicationOwnership.Seal(
                projectRoot,
                directory,
                kind,
                package.PackageName,
                package.PackageVersion,
                Guid.NewGuid().ToString("N"));
        }

        private string GetJournalPath()
        {
            return Path.Combine(YooAsset3PublicationTransaction.GetStateRoot(projectRoot), "active.json");
        }

        private static string ReadFile(string directory, string fileName)
        {
            return File.ReadAllText(Path.Combine(directory, fileName));
        }

        private static void CreateDirectoryLink(string linkPath, string targetPath)
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            var startInfo = new ProcessStartInfo
            {
                FileName = windows ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : "/bin/ln",
                Arguments = windows
                    ? $"/d /c mklink /J {QuoteArgument(linkPath)} {QuoteArgument(targetPath)}"
                    : $"-s {QuoteArgument(targetPath)} {QuoteArgument(linkPath)}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.That(
                    process.ExitCode,
                    Is.Zero,
                    $"Failed to create a test reparse point. Output: {standardOutput} Error: {standardError}");
            }
        }

        private static void DeleteDirectoryLink(string linkPath)
        {
            if (!Directory.Exists(linkPath) && !File.Exists(linkPath))
            {
                return;
            }

            try
            {
                Directory.Delete(linkPath, false);
            }
            catch (IOException)
            {
                File.Delete(linkPath);
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class UnusedBuildPipeline : IBuildPipeline
        {
            public BuildResult Run(BuildParameters buildParameters, bool enableLog)
            {
                throw new InvalidOperationException("The filesystem transaction tests do not execute YooAsset.");
            }
        }
    }
}
