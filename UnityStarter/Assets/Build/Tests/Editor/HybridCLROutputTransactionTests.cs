using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Build.Pipeline.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class HybridCLROutputTransactionTests
    {
        private string sandboxRoot;
        private string projectRoot;
        private string hotUpdateDirectory;
        private string aotDirectory;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter",
                "HybridCLROutputTransactionTests",
                Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(sandboxRoot, "UnityProject");
            hotUpdateDirectory = Path.Combine(projectRoot, "Assets", "Generated", "HotUpdate");
            aotDirectory = Path.Combine(projectRoot, "Assets", "Generated", "AOT");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Temp"));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(sandboxRoot) && Directory.Exists(sandboxRoot))
            {
                string expectedParent = Path.GetFullPath(Path.Combine(
                    Path.GetTempPath(),
                    "UnityStarter",
                    "HybridCLROutputTransactionTests"));
                string candidate = Path.GetFullPath(sandboxRoot);
                Assert.That(Path.GetDirectoryName(candidate), Is.EqualTo(expectedParent));
                Assert.That(Guid.TryParseExact(Path.GetFileName(candidate), "N", out _), Is.True);
                Directory.Delete(sandboxRoot, recursive: true);
            }
        }

        [Test]
        public void Commit_ReplacesAllOutputsAndWritesOwnershipManifests()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageSingleArtifact(transaction, HybridCLRBuilder.HotUpdateOutputRole, "Game.dll.bytes", "hot-v1");
                StageSingleArtifact(transaction, HybridCLRBuilder.AOTOutputRole, "mscorlib.dll.bytes", "aot-v1");
                transaction.Commit();
            }

            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("hot-v1"));
            Assert.That(File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")), Is.EqualTo("aot-v1"));
            Assert.That(
                File.Exists(Path.Combine(hotUpdateDirectory, HybridCLROutputTransaction.OwnershipManifestFileName)),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(aotDirectory, HybridCLROutputTransaction.OwnershipManifestFileName)),
                Is.True);
            Assert.That(File.Exists(hotUpdateDirectory + ".meta"), Is.True);
            string manifest = File.ReadAllText(Path.Combine(
                hotUpdateDirectory,
                HybridCLROutputTransaction.OwnershipManifestFileName));
            StringAssert.Contains("\"formatVersion\": 1", manifest);
            StringAssert.Contains("\"transactionId\"", manifest);
            StringAssert.Contains("\"size\"", manifest);
            StringAssert.Contains("\"sha256\"", manifest);
            Assert.DoesNotThrow(() => HybridCLROutputTransaction.ValidateExistingOutputs(targets));
        }

        [Test]
        public void Commit_PreservesMetaForSameNamedArtifact()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "v1");
            string metaPath = Path.Combine(hotUpdateDirectory, "Game.dll.bytes.meta");
            string originalMeta = File.ReadAllText(metaPath);

            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageSingleArtifact(transaction, HybridCLRBuilder.HotUpdateOutputRole, "Game.dll.bytes", "v2");
                StageSingleArtifact(transaction, HybridCLRBuilder.AOTOutputRole, "mscorlib.dll.bytes", "v2");
                transaction.Commit();
            }

            Assert.That(File.ReadAllText(metaPath), Is.EqualTo(originalMeta));
            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("v2"));
        }

        [Test]
        public void ActivateForDownstream_WhenRunFails_RestoresExactPreviousOutputs()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");

            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                transaction.ActivateForDownstream();

                Assert.That(
                    File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")),
                    Is.EqualTo("new"));
            }

            Assert.That(
                File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")),
                Is.EqualTo("old"));
            Assert.That(
                File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")),
                Is.EqualTo("old"));
            Assert.That(
                File.Exists(HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.False);
        }

        [Test]
        public void ActivateForDownstream_WhenTerminalBarrierCommits_KeepsNewOutputs()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");
            HybridCLROutputTransaction transaction =
                HybridCLROutputTransaction.Begin(projectRoot, targets);
            try
            {
                StageOutputs(transaction, "new");
                transaction.ActivateForDownstream();
                BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                    projectRoot,
                    "hybridclr-test-run",
                    new IBuildDeferredPublication[] { transaction });

                transaction.Publish();
                barrier.CommitDecision();
                transaction.Complete();
                transaction.Dispose();
                transaction = null;
                barrier.Complete();
            }
            finally
            {
                transaction?.Dispose();
            }

            Assert.That(
                File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")),
                Is.EqualTo("new"));
            Assert.That(
                File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")),
                Is.EqualTo("new"));
            Assert.That(
                File.Exists(HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.False);
            Assert.That(
                Directory.Exists(BuildPublicationBarrier.GetStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void Begin_NonEmptyDirectoryWithoutOwnershipManifest_FailsClosed()
        {
            Directory.CreateDirectory(hotUpdateDirectory);
            string businessAsset = Path.Combine(hotUpdateDirectory, "BusinessData.bytes");
            File.WriteAllText(businessAsset, "preserve");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                HybridCLROutputTransaction.Begin(projectRoot, CreateTargets()));

            StringAssert.Contains("Build-exclusive", exception.Message);
            Assert.That(File.ReadAllText(businessAsset), Is.EqualTo("preserve"));
        }

        [Test]
        public void Begin_ManagedDirectoryWithUndeclaredFile_FailsClosed()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "v1");
            string unknownFile = Path.Combine(hotUpdateDirectory, "README.txt");
            File.WriteAllText(unknownFile, "preserve");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                HybridCLROutputTransaction.Begin(projectRoot, targets));

            StringAssert.Contains("undeclared file", exception.Message);
            Assert.That(File.ReadAllText(unknownFile), Is.EqualTo("preserve"));
        }

        [Test]
        public void Commit_WhenSecondPublicationFails_RollsBackFirstOutput()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");

            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageSingleArtifact(transaction, HybridCLRBuilder.HotUpdateOutputRole, "Game.dll.bytes", "new");
                StageSingleArtifact(transaction, HybridCLRBuilder.AOTOutputRole, "mscorlib.dll.bytes", "new");

                IOException exception = Assert.Throws<IOException>(() => transaction.Commit(role =>
                {
                    if (role == HybridCLRBuilder.AOTOutputRole)
                    {
                        throw new IOException("Injected publication failure.");
                    }
                }));
                StringAssert.Contains("Injected publication failure", exception.Message);
            }

            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")), Is.EqualTo("old"));
            Assert.DoesNotThrow(() => HybridCLROutputTransaction.ValidateExistingOutputs(targets));
        }

        [Test]
        public void CompleteStaging_MissingDeclaredArtifact_DoesNotModifyExistingOutputs()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");

            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                Assert.Throws<InvalidOperationException>(() => transaction.CompleteStaging(
                    HybridCLRBuilder.HotUpdateOutputRole,
                    new[] { "Missing.dll.bytes" }));
            }

            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")), Is.EqualTo("old"));
        }

        [Test]
        public void Begin_OverlappingOutputDirectories_Throws()
        {
            var targets = new[]
            {
                new HybridCLROutputTarget(HybridCLRBuilder.HotUpdateOutputRole, hotUpdateDirectory),
                new HybridCLROutputTarget(
                    HybridCLRBuilder.AOTOutputRole,
                    Path.Combine(hotUpdateDirectory, "Nested"))
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                HybridCLROutputTransaction.Begin(projectRoot, targets));
            StringAssert.Contains("must not overlap", exception.Message);
        }

        [Test]
        public void Begin_WhileAnotherTransactionIsActive_Throws()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            using (HybridCLROutputTransaction first =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    HybridCLROutputTransaction.Begin(projectRoot, targets));
                StringAssert.Contains("Another HybridCLR output transaction is active", exception.Message);
            }
        }

        [Test]
        public void RecoverPending_WhenNoStateExists_DoesNotCreateTransactionDirectories()
        {
            Assert.That(HybridCLROutputTransaction.RecoverPending(projectRoot), Is.False);
            Assert.That(Directory.Exists(Path.Combine(projectRoot, ".buildpipeline")), Is.False);
        }

        [Test]
        public void EnsureNoPendingRecovery_WhenNoStateExists_IsZeroWrite()
        {
            string stateRoot = Path.GetDirectoryName(
                HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot));

            Assert.That(Directory.Exists(stateRoot), Is.False);
            Assert.DoesNotThrow(() =>
                HybridCLROutputTransaction.EnsureNoPendingRecovery(projectRoot));
            Assert.That(Directory.Exists(stateRoot), Is.False);
        }

        [Test]
        public void GetStagingFilePath_PortableReservedName_Throws()
        {
            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, CreateTargets()))
            {
                Assert.Throws<InvalidOperationException>(() => transaction.GetStagingFilePath(
                    HybridCLRBuilder.HotUpdateOutputRole,
                    "CON.dll.bytes"));
            }
        }

        [Test]
        public void GetFinalFilePath_WhenArtifactExceedsWin32MaxPathBudget_ThrowsBeforeWrite()
        {
            int fileNameLength = BuildPathPolicy.Win32MaxPathCharacters
                - Path.GetFullPath(hotUpdateDirectory).Length;
            const string extension = ".bytes";
            Assert.That(fileNameLength, Is.GreaterThan(extension.Length));
            string fileName = new string(
                                  'p',
                                  fileNameLength - extension.Length)
                              + extension;

            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, CreateTargets()))
            {
                Assert.Throws<PathTooLongException>(() =>
                    transaction.GetFinalFilePath(
                        HybridCLRBuilder.HotUpdateOutputRole,
                        fileName));
            }
        }

        [Test]
        public void RecoverPending_AfterBackupMoveCrash_RestoresOriginalOutputsAndRemovesState()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");

            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                Assert.Catch<IOException>(() => transaction.CommitForTesting((checkpoint, role) =>
                    checkpoint == HybridCLROutputTransaction.CrashCheckpoint.AfterBackupMoveBeforeJournal
                    && role == HybridCLRBuilder.HotUpdateOutputRole));
            }

            string journalPath = HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot);
            Assert.That(File.Exists(journalPath), Is.True);
            string backupDirectory = Directory.GetDirectories(
                Path.GetDirectoryName(hotUpdateDirectory),
                ".buildpipeline-hybridclr-*.backup",
                SearchOption.TopDirectoryOnly).Single();
            Assert.That(
                Path.GetDirectoryName(backupDirectory),
                Is.EqualTo(Path.GetDirectoryName(hotUpdateDirectory)));
            StringAssert.DoesNotContain(
                Path.DirectorySeparatorChar + ".buildpipeline" + Path.DirectorySeparatorChar,
                backupDirectory);
            Assert.That(HybridCLROutputTransaction.RecoverPending(projectRoot, targets), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")), Is.EqualTo("old"));
            Assert.That(File.Exists(journalPath), Is.False);
        }

        [Test]
        public void RecoverPending_WhenRootMetaDisappearsDuringDirectoryAbsence_RestoresExactGuid()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");
            string rootMetaPath = hotUpdateDirectory + ".meta";
            string originalRootMeta = File.ReadAllText(rootMetaPath);

            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                Assert.Catch<IOException>(() => transaction.CommitForTesting((checkpoint, role) =>
                    checkpoint == HybridCLROutputTransaction.CrashCheckpoint.AfterBackupMoveBeforeJournal
                    && role == HybridCLRBuilder.HotUpdateOutputRole));
            }

            Assert.That(Directory.Exists(hotUpdateDirectory), Is.False);
            File.Delete(rootMetaPath);
            Assert.That(HybridCLROutputTransaction.RecoverPending(projectRoot), Is.True);

            Assert.That(Directory.Exists(hotUpdateDirectory), Is.True);
            Assert.That(File.ReadAllText(rootMetaPath), Is.EqualTo(originalRootMeta));
            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("old"));
        }

        [Test]
        public void RecoverPending_AfterNewRootMetaInstallCrash_RemovesSidecarAndOutputs()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                Assert.Catch<IOException>(() => transaction.CommitForTesting((checkpoint, role) =>
                    checkpoint == HybridCLROutputTransaction.CrashCheckpoint.AfterRootMetaInstallMoveBeforeJournal
                    && role == HybridCLRBuilder.HotUpdateOutputRole));
            }

            Assert.That(Directory.Exists(hotUpdateDirectory), Is.False);
            Assert.That(File.Exists(hotUpdateDirectory + ".meta"), Is.True);
            Assert.That(HybridCLROutputTransaction.RecoverPending(projectRoot), Is.True);
            Assert.That(Directory.Exists(hotUpdateDirectory), Is.False);
            Assert.That(Directory.Exists(aotDirectory), Is.False);
            Assert.That(File.Exists(hotUpdateDirectory + ".meta"), Is.False);
            Assert.That(File.Exists(aotDirectory + ".meta"), Is.False);
        }

        [Test]
        public void Commit_WhenInitialArtifactIsExternallyReplaced_FailsClosedAndRetainsEvidence()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");
            string artifact = Path.Combine(hotUpdateDirectory, "Game.dll.bytes");
            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                File.WriteAllText(artifact, "external replacement");

                AggregateException exception = Assert.Throws<AggregateException>(() => transaction.Commit());
                StringAssert.Contains("identity changed", exception.ToString().ToLowerInvariant());
            }

            Assert.That(File.ReadAllText(artifact), Is.EqualTo("external replacement"));
            Assert.That(
                File.Exists(HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.True);
            Assert.That(GetTransactionDirectories(), Has.Length.EqualTo(1));
        }

        [Test]
        public void Commit_WhenStagedArtifactIsExternallyReplaced_FailsClosedAndRetainsEvidence()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");
            string stagedArtifact;
            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                stagedArtifact = transaction.GetStagingFilePath(
                    HybridCLRBuilder.HotUpdateOutputRole,
                    "Game.dll.bytes");
                File.WriteAllText(stagedArtifact, "external replacement");

                AggregateException exception = Assert.Throws<AggregateException>(() => transaction.Commit());
                StringAssert.Contains("identity changed", exception.ToString().ToLowerInvariant());
            }

            Assert.That(File.ReadAllText(stagedArtifact), Is.EqualTo("external replacement"));
            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("old"));
            Assert.That(
                File.Exists(HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.True);
            Assert.That(GetTransactionDirectories(), Has.Length.EqualTo(1));
        }

        [Test]
        public void RecoverPending_AfterConfigurationPathsChange_UsesJournalOwnedTargets()
        {
            HybridCLROutputTarget[] oldTargets = CreateTargets();
            SeedManagedOutputs(oldTargets, "old");
            LeaveCrashedTransaction(oldTargets);
            string journalPath = HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot);
            StringAssert.StartsWith(
                Path.Combine(projectRoot, ".buildpipeline", "transactions", "hybridclr"),
                Path.GetDirectoryName(journalPath));
            StringAssert.DoesNotContain(
                Path.Combine(projectRoot, "Temp") + Path.DirectorySeparatorChar,
                journalPath);

            var newTargets = new[]
            {
                new HybridCLROutputTarget(
                    HybridCLRBuilder.HotUpdateOutputRole,
                    Path.Combine(projectRoot, "Assets", "GeneratedV2", "HotUpdate")),
                new HybridCLROutputTarget(
                    HybridCLRBuilder.AOTOutputRole,
                    Path.Combine(projectRoot, "Assets", "GeneratedV2", "AOT"))
            };

            Assert.That(HybridCLROutputTransaction.RecoverPending(projectRoot), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")), Is.EqualTo("old"));
            using (HybridCLROutputTransaction ignored =
                   HybridCLROutputTransaction.Begin(projectRoot, newTargets))
            {
            }
        }

        [Test]
        public void Runner_WhenHybridClrIsDisabled_DoesNotRecoverJournalImplicitly()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");
            LeaveCrashedTransaction(targets);

            string journalPath = HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot);
            byte[] journalBeforeRetry = File.ReadAllBytes(journalPath);
            string[] transactionDirectoriesBeforeRetry = GetTransactionDirectories();

            string buildRoot = Path.Combine(projectRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            var request = new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.test.product",
                "Assets/Generated/VersionInfo.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                UnityEditor.BuildTarget.StandaloneWindows64,
                UnityEditor.Build.NamedBuildTarget.Standalone,
                UnityEditor.ScriptingImplementation.Mono2x,
                projectRoot,
                buildRoot,
                Path.Combine(outputDirectory, "TestProduct.exe"),
                outputDirectory,
                outputIsFolder: false,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: "1.0.0",
                identityOverride: BuildIdentityOverride.Empty,
                steps: new[]
                {
                    new BuildStepInvocation(BuildStepTypeIds.Player, BuildStepTypeIds.Player)
                });

            BuildRunResult result = new BuildPipelineRunner(
                    eventSink: new NoOpEventSink(),
                    trustedProjectRoot: projectRoot,
                    isEditorBusy: () => false,
                    versionResolver: BuildTestVersionResolver.ResolveClean)
                .Run(request);

            Assert.That(result.Succeeded, Is.False);
            StringAssert.Contains("Build workspace status is 'RecoveryRequired'", result.Failure.ToString());
            Assert.That(File.ReadAllBytes(journalPath), Is.EqualTo(journalBeforeRetry));
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(GetTransactionDirectories(), Is.EqualTo(transactionDirectoriesBeforeRetry));
        }

        [Test]
        public void RecoverPending_AfterInstallCrashWithInitiallyAbsentOutputs_RemovesPublishedOutputs()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                Assert.Catch<IOException>(() => transaction.CommitForTesting((checkpoint, role) =>
                    checkpoint == HybridCLROutputTransaction.CrashCheckpoint.AfterInstallMoveBeforeJournal
                    && role == HybridCLRBuilder.HotUpdateOutputRole));
            }

            Assert.That(Directory.Exists(hotUpdateDirectory), Is.True);
            Assert.That(HybridCLROutputTransaction.RecoverPending(projectRoot, targets), Is.True);
            Assert.That(Directory.Exists(hotUpdateDirectory), Is.False);
            Assert.That(Directory.Exists(aotDirectory), Is.False);
            Assert.That(File.Exists(hotUpdateDirectory + ".meta"), Is.False);
            Assert.That(File.Exists(aotDirectory + ".meta"), Is.False);
            Assert.That(
                File.Exists(HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.False);
            Assert.That(GetTransactionDirectories(), Is.Empty);
        }

        [Test]
        public void Begin_AfterSecondInstallMoveCrash_FailsClosedUntilExplicitRecovery()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");

            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                Assert.Catch<IOException>(() => transaction.CommitForTesting((checkpoint, role) =>
                    checkpoint == HybridCLROutputTransaction.CrashCheckpoint.AfterInstallMoveBeforeJournal
                    && role == HybridCLRBuilder.AOTOutputRole));
            }

            string journalPath = HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot);
            byte[] journalBeforeRetry = File.ReadAllBytes(journalPath);
            string hotUpdateBeforeRetry = File.ReadAllText(
                Path.Combine(hotUpdateDirectory, "Game.dll.bytes"));
            string aotBeforeRetry = File.ReadAllText(
                Path.Combine(aotDirectory, "mscorlib.dll.bytes"));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                HybridCLROutputTransaction.Begin(projectRoot, targets));

            StringAssert.Contains("Pending HybridCLR output recovery", exception.Message);
            Assert.That(File.ReadAllBytes(journalPath), Is.EqualTo(journalBeforeRetry));
            Assert.That(
                File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")),
                Is.EqualTo(hotUpdateBeforeRetry));
            Assert.That(
                File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")),
                Is.EqualTo(aotBeforeRetry));

            Assert.That(HybridCLROutputTransaction.RecoverPending(projectRoot, targets), Is.True);
            using (HybridCLROutputTransaction next =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("old"));
                Assert.That(File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")), Is.EqualTo("old"));
            }

            Assert.That(
                File.Exists(journalPath),
                Is.False);
        }

        [Test]
        public void RecoverPending_AfterCommittedJournalCrash_KeepsNewOutputsAndFinishesCleanup()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");

            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                Assert.Catch<IOException>(() => transaction.CommitForTesting((checkpoint, role) =>
                    checkpoint == HybridCLROutputTransaction.CrashCheckpoint.AfterCommittedJournalBeforeCleanup));
                Assert.That(transaction.OutputsCommitted, Is.True);
            }

            Assert.That(HybridCLROutputTransaction.RecoverPending(projectRoot, targets), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")), Is.EqualTo("new"));
            Assert.That(
                File.Exists(HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.False);
        }

        [Test]
        public void RecoverPending_WhenActiveJournalIsMissing_RecoversFromValidTemporaryCandidate()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");
            LeaveCrashedTransaction(targets);
            string journalPath = HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot);
            string scratchRoot = GetTransactionDirectories().Single();
            string transactionId = Path.GetFileName(scratchRoot);
            string journal = File.ReadAllText(journalPath, Encoding.UTF8);
            Match sequenceMatch = Regex.Match(journal, "\\\"sequence\\\"\\s*:\\s*(\\d+)");
            Assert.That(sequenceMatch.Success, Is.True);
            string temporaryPath = journalPath
                + ".tmp-"
                + transactionId
                + "-"
                + sequenceMatch.Groups[1].Value
                + "-"
                + Guid.NewGuid().ToString("N");
            File.Move(journalPath, temporaryPath);

            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(HybridCLROutputTransaction.RecoverPending(projectRoot, targets), Is.True);

            Assert.That(File.ReadAllText(Path.Combine(hotUpdateDirectory, "Game.dll.bytes")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(aotDirectory, "mscorlib.dll.bytes")), Is.EqualTo("old"));
            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(File.Exists(temporaryPath), Is.False);
            Assert.That(GetTransactionDirectories(), Is.Empty);
        }

        [Test]
        public void RecoverPending_WhenJournalChecksumIsCorrupt_FailsClosedAndRetainsRecoveryState()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");
            LeaveCrashedTransaction(targets);
            string journalPath = HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot);
            string json = File.ReadAllText(journalPath, Encoding.UTF8);
            const string ChecksumMarker = "\"checksum\": \"";
            int checksumIndex = json.IndexOf(ChecksumMarker, StringComparison.Ordinal);
            Assert.That(checksumIndex, Is.GreaterThanOrEqualTo(0));
            checksumIndex += ChecksumMarker.Length;
            char replacement = json[checksumIndex] == '0' ? '1' : '0';
            json = json.Substring(0, checksumIndex) + replacement + json.Substring(checksumIndex + 1);
            File.WriteAllText(journalPath, json, new UTF8Encoding(false));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                HybridCLROutputTransaction.RecoverPending(projectRoot, targets));

            StringAssert.Contains("checksum", exception.Message.ToLowerInvariant());
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(GetTransactionDirectories().Length, Is.EqualTo(1));
        }

        [Test]
        public void RecoverPending_WhenJournalExceedsBound_FailsClosedAndRetainsRecoveryState()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");
            LeaveCrashedTransaction(targets);
            string journalPath = HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot);
            File.WriteAllBytes(journalPath, new byte[4 * 1024 * 1024 + 1]);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                HybridCLROutputTransaction.RecoverPending(projectRoot, targets));

            StringAssert.Contains("size", exception.Message.ToLowerInvariant());
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(GetTransactionDirectories().Length, Is.EqualTo(1));
        }

        [Test]
        public void RecoverPending_WhenTargetStageAndBackupExist_FailsClosedWithoutDeletingAnyCopy()
        {
            HybridCLROutputTarget[] targets = CreateTargets();
            SeedManagedOutputs(targets, "old");
            LeaveCrashedTransaction(targets);
            string scratchRoot = GetTransactionDirectories().Single();
            string hotStage = Directory.GetDirectories(
                scratchRoot,
                "staging-000-HotUpdate",
                SearchOption.TopDirectoryOnly).Single();
            CopyDirectory(hotStage, hotUpdateDirectory);

            AggregateException exception = Assert.Throws<AggregateException>(() =>
                HybridCLROutputTransaction.RecoverPending(projectRoot, targets));

            StringAssert.Contains("ambiguous", exception.ToString().ToLowerInvariant());
            Assert.That(Directory.Exists(hotUpdateDirectory), Is.True);
            Assert.That(Directory.Exists(hotStage), Is.True);
            string backupParent = Path.GetDirectoryName(hotUpdateDirectory);
            Assert.That(
                Directory.GetDirectories(
                    backupParent,
                    ".buildpipeline-hybridclr-*.backup",
                    SearchOption.TopDirectoryOnly).Length,
                Is.EqualTo(1));
            Assert.That(
                File.Exists(HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.True);
        }

        [Test]
        public void Begin_WhenDetachedGuidScratchExistsWithoutJournal_FailsClosedAndPreservesIt()
        {
            string journalPath = HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot);
            string scratchParent = Path.GetDirectoryName(journalPath);
            string detached = Path.Combine(scratchParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(detached);
            string evidence = Path.Combine(detached, "evidence.txt");
            File.WriteAllText(evidence, "preserve");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                HybridCLROutputTransaction.Begin(projectRoot, CreateTargets()));

            StringAssert.Contains("Detached", exception.Message);
            Assert.That(File.ReadAllText(evidence), Is.EqualTo("preserve"));
        }

        private HybridCLROutputTarget[] CreateTargets()
        {
            return new[]
            {
                new HybridCLROutputTarget(HybridCLRBuilder.HotUpdateOutputRole, hotUpdateDirectory),
                new HybridCLROutputTarget(HybridCLRBuilder.AOTOutputRole, aotDirectory)
            };
        }

        private void SeedManagedOutputs(HybridCLROutputTarget[] targets, string content)
        {
            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageSingleArtifact(transaction, HybridCLRBuilder.HotUpdateOutputRole, "Game.dll.bytes", content);
                StageSingleArtifact(transaction, HybridCLRBuilder.AOTOutputRole, "mscorlib.dll.bytes", content);
                transaction.Commit();
            }
        }

        private void LeaveCrashedTransaction(HybridCLROutputTarget[] targets)
        {
            using (HybridCLROutputTransaction transaction =
                   HybridCLROutputTransaction.Begin(projectRoot, targets))
            {
                StageOutputs(transaction, "new");
                Assert.Catch<IOException>(() => transaction.CommitForTesting((checkpoint, role) =>
                    checkpoint == HybridCLROutputTransaction.CrashCheckpoint.AfterBackupMoveBeforeJournal
                    && role == HybridCLRBuilder.HotUpdateOutputRole));
            }
        }

        private void StageOutputs(HybridCLROutputTransaction transaction, string content)
        {
            StageSingleArtifact(
                transaction,
                HybridCLRBuilder.HotUpdateOutputRole,
                "Game.dll.bytes",
                content);
            StageSingleArtifact(
                transaction,
                HybridCLRBuilder.AOTOutputRole,
                "mscorlib.dll.bytes",
                content);
        }

        private sealed class NoOpEventSink : IBuildEventSink
        {
            public void RunStarted(
                BuildExecutionContext context,
                System.Collections.Generic.IReadOnlyList<CompiledBuildStep> plan) { }

            public void StepStarted(BuildExecutionContext context, CompiledBuildStep step) { }
            public void StepFinished(BuildExecutionContext context, BuildStepResult result) { }
            public void RunFinished(BuildExecutionContext context, BuildRunResult result) { }
        }

        private string[] GetTransactionDirectories()
        {
            string scratchParent = Path.GetDirectoryName(
                HybridCLROutputTransaction.GetActiveJournalPathForTesting(projectRoot));
            return Directory.Exists(scratchParent)
                ? Directory.GetDirectories(scratchParent, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _))
                    .ToArray()
                : Array.Empty<string>();
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
            }
        }

        private static void StageSingleArtifact(
            HybridCLROutputTransaction transaction,
            string role,
            string fileName,
            string content)
        {
            File.WriteAllText(transaction.GetStagingFilePath(role, fileName), content);
            string listFileName = role == HybridCLRBuilder.HotUpdateOutputRole
                ? "HotUpdate.bytes"
                : "AOT.bytes";
            File.WriteAllText(transaction.GetStagingFilePath(role, listFileName), "{}");
            transaction.CompleteStaging(role, new[] { fileName, listFileName });
        }
    }
}
