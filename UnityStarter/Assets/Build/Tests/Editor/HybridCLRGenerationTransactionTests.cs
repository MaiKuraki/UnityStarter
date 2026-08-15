using System;
using System.IO;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class HybridCLRGenerationTransactionTests
    {
        private string sandboxRoot;
        private string projectRoot;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter",
                "HybridCLRGenerationTransactionTests",
                Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(sandboxRoot, "UnityProject");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Library"));
        }

        [TearDown]
        public void TearDown()
        {
            if (string.IsNullOrWhiteSpace(sandboxRoot) || !Directory.Exists(sandboxRoot))
            {
                return;
            }

            string expectedParent = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "UnityStarter",
                "HybridCLRGenerationTransactionTests"));
            string candidate = Path.GetFullPath(sandboxRoot);
            Assert.That(Path.GetDirectoryName(candidate), Is.EqualTo(expectedParent));
            Assert.That(Guid.TryParseExact(Path.GetFileName(candidate), "N", out _), Is.True);
            Directory.Delete(candidate, recursive: true);
        }

        [Test]
        public void Dispose_WhenGenerationFails_RestoresFilesDirectoriesAndMeta()
        {
            string hotDirectory = SeedDirectory("HybridCLRData/HotUpdateDlls/Android", "old-dll");
            string linkFile = SeedFile("Assets/HybridCLRGenerate/link.xml", "old-link");
            string linkMeta = SeedFile("Assets/HybridCLRGenerate/link.xml.meta", "old-meta");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddMirrorDirectory(hotDirectory);
            plan.AddSnapshotFile(linkFile);
            plan.AddSnapshotFile(linkMeta);

            using (HybridCLRGenerationTransaction transaction =
                   HybridCLRGenerationTransaction.Begin(plan))
            {
                File.WriteAllText(Path.Combine(hotDirectory, "Game.dll"), "new-dll");
                File.WriteAllText(linkFile, "new-link");
                File.WriteAllText(linkMeta, "new-meta");
            }

            Assert.That(
                File.ReadAllText(Path.Combine(hotDirectory, "Game.dll")),
                Is.EqualTo("old-dll"));
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
            Assert.That(File.ReadAllText(linkMeta), Is.EqualTo("old-meta"));
            Assert.That(
                File.Exists(HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.False);
        }

        [Test]
        public void CommitForTesting_KeepsGeneratedStateAndRemovesJournal()
        {
            string hotDirectory = SeedDirectory("HybridCLRData/HotUpdateDlls/Android", "old");
            string methodBridge = SeedFile(
                "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/hybridclr/generated/MethodBridge.cpp",
                "old-bridge");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddMirrorDirectory(hotDirectory);
            plan.AddSnapshotFile(methodBridge);

            using (HybridCLRGenerationTransaction transaction =
                   HybridCLRGenerationTransaction.Begin(plan))
            {
                File.WriteAllText(Path.Combine(hotDirectory, "Game.dll"), "new");
                File.WriteAllText(methodBridge, "new-bridge");
                transaction.CommitForTesting();
            }

            Assert.That(
                File.ReadAllText(Path.Combine(hotDirectory, "Game.dll")),
                Is.EqualTo("new"));
            Assert.That(File.ReadAllText(methodBridge), Is.EqualTo("new-bridge"));
            Assert.That(
                File.Exists(HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.False);
        }

        [Test]
        public void Begin_WhenProcessStopsAfterDirectoryBackup_RecoveryRestoresOriginal()
        {
            string hotDirectory = SeedDirectory("HybridCLRData/HotUpdateDlls/Android", "old");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddMirrorDirectory(hotDirectory);

            Assert.Throws<HybridCLRGenerationTransaction.SimulatedProcessCrashException>(() =>
                HybridCLRGenerationTransaction.BeginForTesting(
                    plan,
                    (checkpoint, _) => checkpoint
                        == HybridCLRGenerationTransaction.CrashCheckpoint.AfterBackupMutationBeforeJournal));

            Assert.That(
                File.Exists(HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.True);
            Assert.That(
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out bool assetsChanged),
                Is.True);
            Assert.That(assetsChanged, Is.False);
            Assert.That(
                File.ReadAllText(Path.Combine(hotDirectory, "Game.dll")),
                Is.EqualTo("old"));
        }

        [Test]
        public void RecoverPending_AfterActiveGenerationCrash_RollsBackPartialOutputs()
        {
            string strippedDirectory = SeedDirectory(
                "HybridCLRData/AssembliesPostIl2CppStrip/Android",
                "old-aot");
            string aotReference = SeedFile(
                "Assets/HybridCLRGenerate/AOTGenericReferences.cs",
                "old-reference");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddMirrorDirectory(strippedDirectory);
            plan.AddSnapshotFile(aotReference);

            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(Path.Combine(strippedDirectory, "mscorlib.dll"), "partial-aot");
            File.WriteAllText(aotReference, "partial-reference");
            transaction.AbandonForTesting();

            Assert.That(
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out bool assetsChanged),
                Is.True);
            Assert.That(assetsChanged, Is.True);
            Assert.That(
                File.ReadAllText(Path.Combine(strippedDirectory, "Game.dll")),
                Is.EqualTo("old-aot"));
            Assert.That(File.Exists(Path.Combine(strippedDirectory, "mscorlib.dll")), Is.False);
            Assert.That(File.ReadAllText(aotReference), Is.EqualTo("old-reference"));
        }

        [Test]
        public void RecoverPending_AfterCommittedJournalCrash_KeepsGeneratedOutputs()
        {
            string methodBridge = SeedFile(
                "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/hybridclr/generated/MethodBridge.cpp",
                "old");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(methodBridge);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(methodBridge, "new");

            Assert.Throws<HybridCLRGenerationTransaction.SimulatedProcessCrashException>(() =>
                transaction.CommitForTesting((checkpoint, _) => checkpoint
                    == HybridCLRGenerationTransaction.CrashCheckpoint.AfterCommittedJournalBeforeCleanup));
            transaction.Dispose();

            Assert.That(
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out bool assetsChanged),
                Is.True);
            Assert.That(assetsChanged, Is.False);
            Assert.That(File.ReadAllText(methodBridge), Is.EqualTo("new"));
        }

        [Test]
        public void Dispose_WhenGeneratedAssetParentWasAbsent_RemovesGeneratedResidue()
        {
            string generatedFile = Path.Combine(
                projectRoot,
                "Assets",
                "HybridCLRGenerate",
                "link.xml");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddGeneratedAssetFile(generatedFile);

            using (HybridCLRGenerationTransaction transaction =
                   HybridCLRGenerationTransaction.Begin(plan))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(generatedFile));
                File.WriteAllText(generatedFile, "generated");
                File.WriteAllText(generatedFile + ".meta", "file-meta");
                File.WriteAllText(
                    Path.GetDirectoryName(generatedFile) + ".meta",
                    "folder-meta");
            }

            Assert.That(File.Exists(generatedFile), Is.False);
            Assert.That(File.Exists(generatedFile + ".meta"), Is.False);
            Assert.That(Directory.Exists(Path.GetDirectoryName(generatedFile)), Is.False);
            Assert.That(File.Exists(Path.GetDirectoryName(generatedFile) + ".meta"), Is.False);
        }

        [Test]
        public void RecoverPending_WhenJournalIsTampered_FailsClosedAndKeepsEvidence()
        {
            string linkFile = SeedFile("Assets/HybridCLRGenerate/link.xml", "old");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(linkFile, "new");
            transaction.AbandonForTesting();

            string journalPath =
                HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot);
            string journal = File.ReadAllText(journalPath);
            File.WriteAllText(
                journalPath,
                journal.Replace("\"phase\": \"Active\"", "\"phase\": \"Committed\""));

            Assert.Throws<InvalidDataException>(() =>
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out _));
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("new"));
            Assert.That(File.Exists(journalPath), Is.True);
        }

        [Test]
        public void Begin_WhenDetachedScratchExists_FailsClosedAndKeepsEvidence()
        {
            string linkFile = SeedFile("Assets/HybridCLRGenerate/link.xml", "old");
            string stateRoot = Path.GetDirectoryName(
                HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot));
            string detached = Path.Combine(stateRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(detached);
            File.WriteAllText(Path.Combine(detached, "backup-000"), "evidence");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);

            Assert.Throws<InvalidDataException>(() =>
                HybridCLRGenerationTransaction.Begin(plan));
            Assert.That(File.ReadAllText(Path.Combine(detached, "backup-000")), Is.EqualTo("evidence"));
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old"));
        }

        [Test]
        public void RecoverPending_WhenFileBackupIsCorrupt_FailsBeforeDisplacingCurrentTarget()
        {
            string linkFile = SeedFile("Assets/HybridCLRGenerate/link.xml", "old");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(linkFile, "generated");
            transaction.AbandonForTesting();

            string stateRoot = Path.GetDirectoryName(
                HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot));
            string[] scratchDirectories = Directory.GetDirectories(stateRoot);
            Assert.That(scratchDirectories, Has.Length.EqualTo(1));
            File.WriteAllText(Path.Combine(scratchDirectories[0], "backup-000"), "corrupt");

            Assert.Catch<IOException>(() =>
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out _));
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("generated"));
            Assert.That(
                File.Exists(HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.True);
        }

        [Test]
        public void RecoverPending_JournalWithoutCurrentDocumentType_IsRejectedAndPreserved()
        {
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(linkFile, "generated-link");
            transaction.AbandonForTesting();

            string journalPath =
                HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot);
            string journal = File.ReadAllText(journalPath);
            string unsupported = journal.Replace(
                "  \"documentType\": \"hybridclr-generation-transaction\",\r\n",
                string.Empty);
            if (string.Equals(unsupported, journal, StringComparison.Ordinal))
            {
                unsupported = journal.Replace(
                    "  \"documentType\": \"hybridclr-generation-transaction\",\n",
                    string.Empty);
            }

            Assert.That(unsupported, Is.Not.EqualTo(journal));
            File.WriteAllText(journalPath, unsupported);

            Assert.Catch(() =>
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out _));
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("generated-link"));
            Assert.That(File.Exists(journalPath), Is.True);
        }

        [Test]
        public void SuspendForSourceQualification_RestoresOriginalThenResumesGeneratedState()
        {
            string hotDirectory = SeedDirectory(
                "HybridCLRData/HotUpdateDlls/Android",
                "old-dll");
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            string generatedFile = Path.Combine(
                projectRoot,
                "Assets",
                "HybridCLRGenerate",
                "AOTGenericReferences.cs");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddMirrorDirectory(hotDirectory);
            plan.AddSnapshotFile(linkFile);
            plan.AddGeneratedAssetFile(generatedFile);

            using (HybridCLRGenerationTransaction transaction =
                   HybridCLRGenerationTransaction.Begin(plan))
            {
                File.WriteAllText(Path.Combine(hotDirectory, "Game.dll"), "new-dll");
                File.WriteAllText(Path.Combine(hotDirectory, "Extra.dll"), "new-extra");
                File.WriteAllText(linkFile, "new-link");
                File.WriteAllText(generatedFile, "generated-reference");
                File.WriteAllText(generatedFile + ".meta", "generated-meta");

                using (transaction.SuspendForSourceQualification())
                {
                    Assert.That(
                        File.ReadAllText(Path.Combine(hotDirectory, "Game.dll")),
                        Is.EqualTo("old-dll"));
                    Assert.That(
                        File.Exists(Path.Combine(hotDirectory, "Extra.dll")),
                        Is.False);
                    Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
                    Assert.That(File.Exists(generatedFile), Is.False);
                    Assert.That(File.Exists(generatedFile + ".meta"), Is.False);
                }

                Assert.That(
                    File.ReadAllText(Path.Combine(hotDirectory, "Game.dll")),
                    Is.EqualTo("new-dll"));
                Assert.That(
                    File.ReadAllText(Path.Combine(hotDirectory, "Extra.dll")),
                    Is.EqualTo("new-extra"));
                Assert.That(File.ReadAllText(linkFile), Is.EqualTo("new-link"));
                Assert.That(File.ReadAllText(generatedFile), Is.EqualTo("generated-reference"));
                Assert.That(File.ReadAllText(generatedFile + ".meta"), Is.EqualTo("generated-meta"));
            }

            Assert.That(
                File.ReadAllText(Path.Combine(hotDirectory, "Game.dll")),
                Is.EqualTo("old-dll"));
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
            Assert.That(File.Exists(generatedFile), Is.False);
        }

        [Test]
        public void SuspendForSourceQualification_WhenGenerationDeletesExistingFile_RollbackRestoresOriginal()
        {
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);

            using (HybridCLRGenerationTransaction transaction =
                   HybridCLRGenerationTransaction.Begin(plan))
            {
                File.Delete(linkFile);
                using (transaction.SuspendForSourceQualification())
                {
                    Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
                }

                Assert.That(File.Exists(linkFile), Is.False);
            }

            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
        }

        [Test]
        public void SuspendForSourceQualification_AtomicSwapPreservesExactOriginalMetadata()
        {
            string directory = SeedDirectory(
                "HybridCLRData/HotUpdateDlls/Android",
                "old-dll");
            string original = Path.Combine(directory, "Game.dll");
            DateTime originalWriteTime = new DateTime(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(original, originalWriteTime);
            File.SetAttributes(original, FileAttributes.Archive);
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddMirrorDirectory(directory);

            using (HybridCLRGenerationTransaction transaction =
                   HybridCLRGenerationTransaction.Begin(plan))
            {
                File.WriteAllText(original, "generated-dll");
                using (transaction.SuspendForSourceQualification())
                {
                    Assert.That(File.ReadAllText(original), Is.EqualTo("old-dll"));
                    Assert.That(File.GetLastWriteTimeUtc(original), Is.EqualTo(originalWriteTime));
                    Assert.That(
                        File.GetAttributes(original) & FileAttributes.Archive,
                        Is.EqualTo(FileAttributes.Archive));
                }

                Assert.That(File.ReadAllText(original), Is.EqualTo("generated-dll"));
            }
        }

        [Test]
        public void ResumeAfterSourceQualification_WhenSourceViewIsTampered_PreservesEvidence()
        {
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(linkFile, "generated-link");
            IDisposable suspension = transaction.SuspendForSourceQualification();
            File.WriteAllText(linkFile, "unknown-tamper");

            IOException exception = Assert.Throws<IOException>(suspension.Dispose);

            Assert.That(exception.Message, Does.Contain("Unknown evidence was preserved"));
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("unknown-tamper"));
            Assert.That(
                File.Exists(HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.True);
            transaction.AbandonForTesting();
        }

        [Test]
        public void ResumeAfterSourceQualification_WhenHeldGeneratedStateIsTampered_PreservesEvidence()
        {
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(linkFile, "generated-link");
            IDisposable suspension = transaction.SuspendForSourceQualification();
            string stateRoot = Path.GetDirectoryName(
                HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot));
            string scratch = Directory.GetDirectories(stateRoot)[0];
            File.WriteAllText(Path.Combine(scratch, "discard-000"), "unknown-held-tamper");

            IOException exception = Assert.Throws<IOException>(suspension.Dispose);

            Assert.That(exception.Message, Does.Contain("Unknown evidence was preserved"));
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
            Assert.That(File.ReadAllText(Path.Combine(scratch, "discard-000")),
                Is.EqualTo("unknown-held-tamper"));
            transaction.AbandonForTesting();
        }

        [TestCase("Assets/GeneratedOutput/Child")]
        [TestCase("Assets")]
        [TestCase("Assets/GeneratedOutput.meta")]
        public void ValidateNoOutputTargetOverlap_RejectsTargetAncestorChildAndMeta(
            string generationRelativePath)
        {
            string generationPath = Path.Combine(
                projectRoot,
                generationRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var plan = new HybridCLRGenerationPlan(projectRoot);
            if (Directory.Exists(generationPath))
            {
                plan.AddMirrorDirectory(generationPath);
            }
            else
            {
                plan.AddSnapshotFile(generationPath);
            }
            string output = Path.Combine(projectRoot, "Assets", "GeneratedOutput");

            using (HybridCLRGenerationTransaction transaction =
                   HybridCLRGenerationTransaction.Begin(plan))
            {
                Assert.Throws<InvalidOperationException>(() =>
                    transaction.ValidateNoOutputTargetOverlap(
                        new[] { new HybridCLROutputTarget("HotUpdate", output) }));
            }
        }

        [Test]
        public void RecoverPending_AfterSuspendedProcessStops_RestoresOriginalState()
        {
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(linkFile, "generated-link");

            transaction.SuspendForSourceQualification();
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
            transaction.AbandonForTesting();
            BuildPublicationBarrier barrier = CreateCommittedTerminalBarrier();

            Assert.That(
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out bool assetsChanged),
                Is.True);
            Assert.That(assetsChanged, Is.True);
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
            Assert.That(
                File.Exists(HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.False);
            barrier.Complete();
        }

        [Test]
        public void RecoverPending_AfterOriginallyAbsentTargetIsSuspended_IgnoresCommitAndRestoresAbsence()
        {
            string generatedFile = Path.Combine(
                projectRoot,
                "Assets",
                "HybridCLRGenerate",
                "AOTGenericReferences.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(generatedFile));
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(generatedFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(generatedFile, "generated-reference");

            transaction.SuspendForSourceQualification();
            Assert.That(File.Exists(generatedFile), Is.False);
            transaction.AbandonForTesting();
            BuildPublicationBarrier barrier = CreateCommittedTerminalBarrier();

            Assert.That(
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out bool assetsChanged),
                Is.True);
            Assert.That(assetsChanged, Is.True);
            Assert.That(File.Exists(generatedFile), Is.False);
            Assert.That(
                File.Exists(HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.False);
            barrier.Complete();
        }

        [Test]
        public void RecoverPending_WhenSuspensionStopsAfterGeneratedDisplacement_RestoresOriginal()
        {
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(linkFile, "generated-link");

            Assert.Throws<HybridCLRGenerationTransaction.SimulatedProcessCrashException>(() =>
                transaction.SuspendForSourceQualificationForTesting(
                    (checkpoint, _) => checkpoint
                        == HybridCLRGenerationTransaction.CrashCheckpoint
                            .AfterSuspendedTargetDisplacedBeforeRestore));
            transaction.Dispose();
            BuildPublicationBarrier barrier = CreateCommittedTerminalBarrier();

            Assert.That(
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out bool assetsChanged),
                Is.True);
            Assert.That(assetsChanged, Is.True);
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
            barrier.Complete();
        }

        [Test]
        public void RecoverPending_WhenResumeStopsAfterOriginalDisplacement_RestoresOriginal()
        {
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(linkFile, "generated-link");
            IDisposable suspension =
                transaction.SuspendForSourceQualificationForTesting(
                    (checkpoint, _) => checkpoint
                        == HybridCLRGenerationTransaction.CrashCheckpoint
                            .AfterResumeOriginalDisplacedBeforeGeneratedRestore);

            Assert.Throws<HybridCLRGenerationTransaction.SimulatedProcessCrashException>(
                suspension.Dispose);
            transaction.Dispose();
            BuildPublicationBarrier barrier = CreateCommittedTerminalBarrier();

            Assert.That(
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out bool assetsChanged),
                Is.True);
            Assert.That(assetsChanged, Is.True);
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
            barrier.Complete();
        }

        [Test]
        public void RecoverPending_WhenDeletedGeneratedFileResumeStops_RestoresOriginal()
        {
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.Delete(linkFile);
            IDisposable suspension =
                transaction.SuspendForSourceQualificationForTesting(
                    (checkpoint, _) => checkpoint
                        == HybridCLRGenerationTransaction.CrashCheckpoint
                            .AfterResumeOriginalDisplacedBeforeGeneratedRestore);

            Assert.Throws<HybridCLRGenerationTransaction.SimulatedProcessCrashException>(
                suspension.Dispose);
            transaction.Dispose();
            BuildPublicationBarrier barrier = CreateCommittedTerminalBarrier();

            Assert.That(
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out bool assetsChanged),
                Is.True);
            Assert.That(assetsChanged, Is.True);
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("old-link"));
            barrier.Complete();
        }

        [Test]
        public void RecoverPending_ActivePhaseWithTerminalCommit_KeepsGeneratedState()
        {
            string linkFile = SeedFile(
                "Assets/HybridCLRGenerate/link.xml",
                "old-link");
            var plan = new HybridCLRGenerationPlan(projectRoot);
            plan.AddSnapshotFile(linkFile);
            HybridCLRGenerationTransaction transaction =
                HybridCLRGenerationTransaction.Begin(plan);
            File.WriteAllText(linkFile, "generated-link");
            transaction.AbandonForTesting();
            BuildPublicationBarrier barrier = CreateCommittedTerminalBarrier();

            Assert.That(
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out bool assetsChanged),
                Is.True);
            Assert.That(assetsChanged, Is.False);
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("generated-link"));
            barrier.Complete();
        }

        [Test]
        public void RecoveryParticipant_ClaimsGenerationStateWithHigherPriority()
        {
            var participant = new HybridCLRGenerationRecoveryParticipant();

            Assert.That(participant.Id, Is.EqualTo("HybridCLRGeneration"));
            Assert.That(participant.Priority, Is.EqualTo(200));
            CollectionAssert.AreEqual(
                new[] { HybridCLRGenerationTransaction.StateRelativePath },
                participant.StateDirectoryRelativePaths);
        }

        private string SeedDirectory(string relativePath, string content)
        {
            string directory = Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Game.dll"), content);
            return directory;
        }

        private string SeedFile(string relativePath, string content)
        {
            string file = Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, content);
            return file;
        }

        private BuildPublicationBarrier CreateCommittedTerminalBarrier()
        {
            var publication = new TerminalDecisionPublication();
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "hybridclr-generation-recovery",
                new IBuildDeferredPublication[] { publication });
            publication.Publish();
            barrier.CommitDecision();
            return barrier;
        }

        private sealed class TerminalDecisionPublication : IBuildDeferredPublication
        {
            public string Id => HybridCLROutputTransaction.PublicationId;
            public string RecoveryStateRelativePath =>
                HybridCLROutputTransaction.StateRelativePath;

            public void Publish()
            {
            }

            public void Complete()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
