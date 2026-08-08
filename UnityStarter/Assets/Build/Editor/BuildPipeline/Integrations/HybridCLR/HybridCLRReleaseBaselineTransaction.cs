using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Atomically publishes a target-isolated HybridCLR Player release baseline.
    /// The baseline stays reversible until the shared terminal publication barrier commits.
    /// </summary>
    internal sealed class HybridCLRReleaseBaselineTransaction : IBuildDeferredPublication
    {
        [Serializable]
        private sealed class Journal
        {
            public int formatVersion;
            public long sequence;
            public string transactionId;
            public string phase;
            public string projectRoot;
            public string buildRoot;
            public string finalDirectory;
            public string stageDirectory;
            public string backupDirectory;
            public string initialManifestHash;
            public string stagedManifestHash;
            public string releaseKey;
            public string applicationIdentifier;
            public string applicationVersion;
            public string hotUpdateInvocationId;
            public string buildTarget;
            public string namedBuildTarget;
            public string scriptingBackend;
            public string unityVersion;
            public string hybridCLRPackageIdentity;
            public string authoringConfigurationHash;
            public string hybridCLRSettingsHash;
            public string playerConfigurationHash;
            public string compatibilityHash;
            public string[] hotUpdateAssemblies;
            public string checksum;
        }

        internal const string PublicationId = "hot-update:hybridclr-release-baseline";
        internal const string StateRelativePath =
            ".buildpipeline/transactions/hybridclr-release-baseline";

        private const int JournalFormatVersion = 1;
        private const string LockFileName = "build.lock";
        private const string ActiveJournalFileName = "active.json";
        private const string JournalTemporaryPrefix = ActiveJournalFileName + ".tmp-";
        private const string PreparingPhase = "Preparing";
        private const string StagedPhase = "Staged";
        private const string PublishingPhase = "Publishing";
        private const string BackedUpPhase = "BackedUp";
        private const string PublishedPhase = "Published";
        private const string CommittedPhase = "Committed";
        private const int MaximumStateEntries = 8;
        private const int MaximumJournalTemporaryFiles = 4;
        private const int MaximumDeleteEntries =
            HybridCLRReleaseBaselineStore.MaximumAOTAssemblyCount + 8;
        private const long MaximumJournalBytes = 1024L * 1024L;
        private const long MaximumJournalSequence = 64;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly string projectRoot;
        private readonly string stateRoot;
        private readonly string activeJournalPath;
        private readonly HybridCLRReleaseBaselineExpectation expectation;
        private readonly Journal journal;
        private readonly FileStream stateLock;
        private bool published;
        private bool completed;
        private bool preserveStateForRecovery;
        private bool disposed;

        private HybridCLRReleaseBaselineTransaction(
            string projectRoot,
            string stateRoot,
            HybridCLRReleaseBaselineExpectation expectation,
            Journal journal,
            FileStream stateLock)
        {
            this.projectRoot = projectRoot;
            this.stateRoot = stateRoot;
            this.expectation = expectation;
            this.journal = journal;
            this.stateLock = stateLock;
            activeJournalPath = Path.Combine(stateRoot, ActiveJournalFileName);
        }

        public string Id => PublicationId;
        public string RecoveryStateRelativePath => StateRelativePath;

        internal static HybridCLRReleaseBaselineTransaction Stage(
            HybridCLRReleaseBaselineExpectation expectation,
            string playerInvocationId,
            string strippedAOTSourceDirectory,
            BuildVersionContext sourceVersion)
        {
            if (expectation == null)
            {
                throw new ArgumentNullException(nameof(expectation));
            }

            string projectRoot = NormalizeProjectRoot(expectation.ProjectRoot);
            string stateRoot = PrepareStateRoot(projectRoot);
            FileStream stateLock = AcquireStateLock(stateRoot);
            try
            {
                CleanupJournalTemporaryFiles(stateRoot);
                EnsureNoPendingRecoveryUnderLock(stateRoot);
                EnsureNoDetachedState(stateRoot);

                HybridCLRReleaseBaseline existing =
                    HybridCLRReleaseBaselineStore.ValidateForReplacement(expectation);
                string transactionId = Guid.NewGuid().ToString("N");
                var journal = new Journal
                {
                    formatVersion = JournalFormatVersion,
                    transactionId = transactionId,
                    phase = PreparingPhase,
                    projectRoot = projectRoot,
                    buildRoot = expectation.BuildRoot,
                    finalDirectory = expectation.FinalDirectory,
                    stageDirectory = Path.Combine(stateRoot, "stage-" + transactionId),
                    backupDirectory = Path.Combine(stateRoot, "backup-" + transactionId),
                    initialManifestHash = existing?.ManifestHash ?? string.Empty,
                    stagedManifestHash = string.Empty,
                    releaseKey = expectation.ReleaseKey,
                    applicationIdentifier = expectation.ApplicationIdentifier,
                    applicationVersion = expectation.ApplicationVersion,
                    hotUpdateInvocationId = expectation.HotUpdateInvocationId,
                    buildTarget = expectation.BuildTarget,
                    namedBuildTarget = expectation.NamedBuildTarget,
                    scriptingBackend = expectation.ScriptingBackend,
                    unityVersion = expectation.UnityVersion,
                    hybridCLRPackageIdentity = expectation.HybridCLRPackageIdentity,
                    authoringConfigurationHash = expectation.AuthoringConfigurationHash,
                    hybridCLRSettingsHash = expectation.HybridCLRSettingsHash,
                    playerConfigurationHash = expectation.PlayerConfigurationHash,
                    compatibilityHash = expectation.CompatibilityHash,
                    hotUpdateAssemblies = expectation.HotUpdateAssemblies.ToArray(),
                    checksum = string.Empty
                };
                ValidateJournalPaths(projectRoot, stateRoot, journal, expectation);
                string journalPath = Path.Combine(stateRoot, ActiveJournalFileName);
                PersistJournal(journal, journalPath, createNew: true);

                try
                {
                    StageBaseline(
                        expectation,
                        playerInvocationId,
                        strippedAOTSourceDirectory,
                        sourceVersion,
                        journal);
                    journal.phase = StagedPhase;
                    PersistJournal(journal, journalPath, createNew: false);
                }
                catch (Exception stageFailure)
                {
                    Exception failure = stageFailure;
                    try
                    {
                        Rollback(journal, journalPath, expectation, stateRoot);
                    }
                    catch (Exception rollbackFailure)
                    {
                        failure = new AggregateException(
                            "HybridCLR release-baseline staging failed and durable cleanup did not complete.",
                            stageFailure,
                            rollbackFailure);
                    }

                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                return new HybridCLRReleaseBaselineTransaction(
                    projectRoot,
                    stateRoot,
                    expectation,
                    journal,
                    stateLock);
            }
            catch
            {
                stateLock.Dispose();
                throw;
            }
        }

        internal static void EnsureNoPendingRecovery(string projectRoot)
        {
            string project = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(project);
            EnsureStateRootIsSafe(project, stateRoot);
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            using (FileStream stateLock = AcquireStateLock(stateRoot))
            {
                CleanupJournalTemporaryFiles(stateRoot);
                EnsureNoPendingRecoveryUnderLock(stateRoot);
                EnsureNoDetachedState(stateRoot);
            }
        }

        internal static bool RecoverPending(string projectRoot)
        {
            string project = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(project);
            EnsureStateRootIsSafe(project, stateRoot);
            if (!Directory.Exists(stateRoot))
            {
                return false;
            }

            using (FileStream stateLock = AcquireStateLock(stateRoot))
            {
                CleanupJournalTemporaryFiles(stateRoot);
                string journalPath = Path.Combine(stateRoot, ActiveJournalFileName);
                if (!File.Exists(journalPath))
                {
                    EnsureNoDetachedState(stateRoot);
                    return false;
                }

                Journal journal = ReadJournal(project, stateRoot, journalPath);
                HybridCLRReleaseBaselineExpectation expectation =
                    CreateExpectationFromJournal(journal);
                BuildPublicationDecision decision = BuildPublicationBarrier.GetDecision(
                    project,
                    PublicationId,
                    StateRelativePath);
                if (decision == BuildPublicationDecision.Commit)
                {
                    CompleteCommittedRecovery(journal, journalPath, expectation, stateRoot);
                }
                else
                {
                    Rollback(journal, journalPath, expectation, stateRoot);
                }

                EnsureNoDetachedState(stateRoot);
                return true;
            }
        }

        internal static string GetActiveJournalPathForTesting(string projectRoot)
        {
            return Path.Combine(GetStateRoot(NormalizeProjectRoot(projectRoot)), ActiveJournalFileName);
        }

        internal void CommitForTesting()
        {
            Publish();
            CompleteCore(requireTerminalDecision: false);
        }

        public void Publish()
        {
            ThrowIfDisposed();
            if (published || completed || journal.phase != StagedPhase)
            {
                throw new InvalidOperationException(
                    "HybridCLR release baseline has already entered terminal publication.");
            }

            try
            {
                VerifyStage(journal, expectation);
                VerifyFinalMatchesInitial(journal, expectation);
                journal.phase = PublishingPhase;
                PersistJournal(journal, activeJournalPath, createNew: false);

                string parent = Path.GetDirectoryName(journal.finalDirectory);
                Directory.CreateDirectory(parent);
                HybridCLRReleaseBaselineStore.ValidateBaselineDirectory(
                    projectRoot,
                    expectation.BuildRoot,
                    journal.finalDirectory,
                    "HybridCLR release baseline");
                if (Directory.Exists(journal.finalDirectory))
                {
                    if (Directory.Exists(journal.backupDirectory)
                        || File.Exists(journal.backupDirectory))
                    {
                        throw new IOException(
                            $"HybridCLR release-baseline backup already exists: '{journal.backupDirectory}'.");
                    }

                    Directory.Move(journal.finalDirectory, journal.backupDirectory);
                    journal.phase = BackedUpPhase;
                    PersistJournal(journal, activeJournalPath, createNew: false);
                }

                if (Directory.Exists(journal.finalDirectory)
                    || File.Exists(journal.finalDirectory))
                {
                    throw new IOException(
                        $"HybridCLR release-baseline destination is occupied before installation: '{journal.finalDirectory}'.");
                }

                Directory.Move(journal.stageDirectory, journal.finalDirectory);
                HybridCLRReleaseBaseline publishedBaseline =
                    HybridCLRReleaseBaselineStore.ValidateAndResolve(expectation);
                RequireHash(
                    publishedBaseline.ManifestHash,
                    journal.stagedManifestHash,
                    "published release-baseline manifest");
                journal.phase = PublishedPhase;
                PersistJournal(journal, activeJournalPath, createNew: false);
                published = true;
            }
            catch (Exception publishFailure)
            {
                try
                {
                    Rollback(journal, activeJournalPath, expectation, stateRoot);
                    completed = true;
                }
                catch (Exception rollbackFailure)
                {
                    preserveStateForRecovery = true;
                    throw new AggregateException(
                        "HybridCLR release-baseline publication failed and durable rollback did not complete.",
                        publishFailure,
                        rollbackFailure);
                }

                ExceptionDispatchInfo.Capture(publishFailure).Throw();
            }
        }

        public void Complete()
        {
            CompleteCore(requireTerminalDecision: true);
        }

        private void CompleteCore(bool requireTerminalDecision)
        {
            ThrowIfDisposed();
            if (!published || completed || journal.phase != PublishedPhase)
            {
                throw new InvalidOperationException(
                    "HybridCLR release baseline must be published before terminal completion.");
            }

            if (requireTerminalDecision
                && BuildPublicationBarrier.GetDecision(
                    projectRoot,
                    PublicationId,
                    StateRelativePath) != BuildPublicationDecision.Commit)
            {
                throw new InvalidOperationException(
                    "HybridCLR release baseline cannot complete without the shared terminal commit decision.");
            }

            try
            {
                VerifyFinalMatchesStaged(journal, expectation);
                journal.phase = CommittedPhase;
                PersistJournal(journal, activeJournalPath, createNew: false);
                CleanupCommitted(journal, activeJournalPath, stateRoot);
                completed = true;
            }
            catch (Exception exception)
            {
                preserveStateForRecovery = true;
                throw new IOException(
                    "HybridCLR release baseline was selected by the terminal commit decision, but durable cleanup did not complete.",
                    exception);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Exception failure = null;
            try
            {
                if (!completed && !preserveStateForRecovery)
                {
                    BuildPublicationDecision decision = published
                        ? BuildPublicationBarrier.GetDecision(
                            projectRoot,
                            PublicationId,
                            StateRelativePath)
                        : BuildPublicationDecision.None;
                    if (decision == BuildPublicationDecision.Commit)
                    {
                        preserveStateForRecovery = true;
                    }
                    else
                    {
                        Rollback(journal, activeJournalPath, expectation, stateRoot);
                        completed = true;
                    }
                }
            }
            catch (Exception exception)
            {
                preserveStateForRecovery = true;
                failure = exception;
            }
            finally
            {
                disposed = true;
                stateLock.Dispose();
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static void StageBaseline(
            HybridCLRReleaseBaselineExpectation expectation,
            string playerInvocationId,
            string sourceDirectory,
            BuildVersionContext sourceVersion,
            Journal journal)
        {
            IReadOnlyList<HybridCLRReleaseBaselineStore.AOTAssembly> assemblies =
                HybridCLRReleaseBaselineStore.CaptureAOTAssemblies(sourceDirectory);
            Directory.CreateDirectory(journal.stageDirectory);
            string aotStage = Path.Combine(
                journal.stageDirectory,
                HybridCLRReleaseBaselineStore.AOTDirectoryName);
            Directory.CreateDirectory(aotStage);

            foreach (HybridCLRReleaseBaselineStore.AOTAssembly assembly in assemblies)
            {
                string source = BuildPathPolicy.EnsureSafeReadableFile(
                    sourceDirectory,
                    Path.Combine(sourceDirectory, assembly.fileName));
                string destination = Path.Combine(aotStage, assembly.fileName);
                CopyFileDurably(source, destination);
                RequireHash(
                    HybridCLRReleaseBaselineStore.ComputeFileSha256(destination),
                    assembly.sha256,
                    $"staged AOT assembly '{assembly.fileName}'");
            }

            HybridCLRReleaseBaselineStore.Manifest manifest =
                HybridCLRReleaseBaselineStore.CreateManifest(
                    expectation,
                    playerInvocationId,
                    assemblies,
                    sourceVersion);
            string manifestPath = Path.Combine(
                journal.stageDirectory,
                HybridCLRReleaseBaselineStore.ManifestFileName);
            WriteFileDurably(
                manifestPath,
                StrictUtf8.GetBytes(
                    HybridCLRReleaseBaselineStore.SerializeManifest(
                        manifest,
                        prettyPrint: true)));
            HybridCLRReleaseBaseline staged =
                HybridCLRReleaseBaselineStore.ValidateStagedDirectory(
                    journal.stageDirectory,
                    expectation);
            journal.stagedManifestHash = staged.ManifestHash;
        }

        private static void CompleteCommittedRecovery(
            Journal journal,
            string journalPath,
            HybridCLRReleaseBaselineExpectation expectation,
            string stateRoot)
        {
            EnsureCommittedTargetInstalled(journal, expectation);
            journal.phase = CommittedPhase;
            PersistJournal(journal, journalPath, createNew: false);
            CleanupCommitted(journal, journalPath, stateRoot);
        }

        private static void EnsureCommittedTargetInstalled(
            Journal journal,
            HybridCLRReleaseBaselineExpectation expectation)
        {
            string finalHash = TryGetFinalManifestHash(expectation);
            if (string.Equals(finalHash, journal.stagedManifestHash, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(finalHash))
            {
                if (!string.Equals(finalHash, journal.initialManifestHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "HybridCLR release-baseline destination contains an unrecognized competing write during committed recovery.");
                }

                if (Directory.Exists(journal.backupDirectory))
                {
                    throw new InvalidDataException(
                        "HybridCLR release-baseline committed recovery found both the original destination and its backup.");
                }

                Directory.Move(journal.finalDirectory, journal.backupDirectory);
            }

            if (!Directory.Exists(journal.stageDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Committed HybridCLR release-baseline staging directory is missing: '{journal.stageDirectory}'.");
            }

            HybridCLRReleaseBaseline staged =
                HybridCLRReleaseBaselineStore.ValidateStagedDirectory(
                    journal.stageDirectory,
                    expectation);
            RequireHash(staged.ManifestHash, journal.stagedManifestHash, "staged release baseline");
            Directory.Move(journal.stageDirectory, journal.finalDirectory);
            VerifyFinalMatchesStaged(journal, expectation);
        }

        private static void Rollback(
            Journal journal,
            string journalPath,
            HybridCLRReleaseBaselineExpectation expectation,
            string stateRoot)
        {
            ValidateJournalPaths(expectation.ProjectRoot, stateRoot, journal, expectation);
            string finalHash = TryGetFinalManifestHash(expectation);
            if (!string.IsNullOrEmpty(finalHash)
                && !string.Equals(finalHash, journal.initialManifestHash, StringComparison.Ordinal)
                && !string.Equals(finalHash, journal.stagedManifestHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline rollback found an unrecognized competing destination write.");
            }

            if (string.Equals(finalHash, journal.stagedManifestHash, StringComparison.Ordinal))
            {
                DeleteDirectoryTree(
                    journal.finalDirectory,
                    expectation.BuildRoot);
            }

            if (Directory.Exists(journal.backupDirectory))
            {
                HybridCLRReleaseBaseline backup =
                    HybridCLRReleaseBaselineStore.ValidateReplacementDirectory(
                        journal.backupDirectory,
                        expectation);
                RequireHash(
                    backup.ManifestHash,
                    journal.initialManifestHash,
                    "release-baseline rollback backup");
                if (Directory.Exists(journal.finalDirectory)
                    || File.Exists(journal.finalDirectory))
                {
                    throw new IOException(
                        "HybridCLR release-baseline rollback cannot restore its backup because the destination is occupied.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(journal.finalDirectory));
                Directory.Move(journal.backupDirectory, journal.finalDirectory);
            }
            else if (!string.IsNullOrEmpty(journal.initialManifestHash))
            {
                string restoredHash = TryGetFinalManifestHash(expectation);
                RequireHash(
                    restoredHash,
                    journal.initialManifestHash,
                    "restored release baseline");
            }

            if (Directory.Exists(journal.stageDirectory))
            {
                DeleteDirectoryTree(
                    journal.stageDirectory,
                    stateRoot);
            }

            DeleteFileIfPresent(journalPath);
            CleanupJournalTemporaryFiles(stateRoot);
            EnsureNoDetachedState(stateRoot);
        }

        private static void CleanupCommitted(
            Journal journal,
            string journalPath,
            string stateRoot)
        {
            if (Directory.Exists(journal.backupDirectory))
            {
                DeleteDirectoryTree(
                    journal.backupDirectory,
                    stateRoot);
            }

            if (Directory.Exists(journal.stageDirectory))
            {
                DeleteDirectoryTree(
                    journal.stageDirectory,
                    stateRoot);
            }

            DeleteFileIfPresent(journalPath);
            CleanupJournalTemporaryFiles(stateRoot);
            EnsureNoDetachedState(stateRoot);
        }

        private static void VerifyStage(
            Journal journal,
            HybridCLRReleaseBaselineExpectation expectation)
        {
            HybridCLRReleaseBaseline staged =
                HybridCLRReleaseBaselineStore.ValidateStagedDirectory(
                    journal.stageDirectory,
                    expectation);
            RequireHash(staged.ManifestHash, journal.stagedManifestHash, "staged release baseline");
        }

        private static void VerifyFinalMatchesInitial(
            Journal journal,
            HybridCLRReleaseBaselineExpectation expectation)
        {
            string actual = TryGetFinalManifestHash(expectation);
            RequireHash(actual, journal.initialManifestHash, "initial release baseline");
        }

        private static void VerifyFinalMatchesStaged(
            Journal journal,
            HybridCLRReleaseBaselineExpectation expectation)
        {
            HybridCLRReleaseBaseline baseline =
                HybridCLRReleaseBaselineStore.ValidateAndResolve(expectation);
            RequireHash(
                baseline.ManifestHash,
                journal.stagedManifestHash,
                "published release baseline");
        }

        private static string TryGetFinalManifestHash(
            HybridCLRReleaseBaselineExpectation expectation)
        {
            HybridCLRReleaseBaseline baseline =
                HybridCLRReleaseBaselineStore.ValidateForReplacement(expectation);
            return baseline?.ManifestHash ?? string.Empty;
        }

        private static Journal ReadJournal(
            string projectRoot,
            string stateRoot,
            string journalPath)
        {
            var info = new FileInfo(journalPath);
            if (info.Length <= 0 || info.Length > MaximumJournalBytes)
            {
                throw new InvalidDataException(
                    $"HybridCLR release-baseline journal exceeds the {MaximumJournalBytes}-byte budget.");
            }

            Journal journal;
            try
            {
                journal = JsonUtility.FromJson<Journal>(
                    File.ReadAllText(journalPath, StrictUtf8));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline journal is invalid JSON.",
                    exception);
            }

            if (journal == null
                || journal.formatVersion != JournalFormatVersion
                || journal.sequence <= 0
                || journal.sequence > MaximumJournalSequence
                || string.IsNullOrWhiteSpace(journal.transactionId)
                || !IsKnownPhase(journal.phase))
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline journal identity or phase is invalid.");
            }

            string checksum = HybridCLRReleaseBaselineStore.RequireSha256(
                journal.checksum,
                "journal checksum");
            if (!string.Equals(checksum, ComputeJournalChecksum(journal), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline journal checksum does not match its content.");
            }

            HybridCLRReleaseBaselineExpectation expectation =
                CreateExpectationFromJournal(journal);
            ValidateJournalPaths(projectRoot, stateRoot, journal, expectation);
            return journal;
        }

        private static HybridCLRReleaseBaselineExpectation CreateExpectationFromJournal(
            Journal journal)
        {
            return new HybridCLRReleaseBaselineExpectation(
                journal.projectRoot,
                journal.buildRoot,
                journal.finalDirectory,
                journal.releaseKey,
                journal.applicationIdentifier,
                journal.applicationVersion,
                journal.hotUpdateInvocationId,
                journal.buildTarget,
                journal.namedBuildTarget,
                journal.scriptingBackend,
                journal.unityVersion,
                journal.hybridCLRPackageIdentity,
                journal.authoringConfigurationHash,
                journal.hybridCLRSettingsHash,
                journal.playerConfigurationHash,
                journal.compatibilityHash,
                journal.hotUpdateAssemblies ?? Array.Empty<string>());
        }

        private static void PersistJournal(
            Journal journal,
            string journalPath,
            bool createNew)
        {
            if (journal.sequence >= MaximumJournalSequence)
            {
                throw new InvalidOperationException(
                    $"HybridCLR release-baseline journal exceeded {MaximumJournalSequence} durable updates.");
            }

            journal.sequence = checked(journal.sequence + 1);
            journal.checksum = ComputeJournalChecksum(journal);
            byte[] bytes = StrictUtf8.GetBytes(JsonUtility.ToJson(journal, true));
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    $"HybridCLR release-baseline journal exceeds {MaximumJournalBytes} bytes.");
            }

            string temporaryPath = Path.Combine(
                Path.GetDirectoryName(journalPath),
                JournalTemporaryPrefix + journal.transactionId + "-" +
                journal.sequence.ToString(CultureInfo.InvariantCulture));
            WriteFileDurably(temporaryPath, bytes, createNew: true);
            if (createNew)
            {
                if (File.Exists(journalPath))
                {
                    throw new IOException(
                        $"HybridCLR release-baseline journal already exists: '{journalPath}'.");
                }

                File.Move(temporaryPath, journalPath);
            }
            else
            {
                if (!File.Exists(journalPath))
                {
                    throw new FileNotFoundException(
                        "HybridCLR release-baseline journal disappeared before a durable update.",
                        journalPath);
                }

                File.Replace(temporaryPath, journalPath, null);
            }
        }

        private static string ComputeJournalChecksum(Journal journal)
        {
            string original = journal.checksum;
            try
            {
                journal.checksum = string.Empty;
                return HybridCLRReleaseBaselineStore.ComputeTextSha256(
                    JsonUtility.ToJson(journal, false));
            }
            finally
            {
                journal.checksum = original;
            }
        }

        private static void ValidateJournalPaths(
            string projectRoot,
            string stateRoot,
            Journal journal,
            HybridCLRReleaseBaselineExpectation expectation)
        {
            string project = NormalizeProjectRoot(projectRoot);
            string expectedStateRoot = GetStateRoot(project);
            if (!HybridCLRReleaseBaselineStore.PathsEqual(stateRoot, expectedStateRoot)
                || !HybridCLRReleaseBaselineStore.PathsEqual(journal.projectRoot, project)
                || !HybridCLRReleaseBaselineStore.PathsEqual(
                    journal.buildRoot,
                    expectation.BuildRoot)
                || !HybridCLRReleaseBaselineStore.PathsEqual(
                    journal.finalDirectory,
                    expectation.FinalDirectory))
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline journal contains an unexpected project, build, state, or destination path.");
            }

            string expectedStage = Path.Combine(
                stateRoot,
                "stage-" + journal.transactionId);
            string expectedBackup = Path.Combine(
                stateRoot,
                "backup-" + journal.transactionId);
            if (!HybridCLRReleaseBaselineStore.PathsEqual(journal.stageDirectory, expectedStage)
                || !HybridCLRReleaseBaselineStore.PathsEqual(journal.backupDirectory, expectedBackup))
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline journal scratch paths are invalid.");
            }

            HybridCLRReleaseBaselineStore.ValidateBaselineDirectory(
                project,
                expectation.BuildRoot,
                expectation.FinalDirectory,
                "HybridCLR release baseline");
            EnsureStateRootIsSafe(project, stateRoot);
        }

        private static void EnsureNoPendingRecoveryUnderLock(string stateRoot)
        {
            if (File.Exists(Path.Combine(stateRoot, ActiveJournalFileName)))
            {
                throw new InvalidOperationException(
                    "A HybridCLR release-baseline transaction requires explicit workspace recovery before another build.");
            }
        }

        private static void EnsureNoDetachedState(string stateRoot)
        {
            string[] entries = Directory.GetFileSystemEntries(
                stateRoot,
                "*",
                SearchOption.TopDirectoryOnly);
            if (entries.Length > MaximumStateEntries)
            {
                throw new InvalidDataException(
                    $"HybridCLR release-baseline state contains more than {MaximumStateEntries} entries.");
            }

            foreach (string entry in entries)
            {
                string name = Path.GetFileName(entry);
                if (string.Equals(name, LockFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"HybridCLR release-baseline state contains detached transaction evidence: '{entry}'.");
            }
        }

        private static string PrepareStateRoot(string projectRoot)
        {
            string stateRoot = GetStateRoot(projectRoot);
            EnsureStateRootIsSafe(projectRoot, stateRoot);
            Directory.CreateDirectory(stateRoot);
            EnsureStateRootIsSafe(projectRoot, stateRoot);
            return stateRoot;
        }

        private static string GetStateRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                StateRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void EnsureStateRootIsSafe(string projectRoot, string stateRoot)
        {
            string expected = GetStateRoot(projectRoot);
            if (!HybridCLRReleaseBaselineStore.PathsEqual(expected, stateRoot)
                || !HybridCLRReleaseBaselineStore.IsDescendant(projectRoot, stateRoot)
                || File.Exists(stateRoot))
            {
                throw new InvalidOperationException(
                    $"HybridCLR release-baseline state root is unsafe: '{stateRoot}'.");
            }

            string current = Directory.Exists(stateRoot)
                ? stateRoot
                : Path.GetDirectoryName(stateRoot);
            while (!string.IsNullOrEmpty(current)
                   && HybridCLRReleaseBaselineStore.IsDescendant(projectRoot, current))
            {
                if (Directory.Exists(current))
                {
                    HybridCLRReleaseBaselineStore.RejectReparsePoint(
                        current,
                        "HybridCLR release-baseline state path");
                }

                current = Path.GetDirectoryName(current);
            }
        }

        private static FileStream AcquireStateLock(string stateRoot)
        {
            string lockPath = Path.Combine(stateRoot, LockFileName);
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                FileOptions.WriteThrough);
        }

        private static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException(
                    "Unity project root is required.",
                    nameof(projectRoot));
            }

            string project = Path.GetFullPath(projectRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(project))
            {
                throw new DirectoryNotFoundException(
                    $"Unity project root was not found: '{project}'.");
            }

            return project;
        }

        private static void CleanupJournalTemporaryFiles(string stateRoot)
        {
            string[] files = Directory.GetFiles(
                stateRoot,
                JournalTemporaryPrefix + "*",
                SearchOption.TopDirectoryOnly);
            if (files.Length > MaximumJournalTemporaryFiles)
            {
                throw new InvalidDataException(
                    $"HybridCLR release-baseline state contains more than {MaximumJournalTemporaryFiles} journal temporary files.");
            }

            foreach (string file in files)
            {
                HybridCLRReleaseBaselineStore.RejectReparsePoint(
                    file,
                    "HybridCLR release-baseline journal temporary file");
                File.Delete(file);
            }
        }

        private static void CopyFileDurably(string source, string destination)
        {
            byte[] buffer = new byte[1024 * 1024];
            using (var input = new FileStream(
                       source,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       buffer.Length,
                       FileOptions.SequentialScan))
            using (var output = new FileStream(
                       destination,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       buffer.Length,
                       FileOptions.WriteThrough))
            {
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, count);
                }

                output.Flush(true);
            }
        }

        private static void WriteFileDurably(
            string path,
            byte[] bytes,
            bool createNew = true)
        {
            using (var stream = new FileStream(
                       path,
                       createNew ? FileMode.CreateNew : FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void DeleteDirectoryTree(
            string path,
            string approvedRoot)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            if (!HybridCLRReleaseBaselineStore.IsDescendant(approvedRoot, path))
            {
                throw new InvalidOperationException(
                    $"HybridCLR release-baseline cleanup escaped its approved root: '{path}'.");
            }

            HybridCLRReleaseBaselineStore.RejectReparsePoint(
                path,
                "HybridCLR release-baseline cleanup root");
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(path);
            int entryCount = 0;
            while (pendingDirectories.Count > 0)
            {
                string directory = pendingDirectories.Pop();
                string[] entries = Directory.GetFileSystemEntries(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly);
                foreach (string entry in entries)
                {
                    entryCount++;
                    if (entryCount > MaximumDeleteEntries)
                    {
                        throw new InvalidOperationException(
                            $"HybridCLR release-baseline cleanup exceeds {MaximumDeleteEntries} entries: '{path}'.");
                    }

                    HybridCLRReleaseBaselineStore.RejectReparsePoint(
                        entry,
                        "HybridCLR release-baseline cleanup entry");
                    if (Directory.Exists(entry))
                    {
                        pendingDirectories.Push(entry);
                    }
                }
            }

            Directory.Delete(path, recursive: true);
        }

        private static void DeleteFileIfPresent(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            HybridCLRReleaseBaselineStore.RejectReparsePoint(
                path,
                "HybridCLR release-baseline state file");
            File.Delete(path);
        }

        private static void RequireHash(string actual, string expected, string displayName)
        {
            if (!string.Equals(
                    actual ?? string.Empty,
                    expected ?? string.Empty,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"HybridCLR {displayName} identity changed during its transaction.");
            }
        }

        private static bool IsKnownPhase(string phase)
        {
            return phase == PreparingPhase
                || phase == StagedPhase
                || phase == PublishingPhase
                || phase == BackedUpPhase
                || phase == PublishedPhase
                || phase == CommittedPhase;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(HybridCLRReleaseBaselineTransaction));
            }
        }
    }
}
