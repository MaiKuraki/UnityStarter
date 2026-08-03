using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Owns the project-wide write-ahead journal for transient Unity project state.
    /// The lock intentionally spans both BuildGlobalStateScope and VersionInfoAssetScope.
    /// </summary>
    internal sealed class GlobalBuildStateTransaction
    {
        private const string SchemaVersion = "1";
        private const string StateDirectoryRelativePath = "Library/BuildPipeline/GlobalState";
        private const string JournalFileName = "active.json";
        private const string LockFileName = "build.lock";
        private const int BufferSize = 8192;
        private const int MaximumJournalBytes = 512 * 1024;
        private const int MaximumSnapshotBytes = 16 * 1024 * 1024;
        private const int MaximumPathCharacters = 2048;
        private const int MaximumTransactionDirectories = 4;
        private const int MaximumOwnedParentEntries = 256;
        private const string OwnedParentMarkerFileName = "BuildPipelineGlobalState.owner";

        private static GlobalBuildStateTransaction current;

        private readonly string projectRoot;
        private readonly string stateDirectory;
        private readonly string journalPath;
        private readonly string lockPath;
        private FileStream lockStream;
        private Journal journal;
        private Journal pendingRecoveryJournal;
        private bool released;
#if UNITY_INCLUDE_TESTS
        private Action beforePlayerSettingsRestoreReplaceForTests;
        private Action beforeVersionInfoInstallReplaceForTests;
#endif

        private GlobalBuildStateTransaction(
            string projectRoot,
            string stateDirectory,
            string journalPath,
            string lockPath,
            FileStream lockStream)
        {
            this.projectRoot = projectRoot;
            this.stateDirectory = stateDirectory;
            this.journalPath = journalPath;
            this.lockPath = lockPath;
            this.lockStream = lockStream;
        }

        internal bool HasPendingRecovery => pendingRecoveryJournal != null;

        internal BuildTargetRecoveryState PendingBuildTargetState
        {
            get
            {
                if (pendingRecoveryJournal == null)
                {
                    throw new InvalidOperationException("No interrupted global-state transaction is pending recovery.");
                }

                return new BuildTargetRecoveryState(
                    pendingRecoveryJournal.originalActiveBuildTarget,
                    pendingRecoveryJournal.originalExportAndroidProject,
                    pendingRecoveryJournal.requestedBuildTarget,
                    pendingRecoveryJournal.originalScriptingBackend,
                    pendingRecoveryJournal.originalCompanyName,
                    pendingRecoveryJournal.originalProductName,
                    pendingRecoveryJournal.originalBundleVersion,
                    pendingRecoveryJournal.originalApplicationIdentifier);
            }
        }

        internal bool PendingRecoveryHasVersionInfo =>
            pendingRecoveryJournal != null && pendingRecoveryJournal.versionInfo != null;

        internal string PendingRecoveryVersionInfoAssetPath =>
            PendingRecoveryHasVersionInfo
                ? pendingRecoveryJournal.versionInfo.asset.relativePath
                : string.Empty;

        internal bool PendingRecoveryVersionInfoOriginallyExisted =>
            PendingRecoveryHasVersionInfo && pendingRecoveryJournal.versionInfo.asset.existed;

        internal string VersionInfoStageAssetPath
        {
            get
            {
                EnsureActiveJournal();
                if (journal.versionInfo == null)
                {
                    throw new InvalidOperationException("VersionInfoData has not been enlisted in the global-state transaction.");
                }

                return journal.versionInfo.stageAssetPath;
            }
        }

        internal static GlobalBuildStateTransaction Acquire(string projectRootPath)
        {
            if (current != null)
            {
                throw new InvalidOperationException(
                    "A global build-state transaction is already active in this Unity process.");
            }

            string canonicalProjectRoot = CanonicalizeDirectory(projectRootPath, nameof(projectRootPath));
            EnsurePathHasNoReparsePoints(canonicalProjectRoot, canonicalProjectRoot, allowMissingLeaf: false);

            string stateDirectory = ResolveProjectRelativePath(
                canonicalProjectRoot,
                StateDirectoryRelativePath,
                allowMissingLeaf: true);
            Directory.CreateDirectory(stateDirectory);
            EnsurePathHasNoReparsePoints(canonicalProjectRoot, stateDirectory, allowMissingLeaf: false);

            string lockPath = Path.Combine(stateDirectory, LockFileName);
            FileStream stream;
            try
            {
                stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    BufferSize,
                    FileOptions.WriteThrough);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    $"Another build process owns the global Unity-state lock: '{lockPath}'.",
                    exception);
            }

            var transaction = new GlobalBuildStateTransaction(
                canonicalProjectRoot,
                stateDirectory,
                Path.Combine(stateDirectory, JournalFileName),
                lockPath,
                stream);
            try
            {
                transaction.WriteLockOwner();
                transaction.LoadAndRestorePendingTransaction();
                current = transaction;
                return transaction;
            }
            catch (Exception operationException)
            {
                Exception releaseException = transaction.TryReleaseLock();
                if (releaseException != null)
                {
                    throw new AggregateException(
                        "Global-state transaction acquisition and lock release both failed.",
                        operationException,
                        releaseException);
                }

                ExceptionDispatchInfo.Capture(operationException).Throw();
                throw;
            }
        }

        internal static GlobalBuildStateTransaction RequireCurrent()
        {
            if (current == null || current.released || current.journal == null)
            {
                throw new InvalidOperationException(
                    "VersionInfoData must be created inside an active BuildGlobalStateScope transaction.");
            }

            return current;
        }

        internal void ConfirmPendingRecovery()
        {
            EnsureNotReleased();
            if (pendingRecoveryJournal == null)
            {
                return;
            }

            VerifyOriginalState(pendingRecoveryJournal);
            CleanupTransactionArtifacts(pendingRecoveryJournal);
            pendingRecoveryJournal = null;
            EnsureNoDetachedArtifacts();
        }

        internal void ReassertPendingRecovery()
        {
            EnsureNotReleased();
            if (pendingRecoveryJournal == null)
            {
                return;
            }

            RestoreJournalState(pendingRecoveryJournal);
        }

        internal void Begin(
            string playerSettingsRelativePath,
            int originalActiveBuildTarget,
            bool originalExportAndroidProject,
            int requestedBuildTarget,
            int originalScriptingBackend,
            string originalCompanyName,
            string originalProductName,
            string originalBundleVersion,
            string originalApplicationIdentifier)
        {
            EnsureNotReleased();
            if (pendingRecoveryJournal != null)
            {
                throw new InvalidOperationException(
                    "The interrupted transaction must be confirmed before another transaction can begin.");
            }

            if (journal != null || File.Exists(journalPath))
            {
                throw new InvalidOperationException("A global-state journal already exists.");
            }

            EnsureNoDetachedArtifacts();
            string transactionId = Guid.NewGuid().ToString("N");
            string transactionDirectoryRelativePath =
                StateDirectoryRelativePath + "/transaction-" + transactionId;
            string transactionDirectory = ResolveProjectRelativePath(
                projectRoot,
                transactionDirectoryRelativePath,
                allowMissingLeaf: true);

            string playerPath = NormalizeAndValidateProjectRelativePath(
                projectRoot,
                playerSettingsRelativePath,
                "PlayerSettings path");
            FileRecord playerRecord = CaptureFileRecord(
                playerPath,
                transactionDirectoryRelativePath + "/player-settings.snapshot",
                requireExisting: true);
            if ((playerRecord.attributes & (int)FileAttributes.ReadOnly) != 0)
            {
                throw new InvalidOperationException(
                    $"PlayerSettings must be writable for a transactional build: '{playerPath}'.");
            }

            journal = new Journal
            {
                schemaVersion = SchemaVersion,
                transactionId = transactionId,
                projectRoot = NormalizeAbsolutePath(projectRoot),
                transactionDirectory = transactionDirectoryRelativePath,
                phase = GlobalPhasePreparing,
                sequence = 0,
                originalActiveBuildTarget = originalActiveBuildTarget,
                originalExportAndroidProject = originalExportAndroidProject,
                requestedBuildTarget = requestedBuildTarget,
                originalScriptingBackend = originalScriptingBackend,
                originalCompanyName = originalCompanyName ?? string.Empty,
                originalProductName = originalProductName ?? string.Empty,
                originalBundleVersion = originalBundleVersion ?? string.Empty,
                originalApplicationIdentifier = originalApplicationIdentifier ?? string.Empty,
                playerSettings = playerRecord
            };

            WriteJournal();
            Directory.CreateDirectory(transactionDirectory);
            EnsurePathHasNoReparsePoints(projectRoot, transactionDirectory, allowMissingLeaf: false);
            WriteSnapshot(playerRecord);
            journal.phase = GlobalPhasePrepared;
            WriteJournal();
        }

        internal void BeginGlobalMutation()
        {
            RequirePhase(GlobalPhasePrepared);
            journal.phase = GlobalPhaseApplying;
            WriteJournal();
        }

        internal PlayerSettingsPersistenceToken CapturePlayerSettingsPersistenceToken()
        {
            RequirePhase(GlobalPhaseApplying);
            FileIdentity identity = CaptureIdentity(
                journal.playerSettings.relativePath,
                requireExisting: true);
            return new PlayerSettingsPersistenceToken(identity.length, identity.sha256);
        }

        internal void MarkGlobalMutationApplied(
            PlayerSettingsPersistenceToken expectedPersistence,
            bool requireContentChange = false)
        {
            RequirePhase(GlobalPhaseApplying);
            if (expectedPersistence == null)
            {
                throw new ArgumentNullException(nameof(expectedPersistence));
            }

            FileIdentity persistedIdentity = CaptureIdentity(
                journal.playerSettings.relativePath,
                requireExisting: true);
            if (persistedIdentity.length != expectedPersistence.Length
                || !FixedTimeEquals(persistedIdentity.sha256, expectedPersistence.Sha256))
            {
                throw new IOException(
                    $"PlayerSettings changed after the targeted persistence barrier: '{journal.playerSettings.relativePath}'. " +
                    "The candidate post-image was not adopted and the journal was retained.");
            }

            if (requireContentChange && MatchesRecordContent(journal.playerSettings, persistedIdentity))
            {
                throw new IOException(
                    $"PlayerSettings did not persist the requested build-state changes: '{journal.playerSettings.relativePath}'.");
            }

            journal.transientPlayerSettings = persistedIdentity;
            journal.phase = GlobalPhaseActive;
            WriteJournal();
            EnsurePlayerSettingsOwned();
        }

        internal void EnsurePlayerSettingsUnchangedBeforePersistence()
        {
            RequirePhase(GlobalPhaseApplying);
            FileIdentity currentIdentity = CaptureIdentity(
                journal.playerSettings.relativePath,
                requireExisting: true);
            if (!MatchesRecordContent(journal.playerSettings, currentIdentity))
            {
                throw new IOException(
                    $"PlayerSettings changed before the pipeline persistence barrier: '{journal.playerSettings.relativePath}'. " +
                    "The journal and snapshot were retained; inspect the competing change before recovery.");
            }
        }

        internal void EnsurePlayerSettingsOwned()
        {
            EnsureActiveJournal();
            FileIdentity currentIdentity = CaptureIdentity(
                journal.playerSettings.relativePath,
                requireExisting: true);
            if (journal.transientPlayerSettings == null
                || !SameContent(currentIdentity, journal.transientPlayerSettings))
            {
                throw new IOException(
                    $"PlayerSettings no longer matches the build transaction's authorized content: '{journal.playerSettings.relativePath}'. " +
                    "The Player output will not be published and recovery will stop fail-closed.");
            }
        }

        internal void PrepareVersionInfo(string assetRelativePath)
        {
            RequirePhase(GlobalPhaseActive);
            if (journal.versionInfo != null)
            {
                throw new InvalidOperationException("VersionInfoData is already enlisted in this transaction.");
            }

            string assetPath = NormalizeAndValidateProjectRelativePath(
                projectRoot,
                assetRelativePath,
                "VersionInfoData path");
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"VersionInfoData path must be a project-relative .asset path below Assets: '{assetPath}'.");
            }

            string parentRelativePath = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string missingParentRoot = FindFirstMissingAssetDirectory(parentRelativePath);
            bool ownsParentRoot = !string.IsNullOrEmpty(missingParentRoot);
            if (!ownsParentRoot)
            {
                string parentPath = ResolveProjectRelativePath(projectRoot, parentRelativePath, allowMissingLeaf: false);
                EnsurePathHasNoReparsePoints(projectRoot, parentPath, allowMissingLeaf: false);
            }
            string metaPath = assetPath + ".meta";
            FileRecord assetRecord = CaptureFileRecord(
                assetPath,
                journal.transactionDirectory + "/version-info-asset.snapshot",
                requireExisting: false);
            FileRecord metaRecord = CaptureFileRecord(
                metaPath,
                journal.transactionDirectory + "/version-info-meta.snapshot",
                requireExisting: false);
            if (assetRecord.existed != metaRecord.existed)
            {
                throw new InvalidOperationException(
                    $"VersionInfoData asset and meta existence do not match: '{assetPath}'.");
            }

            string stageAssetPath = parentRelativePath +
                "/__BuildPipelineVersionInfo_" + journal.transactionId + ".asset";
            string stageMetaPath = stageAssetPath + ".meta";
            EnsureFileAbsent(stageAssetPath, "transaction staging asset");
            EnsureFileAbsent(stageMetaPath, "transaction staging meta file");

            journal.versionInfo = new VersionInfoRecord
            {
                state = VersionStatePreparing,
                asset = assetRecord,
                meta = metaRecord,
                stageAssetPath = stageAssetPath,
                stageMetaPath = stageMetaPath,
                ownsParentRoot = ownsParentRoot,
                ownedParentRootPath = ownsParentRoot ? missingParentRoot : string.Empty,
                ownedParentRootMetaPath = ownsParentRoot ? missingParentRoot + ".meta" : string.Empty,
                ownedParentScratchPath = ownsParentRoot
                    ? GetOwnedParentScratchPath(missingParentRoot, journal.transactionId)
                    : string.Empty,
                ownedParentMarkerSha256 = ownsParentRoot
                    ? ComputeSha256(GetOwnedParentMarkerBytes(journal.transactionId))
                    : string.Empty
            };
            journal.hasVersionInfo = true;
            WriteJournal();
            WriteSnapshot(assetRecord);
            WriteSnapshot(metaRecord);
            if (ownsParentRoot)
            {
                PrepareAndInstallOwnedParent(journal.versionInfo, parentRelativePath);
            }

            journal.versionInfo.state = VersionStatePrepared;
            WriteJournal();
        }

        internal void MarkVersionStageReady()
        {
            RequireVersionState(VersionStatePrepared);
            journal.versionInfo.stageAsset = CaptureIdentity(
                journal.versionInfo.stageAssetPath,
                requireExisting: true);
            journal.versionInfo.stageMeta = CaptureIdentity(
                journal.versionInfo.stageMetaPath,
                requireExisting: true);
            journal.versionInfo.state = VersionStateStageReady;
            WriteJournal();
        }

        internal void PublishStagedVersionInfo()
        {
            RequireVersionState(VersionStateStageReady);
            journal.versionInfo.state = VersionStateInstalling;
            WriteJournal();

            VerifyOriginalFileOrAbsence(journal.versionInfo.asset);
            VerifyOriginalFileOrAbsence(journal.versionInfo.meta);

            string stageAssetPath = ResolveProjectRelativePath(
                projectRoot,
                journal.versionInfo.stageAssetPath,
                allowMissingLeaf: false);
            string targetAssetPath = ResolveProjectRelativePath(
                projectRoot,
                journal.versionInfo.asset.relativePath,
                allowMissingLeaf: !journal.versionInfo.asset.existed);

            if (journal.versionInfo.asset.existed)
            {
                byte[] stagedBytes = ReadBoundedFile(stageAssetPath, MaximumSnapshotBytes, "VersionInfoData staging asset");
                ReplaceExistingForInstallation(
                    targetAssetPath,
                    stagedBytes,
                    new DateTime(journal.versionInfo.stageAsset.lastWriteTimeUtcTicks, DateTimeKind.Utc),
                    (FileAttributes)journal.versionInfo.stageAsset.attributes,
                    journal.versionInfo.stageAsset,
                    journal.versionInfo.asset);
            }
            else
            {
                MoveOwnedStageFile(
                    journal.versionInfo.stageAssetPath,
                    journal.versionInfo.asset.relativePath,
                    journal.versionInfo.stageAsset);
                MoveOwnedStageFile(
                    journal.versionInfo.stageMetaPath,
                    journal.versionInfo.meta.relativePath,
                    journal.versionInfo.stageMeta);
            }

            journal.versionInfo.installedAsset = CaptureIdentity(
                journal.versionInfo.asset.relativePath,
                requireExisting: true);
            journal.versionInfo.installedMeta = CaptureIdentity(
                journal.versionInfo.meta.relativePath,
                requireExisting: true);
            if (journal.versionInfo.asset.existed
                && !MatchesRecordContent(journal.versionInfo.meta, journal.versionInfo.installedMeta))
            {
                throw new IOException(
                    $"VersionInfoData meta changed during installation: '{journal.versionInfo.meta.relativePath}'. " +
                    "The journal and transaction scratch were retained.");
            }

            journal.versionInfo.state = VersionStateInstalled;
            WriteJournal();
        }

        internal void RefreshInstalledVersionIdentity()
        {
            RequireVersionState(VersionStateInstalled);
            FileIdentity actualAsset = CaptureIdentity(
                journal.versionInfo.asset.relativePath,
                requireExisting: true);
            FileIdentity actualMeta = CaptureIdentity(
                journal.versionInfo.meta.relativePath,
                requireExisting: true);
            if (!SameContent(actualAsset, journal.versionInfo.installedAsset)
                || !SameContent(actualMeta, journal.versionInfo.installedMeta))
            {
                throw new IOException(
                    "Unity import changed the transient VersionInfoData content or meta identity after installation.");
            }

            journal.versionInfo.installedAsset = actualAsset;
            journal.versionInfo.installedMeta = actualMeta;
            WriteJournal();
        }

        internal void RestoreVersionInfoFiles()
        {
            EnsureActiveJournal();
            if (journal.versionInfo == null)
            {
                return;
            }

            RestoreVersionInfo(journal);
        }

        internal void ConfirmVersionInfoRestored()
        {
            EnsureActiveJournal();
            if (journal.versionInfo == null)
            {
                return;
            }

            VerifyVersionOriginalState(journal.versionInfo);
            journal.versionInfo.state = VersionStateRestored;
            WriteJournal();
        }

        internal void RestorePlayerSettingsFile()
        {
            EnsureActiveJournal();
            RestoreOriginalFile(journal.playerSettings, allowOwnedTransient: true, journal.transientPlayerSettings);
        }

        internal void Complete()
        {
            EnsureActiveJournal();
            if (journal.versionInfo != null
                && !string.Equals(journal.versionInfo.state, VersionStateRestored, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "VersionInfoData restoration was not confirmed; the global-state journal must be retained.");
            }

            VerifyOriginalState(journal);
            journal.phase = GlobalPhaseRestored;
            WriteJournal();
            CleanupTransactionArtifacts(journal);
            journal = null;
        }

        internal Exception Release()
        {
            if (released)
            {
                return null;
            }

            released = true;
            if (ReferenceEquals(current, this))
            {
                current = null;
            }

            return TryReleaseLock();
        }

        internal void AbandonForProcessTerminationSimulation()
        {
            Exception releaseFailure = Release();
            if (releaseFailure != null)
            {
                throw releaseFailure;
            }
        }

        internal static string GetJournalPathForTests(string projectRootPath)
        {
            string root = CanonicalizeDirectory(projectRootPath, nameof(projectRootPath));
            return Path.Combine(root, StateDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar), JournalFileName);
        }

