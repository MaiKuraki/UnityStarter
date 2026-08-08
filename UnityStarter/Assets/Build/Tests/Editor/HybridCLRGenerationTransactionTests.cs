using System;
using System.IO;
using Build.Pipeline.Editor;
using NUnit.Framework;

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

            Assert.Throws<IOException>(() =>
                HybridCLRGenerationTransaction.RecoverPending(projectRoot, out _));
            Assert.That(File.ReadAllText(linkFile), Is.EqualTo("generated"));
            Assert.That(
                File.Exists(HybridCLRGenerationTransaction.GetActiveJournalPathForTesting(projectRoot)),
                Is.True);
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
    }
}
