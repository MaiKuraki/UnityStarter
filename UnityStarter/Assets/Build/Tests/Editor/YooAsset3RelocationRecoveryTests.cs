using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build.Pipeline.Editor;
using Build.Pipeline.Integrations.YooAsset3.Publication;
using NUnit.Framework;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    /// <summary>
    /// Core-assembly tests for the relocation recovery trust boundary, the recovery coordinator
    /// contract, and per-transaction MissingBoth retirement. Like
    /// <see cref="YooAsset3PublicationRecoveryTests"/>, the fixture uses a single short directory
    /// under Temp as the project root so the Win32 MAX_PATH budget checks cannot overflow on long
    /// repository checkouts.
    /// </summary>
    public sealed class YooAsset3RelocationRecoveryTests
    {
        private static readonly IJournalSerializer Serializer = UnityJournalSerializer.Instance;

        private string testRoot;
        private string projectRoot;
        private string relocationRoot;
        private string streamingAssetsRoot;

        [SetUp]
        public void SetUp()
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            // Keep the fixture root short (see YooAsset3PublicationRecoveryTests.SetUp).
            testRoot = Path.Combine(
                unityProjectRoot,
                "Temp",
                "Y3R" + Guid.NewGuid().ToString("N").Substring(0, 8));
            projectRoot = testRoot;
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
            relocationRoot = RelocationJournalStore.GetRelocationRoot(projectRoot);
            streamingAssetsRoot = RelocationPathPolicy.GetStreamingAssetsRoot(projectRoot);
            Directory.CreateDirectory(relocationRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRoot))
            {
                DeleteTreeIncludingReparsePoints(testRoot);
            }
        }

        // The Mono runtime hosted by Unity 2022.3 fails Directory.Delete(recursive: true) with
        // UnauthorizedAccessException when the tree contains a directory junction: the recursive
        // removal follows the reparse point instead of removing it, and the junction test leaves
        // one behind. Remove every reparse point first with a non-recursive delete (which deletes
        // the link itself, never its target), then delete the remaining tree.
        private static void DeleteTreeIncludingReparsePoints(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        if (File.Exists(entry))
                        {
                            File.Delete(entry);
                        }
                        else
                        {
                            Directory.Delete(entry, false);
                        }

                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                }
            }

            Directory.Delete(root, true);
        }

        [Test]
        public void RestorePending_RestoresRelocatedFileAndDirectory_AndRetiresCleanJournal()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string originalFile = Path.Combine(streamingAssetsRoot, "Pkg", "marker.json");
            string relocatedFile = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            string originalDirectory = Path.Combine(streamingAssetsRoot, "PkgBackup");
            string relocatedDirectory = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".dir");
            Directory.CreateDirectory(Path.GetDirectoryName(originalFile));
            Directory.CreateDirectory(relocatedDirectory);
            File.WriteAllText(relocatedFile, "marker");
            File.WriteAllText(Path.Combine(relocatedDirectory, "payload.txt"), "backup");

            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, originalFile, relocatedFile, RelocationJournalStore.KindFile),
                    NewEntry(transactionId, originalDirectory, relocatedDirectory, RelocationJournalStore.KindDirectory)),
                projectRoot,
                Serializer);

            int restored = RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp);

            Assert.That(restored, Is.EqualTo(2));
            Assert.That(File.ReadAllText(originalFile), Is.EqualTo("marker"));
            Assert.That(File.ReadAllText(Path.Combine(originalDirectory, "payload.txt")), Is.EqualTo("backup"));
            Assert.That(File.Exists(relocatedFile), Is.False);
            Assert.That(Directory.Exists(relocatedDirectory), Is.False);
            Assert.That(File.Exists(RelocationJournalStore.GetJournalPath(projectRoot, transactionId)), Is.False);
        }

        [Test]
        public void RestorePending_IsIdempotent_SecondRunIsANoOp()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string originalFile = Path.Combine(streamingAssetsRoot, "Pkg", "marker.json");
            string relocatedFile = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            Directory.CreateDirectory(Path.GetDirectoryName(originalFile));
            File.WriteAllText(relocatedFile, "marker");
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, originalFile, relocatedFile, RelocationJournalStore.KindFile)),
                projectRoot,
                Serializer);

            int firstRun = RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp);
            int secondRun = RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp);

            Assert.That(firstRun, Is.EqualTo(1));
            Assert.That(secondRun, Is.EqualTo(0));
            Assert.That(File.ReadAllText(originalFile), Is.EqualTo("marker"));
            Assert.That(Directory.Exists(RelocationJournalStore.GetStateRoot(projectRoot)), Is.False);
        }

        [Test]
        public void RestorePending_RejectsOriginalPathOutsideStreamingAssets()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string originalFile = Path.Combine(projectRoot, "Elsewhere", "marker.json");
            string relocatedFile = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            File.WriteAllText(relocatedFile, "marker");
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, originalFile, relocatedFile, RelocationJournalStore.KindFile)),
                projectRoot,
                Serializer);

            Assert.That(
                () => RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp),
                Throws.InvalidOperationException.With.Message.Contains("Blocked entries"));

            // Fail closed: the artifact was not moved outside the approved root.
            Assert.That(File.ReadAllText(relocatedFile), Is.EqualTo("marker"));
            Assert.That(Directory.Exists(Path.Combine(projectRoot, "Elsewhere")), Is.False);
            RelocationJournalDocument persisted = RelocationJournalStore.Load(projectRoot, transactionId, Serializer);
            Assert.That(persisted.entries[0].state, Is.EqualTo(RelocationJournalStore.ConflictState));
        }

        [Test]
        public void RestorePending_RejectsRelocatedPathOutsideRelocationRoot()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string originalFile = Path.Combine(streamingAssetsRoot, "Pkg", "marker.json");
            string relocatedFile = Path.Combine(projectRoot, "Elsewhere", "marker.file");
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, originalFile, relocatedFile, RelocationJournalStore.KindFile)),
                projectRoot,
                Serializer);

            Assert.That(
                () => RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp),
                Throws.InvalidOperationException.With.Message.Contains("Blocked entries"));

            // Nothing was moved into StreamingAssets from outside the relocation root.
            Assert.That(File.Exists(originalFile), Is.False);
            RelocationJournalDocument persisted = RelocationJournalStore.Load(projectRoot, transactionId, Serializer);
            Assert.That(persisted.entries[0].state, Is.EqualTo(RelocationJournalStore.ConflictState));
        }

        [Test]
        public void RestorePending_RejectsOriginalPathThroughAReparsePoint()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("Junction creation is Windows-only; on other platforms the per-segment reparse check is covered by unit-level validation tests.");
            }

            string transactionId = Guid.NewGuid().ToString("N");
            string junctionTarget = Path.Combine(projectRoot, "Elsewhere", "real");
            string junctionPath = Path.Combine(streamingAssetsRoot, "linked");
            Directory.CreateDirectory(junctionTarget);
            RunHidden("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{junctionTarget}\"");
            Assume.That(Directory.Exists(junctionPath), Is.True, "mklink /J failed to create a junction.");

            string originalFile = Path.Combine(junctionPath, "marker.json");
            string relocatedFile = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            File.WriteAllText(relocatedFile, "marker");
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, originalFile, relocatedFile, RelocationJournalStore.KindFile)),
                projectRoot,
                Serializer);

            Assert.That(
                () => RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp),
                Throws.InvalidOperationException.With.Message.Contains("Blocked entries"));

            Assert.That(File.ReadAllText(relocatedFile), Is.EqualTo("marker"));
        }

        [Test]
        public void RestorePending_RejectsKindMismatchAtOriginalPath_AsConflict()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            // Journal records a file, but a directory occupies the original path while the
            // relocated artifact is missing. This must be a Conflict, not a MissingBoth that
            // could retire the journal or move onto the directory.
            string originalFile = Path.Combine(streamingAssetsRoot, "Pkg", "marker.json");
            string relocatedFile = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            Directory.CreateDirectory(originalFile);
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, originalFile, relocatedFile, RelocationJournalStore.KindFile)),
                projectRoot,
                Serializer);

            Assert.That(
                () => RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp),
                Throws.InvalidOperationException.With.Message.Contains("Blocked entries"));

            Assert.That(Directory.Exists(originalFile), Is.True, "the mismatched directory must be left untouched.");
            RelocationJournalDocument persisted = RelocationJournalStore.Load(projectRoot, transactionId, Serializer);
            Assert.That(persisted.entries[0].state, Is.EqualTo(RelocationJournalStore.ConflictState));
        }

        [Test]
        public void RestorePending_KeepsConflictWhenBothPathsExist_AndOverwritesNothing()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string originalFile = Path.Combine(streamingAssetsRoot, "Pkg", "marker.json");
            string relocatedFile = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            Directory.CreateDirectory(Path.GetDirectoryName(originalFile));
            File.WriteAllText(originalFile, "recreated");
            File.WriteAllText(relocatedFile, "relocated");
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, originalFile, relocatedFile, RelocationJournalStore.KindFile)),
                projectRoot,
                Serializer);

            Assert.That(
                () => RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp),
                Throws.InvalidOperationException.With.Message.Contains("Blocked entries"));

            Assert.That(File.ReadAllText(originalFile), Is.EqualTo("recreated"));
            Assert.That(File.ReadAllText(relocatedFile), Is.EqualTo("relocated"));
        }

        [Test]
        public void RestorePending_RetiresAllMissingBothJournal_ButKeepsConflictedJournal()
        {
            string transactionIdA = Guid.NewGuid().ToString("N");
            string transactionIdB = Guid.NewGuid().ToString("N");
            // Journal A: every entry is missing on both sides -> lost with Temp -> retire.
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionIdA,
                    NewEntry(
                        transactionIdA,
                        Path.Combine(streamingAssetsRoot, "Pkg", "marker.json"),
                        Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file"),
                        RelocationJournalStore.KindFile),
                    NewEntry(
                        transactionIdA,
                        Path.Combine(streamingAssetsRoot, "PkgBackup"),
                        Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".dir"),
                        RelocationJournalStore.KindDirectory)),
                projectRoot,
                Serializer);

            // Journal B: both paths exist -> a real Conflict -> retain and fail closed.
            string originalFileB = Path.Combine(streamingAssetsRoot, "Other", "marker.json");
            string relocatedFileB = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            Directory.CreateDirectory(Path.GetDirectoryName(originalFileB));
            File.WriteAllText(originalFileB, "recreated");
            File.WriteAllText(relocatedFileB, "relocated");
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionIdB,
                    NewEntry(transactionIdB, originalFileB, relocatedFileB, RelocationJournalStore.KindFile)),
                projectRoot,
                Serializer);

            Assert.That(
                () => RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp),
                Throws.InvalidOperationException);

            Assert.That(
                File.Exists(RelocationJournalStore.GetJournalPath(projectRoot, transactionIdA)),
                Is.False,
                "the all-MissingBoth journal must be retired independently of other journals.");
            Assert.That(
                File.Exists(RelocationJournalStore.GetJournalPath(projectRoot, transactionIdB)),
                Is.True,
                "the conflicted journal must be retained for manual resolution.");
            Assert.That(File.ReadAllText(originalFileB), Is.EqualTo("recreated"));
        }

        [Test]
        public void RestorePending_DoesNotRetireJournalWithMixedRestoredAndMissingBothEntries()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string restorableOriginal = Path.Combine(streamingAssetsRoot, "Pkg", "marker.json");
            string restorableRelocated = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            Directory.CreateDirectory(Path.GetDirectoryName(restorableOriginal));
            File.WriteAllText(restorableRelocated, "marker");
            string lostOriginal = Path.Combine(streamingAssetsRoot, "PkgBackup");
            string lostRelocated = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".dir");
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, restorableOriginal, restorableRelocated, RelocationJournalStore.KindFile),
                    NewEntry(transactionId, lostOriginal, lostRelocated, RelocationJournalStore.KindDirectory)),
                projectRoot,
                Serializer);

            Assert.That(
                () => RelocationRecovery.RestorePending(projectRoot, Serializer, NoOp),
                Throws.InvalidOperationException);

            // A partial loss is a real inconsistency: the journal stays even though one entry
            // was restored successfully.
            Assert.That(File.ReadAllText(restorableOriginal), Is.EqualTo("marker"));
            Assert.That(
                File.Exists(RelocationJournalStore.GetJournalPath(projectRoot, transactionId)),
                Is.True,
                "a journal mixing Restored and MissingBoth entries must not be retired.");
        }

        [Test]
        public void Coordinator_PriorityContract_PlacesRelocationBeforePublication()
        {
            // BuildWorkspaceService.RecoverPhase orders participants by descending Priority, so a
            // higher value runs first. Relocation recovery must run before the publication
            // participant so the rollback finds its originals back in place.
            Assert.That(
                new RelocationRecoveryCoordinator().Priority,
                Is.GreaterThan(new PublicationRecoveryCoordinator().Priority));
        }

        [Test]
        public void Coordinator_ParticipantsAreNotCoordinators()
        {
            // Neither participant implements the IBuildRecoveryCoordinator marker interface, so
            // both execute in the same first RecoverUnderLease phase under descending Priority
            // ordering (there is no separate coordinator phase that could change the order).
            Assert.That(
                typeof(RelocationRecoveryCoordinator).GetInterfaces(),
                Has.No.Member(typeof(IBuildRecoveryCoordinator)));
            Assert.That(
                typeof(PublicationRecoveryCoordinator).GetInterfaces(),
                Has.No.Member(typeof(IBuildRecoveryCoordinator)));
        }

        [Test]
        public void Coordinator_StateClaim_MatchesRelocationJournalStateRoot()
        {
            var coordinator = new RelocationRecoveryCoordinator();
            Assert.That(
                coordinator.StateDirectoryRelativePaths,
                Does.Contain(RelocationJournalStore.StateRootRelativePath));
        }

        [Test]
        public void Coordinator_Recover_RestoresPendingRelocationJournal()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string originalFile = Path.Combine(streamingAssetsRoot, "Pkg", "marker.json");
            string relocatedFile = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            Directory.CreateDirectory(Path.GetDirectoryName(originalFile));
            File.WriteAllText(relocatedFile, "marker");
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, originalFile, relocatedFile, RelocationJournalStore.KindFile)),
                projectRoot,
                Serializer);

            new RelocationRecoveryCoordinator().Recover(projectRoot);

            Assert.That(File.ReadAllText(originalFile), Is.EqualTo("marker"));
            Assert.That(File.Exists(relocatedFile), Is.False);
            Assert.That(File.Exists(RelocationJournalStore.GetJournalPath(projectRoot, transactionId)), Is.False);
        }

        [Test]
        public void PublicationCoordinator_Recover_DoesNotRestoreRelocationJournals()
        {
            // Relocation recovery has a single owner (RelocationRecoveryCoordinator). The
            // publication coordinator deliberately leaves relocation journals untouched, so a
            // relocation journal is restored exactly once per recovery run.
            string transactionId = Guid.NewGuid().ToString("N");
            string originalFile = Path.Combine(streamingAssetsRoot, "Pkg", "marker.json");
            string relocatedFile = Path.Combine(relocationRoot, Guid.NewGuid().ToString("N") + ".file");
            Directory.CreateDirectory(Path.GetDirectoryName(originalFile));
            File.WriteAllText(relocatedFile, "marker");
            RelocationJournalStore.Persist(
                CreateJournal(
                    transactionId,
                    NewEntry(transactionId, originalFile, relocatedFile, RelocationJournalStore.KindFile)),
                projectRoot,
                Serializer);

            new PublicationRecoveryCoordinator().Recover(projectRoot);

            Assert.That(File.ReadAllText(relocatedFile), Is.EqualTo("marker"));
            Assert.That(
                File.Exists(RelocationJournalStore.GetJournalPath(projectRoot, transactionId)),
                Is.True,
                "the relocation journal must remain pending until the relocation coordinator runs.");
        }

        [Test]
        public void EnumeratePendingTransactionIds_IgnoresTemporaryCandidateFiles()
        {
            // Pins the pattern contract that "*.json" never matches "<id>.json.tmp-*" on any
            // platform (the Windows 3-character-extension wildcard quirk does not apply to a
            // four-character extension).
            string transactionId = Guid.NewGuid().ToString("N");
            string staleTemporary = RelocationJournalStore.GetJournalPath(projectRoot, transactionId)
                + ".tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(RelocationJournalStore.GetStateRoot(projectRoot));
            File.WriteAllText(staleTemporary, "{ not a journal }");

            Assert.That(
                RelocationJournalStore.EnumeratePendingTransactionIds(projectRoot),
                Is.Empty);
            Assert.That(File.Exists(staleTemporary), Is.True);
        }

        [Test]
        public void RelocationRoot_IsSharedBetweenStoreAndPolicy()
        {
            Assert.That(
                RelocationJournalStore.GetRelocationRoot(projectRoot),
                Is.EqualTo(Path.GetFullPath(Path.Combine(
                    projectRoot, "Temp", "BuildPipeline", "YooAssetPublicationMarkers"))));
            Assert.That(
                RelocationPathPolicy.GetStreamingAssetsRoot(projectRoot),
                Is.EqualTo(Path.GetFullPath(Path.Combine(projectRoot, "Assets", "StreamingAssets"))));
        }

        private static RelocationJournalDocument CreateJournal(
            string transactionId,
            params RelocationEntry[] entries)
        {
            RelocationJournalDocument document = RelocationJournalStore.Create(transactionId);
            var ordered = new List<RelocationEntry>(entries.Length);
            for (int index = 0; index < entries.Length; index++)
            {
                entries[index].order = index;
                ordered.Add(entries[index]);
            }

            document.entries = ordered.ToArray();
            return document;
        }

        private static RelocationEntry NewEntry(
            string transactionId,
            string originalPath,
            string relocatedPath,
            string kind,
            string state = null)
        {
            return new RelocationEntry
            {
                transactionId = transactionId,
                originalPath = originalPath,
                relocatedPath = relocatedPath,
                kind = kind,
                state = state ?? RelocationJournalStore.PlannedState,
                attemptCount = 0,
                lastError = string.Empty
            };
        }

        private static void RunHidden(string fileName, string arguments)
        {
            using (var process = System.Diagnostics.Process.Start(
                       new System.Diagnostics.ProcessStartInfo
                       {
                           FileName = fileName,
                           Arguments = arguments,
                           CreateNoWindow = true,
                           UseShellExecute = false
                       }))
            {
                if (process != null && !process.WaitForExit(10000))
                {
                    Assert.Fail($"'{fileName} {arguments}' timed out.");
                }
            }
        }

        private static void NoOp(string message)
        {
        }
    }
}