#if UNITY_INCLUDE_TESTS
        internal void SetBeforePlayerSettingsRestoreReplaceForTests(Action callback)
        {
            beforePlayerSettingsRestoreReplaceForTests = callback;
        }

        internal void SetBeforeVersionInfoInstallReplaceForTests(Action callback)
        {
            beforeVersionInfoInstallReplaceForTests = callback;
        }
#endif

        private void LoadAndRestorePendingTransaction()
        {
            ValidateStateDirectoryInventoryBeforeLoad();
            if (!File.Exists(journalPath))
            {
                EnsureNoDetachedArtifacts();
                return;
            }

            Journal loaded = ReadJournal(journalPath);
            ValidateJournal(loaded);
            CleanupAtomicJournalScratch(loaded);
            pendingRecoveryJournal = loaded;
            RestoreJournalState(loaded);
        }

        private void RestoreJournalState(Journal interrupted)
        {
            if (interrupted.versionInfo != null)
            {
                RestoreVersionInfo(interrupted);
            }

            if (string.Equals(interrupted.phase, GlobalPhasePreparing, StringComparison.Ordinal))
            {
                VerifyIdentity(interrupted.playerSettings, CaptureIdentity(
                    interrupted.playerSettings.relativePath,
                    requireExisting: true), "PlayerSettings");
                return;
            }

            RestoreOriginalFile(
                interrupted.playerSettings,
                allowOwnedTransient: true,
                interrupted.transientPlayerSettings);
        }

        private void RestoreVersionInfo(Journal owner)
        {
            VersionInfoRecord version = owner.versionInfo;
            if (string.Equals(version.state, VersionStatePreparing, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStateParentPrepared, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStateParentInstalling, StringComparison.Ordinal))
            {
                VerifyOriginalFileOrAbsence(version.asset);
                VerifyOriginalFileOrAbsence(version.meta);
                DeleteTransactionStage(version.stageAssetPath, version.stageAsset, "VersionInfoData staging asset");
                DeleteTransactionStage(version.stageMetaPath, version.stageMeta, "VersionInfoData staging meta file");
                CleanupOwnedParent(version);
                VerifyVersionOriginalState(version);
                return;
            }

            CleanupInterruptedVersionInstall(owner, version);
            ValidateVersionFilesystemForRecovery(version);

            if (version.asset.existed)
            {
                RestoreOriginalFile(version.asset, allowOwnedTransient: true, version.installedAsset ?? version.stageAsset);
                RestoreOriginalFile(version.meta, allowOwnedTransient: true, version.installedMeta ?? version.stageMeta);
            }
            else
            {
                DeleteOwnedTransientFile(
                    version.asset.relativePath,
                    version.installedAsset ?? version.stageAsset,
                    "transient VersionInfoData asset");
                DeleteOwnedTransientFile(
                    version.meta.relativePath,
                    version.installedMeta ?? version.stageMeta,
                    "transient VersionInfoData meta file");
            }

            DeleteTransactionStage(version.stageAssetPath, version.stageAsset, "VersionInfoData staging asset");
            DeleteTransactionStage(version.stageMetaPath, version.stageMeta, "VersionInfoData staging meta file");
            CleanupOwnedParent(version);
            VerifyVersionOriginalState(version);
        }

        private void ValidateVersionFilesystemForRecovery(VersionInfoRecord version)
        {
            if (string.Equals(version.state, VersionStatePreparing, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStateParentPrepared, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStateParentInstalling, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStatePrepared, StringComparison.Ordinal))
            {
                VerifyOriginalFileOrAbsence(version.asset);
                VerifyOriginalFileOrAbsence(version.meta);
                return;
            }

            ValidateCurrentAsOriginalOrOwned(
                version.asset,
                version.installedAsset ?? version.stageAsset,
                "VersionInfoData asset");
            ValidateCurrentAsOriginalOrOwned(
                version.meta,
                version.installedMeta ?? version.stageMeta,
                "VersionInfoData meta file");
        }

        private void ValidateCurrentAsOriginalOrOwned(
            FileRecord original,
            FileIdentity owned,
            string label)
        {
            FileIdentity currentIdentity = CaptureIdentity(original.relativePath, requireExisting: false);
            bool matchesOriginal = original.existed
                ? MatchesRecordContent(original, currentIdentity)
                : currentIdentity != null && !currentIdentity.exists;
            if (matchesOriginal
                || (owned != null && SameContent(currentIdentity, owned)))
            {
                return;
            }

            throw new IOException(
                $"Interrupted global-state recovery found an externally changed {label}: '{original.relativePath}'. " +
                "The journal was retained and recovery stopped.");
        }

        private void RestoreOriginalFile(
            FileRecord original,
            bool allowOwnedTransient,
            FileIdentity ownedTransient)
        {
            if (!original.existed)
            {
                throw new InvalidOperationException(
                    $"Cannot restore absent file '{original.relativePath}' through the existing-file path.");
            }

            string absolutePath = ResolveProjectRelativePath(
                projectRoot,
                original.relativePath,
                allowMissingLeaf: false);
            string transactionId = GetCurrentTransactionId();
            CleanupInterruptedRestoreScratch(
                original,
                allowOwnedTransient,
                ownedTransient,
                absolutePath,
                absolutePath + ".globalstate-restore-" + transactionId + ".tmp",
                absolutePath + ".globalstate-restore-" + transactionId + ".bak");
            FileIdentity currentIdentity = CaptureIdentity(original.relativePath, requireExisting: true);
            if (!MatchesRecordContent(original, currentIdentity)
                && (!allowOwnedTransient || ownedTransient == null || !SameContent(currentIdentity, ownedTransient)))
            {
                throw new IOException(
                    $"Refusing to overwrite an unrecognized global-state file: '{original.relativePath}'.");
            }

            byte[] originalBytes = ReadAndVerifySnapshot(original);
            RestoreExistingFileDurably(
                original,
                allowOwnedTransient,
                ownedTransient,
                absolutePath,
                originalBytes);
            VerifyOriginalFileOrAbsence(original);
        }

        private void RestoreExistingFileDurably(
            FileRecord original,
            bool allowOwnedTransient,
            FileIdentity ownedTransient,
            string absolutePath,
            byte[] originalBytes)
        {
            string transactionId = GetCurrentTransactionId();
            string temporaryPath = absolutePath + ".globalstate-restore-" + transactionId + ".tmp";
            string backupPath = absolutePath + ".globalstate-restore-" + transactionId + ".bak";
            CleanupInterruptedRestoreScratch(
                original,
                allowOwnedTransient,
                ownedTransient,
                absolutePath,
                temporaryPath,
                backupPath);

            WriteDurably(temporaryPath, originalBytes, createNew: true);
            FileIdentity beforeReplace = CaptureIdentity(original.relativePath, requireExisting: true);
            if (!IsAllowedRestoreInput(original, allowOwnedTransient, ownedTransient, beforeReplace))
            {
                throw new IOException(
                    $"PlayerSettings changed immediately before its atomic restoration: '{original.relativePath}'.");
            }

            FileAttributes currentAttributes = File.GetAttributes(absolutePath);
            if ((currentAttributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(absolutePath, currentAttributes & ~FileAttributes.ReadOnly);
            }

#if UNITY_INCLUDE_TESTS
            beforePlayerSettingsRestoreReplaceForTests?.Invoke();
#endif
            File.Replace(temporaryPath, absolutePath, backupPath);
            FileIdentity replacedIdentity = CaptureIdentity(
                GetProjectRelativePath(backupPath),
                requireExisting: true);
            if (!IsAllowedRestoreInput(original, allowOwnedTransient, ownedTransient, replacedIdentity))
            {
                throw new IOException(
                    $"Atomic PlayerSettings restoration captured an unrecognized competing write in '{GetProjectRelativePath(backupPath)}'. " +
                    "The backup and journal were retained; no competing bytes were deleted.");
            }

            File.SetLastWriteTimeUtc(
                absolutePath,
                new DateTime(original.lastWriteTimeUtcTicks, DateTimeKind.Utc));
            File.SetAttributes(absolutePath, (FileAttributes)original.attributes);
            VerifyOriginalFileOrAbsence(original);
            DeleteFileExactly(backupPath);
        }

        private void CleanupInterruptedRestoreScratch(
            FileRecord original,
            bool allowOwnedTransient,
            FileIdentity ownedTransient,
            string absolutePath,
            string temporaryPath,
            string backupPath)
        {
            EnsureTransactionScratchPath(temporaryPath);
            EnsureTransactionScratchPath(backupPath);
            if (!File.Exists(absolutePath))
            {
                throw new IOException(
                    $"Transactional restore destination disappeared: '{original.relativePath}'.");
            }

            if (File.Exists(backupPath))
            {
                FileIdentity currentIdentity = CaptureIdentity(original.relativePath, requireExisting: true);
                if (!MatchesRecordContent(original, currentIdentity))
                {
                    throw new IOException(
                        $"Interrupted restore found an unrecognized destination while its backup exists: '{original.relativePath}'.");
                }

                FileIdentity backupIdentity = CaptureIdentity(
                    GetProjectRelativePath(backupPath),
                    requireExisting: true);
                if (!IsAllowedRestoreInput(original, allowOwnedTransient, ownedTransient, backupIdentity))
                {
                    throw new IOException(
                        $"Interrupted restore retained an unrecognized competing backup: '{GetProjectRelativePath(backupPath)}'.");
                }

                File.SetLastWriteTimeUtc(
                    absolutePath,
                    new DateTime(original.lastWriteTimeUtcTicks, DateTimeKind.Utc));
                File.SetAttributes(absolutePath, (FileAttributes)original.attributes);
                VerifyOriginalFileOrAbsence(original);
                DeleteFileExactly(backupPath);
            }

            if (File.Exists(temporaryPath))
            {
                DeleteFileExactly(temporaryPath);
            }
        }

        private void ReplaceExistingForInstallation(
            string absolutePath,
            byte[] stagedBytes,
            DateTime stagedLastWriteTimeUtc,
            FileAttributes stagedAttributes,
            FileIdentity stagedIdentity,
            FileRecord originalIdentity)
        {
            string transactionId = GetCurrentTransactionId();
            string temporaryPath = absolutePath + ".globalstate-install-" + transactionId + ".tmp";
            string backupPath = absolutePath + ".globalstate-install-" + transactionId + ".bak";
            EnsureTransactionScratchPath(temporaryPath);
            EnsureTransactionScratchPath(backupPath);
            if (File.Exists(temporaryPath) || File.Exists(backupPath))
            {
                throw new IOException(
                    $"VersionInfoData installation scratch already exists: '{absolutePath}'.");
            }

            WriteDurably(temporaryPath, stagedBytes, createNew: true);
            FileIdentity beforeReplace = CaptureIdentity(
                originalIdentity.relativePath,
                requireExisting: true);
            if (!MatchesRecordIdentity(originalIdentity, beforeReplace))
            {
                throw new IOException(
                    $"VersionInfoData changed immediately before installation: '{originalIdentity.relativePath}'.");
            }

            FileAttributes currentAttributes = File.GetAttributes(absolutePath);
            if ((currentAttributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(absolutePath, currentAttributes & ~FileAttributes.ReadOnly);
            }

#if UNITY_INCLUDE_TESTS
            beforeVersionInfoInstallReplaceForTests?.Invoke();
#endif
            File.Replace(temporaryPath, absolutePath, backupPath);
            FileIdentity replacedIdentity = CaptureIdentity(
                GetProjectRelativePath(backupPath),
                requireExisting: true);
            if (!MatchesRecordContent(originalIdentity, replacedIdentity))
            {
                throw new IOException(
                    $"Atomic VersionInfoData installation captured an unrecognized competing write in '{GetProjectRelativePath(backupPath)}'. " +
                    "The backup and journal were retained; no competing bytes were deleted.");
            }

            File.SetLastWriteTimeUtc(absolutePath, stagedLastWriteTimeUtc);
            File.SetAttributes(absolutePath, stagedAttributes);
            FileIdentity installedIdentity = CaptureIdentity(
                journal.versionInfo.asset.relativePath,
                requireExisting: true);
            if (!SameContent(installedIdentity, stagedIdentity))
            {
                throw new IOException("VersionInfoData installation content verification failed.");
            }

            DeleteFileExactly(backupPath);
        }

        private void CleanupInterruptedVersionInstall(Journal owner, VersionInfoRecord version)
        {
            string targetPath = ResolveProjectRelativePath(
                projectRoot,
                version.asset.relativePath,
                allowMissingLeaf: !version.asset.existed);
            string temporaryPath = targetPath + ".globalstate-install-" + owner.transactionId + ".tmp";
            string backupPath = targetPath + ".globalstate-install-" + owner.transactionId + ".bak";
            EnsureTransactionScratchPath(temporaryPath);
            EnsureTransactionScratchPath(backupPath);

            if (File.Exists(backupPath))
            {
                if (!File.Exists(targetPath))
                {
                    throw new IOException(
                        $"VersionInfoData target disappeared while installation backup exists: '{version.asset.relativePath}'.");
                }

                FileIdentity currentIdentity = CaptureIdentity(version.asset.relativePath, requireExisting: true);
                bool recognized = MatchesRecordContent(version.asset, currentIdentity)
                    || (version.stageAsset != null && SameContent(currentIdentity, version.stageAsset))
                    || (version.installedAsset != null && SameContent(currentIdentity, version.installedAsset));
                if (!recognized)
                {
                    throw new IOException(
                        $"Interrupted VersionInfoData installation found an externally changed target: '{version.asset.relativePath}'.");
                }

                FileIdentity backupIdentity = CaptureIdentity(
                    GetProjectRelativePath(backupPath),
                    requireExisting: true);
                if (!MatchesRecordContent(version.asset, backupIdentity))
                {
                    throw new IOException(
                        $"Interrupted VersionInfoData installation retained an unrecognized competing backup: '{GetProjectRelativePath(backupPath)}'.");
                }

                DeleteFileExactly(backupPath);
            }

            if (File.Exists(temporaryPath))
            {
                DeleteFileExactly(temporaryPath);
            }
        }

        private void EnsureTransactionScratchPath(string absolutePath)
        {
            ResolveProjectRelativePath(
                projectRoot,
                GetProjectRelativePath(absolutePath),
                allowMissingLeaf: true);
        }

        private string GetCurrentTransactionId()
        {
            string transactionId = journal?.transactionId ?? pendingRecoveryJournal?.transactionId;
            if (!IsGuidN(transactionId))
            {
                throw new InvalidOperationException("No valid transaction id owns the global-state operation.");
            }

            return transactionId;
        }

        private string GetProjectRelativePath(string absolutePath)
        {
            string canonical = Path.GetFullPath(absolutePath);
            string rootWithSeparator = projectRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!canonical.StartsWith(rootWithSeparator, PathComparison))
            {
                throw new IOException($"Global-state scratch path escapes the project root: '{absolutePath}'.");
            }

            return canonical.Substring(rootWithSeparator.Length).Replace('\\', '/');
        }

        private void DeleteOwnedTransientFile(string relativePath, FileIdentity ownedIdentity, string label)
        {
            string absolutePath = ResolveProjectRelativePath(projectRoot, relativePath, allowMissingLeaf: true);
            if (!File.Exists(absolutePath))
            {
                return;
            }

            if (ownedIdentity == null)
            {
                throw new IOException(
                    $"Cannot prove ownership of {label} '{relativePath}'; recovery stopped.");
            }

            FileIdentity currentIdentity = CaptureIdentity(relativePath, requireExisting: true);
            if (!SameContent(currentIdentity, ownedIdentity))
            {
                throw new IOException(
                    $"Refusing to delete externally changed {label} '{relativePath}'.");
            }

            DeleteFileExactly(absolutePath);
        }

        private void DeleteTransactionStage(string relativePath, FileIdentity expectedIdentity, string label)
        {
            string absolutePath = ResolveProjectRelativePath(projectRoot, relativePath, allowMissingLeaf: true);
            if (!File.Exists(absolutePath))
            {
                return;
            }

            if (expectedIdentity != null)
            {
                FileIdentity currentIdentity = CaptureIdentity(relativePath, requireExisting: true);
                if (!SameContent(currentIdentity, expectedIdentity))
                {
                    throw new IOException(
                        $"Refusing to delete externally changed {label} '{relativePath}'.");
                }
            }
            else
            {
                string ownerId = journal?.transactionId ?? pendingRecoveryJournal?.transactionId;
                if (string.IsNullOrEmpty(ownerId)
                    || relativePath.IndexOf(ownerId, StringComparison.Ordinal) < 0)
                {
                    throw new IOException($"Cannot prove ownership of {label} '{relativePath}'.");
                }
            }

            DeleteFileExactly(absolutePath);
        }

        private void VerifyOriginalState(Journal state)
        {
            VerifyOriginalFileOrAbsence(state.playerSettings);
            if (state.versionInfo != null)
            {
                VerifyVersionOriginalState(state.versionInfo);
            }
        }

        private void VerifyVersionOriginalState(VersionInfoRecord version)
        {
            VerifyOriginalFileOrAbsence(version.asset);
            VerifyOriginalFileOrAbsence(version.meta);
            EnsureFileAbsent(version.stageAssetPath, "VersionInfoData staging asset");
            EnsureFileAbsent(version.stageMetaPath, "VersionInfoData staging meta file");
            if (version.ownsParentRoot)
            {
                EnsureFileSystemEntryAbsent(version.ownedParentRootPath, "owned VersionInfoData parent root");
                EnsureFileSystemEntryAbsent(version.ownedParentRootMetaPath, "owned VersionInfoData parent meta file");
                EnsureFileSystemEntryAbsent(version.ownedParentScratchPath, "owned VersionInfoData parent scratch");
            }
        }

        private void VerifyOriginalFileOrAbsence(FileRecord record)
        {
            FileIdentity currentIdentity = CaptureIdentity(record.relativePath, requireExisting: false);
            if (!MatchesRecordExistenceAndIdentity(record, currentIdentity))
            {
                throw new IOException(
                    $"Global-state restoration verification failed for '{record.relativePath}'.");
            }
        }

        private void CleanupTransactionArtifacts(Journal completed)
        {
            string transactionDirectory = ResolveProjectRelativePath(
                projectRoot,
                completed.transactionDirectory,
                allowMissingLeaf: true);
            if (Directory.Exists(transactionDirectory))
            {
                EnsurePathHasNoReparsePoints(projectRoot, transactionDirectory, allowMissingLeaf: false);
                var expectedSnapshots = new HashSet<string>(PathComparer);
                AddExpectedSnapshot(completed.playerSettings, expectedSnapshots);
                if (completed.versionInfo != null)
                {
                    AddExpectedSnapshot(completed.versionInfo.asset, expectedSnapshots);
                    AddExpectedSnapshot(completed.versionInfo.meta, expectedSnapshots);
                }

                foreach (string entry in Directory.GetFileSystemEntries(transactionDirectory))
                {
                    string canonicalEntry = Path.GetFullPath(entry);
                    if (!expectedSnapshots.Remove(canonicalEntry) || Directory.Exists(canonicalEntry))
                    {
                        throw new IOException(
                            $"Unrecognized global-state transaction artifact blocks cleanup: '{canonicalEntry}'.");
                    }

                    DeleteFileExactly(canonicalEntry);
                }

                if (expectedSnapshots.Count != 0)
                {
                    foreach (string missingSnapshot in expectedSnapshots)
                    {
                        if (File.Exists(missingSnapshot))
                        {
                            throw new IOException(
                                $"Global-state snapshot inventory changed during cleanup: '{missingSnapshot}'.");
                        }
                    }
                }

                Directory.Delete(transactionDirectory, recursive: false);
                if (Directory.Exists(transactionDirectory))
                {
                    throw new IOException(
                        $"Global-state transaction directory still exists after cleanup: '{transactionDirectory}'.");
                }
            }

            CleanupAtomicJournalScratch(completed);
            DeleteFileExactly(journalPath);
        }

        private void WriteJournal()
        {
            EnsureNotReleased();
            journal.sequence++;
            byte[] payloadBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(journal, false));
            if (payloadBytes.Length > MaximumJournalBytes)
            {
                throw new IOException(
                    $"Global-state journal payload exceeds {MaximumJournalBytes} bytes.");
            }

            var envelope = new JournalEnvelope
            {
                schemaVersion = SchemaVersion,
                payloadBase64 = Convert.ToBase64String(payloadBytes),
                sha256 = ComputeSha256(payloadBytes)
            };
            byte[] envelopeBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope, true));
            if (envelopeBytes.Length > MaximumJournalBytes)
            {
                throw new IOException(
                    $"Global-state journal exceeds {MaximumJournalBytes} bytes.");
            }

            string temporaryPath = journalPath + ".tmp-" + journal.transactionId + "-" + journal.sequence;
            string backupPath = journalPath + ".bak";
            WriteDurably(temporaryPath, envelopeBytes, createNew: true);
            try
            {
                if (File.Exists(journalPath))
                {
                    if (File.Exists(backupPath))
                    {
                        DeleteFileExactly(backupPath);
                    }

                    File.Replace(temporaryPath, journalPath, backupPath);
                    DeleteFileExactly(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, journalPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    DeleteFileExactly(temporaryPath);
                }
            }

            Journal verified = ReadJournal(journalPath);
            if (!string.Equals(verified.transactionId, journal.transactionId, StringComparison.Ordinal)
                || verified.sequence != journal.sequence)
            {
                throw new IOException("Global-state journal verification did not observe the newly written sequence.");
            }
        }

        private Journal ReadJournal(string path)
        {
            byte[] envelopeBytes = ReadBoundedFile(path, MaximumJournalBytes, "global-state journal");
            JournalEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<JournalEnvelope>(Encoding.UTF8.GetString(envelopeBytes));
            }
            catch (Exception exception)
            {
                throw new IOException($"Global-state journal is malformed: '{path}'.", exception);
            }

            if (envelope == null
                || !string.Equals(envelope.schemaVersion, SchemaVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(envelope.payloadBase64)
                || string.IsNullOrWhiteSpace(envelope.sha256))
            {
                throw new IOException($"Global-state journal envelope is invalid: '{path}'.");
            }

            byte[] payloadBytes;
            try
            {
                payloadBytes = Convert.FromBase64String(envelope.payloadBase64);
            }
            catch (FormatException exception)
            {
                throw new IOException($"Global-state journal payload is not valid Base64: '{path}'.", exception);
            }

            if (payloadBytes.Length > MaximumJournalBytes
                || !FixedTimeEquals(envelope.sha256, ComputeSha256(payloadBytes)))
            {
                throw new IOException($"Global-state journal checksum validation failed: '{path}'.");
            }

            Journal parsed;
            try
            {
                parsed = JsonUtility.FromJson<Journal>(Encoding.UTF8.GetString(payloadBytes));
            }
            catch (Exception exception)
            {
                throw new IOException($"Global-state journal payload is malformed: '{path}'.", exception);
            }

            if (parsed == null)
            {
                throw new IOException($"Global-state journal payload is empty: '{path}'.");
            }

            return parsed;
        }

        private void ValidateJournal(Journal candidate)
        {
            if (!string.Equals(candidate.schemaVersion, SchemaVersion, StringComparison.Ordinal)
                || !IsGuidN(candidate.transactionId)
                || candidate.sequence <= 0
                || !IsKnownGlobalPhase(candidate.phase))
            {
                throw new IOException("Global-state journal header is invalid.");
            }

            ValidateBoundedJournalString(candidate.originalCompanyName, "company name");
            ValidateBoundedJournalString(candidate.originalProductName, "product name");
            ValidateBoundedJournalString(candidate.originalBundleVersion, "bundle version");
            ValidateBoundedJournalString(candidate.originalApplicationIdentifier, "application identifier");

            if (!BuildCommandLine.IsSupportedBuildTarget(
                    (BuildTarget)candidate.originalActiveBuildTarget)
                || !BuildCommandLine.IsSupportedBuildTarget(
                    (BuildTarget)candidate.requestedBuildTarget))
            {
                throw new IOException(
                    "Global-state journal contains an unsupported build target.");
            }

            var originalBackend = (ScriptingImplementation)candidate.originalScriptingBackend;
            if (originalBackend != ScriptingImplementation.Mono2x
                && originalBackend != ScriptingImplementation.IL2CPP)
            {
                throw new IOException(
                    "Global-state journal contains an unsupported scripting backend.");
            }

            if (!PathEquals(candidate.projectRoot, NormalizeAbsolutePath(projectRoot)))
            {
                throw new IOException(
                    "The global-state journal belongs to a different project path. " +
                    $"Recorded='{candidate.projectRoot}', current='{NormalizeAbsolutePath(projectRoot)}'.");
            }

            string expectedTransactionDirectory =
                StateDirectoryRelativePath + "/transaction-" + candidate.transactionId;
            if (!string.Equals(candidate.transactionDirectory, expectedTransactionDirectory, StringComparison.Ordinal))
            {
                throw new IOException("Global-state transaction directory does not match its transaction id.");
            }

            ValidateFileRecord(candidate.playerSettings, requireExistingRecord: true, candidate.transactionDirectory);
            if (!string.Equals(candidate.playerSettings.relativePath, "ProjectSettings/ProjectSettings.asset", StringComparison.Ordinal))
            {
                throw new IOException("Global-state journal references an unexpected PlayerSettings path.");
            }

            candidate.transientPlayerSettings = NormalizeOptionalIdentity(candidate.transientPlayerSettings);
            if (candidate.transientPlayerSettings != null)
            {
                ValidateIdentity(candidate.transientPlayerSettings, candidate.playerSettings.relativePath);
            }

            bool phaseRequiresTransient = string.Equals(candidate.phase, GlobalPhaseActive, StringComparison.Ordinal)
                || string.Equals(candidate.phase, GlobalPhaseRestored, StringComparison.Ordinal);
            if (phaseRequiresTransient != (candidate.transientPlayerSettings != null))
            {
                throw new IOException(
                    "Global-state journal phase and transient PlayerSettings identity are inconsistent.");
            }

            if (!candidate.hasVersionInfo)
            {
                candidate.versionInfo = null;
            }
            else if (candidate.versionInfo != null)
            {
                ValidateVersionRecord(candidate.versionInfo, candidate);
            }
            else
            {
                throw new IOException("Global-state journal declares VersionInfoData without a record.");
            }

            if (candidate.hasVersionInfo
                && !string.Equals(candidate.phase, GlobalPhaseActive, StringComparison.Ordinal)
                && !string.Equals(candidate.phase, GlobalPhaseRestored, StringComparison.Ordinal))
            {
                throw new IOException(
                    "VersionInfoData cannot be enlisted before the global-state transaction is active.");
            }

            string expectedAbsoluteTransactionDirectory = ResolveProjectRelativePath(
                projectRoot,
                candidate.transactionDirectory,
                allowMissingLeaf: true);
            foreach (string directory in Directory.GetDirectories(stateDirectory, "transaction-*", SearchOption.TopDirectoryOnly))
            {
                if (!PathEquals(directory, expectedAbsoluteTransactionDirectory))
                {
                    throw new IOException(
                        $"Detached transaction directory conflicts with the active journal: '{directory}'.");
                }
            }
        }

        private void ValidateVersionRecord(VersionInfoRecord version, Journal owner)
        {
            if (!IsKnownVersionState(version.state))
            {
                throw new IOException("Global-state journal contains an unknown VersionInfoData state.");
            }

            ValidateFileRecord(version.asset, requireExistingRecord: false, owner.transactionDirectory);
            ValidateFileRecord(version.meta, requireExistingRecord: false, owner.transactionDirectory);
            if (!version.asset.relativePath.StartsWith("Assets/", StringComparison.Ordinal)
                || !version.asset.relativePath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(version.meta.relativePath, version.asset.relativePath + ".meta", StringComparison.Ordinal)
                || version.asset.existed != version.meta.existed)
            {
                throw new IOException("Global-state journal contains invalid VersionInfoData paths or existence state.");
            }

            string parent = Path.GetDirectoryName(version.asset.relativePath)?.Replace('\\', '/');
            string expectedStage = parent + "/__BuildPipelineVersionInfo_" + owner.transactionId + ".asset";
            if (!string.Equals(version.stageAssetPath, expectedStage, StringComparison.Ordinal)
                || !string.Equals(version.stageMetaPath, expectedStage + ".meta", StringComparison.Ordinal))
            {
                throw new IOException("Global-state journal contains unexpected VersionInfoData staging paths.");
            }

            ResolveProjectRelativePath(projectRoot, version.stageAssetPath, allowMissingLeaf: true);
            ResolveProjectRelativePath(projectRoot, version.stageMetaPath, allowMissingLeaf: true);
            version.stageAsset = NormalizeOptionalIdentity(version.stageAsset);
            version.stageMeta = NormalizeOptionalIdentity(version.stageMeta);
            version.installedAsset = NormalizeOptionalIdentity(version.installedAsset);
            version.installedMeta = NormalizeOptionalIdentity(version.installedMeta);
            ValidateOptionalIdentity(version.stageAsset, version.stageAssetPath);
            ValidateOptionalIdentity(version.stageMeta, version.stageMetaPath);
            ValidateOptionalIdentity(version.installedAsset, version.asset.relativePath);
            ValidateOptionalIdentity(version.installedMeta, version.meta.relativePath);

            ValidateOwnedVersionParent(version, owner, parent);

            bool stageIdentityRequired = string.Equals(version.state, VersionStateStageReady, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStateInstalling, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStateInstalled, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStateRestored, StringComparison.Ordinal);
            bool hasAnyStageIdentity = version.stageAsset != null || version.stageMeta != null;
            bool hasBothStageIdentities = version.stageAsset != null && version.stageMeta != null;
            if (hasAnyStageIdentity != hasBothStageIdentities
                || stageIdentityRequired != hasBothStageIdentities)
            {
                throw new IOException(
                    "VersionInfoData journal state and staging identities are inconsistent.");
            }

            bool installedIdentityRequired = string.Equals(version.state, VersionStateInstalled, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStateRestored, StringComparison.Ordinal);
            bool hasAnyInstalledIdentity = version.installedAsset != null || version.installedMeta != null;
            bool hasBothInstalledIdentities = version.installedAsset != null && version.installedMeta != null;
            if (hasAnyInstalledIdentity != hasBothInstalledIdentities
                || installedIdentityRequired != hasBothInstalledIdentities)
            {
                throw new IOException(
                    "VersionInfoData journal state and installed identities are inconsistent.");
            }
        }

        private void ValidateOwnedVersionParent(
            VersionInfoRecord version,
            Journal owner,
            string targetParent)
        {
            bool parentPreparationState =
                string.Equals(version.state, VersionStateParentPrepared, StringComparison.Ordinal)
                || string.Equals(version.state, VersionStateParentInstalling, StringComparison.Ordinal);
            if (!version.ownsParentRoot)
            {
                if (parentPreparationState
                    || !string.IsNullOrEmpty(version.ownedParentRootPath)
                    || !string.IsNullOrEmpty(version.ownedParentRootMetaPath)
                    || !string.IsNullOrEmpty(version.ownedParentScratchPath)
                    || !string.IsNullOrEmpty(version.ownedParentMarkerSha256))
                {
                    throw new IOException(
                        "VersionInfoData journal contains unexpected owned-parent state.");
                }

                return;
            }

            if (version.asset.existed
                || version.meta.existed
                || string.IsNullOrEmpty(version.ownedParentRootPath)
                || !version.ownedParentRootPath.StartsWith("Assets/", StringComparison.Ordinal)
                || (!string.Equals(targetParent, version.ownedParentRootPath, StringComparison.Ordinal)
                    && !targetParent.StartsWith(version.ownedParentRootPath + "/", StringComparison.Ordinal)))
            {
                throw new IOException(
                    "VersionInfoData journal contains an invalid owned-parent root.");
            }

            string normalizedRoot = NormalizeAndValidateProjectRelativePath(
                projectRoot,
                version.ownedParentRootPath,
                "owned VersionInfoData parent root");
            string expectedMeta = normalizedRoot + ".meta";
            string expectedScratch = GetOwnedParentScratchPath(normalizedRoot, owner.transactionId);
            string expectedMarkerSha256 = ComputeSha256(GetOwnedParentMarkerBytes(owner.transactionId));
            if (!string.Equals(version.ownedParentRootMetaPath, expectedMeta, StringComparison.Ordinal)
                || !string.Equals(version.ownedParentScratchPath, expectedScratch, StringComparison.Ordinal)
                || !IsSha256(version.ownedParentMarkerSha256)
                || !FixedTimeEquals(version.ownedParentMarkerSha256, expectedMarkerSha256))
            {
                throw new IOException(
                    "VersionInfoData journal contains invalid owned-parent metadata.");
            }

            ResolveProjectRelativePath(projectRoot, version.ownedParentRootMetaPath, allowMissingLeaf: true);
            ResolveProjectRelativePath(projectRoot, version.ownedParentScratchPath, allowMissingLeaf: true);
            string existingParent = Path.GetDirectoryName(version.ownedParentRootPath)?.Replace('\\', '/');
            string existingParentPath = ResolveProjectRelativePath(
                projectRoot,
                existingParent,
                allowMissingLeaf: false);
            if (!Directory.Exists(existingParentPath))
            {
                throw new IOException(
                    "The recorded owner of the VersionInfoData parent no longer has its original existing parent.");
            }
        }

        private void ValidateFileRecord(FileRecord record, bool requireExistingRecord, string transactionDirectory)
        {
            if (record == null
                || string.IsNullOrWhiteSpace(record.relativePath)
                || record.relativePath.Length > MaximumPathCharacters)
            {
                throw new IOException("Global-state journal contains an invalid file record.");
            }

            NormalizeAndValidateProjectRelativePath(projectRoot, record.relativePath, "journal file path");
            if (requireExistingRecord && !record.existed)
            {
                throw new IOException($"Required journal file did not originally exist: '{record.relativePath}'.");
            }

            if (record.existed)
            {
                if (record.length < 0
                    || record.length > MaximumSnapshotBytes
                    || record.lastWriteTimeUtcTicks <= 0
                    || record.lastWriteTimeUtcTicks > DateTime.MaxValue.Ticks
                    || !IsSha256(record.sha256)
                    || !string.Equals(
                        record.snapshotRelativePath,
                        ExpectedSnapshotPath(transactionDirectory, record.relativePath),
                        StringComparison.Ordinal))
                {
                    throw new IOException($"Global-state journal snapshot record is invalid: '{record.relativePath}'.");
                }

                ResolveProjectRelativePath(projectRoot, record.snapshotRelativePath, allowMissingLeaf: true);
            }
            else if (!string.IsNullOrEmpty(record.snapshotRelativePath)
                     || record.length != 0
                     || !string.IsNullOrEmpty(record.sha256))
            {
                throw new IOException($"Absent journal file unexpectedly has snapshot data: '{record.relativePath}'.");
            }
        }

        private static string ExpectedSnapshotPath(string transactionDirectory, string relativePath)
        {
            if (string.Equals(relativePath, "ProjectSettings/ProjectSettings.asset", StringComparison.Ordinal))
            {
                return transactionDirectory + "/player-settings.snapshot";
            }

            return relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? transactionDirectory + "/version-info-meta.snapshot"
                : transactionDirectory + "/version-info-asset.snapshot";
        }

        private FileRecord CaptureFileRecord(
            string relativePath,
            string snapshotRelativePath,
            bool requireExisting)
        {
            string normalized = NormalizeAndValidateProjectRelativePath(
                projectRoot,
                relativePath,
                "transaction file path");
            string absolutePath = ResolveProjectRelativePath(projectRoot, normalized, allowMissingLeaf: !requireExisting);
            bool exists = File.Exists(absolutePath);
            if (requireExisting && !exists)
            {
                throw new FileNotFoundException("Required transactional file was not found.", absolutePath);
            }

            if (!exists)
            {
                return new FileRecord
                {
                    relativePath = normalized,
                    existed = false
                };
            }

            FileIdentity identity = CaptureIdentity(normalized, requireExisting: true);
            return new FileRecord
            {
                relativePath = normalized,
                existed = true,
                length = identity.length,
                sha256 = identity.sha256,
                lastWriteTimeUtcTicks = identity.lastWriteTimeUtcTicks,
                attributes = identity.attributes,
                snapshotRelativePath = snapshotRelativePath
            };
        }

        private FileIdentity CaptureIdentity(string relativePath, bool requireExisting)
        {
            string normalized = NormalizeAndValidateProjectRelativePath(
                projectRoot,
                relativePath,
                "identity path");
            string absolutePath = ResolveProjectRelativePath(projectRoot, normalized, allowMissingLeaf: !requireExisting);
            if (!File.Exists(absolutePath))
            {
                if (requireExisting)
                {
                    throw new FileNotFoundException("Transactional file was not found.", absolutePath);
                }

                return new FileIdentity
                {
                    relativePath = normalized,
                    exists = false
                };
            }

            FileInfo before = new FileInfo(absolutePath);
            if (before.Length > MaximumSnapshotBytes)
            {
                throw new IOException(
                    $"Transactional file exceeds the {MaximumSnapshotBytes}-byte snapshot budget: '{relativePath}'.");
            }

            string hash;
            using (FileStream stream = new FileStream(
                       absolutePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       BufferSize,
                       FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = ToHex(sha256.ComputeHash(stream));
            }

            FileInfo after = new FileInfo(absolutePath);
            FileAttributes attributes = File.GetAttributes(absolutePath);
            if (before.Length != after.Length
                || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
            {
                throw new IOException(
                    $"Transactional file changed while its identity was captured: '{relativePath}'.");
            }

            return new FileIdentity
            {
                relativePath = normalized,
                exists = true,
                length = after.Length,
                sha256 = hash,
                lastWriteTimeUtcTicks = after.LastWriteTimeUtc.Ticks,
                attributes = (int)attributes
            };
        }

        private void WriteSnapshot(FileRecord record)
        {
            if (record == null || !record.existed)
            {
                return;
            }

            string sourcePath = ResolveProjectRelativePath(
                projectRoot,
                record.relativePath,
                allowMissingLeaf: false);
            FileIdentity before = CaptureIdentity(record.relativePath, requireExisting: true);
            if (!MatchesRecordIdentity(record, before))
            {
                throw new IOException(
                    $"Transactional source changed before snapshot capture: '{record.relativePath}'.");
            }

            byte[] bytes = ReadBoundedFile(sourcePath, MaximumSnapshotBytes, "transaction snapshot source");
            if (bytes.LongLength != record.length || !FixedTimeEquals(record.sha256, ComputeSha256(bytes)))
            {
                throw new IOException(
                    $"Transactional source changed before its snapshot was persisted: '{record.relativePath}'.");
            }

            FileIdentity after = CaptureIdentity(record.relativePath, requireExisting: true);
            if (!MatchesRecordIdentity(record, after))
            {
                throw new IOException(
                    $"Transactional source changed during snapshot capture: '{record.relativePath}'.");
            }

            string snapshotPath = ResolveProjectRelativePath(
                projectRoot,
                record.snapshotRelativePath,
                allowMissingLeaf: true);
            string parent = Path.GetDirectoryName(snapshotPath);
            Directory.CreateDirectory(parent);
            EnsurePathHasNoReparsePoints(projectRoot, parent, allowMissingLeaf: false);
            WriteDurably(snapshotPath, bytes, createNew: true);
            ReadAndVerifySnapshot(record);
        }

        private byte[] ReadAndVerifySnapshot(FileRecord record)
        {
            if (record == null || !record.existed)
            {
                throw new InvalidOperationException("Only existing files have durable snapshots.");
            }

            string snapshotPath = ResolveProjectRelativePath(
                projectRoot,
                record.snapshotRelativePath,
                allowMissingLeaf: false);
            byte[] bytes = ReadBoundedFile(snapshotPath, MaximumSnapshotBytes, "global-state snapshot");
            if (bytes.LongLength != record.length || !FixedTimeEquals(record.sha256, ComputeSha256(bytes)))
            {
                throw new IOException(
                    $"Global-state snapshot checksum validation failed: '{record.snapshotRelativePath}'.");
            }

            return bytes;
        }

        private void MoveOwnedStageFile(string sourceRelativePath, string targetRelativePath, FileIdentity expected)
        {
            string source = ResolveProjectRelativePath(projectRoot, sourceRelativePath, allowMissingLeaf: false);
            string target = ResolveProjectRelativePath(projectRoot, targetRelativePath, allowMissingLeaf: true);
            if (File.Exists(target))
            {
                throw new IOException($"VersionInfoData installation target unexpectedly exists: '{targetRelativePath}'.");
            }

            FileIdentity sourceIdentity = CaptureIdentity(sourceRelativePath, requireExisting: true);
            if (!SameContent(sourceIdentity, expected))
            {
                throw new IOException($"VersionInfoData staging file changed before installation: '{sourceRelativePath}'.");
            }

            File.Move(source, target);
        }

        private void EnsureFileAbsent(string relativePath, string label)
        {
            string absolutePath = ResolveProjectRelativePath(projectRoot, relativePath, allowMissingLeaf: true);
            if (File.Exists(absolutePath) || Directory.Exists(absolutePath))
            {
                throw new IOException($"The {label} path is occupied: '{relativePath}'.");
            }
        }

        private void EnsureFileSystemEntryAbsent(string relativePath, string label)
        {
            string absolutePath = ResolveProjectRelativePath(projectRoot, relativePath, allowMissingLeaf: true);
            if (File.Exists(absolutePath) || Directory.Exists(absolutePath))
            {
                throw new IOException($"The {label} still exists: '{relativePath}'.");
            }
        }

        private string FindFirstMissingAssetDirectory(string parentRelativePath)
        {
            string normalized = NormalizeAndValidateProjectRelativePath(
                projectRoot,
                parentRelativePath,
                "VersionInfoData parent path");
            if (!string.Equals(normalized, "Assets", StringComparison.Ordinal)
                && !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new IOException(
                    $"VersionInfoData parent path must be below Assets: '{parentRelativePath}'.");
            }

            string[] segments = normalized.Split('/');
            string current = segments[0];
            string currentAbsolute = ResolveProjectRelativePath(projectRoot, current, allowMissingLeaf: false);
            if (!Directory.Exists(currentAbsolute))
            {
                throw new DirectoryNotFoundException("The Unity Assets directory does not exist.");
            }

            for (int index = 1; index < segments.Length; index++)
            {
                current += "/" + segments[index];
                currentAbsolute = ResolveProjectRelativePath(projectRoot, current, allowMissingLeaf: true);
                if (Directory.Exists(currentAbsolute))
                {
                    EnsurePathHasNoReparsePoints(projectRoot, currentAbsolute, allowMissingLeaf: false);
                    continue;
                }

                if (File.Exists(currentAbsolute))
                {
                    throw new IOException(
                        $"VersionInfoData parent path is occupied by a file: '{current}'.");
                }

                return current;
            }

            return string.Empty;
        }

        private static string GetOwnedParentScratchPath(string missingParentRoot, string transactionId)
        {
            string existingParent = Path.GetDirectoryName(missingParentRoot)?.Replace('\\', '/');
            return existingParent + "/__BuildPipelineParent_" + transactionId;
        }

        private void PrepareAndInstallOwnedParent(
            VersionInfoRecord version,
            string targetParentRelativePath)
        {
            EnsureFileSystemEntryAbsent(version.ownedParentRootPath, "owned VersionInfoData parent root");
            EnsureFileSystemEntryAbsent(version.ownedParentRootMetaPath, "owned VersionInfoData parent meta file");
            EnsureFileSystemEntryAbsent(version.ownedParentScratchPath, "owned VersionInfoData parent scratch");

            string scratchPath = ResolveProjectRelativePath(
                projectRoot,
                version.ownedParentScratchPath,
                allowMissingLeaf: true);
            Directory.CreateDirectory(scratchPath);
            EnsurePathHasNoReparsePoints(projectRoot, scratchPath, allowMissingLeaf: false);

            string markerPath = Path.Combine(scratchPath, OwnedParentMarkerFileName);
            byte[] markerBytes = GetOwnedParentMarkerBytes(journal.transactionId);
            WriteDurably(markerPath, markerBytes, createNew: true);
            VerifyOwnedParentMarker(markerPath, version);

            string remaining = targetParentRelativePath.Substring(version.ownedParentRootPath.Length).Trim('/');
            if (!string.IsNullOrEmpty(remaining))
            {
                string nestedPath = Path.Combine(
                    scratchPath,
                    remaining.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(nestedPath);
                EnsurePathHasNoReparsePoints(projectRoot, nestedPath, allowMissingLeaf: false);
            }

            version.state = VersionStateParentPrepared;
            WriteJournal();
            version.state = VersionStateParentInstalling;
            WriteJournal();

            string rootPath = ResolveProjectRelativePath(
                projectRoot,
                version.ownedParentRootPath,
                allowMissingLeaf: true);
            Directory.Move(scratchPath, rootPath);
            VerifyOwnedParentMarker(
                Path.Combine(rootPath, OwnedParentMarkerFileName),
                version);
        }

        private void CleanupOwnedParent(VersionInfoRecord version)
        {
            if (!version.ownsParentRoot)
            {
                return;
            }

            string rootPath = ResolveProjectRelativePath(
                projectRoot,
                version.ownedParentRootPath,
                allowMissingLeaf: true);
            string rootMetaPath = ResolveProjectRelativePath(
                projectRoot,
                version.ownedParentRootMetaPath,
                allowMissingLeaf: true);
            string scratchPath = ResolveProjectRelativePath(
                projectRoot,
                version.ownedParentScratchPath,
                allowMissingLeaf: true);

            bool rootExists = Directory.Exists(rootPath);
            bool scratchExists = Directory.Exists(scratchPath);
            if (File.Exists(rootPath) || File.Exists(scratchPath) || Directory.Exists(rootMetaPath))
            {
                throw new IOException("Owned VersionInfoData parent paths have incompatible filesystem entry types.");
            }

            if (rootExists && scratchExists)
            {
                throw new IOException(
                    "Both owned VersionInfoData parent root and scratch directories exist; recovery is ambiguous.");
            }

            if (rootExists)
            {
                ValidateOwnedParentTree(rootPath, version);
                Directory.Move(rootPath, scratchPath);
                scratchExists = true;
            }

            if (File.Exists(rootMetaPath))
            {
                EnsurePathHasNoReparsePoints(projectRoot, rootMetaPath, allowMissingLeaf: false);
                DeleteFileExactly(rootMetaPath);
            }

            if (scratchExists)
            {
                DeleteValidatedOwnedParentTree(scratchPath, version);
            }
        }

        private void ValidateOwnedParentTree(string containerPath, VersionInfoRecord version)
        {
            EnsurePathHasNoReparsePoints(projectRoot, containerPath, allowMissingLeaf: false);
            string markerPath = Path.Combine(containerPath, OwnedParentMarkerFileName);
            string[] immediateEntries = Directory.GetFileSystemEntries(containerPath);
            if (!File.Exists(markerPath))
            {
                if (immediateEntries.Length == 0)
                {
                    return;
                }

                throw new IOException(
                    $"Owned VersionInfoData parent marker is missing: '{containerPath}'.");
            }

            VerifyOwnedParentMarker(markerPath, version);
            BuildOwnedParentAllowList(
                containerPath,
                version,
                out HashSet<string> allowedDirectories,
                out HashSet<string> allowedFiles);

            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(containerPath);
            int entryCount = 0;
            while (pendingDirectories.Count > 0)
            {
                string directory = pendingDirectories.Pop();
                EnsurePathHasNoReparsePoints(projectRoot, directory, allowMissingLeaf: false);
                foreach (string entry in Directory.GetFileSystemEntries(directory))
                {
                    entryCount++;
                    if (entryCount > MaximumOwnedParentEntries)
                    {
                        throw new IOException(
                            $"Owned VersionInfoData parent exceeds {MaximumOwnedParentEntries} entries.");
                    }

                    string canonicalEntry = Path.GetFullPath(entry);
                    if (Directory.Exists(canonicalEntry))
                    {
                        EnsurePathHasNoReparsePoints(projectRoot, canonicalEntry, allowMissingLeaf: false);
                        if (!allowedDirectories.Contains(canonicalEntry))
                        {
                            throw new IOException(
                                $"Unrecognized directory exists in the owned VersionInfoData parent: '{canonicalEntry}'.");
                        }

                        pendingDirectories.Push(canonicalEntry);
                    }
                    else
                    {
                        EnsurePathHasNoReparsePoints(projectRoot, canonicalEntry, allowMissingLeaf: false);
                        if (!allowedFiles.Contains(canonicalEntry))
                        {
                            throw new IOException(
                                $"Unrecognized file exists in the owned VersionInfoData parent: '{canonicalEntry}'.");
                        }
                    }
                }
            }
        }

        private void BuildOwnedParentAllowList(
            string containerPath,
            VersionInfoRecord version,
            out HashSet<string> allowedDirectories,
            out HashSet<string> allowedFiles)
        {
            allowedDirectories = new HashSet<string>(PathComparer)
            {
                Path.GetFullPath(containerPath)
            };
            allowedFiles = new HashSet<string>(PathComparer)
            {
                Path.Combine(containerPath, OwnedParentMarkerFileName),
                Path.Combine(containerPath, OwnedParentMarkerFileName + ".meta")
            };

            string targetParent = Path.GetDirectoryName(version.asset.relativePath)?.Replace('\\', '/');
            string remaining = targetParent.Substring(version.ownedParentRootPath.Length).Trim('/');
            string current = containerPath;
            if (!string.IsNullOrEmpty(remaining))
            {
                foreach (string segment in remaining.Split('/'))
                {
                    string next = Path.Combine(current, segment);
                    allowedDirectories.Add(Path.GetFullPath(next));
                    allowedFiles.Add(Path.GetFullPath(next + ".meta"));
                    current = next;
                }
            }

            AddMappedOwnedFile(version.asset.relativePath, version, containerPath, allowedFiles);
            AddMappedOwnedFile(version.meta.relativePath, version, containerPath, allowedFiles);
            AddMappedOwnedFile(version.stageAssetPath, version, containerPath, allowedFiles);
            AddMappedOwnedFile(version.stageMetaPath, version, containerPath, allowedFiles);
        }

        private static void AddMappedOwnedFile(
            string projectRelativePath,
            VersionInfoRecord version,
            string containerPath,
            ISet<string> files)
        {
            string suffix = projectRelativePath.Substring(version.ownedParentRootPath.Length).TrimStart('/');
            files.Add(Path.GetFullPath(Path.Combine(
                containerPath,
                suffix.Replace('/', Path.DirectorySeparatorChar))));
        }

        private void DeleteValidatedOwnedParentTree(string containerPath, VersionInfoRecord version)
        {
            ValidateOwnedParentTree(containerPath, version);
            string markerPath = Path.Combine(containerPath, OwnedParentMarkerFileName);
            if (!File.Exists(markerPath))
            {
                Directory.Delete(containerPath, recursive: false);
                return;
            }

            var directories = new List<string>();
            var pending = new Stack<string>();
            pending.Push(containerPath);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                directories.Add(directory);
                foreach (string entry in Directory.GetFileSystemEntries(directory))
                {
                    if (Directory.Exists(entry))
                    {
                        pending.Push(entry);
                    }
                    else if (!PathEquals(entry, markerPath))
                    {
                        DeleteFileExactly(entry);
                    }
                }
            }

            directories.Sort((left, right) => right.Length.CompareTo(left.Length));
            foreach (string directory in directories)
            {
                if (!PathEquals(directory, containerPath))
                {
                    Directory.Delete(directory, recursive: false);
                }
            }

            DeleteFileExactly(markerPath);
            Directory.Delete(containerPath, recursive: false);
        }

        private static byte[] GetOwnedParentMarkerBytes(string transactionId)
        {
            return Encoding.UTF8.GetBytes(
                "schema=1\ntransaction=" + transactionId + "\nowner=Build.Pipeline.Editor\n");
        }

        private static void VerifyOwnedParentMarker(string markerPath, VersionInfoRecord version)
        {
            byte[] bytes = ReadBoundedFile(markerPath, 1024, "owned VersionInfoData parent marker");
            if (!FixedTimeEquals(version.ownedParentMarkerSha256, ComputeSha256(bytes)))
            {
                throw new IOException(
                    $"Owned VersionInfoData parent marker checksum is invalid: '{markerPath}'.");
            }
        }

        private void ValidateStateDirectoryInventoryBeforeLoad()
        {
            EnsurePathHasNoReparsePoints(projectRoot, stateDirectory, allowMissingLeaf: false);
            string[] directories = Directory.GetDirectories(stateDirectory, "*", SearchOption.TopDirectoryOnly);
            if (directories.Length > MaximumTransactionDirectories)
            {
                throw new IOException("Global-state directory contains too many transaction directories.");
            }

            foreach (string directory in directories)
            {
                string name = Path.GetFileName(directory);
                if (name == null || !name.StartsWith("transaction-", StringComparison.Ordinal))
                {
                    throw new IOException(
                        $"Unrecognized directory exists in the global-state transaction root: '{directory}'.");
                }

                EnsurePathHasNoReparsePoints(projectRoot, directory, allowMissingLeaf: false);
            }

            foreach (string file in Directory.GetFiles(stateDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                bool known = string.Equals(name, LockFileName, StringComparison.Ordinal)
                    || string.Equals(name, JournalFileName, StringComparison.Ordinal)
                    || string.Equals(name, JournalFileName + ".bak", StringComparison.Ordinal)
                    || (name != null && name.StartsWith(JournalFileName + ".tmp-", StringComparison.Ordinal));
                if (!known)
                {
                    throw new IOException(
                        $"Unrecognized file exists in the global-state transaction root: '{file}'.");
                }

                EnsurePathHasNoReparsePoints(projectRoot, file, allowMissingLeaf: false);
            }
        }

        private void EnsureNoDetachedArtifacts()
        {
            string[] directories = Directory.GetDirectories(stateDirectory, "transaction-*", SearchOption.TopDirectoryOnly);
            string[] scratchFiles = Directory.GetFiles(stateDirectory, "active.json.*", SearchOption.TopDirectoryOnly);
            if (directories.Length != 0 || scratchFiles.Length != 0)
            {
                throw new IOException(
                    "Detached global-state transaction artifacts exist without a valid active journal. " +
                    $"Inspect '{stateDirectory}' before another build.");
            }
        }

        private void CleanupAtomicJournalScratch(Journal activeJournal)
        {
            string backupPath = journalPath + ".bak";
            if (File.Exists(backupPath))
            {
                Journal backup = ReadJournal(backupPath);
                if (!string.Equals(backup.transactionId, activeJournal.transactionId, StringComparison.Ordinal)
                    || backup.sequence >= activeJournal.sequence)
                {
                    throw new IOException("Global-state journal backup conflicts with the active journal.");
                }

                DeleteFileExactly(backupPath);
            }

            string prefix = Path.GetFileName(journalPath) + ".tmp-";
            foreach (string temporaryPath in Directory.GetFiles(stateDirectory, prefix + "*", SearchOption.TopDirectoryOnly))
            {
                Journal temporary = ReadJournal(temporaryPath);
                if (!string.Equals(temporary.transactionId, activeJournal.transactionId, StringComparison.Ordinal))
                {
                    throw new IOException(
                        $"Global-state journal temporary candidate belongs to another transaction: '{temporaryPath}'.");
                }

                DeleteFileExactly(temporaryPath);
            }
        }

        private void WriteLockOwner()
        {
            lockStream.SetLength(0);
            string owner =
                "process=" + System.Diagnostics.Process.GetCurrentProcess().Id + Environment.NewLine +
                "acquiredUtc=" + DateTime.UtcNow.ToString("O") + Environment.NewLine +
                "project=" + NormalizeAbsolutePath(projectRoot) + Environment.NewLine;
            byte[] bytes = Encoding.UTF8.GetBytes(owner);
            lockStream.Write(bytes, 0, bytes.Length);
            lockStream.Flush(true);
        }

        private Exception TryReleaseLock()
        {
            try
            {
                lockStream?.Dispose();
                lockStream = null;
                return null;
            }
            catch (Exception exception)
            {
                return new IOException(
                    $"Failed to release the global Unity-state lock '{lockPath}'.",
                    exception);
            }
        }

        private void RequirePhase(string expected)
        {
            EnsureActiveJournal();
            if (!string.Equals(journal.phase, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Expected global-state phase '{expected}', actual '{journal.phase}'.");
            }
        }

        private void RequireVersionState(string expected)
        {
            EnsureActiveJournal();
            if (journal.versionInfo == null
                || !string.Equals(journal.versionInfo.state, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Expected VersionInfoData state '{expected}', actual '{journal.versionInfo?.state ?? "<none>"}'.");
            }
        }

        private void EnsureActiveJournal()
        {
            EnsureNotReleased();
            if (journal == null)
            {
                throw new InvalidOperationException("No active global-state journal exists.");
            }
        }

        private void EnsureNotReleased()
        {
            if (released || lockStream == null)
            {
                throw new ObjectDisposedException(nameof(GlobalBuildStateTransaction));
            }
        }

        private static string NormalizeAndValidateProjectRelativePath(
            string root,
            string relativePath,
            string label)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || relativePath.Length > MaximumPathCharacters
                || Path.IsPathRooted(relativePath)
                || relativePath.Contains("\\")
                || relativePath.StartsWith("/", StringComparison.Ordinal)
                || relativePath.EndsWith("/", StringComparison.Ordinal))
            {
                throw new IOException($"{label} is not a canonical project-relative path: '{relativePath}'.");
            }

            string[] segments = relativePath.Split('/');
            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment)
                    || string.Equals(segment, ".", StringComparison.Ordinal)
                    || string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    throw new IOException($"{label} contains an invalid path segment: '{relativePath}'.");
                }
            }

            ResolveProjectRelativePath(root, relativePath, allowMissingLeaf: true);
            return relativePath;
        }

        private static string ResolveProjectRelativePath(
            string root,
            string relativePath,
            bool allowMissingLeaf)
        {
            string normalized = NormalizeRelativeSeparators(relativePath);
            string absolute = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            string rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(rootWithSeparator, PathComparison))
            {
                throw new IOException($"Transactional path escapes the project root: '{relativePath}'.");
            }

            EnsurePathHasNoReparsePoints(root, absolute, allowMissingLeaf);
            return absolute;
        }

        private static string CanonicalizeDirectory(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A project root is required.", parameterName);
            }

            string fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"Project root does not exist: '{fullPath}'.");
            }

            return fullPath;
        }

        private static void EnsurePathHasNoReparsePoints(
            string root,
            string path,
            bool allowMissingLeaf)
        {
            string canonicalRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string canonicalPath = Path.GetFullPath(path);
            string rootWithSeparator = canonicalRoot + Path.DirectorySeparatorChar;
            if (!PathEquals(canonicalRoot, canonicalPath)
                && !canonicalPath.StartsWith(rootWithSeparator, PathComparison))
            {
                throw new IOException($"Path is outside the project root: '{canonicalPath}'.");
            }

            string current = canonicalRoot;
            CheckReparsePoint(current);
            if (PathEquals(canonicalRoot, canonicalPath))
            {
                return;
            }

            string relative = canonicalPath.Substring(rootWithSeparator.Length);
            string[] segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < segments.Length; index++)
            {
                current = Path.Combine(current, segments[index]);
                bool exists = File.Exists(current) || Directory.Exists(current);
                if (!exists)
                {
                    if (!allowMissingLeaf || index != segments.Length - 1)
                    {
                        return;
                    }

                    return;
                }

                CheckReparsePoint(current);
            }
        }

        private static void CheckReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Transactional path crosses a reparse point: '{path}'.");
            }
        }

        private static byte[] ReadBoundedFile(string path, int maximumBytes, string label)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new FileNotFoundException($"The {label} was not found.", path);
            }

            if (info.Length < 0 || info.Length > maximumBytes)
            {
                throw new IOException(
                    $"The {label} exceeds its {maximumBytes}-byte budget: '{path}'.");
            }

            return File.ReadAllBytes(path);
        }

        private static void WriteDurably(string path, byte[] bytes, bool createNew)
        {
            using (var stream = new FileStream(
                       path,
                       createNew ? FileMode.CreateNew : FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       BufferSize,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void DeleteFileExactly(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
            if (File.Exists(path))
            {
                throw new IOException($"File still exists after deletion: '{path}'.");
            }
        }

        private static bool MatchesRecordExistenceAndIdentity(FileRecord record, FileIdentity identity)
        {
            return record.existed
                ? MatchesRecordIdentity(record, identity)
                : identity != null && !identity.exists;
        }

        private static bool MatchesRecordIdentity(FileRecord record, FileIdentity identity)
        {
            return record != null
                && record.existed
                && identity != null
                && identity.exists
                && record.length == identity.length
                && record.lastWriteTimeUtcTicks == identity.lastWriteTimeUtcTicks
                && record.attributes == identity.attributes
                && FixedTimeEquals(record.sha256, identity.sha256);
        }

        private static bool MatchesRecordContent(FileRecord record, FileIdentity identity)
        {
            return record != null
                && record.existed
                && identity != null
                && identity.exists
                && record.length == identity.length
                && FixedTimeEquals(record.sha256, identity.sha256);
        }

        private static bool IsAllowedRestoreInput(
            FileRecord original,
            bool allowOwnedTransient,
            FileIdentity ownedTransient,
            FileIdentity actual)
        {
            return MatchesRecordContent(original, actual)
                || (allowOwnedTransient
                    && ownedTransient != null
                    && SameContent(actual, ownedTransient));
        }

        private static bool SameContent(FileIdentity first, FileIdentity second)
        {
            return first != null
                && second != null
                && first.exists
                && second.exists
                && first.length == second.length
                && FixedTimeEquals(first.sha256, second.sha256);
        }

        private static void VerifyIdentity(FileRecord expected, FileIdentity actual, string label)
        {
            if (!MatchesRecordIdentity(expected, actual))
            {
                throw new IOException($"{label} changed before the global-state snapshot was completed.");
            }
        }

        private static void ValidateOptionalIdentity(FileIdentity identity, string expectedPath)
        {
            if (identity != null)
            {
                ValidateIdentity(identity, expectedPath);
            }
        }

        private static FileIdentity NormalizeOptionalIdentity(FileIdentity identity)
        {
            if (identity == null)
            {
                return null;
            }

            bool isJsonUtilityDefault = string.IsNullOrEmpty(identity.relativePath)
                && !identity.exists
                && identity.length == 0
                && string.IsNullOrEmpty(identity.sha256)
                && identity.lastWriteTimeUtcTicks == 0
                && identity.attributes == 0;
            return isJsonUtilityDefault ? null : identity;
        }

        private static void ValidateIdentity(FileIdentity identity, string expectedPath)
        {
            if (!string.Equals(identity.relativePath, expectedPath, StringComparison.Ordinal)
                || !identity.exists
                || identity.length < 0
                || identity.length > MaximumSnapshotBytes
                || identity.lastWriteTimeUtcTicks <= 0
                || identity.lastWriteTimeUtcTicks > DateTime.MaxValue.Ticks
                || !IsSha256(identity.sha256))
            {
                throw new IOException($"Global-state journal contains an invalid identity for '{expectedPath}'.");
            }
        }

        private static bool IsGuidN(string value)
        {
            return value != null
                && value.Length == 32
                && Guid.TryParseExact(value, "N", out _);
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool digit = character >= '0' && character <= '9';
                bool lowerHex = character >= 'a' && character <= 'f';
                if (!digit && !lowerHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateBoundedJournalString(string value, string label)
        {
            if (value == null || value.Length > MaximumPathCharacters)
            {
                throw new IOException(
                    $"Global-state journal {label} is invalid or exceeds its size budget.");
            }
        }

        private static bool IsKnownGlobalPhase(string phase)
        {
            return string.Equals(phase, GlobalPhasePreparing, StringComparison.Ordinal)
                || string.Equals(phase, GlobalPhasePrepared, StringComparison.Ordinal)
                || string.Equals(phase, GlobalPhaseApplying, StringComparison.Ordinal)
                || string.Equals(phase, GlobalPhaseActive, StringComparison.Ordinal)
                || string.Equals(phase, GlobalPhaseRestored, StringComparison.Ordinal);
        }

        private static bool IsKnownVersionState(string state)
        {
            return string.Equals(state, VersionStatePreparing, StringComparison.Ordinal)
                || string.Equals(state, VersionStateParentPrepared, StringComparison.Ordinal)
                || string.Equals(state, VersionStateParentInstalling, StringComparison.Ordinal)
                || string.Equals(state, VersionStatePrepared, StringComparison.Ordinal)
                || string.Equals(state, VersionStateStageReady, StringComparison.Ordinal)
                || string.Equals(state, VersionStateInstalling, StringComparison.Ordinal)
                || string.Equals(state, VersionStateInstalled, StringComparison.Ordinal)
                || string.Equals(state, VersionStateRestored, StringComparison.Ordinal);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                builder.Append(bytes[index].ToString("x2"));
            }

            return builder.ToString();
        }

        private static bool FixedTimeEquals(string first, string second)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
            {
                return false;
            }

            byte[] firstBytes = Encoding.ASCII.GetBytes(first);
            byte[] secondBytes = Encoding.ASCII.GetBytes(second);
            if (firstBytes.Length != secondBytes.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < firstBytes.Length; index++)
            {
                difference |= firstBytes[index] ^ secondBytes[index];
            }

            return difference == 0;
        }

        private static string NormalizeAbsolutePath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private static string NormalizeRelativeSeparators(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        private static bool PathEquals(string first, string second)
        {
            return string.Equals(
                first?.TrimEnd('/', '\\'),
                second?.TrimEnd('/', '\\'),
                PathComparison);
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static StringComparer PathComparer =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private void AddExpectedSnapshot(FileRecord record, ISet<string> snapshots)
        {
            if (record == null || !record.existed)
            {
                return;
            }

            snapshots.Add(ResolveProjectRelativePath(
                projectRoot,
                record.snapshotRelativePath,
                allowMissingLeaf: true));
        }

        private const string GlobalPhasePreparing = "Preparing";
        private const string GlobalPhasePrepared = "Prepared";
        private const string GlobalPhaseApplying = "Applying";
        private const string GlobalPhaseActive = "Active";
        private const string GlobalPhaseRestored = "Restored";
        private const string VersionStatePreparing = "Preparing";
        private const string VersionStateParentPrepared = "ParentPrepared";
        private const string VersionStateParentInstalling = "ParentInstalling";
        private const string VersionStatePrepared = "Prepared";
        private const string VersionStateStageReady = "StageReady";
        private const string VersionStateInstalling = "Installing";
        private const string VersionStateInstalled = "Installed";
        private const string VersionStateRestored = "Restored";

        [Serializable]
        private sealed class JournalEnvelope
        {
            public string schemaVersion;
            public string payloadBase64;
            public string sha256;
        }

        [Serializable]
        private sealed class Journal
        {
            public string schemaVersion;
            public string transactionId;
            public string projectRoot;
            public string transactionDirectory;
            public string phase;
            public long sequence;
            public int originalActiveBuildTarget;
            public bool originalExportAndroidProject;
            public int requestedBuildTarget;
            public int originalScriptingBackend;
            public string originalCompanyName;
            public string originalProductName;
            public string originalBundleVersion;
            public string originalApplicationIdentifier;
            public FileRecord playerSettings;
            public FileIdentity transientPlayerSettings;
            public bool hasVersionInfo;
            public VersionInfoRecord versionInfo;
        }

        [Serializable]
        private sealed class VersionInfoRecord
        {
            public string state;
            public FileRecord asset;
            public FileRecord meta;
            public string stageAssetPath;
            public string stageMetaPath;
            public bool ownsParentRoot;
            public string ownedParentRootPath;
            public string ownedParentRootMetaPath;
            public string ownedParentScratchPath;
            public string ownedParentMarkerSha256;
            public FileIdentity stageAsset;
            public FileIdentity stageMeta;
            public FileIdentity installedAsset;
            public FileIdentity installedMeta;
        }

        internal sealed class PlayerSettingsPersistenceToken
        {
            internal long Length { get; }
            internal string Sha256 { get; }

            internal PlayerSettingsPersistenceToken(long length, string sha256)
            {
                Length = length;
                Sha256 = sha256 ?? string.Empty;
            }
        }

        [Serializable]
        private sealed class FileRecord
        {
            public string relativePath;
            public bool existed;
            public long length;
            public string sha256;
            public long lastWriteTimeUtcTicks;
            public int attributes;
            public string snapshotRelativePath;
        }

        [Serializable]
        private sealed class FileIdentity
        {
            public string relativePath;
            public bool exists;
            public long length;
            public string sha256;
            public long lastWriteTimeUtcTicks;
            public int attributes;
        }
    }

    internal readonly struct BuildTargetRecoveryState
    {
        internal BuildTargetRecoveryState(
            int activeBuildTarget,
            bool exportAndroidProject,
            int requestedBuildTarget,
            int scriptingBackend,
            string companyName,
            string productName,
            string bundleVersion,
            string applicationIdentifier)
        {
            ActiveBuildTarget = activeBuildTarget;
            ExportAndroidProject = exportAndroidProject;
            RequestedBuildTarget = requestedBuildTarget;
            ScriptingBackend = scriptingBackend;
            CompanyName = companyName;
            ProductName = productName;
            BundleVersion = bundleVersion;
            ApplicationIdentifier = applicationIdentifier;
        }

        internal int ActiveBuildTarget { get; }

        internal bool ExportAndroidProject { get; }

        internal int RequestedBuildTarget { get; }

        internal int ScriptingBackend { get; }

        internal string CompanyName { get; }

        internal string ProductName { get; }

        internal string BundleVersion { get; }

        internal string ApplicationIdentifier { get; }
    }
}
