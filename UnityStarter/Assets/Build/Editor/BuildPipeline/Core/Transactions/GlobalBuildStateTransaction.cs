using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Owns the project-wide write-ahead journal for transient Unity project state.
    /// The lock intentionally spans both BuildGlobalStateScope and VersionInfoAssetScope.
    /// </summary>
    internal sealed class GlobalBuildStateTransaction
    {
        private const int FormatVersion = 1;
        private const string StateDirectoryRelativePath = ".buildpipeline/transactions/global-state";
        private const string PlayerSettingsRelativePath =
            "ProjectSettings/ProjectSettings.asset";
        private const string EditorBuildSettingsRelativePath =
            "ProjectSettings/EditorBuildSettings.asset";
        private const string JournalFileName = "active.json";
        private const string LockFileName = "build.lock";
        private const int BufferSize = 8192;
        private const int MaximumJournalBytes = 512 * 1024;
        private const int MaximumSnapshotBytes = 16 * 1024 * 1024;
        private const int MaximumPathCharacters = 2048;
        private const int MaximumTransactionDirectories = 4;
        private const int MaximumGeneratedAssetDirectories = 32;
        private const int MaximumFolderMetaBytes = 4096;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static GlobalBuildStateTransaction current;

        private readonly string projectRoot;
        private readonly string stateDirectory;
        private readonly string journalPath;
        private readonly string lockPath;
        private FileStream lockStream;
        private Journal journal;
        private Journal pendingRecoveryJournal;
        private bool activePlayerSettingsMerged;
        private bool pendingPlayerSettingsMerged;
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
                transaction.LoadPendingTransaction();
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
            pendingPlayerSettingsMerged = false;
            EnsureNoDetachedArtifacts();
        }

        internal void RestorePendingTransaction()
        {
            EnsureNotReleased();
            if (pendingRecoveryJournal == null)
            {
                return;
            }

            CleanupAtomicJournalScratch(pendingRecoveryJournal);
            RestoreJournalState(pendingRecoveryJournal);
        }

        internal void Begin(
            string playerSettingsRelativePath,
            int originalActiveBuildTarget,
            int requestedBuildTarget,
            PlayerSettingsOwnedState originalPlayerSettings)
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

            FileRecord editorBuildSettingsRecord = CaptureFileRecord(
                EditorBuildSettingsRelativePath,
                transactionDirectoryRelativePath + "/editor-build-settings.snapshot",
                requireExisting: true);
            if ((editorBuildSettingsRecord.attributes & (int)FileAttributes.ReadOnly) != 0)
            {
                throw new InvalidOperationException(
                    $"EditorBuildSettings must be writable for a transactional build: '{EditorBuildSettingsRelativePath}'.");
            }

            journal = new Journal
            {
                formatVersion = FormatVersion,
                transactionId = transactionId,
                projectRoot = NormalizeAbsolutePath(projectRoot),
                transactionDirectory = transactionDirectoryRelativePath,
                phase = GlobalPhasePreparing,
                sequence = 0,
                originalActiveBuildTarget = originalActiveBuildTarget,
                requestedBuildTarget = requestedBuildTarget,
                originalPlayerSettings = ToRecord(originalPlayerSettings),
                playerSettings = playerRecord,
                editorBuildSettings = editorBuildSettingsRecord
            };

            WriteJournal();
            Directory.CreateDirectory(transactionDirectory);
            EnsurePathHasNoReparsePoints(projectRoot, transactionDirectory, allowMissingLeaf: false);
            WriteSnapshot(playerRecord);
            WriteSnapshot(editorBuildSettingsRecord);
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
            FileIdentity editorBuildSettingsIdentity = CaptureIdentity(
                journal.editorBuildSettings.relativePath,
                requireExisting: true);
            return new PlayerSettingsPersistenceToken(
                identity.length,
                identity.sha256,
                editorBuildSettingsIdentity.length,
                editorBuildSettingsIdentity.sha256);
        }

        internal void MarkEditorBuildSettingsApplied()
        {
            RequirePhase(GlobalPhaseApplying);
            journal.transientEditorBuildSettings = CaptureIdentity(
                journal.editorBuildSettings.relativePath,
                requireExisting: true);
            WriteJournal();
        }

        internal void MarkGlobalMutationApplied(
            PlayerSettingsPersistenceToken expectedPersistence,
            PlayerSettingsOwnedState appliedPlayerSettings,
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

            FileIdentity persistedEditorBuildSettings = CaptureIdentity(
                journal.editorBuildSettings.relativePath,
                requireExisting: true);
            if (persistedEditorBuildSettings.length
                    != expectedPersistence.EditorBuildSettingsLength
                || !FixedTimeEquals(
                    persistedEditorBuildSettings.sha256,
                    expectedPersistence.EditorBuildSettingsSha256))
            {
                throw new IOException(
                    $"EditorBuildSettings changed after the targeted persistence barrier: '{journal.editorBuildSettings.relativePath}'. " +
                    "The candidate post-image was not adopted and the journal was retained.");
            }

            if (requireContentChange && MatchesRecordContent(journal.playerSettings, persistedIdentity))
            {
                throw new IOException(
                    $"PlayerSettings did not persist the requested build-state changes: '{journal.playerSettings.relativePath}'.");
            }

            journal.transientPlayerSettings = persistedIdentity;
            journal.transientEditorBuildSettings = persistedEditorBuildSettings;
            journal.appliedPlayerSettings = ToRecord(appliedPlayerSettings);
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

            FileIdentity currentEditorBuildSettings = CaptureIdentity(
                journal.editorBuildSettings.relativePath,
                requireExisting: true);
            FileIdentity expectedEditorBuildSettings =
                journal.transientEditorBuildSettings;
            if (expectedEditorBuildSettings == null
                || !SameContent(
                    currentEditorBuildSettings,
                    expectedEditorBuildSettings))
            {
                throw new IOException(
                    $"EditorBuildSettings changed before the pipeline persistence barrier: '{journal.editorBuildSettings.relativePath}'. " +
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

            FileIdentity currentEditorBuildSettings = CaptureIdentity(
                journal.editorBuildSettings.relativePath,
                requireExisting: true);
            if (journal.transientEditorBuildSettings == null
                || !SameContent(
                    currentEditorBuildSettings,
                    journal.transientEditorBuildSettings))
            {
                throw new IOException(
                    $"EditorBuildSettings no longer matches the build transaction's authorized content: '{journal.editorBuildSettings.relativePath}'. " +
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
            if (string.IsNullOrWhiteSpace(parentRelativePath)
                || string.Equals(parentRelativePath, "Assets", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "VersionInfoData must be stored in a child directory below Assets; " +
                    "the Assets root is not a valid generated-asset destination.");
            }

            string parentPath = ResolveProjectRelativePath(
                projectRoot,
                parentRelativePath,
                allowMissingLeaf: true);
            GeneratedAssetDirectoryRecord[] generatedDirectories =
                CaptureGeneratedAssetDirectories(parentRelativePath, parentPath);

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
                generatedDirectories = generatedDirectories
            };
            journal.hasVersionInfo = true;
            WriteJournal();
            WriteSnapshot(assetRecord);
            WriteSnapshot(metaRecord);
            CreateGeneratedAssetDirectories(journal.versionInfo);
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

        internal void RestoreGlobalSettingsFiles()
        {
            EnsureActiveJournal();
            RestoreOwnedEditorBuildSettingsFile(journal);
            RestoreOwnedPlayerSettings(journal);
        }

        internal void RestorePendingEditorUserState()
        {
            EnsureNotReleased();
            if (pendingRecoveryJournal == null)
            {
                return;
            }

            RestoreOwnedEditorBuildSettings(
                pendingRecoveryJournal.originalPlayerSettings);
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
            activePlayerSettingsMerged = false;
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

        private void LoadPendingTransaction()
        {
            ValidateStateDirectoryInventoryBeforeLoad();
            if (!File.Exists(journalPath))
            {
                EnsureNoDetachedArtifacts();
                return;
            }

            Journal loaded = ReadJournal(journalPath);
            ValidateJournal(loaded);
            pendingRecoveryJournal = loaded;
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
                VerifyIdentity(interrupted.editorBuildSettings, CaptureIdentity(
                    interrupted.editorBuildSettings.relativePath,
                    requireExisting: true), "EditorBuildSettings");
                return;
            }

            RestoreOwnedEditorBuildSettingsFile(interrupted);
            RestoreOwnedPlayerSettings(interrupted);
        }

        private void RestoreOwnedEditorBuildSettingsFile(Journal owner)
        {
            FileIdentity currentIdentity = CaptureIdentity(
                owner.editorBuildSettings.relativePath,
                requireExisting: true);
            if (MatchesRecordContent(owner.editorBuildSettings, currentIdentity))
            {
                RestoreOriginalFile(
                    owner.editorBuildSettings,
                    allowOwnedTransient:
                        owner.transientEditorBuildSettings != null,
                    owner.transientEditorBuildSettings);
                return;
            }

            if (owner.transientEditorBuildSettings != null
                && SameContent(
                    currentIdentity,
                    owner.transientEditorBuildSettings))
            {
                RestoreOriginalFile(
                    owner.editorBuildSettings,
                    allowOwnedTransient: true,
                    owner.transientEditorBuildSettings);
                return;
            }

            throw new IOException(
                $"EditorBuildSettings changed outside the transaction: '{owner.editorBuildSettings.relativePath}'. " +
                "The snapshot and journal were retained; no unknown content was overwritten.");
        }

        private void RestoreOwnedPlayerSettings(Journal owner)
        {
            FileIdentity currentIdentity = CaptureIdentity(
                owner.playerSettings.relativePath,
                requireExisting: true);
            if (MatchesRecordContent(owner.playerSettings, currentIdentity))
            {
                RestoreOriginalFile(
                    owner.playerSettings,
                    allowOwnedTransient: owner.transientPlayerSettings != null,
                    owner.transientPlayerSettings);
                return;
            }

            if (owner.transientPlayerSettings != null
                && SameContent(currentIdentity, owner.transientPlayerSettings))
            {
                RestoreOriginalFile(
                    owner.playerSettings,
                    allowOwnedTransient: true,
                    owner.transientPlayerSettings);
                return;
            }

            if (owner.appliedPlayerSettings == null)
            {
                throw new IOException(
                    $"PlayerSettings changed before an applied owned-state image was journaled: '{owner.playerSettings.relativePath}'. "
                    + "The snapshot and journal were retained; no unknown content was overwritten.");
            }

            MergeOwnedPlayerSettings(owner);
            if (ReferenceEquals(owner, journal))
            {
                activePlayerSettingsMerged = true;
            }

            if (ReferenceEquals(owner, pendingRecoveryJournal))
            {
                pendingPlayerSettingsMerged = true;
            }
        }

        private void MergeOwnedPlayerSettings(Journal owner)
        {
            PlayerSettings settings = GetPlayerSettingsAssetForMerge();
            if (EditorUtility.IsDirty(settings))
            {
                throw new IOException(
                    "PlayerSettings has unsaved in-memory changes. Property-level recovery refused to overwrite or adopt them.");
            }

            AssetDatabase.Refresh(
                ImportAssetOptions.ForceUpdate
                | ImportAssetOptions.ForceSynchronousImport);
            settings = GetPlayerSettingsAssetForMerge();
            if (EditorUtility.IsDirty(settings))
            {
                throw new IOException(
                    "PlayerSettings became dirty while preparing property-level recovery.");
            }

            OwnedPlayerSettingsRecord current = CaptureOwnedPlayerSettings(
                settings,
                owner.requestedBuildTarget);
            EnsureThreeWayMergeIsSafe(
                current,
                owner.originalPlayerSettings,
                owner.appliedPlayerSettings);
            ApplyOwnedPlayerSettings(
                settings,
                owner.requestedBuildTarget,
                owner.originalPlayerSettings);
            BuildPipelineAssetSaveFilter.SaveOnlyPlayerSettings(settings);
            if (EditorUtility.IsDirty(settings))
            {
                throw new IOException(
                    "PlayerSettings remained dirty after property-level recovery persistence.");
            }

            OwnedPlayerSettingsRecord restored = CaptureOwnedPlayerSettings(
                settings,
                owner.requestedBuildTarget);
            EnsureOwnedPlayerSettingsEqual(
                restored,
                owner.originalPlayerSettings,
                "Property-level PlayerSettings recovery verification");
        }

        private static PlayerSettings GetPlayerSettingsAssetForMerge()
        {
            PlayerSettings[] assets = Resources.FindObjectsOfTypeAll<PlayerSettings>();
            if (assets.Length != 1 || assets[0] == null)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one loaded PlayerSettings asset, but found {assets.Length}.");
            }

            string path = AssetDatabase.GetAssetPath(assets[0]);
            if (!string.Equals(
                    path,
                    BuildPipelineAssetSaveFilter.PlayerSettingsAssetPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected PlayerSettings asset path: '{path}'.");
            }

            return assets[0];
        }

        private static OwnedPlayerSettingsRecord CaptureOwnedPlayerSettings(
            PlayerSettings settings,
            int requestedBuildTarget)
        {
            BuildTarget target = (BuildTarget)requestedBuildTarget;
            NamedBuildTarget namedTarget = BuildRequestFactory.GetNamedBuildTarget(target);
            PlayerSettingsSplashState splash = PlayerSettingsLicensePolicy.Capture(settings);
            return new OwnedPlayerSettingsRecord
            {
                scriptingBackend = (int)PlayerSettings.GetScriptingBackend(namedTarget),
                companyName = PlayerSettings.companyName ?? string.Empty,
                productName = PlayerSettings.productName ?? string.Empty,
                bundleVersion = PlayerSettings.bundleVersion ?? string.Empty,
                applicationIdentifier = PlayerSettings.GetApplicationIdentifier(namedTarget) ?? string.Empty,
                androidBundleVersionCode = PlayerSettings.Android.bundleVersionCode,
                iosBuildNumber = PlayerSettings.iOS.buildNumber ?? string.Empty,
                exportAndroidProject = EditorUserBuildSettings.exportAsGoogleAndroidProject,
                developmentBuild = EditorUserBuildSettings.development,
                editorBuildScenes = CaptureEditorBuildScenes(),
                showSplashScreen = splash.ShowSplashScreen,
                showUnityLogo = splash.ShowUnityLogo,
                preloadedAssetIds = PlayerSettingsPreloadedAssetPolicy.Capture()
            };
        }

        private static void ApplyOwnedPlayerSettings(
            PlayerSettings settings,
            int requestedBuildTarget,
            OwnedPlayerSettingsRecord value)
        {
            BuildTarget target = (BuildTarget)requestedBuildTarget;
            NamedBuildTarget namedTarget = BuildRequestFactory.GetNamedBuildTarget(target);
            PlayerSettings.SetScriptingBackend(
                namedTarget,
                (ScriptingImplementation)value.scriptingBackend);
            PlayerSettings.companyName = value.companyName;
            PlayerSettings.productName = value.productName;
            PlayerSettings.bundleVersion = value.bundleVersion;
            PlayerSettings.SetApplicationIdentifier(
                namedTarget,
                value.applicationIdentifier);
            PlayerSettings.Android.bundleVersionCode = value.androidBundleVersionCode;
            PlayerSettings.iOS.buildNumber = value.iosBuildNumber;
            PlayerSettingsLicensePolicy.ApplyExact(
                settings,
                new PlayerSettingsSplashState(
                    value.showSplashScreen,
                    value.showUnityLogo));
            PlayerSettingsPreloadedAssetPolicy.ApplyExact(value.preloadedAssetIds);
        }

        private static void RestoreOwnedEditorBuildSettings(
            OwnedPlayerSettingsRecord value)
        {
            if (value == null)
            {
                throw new InvalidOperationException(
                    "Original owned PlayerSettings state is unavailable.");
            }

            EditorUserBuildSettings.exportAsGoogleAndroidProject =
                value.exportAndroidProject;
            EditorUserBuildSettings.development = value.developmentBuild;
            EditorBuildSettings.scenes = CreateEditorBuildSettingsScenes(
                value.editorBuildScenes);
        }

        private static void EnsureThreeWayMergeIsSafe(
            OwnedPlayerSettingsRecord current,
            OwnedPlayerSettingsRecord original,
            OwnedPlayerSettingsRecord applied)
        {
            var conflicts = new List<string>();
            AddConflictIfUnknown(
                current.scriptingBackend,
                original.scriptingBackend,
                applied.scriptingBackend,
                "scripting backend",
                conflicts);
            AddConflictIfUnknown(current.companyName, original.companyName, applied.companyName, "company name", conflicts);
            AddConflictIfUnknown(current.productName, original.productName, applied.productName, "product name", conflicts);
            AddConflictIfUnknown(current.bundleVersion, original.bundleVersion, applied.bundleVersion, "bundle version", conflicts);
            AddConflictIfUnknown(current.applicationIdentifier, original.applicationIdentifier, applied.applicationIdentifier, "application identifier", conflicts);
            AddConflictIfUnknown(current.androidBundleVersionCode, original.androidBundleVersionCode, applied.androidBundleVersionCode, "Android bundle version code", conflicts);
            AddConflictIfUnknown(current.iosBuildNumber, original.iosBuildNumber, applied.iosBuildNumber, "iOS build number", conflicts);
            AddConflictIfUnknown(current.exportAndroidProject, original.exportAndroidProject, applied.exportAndroidProject, "Android export setting", conflicts);
            AddConflictIfUnknown(current.developmentBuild, original.developmentBuild, applied.developmentBuild, "Development build setting", conflicts);
            AddEditorSceneConflictIfUnknown(
                current.editorBuildScenes,
                original.editorBuildScenes,
                applied.editorBuildScenes,
                "Editor build scene sequence",
                conflicts);
            AddConflictIfUnknown(current.showSplashScreen, original.showSplashScreen, applied.showSplashScreen, "splash screen visibility", conflicts);
            AddConflictIfUnknown(current.showUnityLogo, original.showUnityLogo, applied.showUnityLogo, "Unity splash logo visibility", conflicts);
            AddSequenceConflictIfUnknown(
                current.preloadedAssetIds,
                original.preloadedAssetIds,
                applied.preloadedAssetIds,
                "preloaded asset sequence",
                conflicts);
            if (conflicts.Count > 0)
            {
                throw new IOException(
                    "PlayerSettings property-level recovery found externally changed owned fields: "
                    + string.Join(", ", conflicts)
                    + ". The journal and snapshot were retained; no unknown content was overwritten.");
            }
        }

        private static void EnsureOwnedPlayerSettingsEqual(
            OwnedPlayerSettingsRecord current,
            OwnedPlayerSettingsRecord expected,
            string operation)
        {
            var conflicts = new List<string>();
            AddConflictIfUnknown(current.scriptingBackend, expected.scriptingBackend, expected.scriptingBackend, "scripting backend", conflicts);
            AddConflictIfUnknown(current.companyName, expected.companyName, expected.companyName, "company name", conflicts);
            AddConflictIfUnknown(current.productName, expected.productName, expected.productName, "product name", conflicts);
            AddConflictIfUnknown(current.bundleVersion, expected.bundleVersion, expected.bundleVersion, "bundle version", conflicts);
            AddConflictIfUnknown(current.applicationIdentifier, expected.applicationIdentifier, expected.applicationIdentifier, "application identifier", conflicts);
            AddConflictIfUnknown(current.androidBundleVersionCode, expected.androidBundleVersionCode, expected.androidBundleVersionCode, "Android bundle version code", conflicts);
            AddConflictIfUnknown(current.iosBuildNumber, expected.iosBuildNumber, expected.iosBuildNumber, "iOS build number", conflicts);
            AddConflictIfUnknown(current.exportAndroidProject, expected.exportAndroidProject, expected.exportAndroidProject, "Android export setting", conflicts);
            AddConflictIfUnknown(current.developmentBuild, expected.developmentBuild, expected.developmentBuild, "Development build setting", conflicts);
            AddEditorSceneConflictIfUnknown(
                current.editorBuildScenes,
                expected.editorBuildScenes,
                expected.editorBuildScenes,
                "Editor build scene sequence",
                conflicts);
            AddConflictIfUnknown(current.showSplashScreen, expected.showSplashScreen, expected.showSplashScreen, "splash screen visibility", conflicts);
            AddConflictIfUnknown(current.showUnityLogo, expected.showUnityLogo, expected.showUnityLogo, "Unity splash logo visibility", conflicts);
            AddSequenceConflictIfUnknown(
                current.preloadedAssetIds,
                expected.preloadedAssetIds,
                expected.preloadedAssetIds,
                "preloaded asset sequence",
                conflicts);
            if (conflicts.Count > 0)
            {
                throw new IOException(
                    operation + " failed for: " + string.Join(", ", conflicts) + ".");
            }
        }

        private static void AddConflictIfUnknown<T>(
            T current,
            T original,
            T applied,
            string label,
            ICollection<string> conflicts)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            if (!comparer.Equals(current, original)
                && !comparer.Equals(current, applied))
            {
                conflicts.Add(label);
            }
        }

        private static void AddSequenceConflictIfUnknown(
            string[] current,
            string[] original,
            string[] applied,
            string label,
            ICollection<string> conflicts)
        {
            if (!PlayerSettingsPreloadedAssetPolicy.SequenceEqual(current, original)
                && !PlayerSettingsPreloadedAssetPolicy.SequenceEqual(current, applied))
            {
                conflicts.Add(label);
            }
        }

        private static void AddEditorSceneConflictIfUnknown(
            EditorBuildSceneRecord[] current,
            EditorBuildSceneRecord[] original,
            EditorBuildSceneRecord[] applied,
            string label,
            ICollection<string> conflicts)
        {
            if (!EditorBuildScenesEqual(current, original)
                && !EditorBuildScenesEqual(current, applied))
            {
                conflicts.Add(label);
            }
        }

        private static bool EditorBuildScenesEqual(
            EditorBuildSceneRecord[] left,
            EditorBuildSceneRecord[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                EditorBuildSceneRecord leftEntry = left[index];
                EditorBuildSceneRecord rightEntry = right[index];
                if (leftEntry == null
                    || rightEntry == null
                    || leftEntry.enabled != rightEntry.enabled
                    || !string.Equals(
                        leftEntry.path,
                        rightEntry.path,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static EditorBuildSceneRecord[] CaptureEditorBuildScenes()
        {
            EditorBuildSettingsScene[] scenes =
                EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            var records = new EditorBuildSceneRecord[scenes.Length];
            for (int index = 0; index < scenes.Length; index++)
            {
                EditorBuildSettingsScene scene = scenes[index];
                records[index] = new EditorBuildSceneRecord
                {
                    path = scene?.path ?? string.Empty,
                    enabled = scene != null && scene.enabled
                };
            }

            return records;
        }

        private static EditorBuildSettingsScene[] CreateEditorBuildSettingsScenes(
            EditorBuildSceneRecord[] records)
        {
            if (records == null)
            {
                throw new InvalidOperationException(
                    "Editor build scene state is unavailable.");
            }

            var scenes = new EditorBuildSettingsScene[records.Length];
            for (int index = 0; index < records.Length; index++)
            {
                EditorBuildSceneRecord record = records[index]
                    ?? throw new InvalidOperationException(
                        "Editor build scene state contains a null entry.");
                scenes[index] = new EditorBuildSettingsScene(
                    record.path,
                    record.enabled);
            }

            return scenes;
        }

        private GeneratedAssetDirectoryRecord[] CaptureGeneratedAssetDirectories(
            string parentRelativePath,
            string parentPath)
        {
            string assetsPath = ResolveProjectRelativePath(
                projectRoot,
                "Assets",
                allowMissingLeaf: false);
            if (!Directory.Exists(assetsPath))
            {
                throw new DirectoryNotFoundException(
                    $"Unity Assets directory does not exist: '{assetsPath}'.");
            }

            EnsurePathHasNoReparsePoints(projectRoot, assetsPath, allowMissingLeaf: false);
            string relativeTail = parentRelativePath.Substring("Assets/".Length);
            string[] segments = relativeTail.Split('/');
            if (segments.Length == 0 || segments.Length > MaximumGeneratedAssetDirectories)
            {
                throw new InvalidOperationException(
                    $"VersionInfoData destination exceeds the {MaximumGeneratedAssetDirectories}-directory budget: '{parentRelativePath}'.");
            }

            var generated = new List<GeneratedAssetDirectoryRecord>(segments.Length);
            string currentRelativePath = "Assets";
            string currentPath = assetsPath;
            bool foundMissingDirectory = false;
            foreach (string segment in segments)
            {
                currentRelativePath += "/" + segment;
                currentPath = Path.Combine(currentPath, segment);
                string metaPath = currentPath + ".meta";

                if (Directory.Exists(currentPath))
                {
                    if (foundMissingDirectory)
                    {
                        throw new IOException(
                            $"VersionInfoData destination contains a directory below an absent parent: '{currentRelativePath}'.");
                    }

                    EnsurePathHasNoReparsePoints(projectRoot, currentPath, allowMissingLeaf: false);
                    EnsureExistingUnityFolderMeta(currentRelativePath, metaPath);
                    continue;
                }

                if (File.Exists(currentPath))
                {
                    throw new IOException(
                        $"VersionInfoData destination directory resolves to a file: '{currentRelativePath}'.");
                }

                if (File.Exists(metaPath) || Directory.Exists(metaPath))
                {
                    throw new IOException(
                        $"VersionInfoData destination has an orphan folder meta path: '{currentRelativePath}.meta'.");
                }

                foundMissingDirectory = true;
                string guid = Guid.NewGuid().ToString("N");
                byte[] metaBytes = CreateFolderMetaBytes(guid);
                generated.Add(new GeneratedAssetDirectoryRecord
                {
                    relativePath = currentRelativePath,
                    guid = guid,
                    metaBase64 = Convert.ToBase64String(metaBytes),
                    metaSha256 = ComputeSha256(metaBytes)
                });
            }

            if (!PathEquals(currentPath, parentPath))
            {
                throw new IOException(
                    $"VersionInfoData destination path changed during directory planning: '{parentRelativePath}'.");
            }

            return generated.ToArray();
        }

        private void CreateGeneratedAssetDirectories(VersionInfoRecord version)
        {
            foreach (GeneratedAssetDirectoryRecord directory in version.generatedDirectories)
            {
                string absolutePath = ResolveProjectRelativePath(
                    projectRoot,
                    directory.relativePath,
                    allowMissingLeaf: true);
                string metaPath = absolutePath + ".meta";
                string parentPath = Path.GetDirectoryName(absolutePath);
                if (string.IsNullOrEmpty(parentPath) || !Directory.Exists(parentPath))
                {
                    throw new DirectoryNotFoundException(
                        $"Transaction-created Unity folder parent is missing: '{directory.relativePath}'.");
                }

                EnsurePathHasNoReparsePoints(projectRoot, parentPath, allowMissingLeaf: false);
                if (File.Exists(absolutePath)
                    || Directory.Exists(absolutePath)
                    || File.Exists(metaPath)
                    || Directory.Exists(metaPath))
                {
                    throw new IOException(
                        $"VersionInfoData destination changed after its transaction plan was persisted: '{directory.relativePath}'.");
                }

                byte[] metaBytes = DecodeFolderMetaBytes(directory);
                WriteDurably(metaPath, metaBytes, createNew: true);
                Directory.CreateDirectory(absolutePath);
                EnsurePathHasNoReparsePoints(projectRoot, absolutePath, allowMissingLeaf: false);
                VerifyGeneratedAssetDirectoryMeta(directory);
            }
        }

        private void DeleteGeneratedAssetDirectories(VersionInfoRecord version)
        {
            GeneratedAssetDirectoryRecord[] directories = version.generatedDirectories
                ?? Array.Empty<GeneratedAssetDirectoryRecord>();
            for (int index = directories.Length - 1; index >= 0; index--)
            {
                GeneratedAssetDirectoryRecord directory = directories[index];
                string absolutePath = ResolveProjectRelativePath(
                    projectRoot,
                    directory.relativePath,
                    allowMissingLeaf: true);
                string metaPath = absolutePath + ".meta";

                if (File.Exists(absolutePath))
                {
                    throw new IOException(
                        $"Transaction-created Unity folder was replaced by a file: '{directory.relativePath}'.");
                }

                if (Directory.Exists(absolutePath))
                {
                    EnsurePathHasNoReparsePoints(projectRoot, absolutePath, allowMissingLeaf: false);
                    VerifyGeneratedAssetDirectoryMeta(directory);
                    using (IEnumerator<string> entries = Directory
                               .EnumerateFileSystemEntries(absolutePath)
                               .GetEnumerator())
                    {
                        if (entries.MoveNext())
                        {
                            throw new IOException(
                                $"Transaction-created Unity folder contains an unknown entry and will not be deleted: '{entries.Current}'.");
                        }
                    }

                    Directory.Delete(absolutePath, recursive: false);
                    if (Directory.Exists(absolutePath))
                    {
                        throw new IOException(
                            $"Transaction-created Unity folder still exists after deletion: '{directory.relativePath}'.");
                    }
                }

                if (Directory.Exists(metaPath))
                {
                    throw new IOException(
                        $"Transaction-created Unity folder meta was replaced by a directory: '{directory.relativePath}.meta'.");
                }

                if (File.Exists(metaPath))
                {
                    VerifyGeneratedAssetDirectoryMeta(directory);
                    DeleteFileExactly(metaPath);
                }
            }
        }

        private void VerifyGeneratedAssetDirectoriesAbsent(VersionInfoRecord version)
        {
            GeneratedAssetDirectoryRecord[] directories = version.generatedDirectories
                ?? Array.Empty<GeneratedAssetDirectoryRecord>();
            foreach (GeneratedAssetDirectoryRecord directory in directories)
            {
                string absolutePath = ResolveProjectRelativePath(
                    projectRoot,
                    directory.relativePath,
                    allowMissingLeaf: true);
                if (File.Exists(absolutePath)
                    || Directory.Exists(absolutePath)
                    || File.Exists(absolutePath + ".meta")
                    || Directory.Exists(absolutePath + ".meta"))
                {
                    throw new IOException(
                        $"Transaction-created Unity folder was not restored to absence: '{directory.relativePath}'.");
                }
            }
        }

        private void EnsureExistingUnityFolderMeta(string relativePath, string metaPath)
        {
            if (Directory.Exists(metaPath) || !File.Exists(metaPath))
            {
                throw new IOException(
                    $"Existing VersionInfoData destination folder is missing its Unity meta file: '{relativePath}.meta'.");
            }

            FileAttributes attributes = File.GetAttributes(metaPath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new IOException(
                    $"Existing VersionInfoData destination folder meta is not a regular file: '{relativePath}.meta'.");
            }
        }

        private void VerifyGeneratedAssetDirectoryMeta(GeneratedAssetDirectoryRecord directory)
        {
            string metaRelativePath = directory.relativePath + ".meta";
            string metaPath = ResolveProjectRelativePath(
                projectRoot,
                metaRelativePath,
                allowMissingLeaf: false);
            if (!File.Exists(metaPath))
            {
                throw new IOException(
                    $"Transaction-created Unity folder meta is missing: '{metaRelativePath}'.");
            }

            FileAttributes attributes = File.GetAttributes(metaPath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new IOException(
                    $"Transaction-created Unity folder meta is not a regular file: '{metaRelativePath}'.");
            }

            byte[] expected = DecodeFolderMetaBytes(directory);
            byte[] actual = ReadBoundedFile(
                metaPath,
                MaximumFolderMetaBytes,
                "transaction-created Unity folder meta");
            if (!FixedTimeEquals(ComputeSha256(actual), directory.metaSha256)
                || !ByteArraysEqual(actual, expected))
            {
                throw new IOException(
                    $"Transaction-created Unity folder meta changed and will not be deleted: '{metaRelativePath}'.");
            }
        }

        private static byte[] DecodeFolderMetaBytes(GeneratedAssetDirectoryRecord directory)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(directory.metaBase64);
                if (bytes.Length <= 0 || bytes.Length > MaximumFolderMetaBytes)
                {
                    throw new IOException(
                        "Transaction-created Unity folder meta exceeds its byte budget.");
                }

                return bytes;
            }
            catch (FormatException exception)
            {
                throw new IOException(
                    "Transaction-created Unity folder meta payload is malformed.",
                    exception);
            }
        }

        private static byte[] CreateFolderMetaBytes(string guid)
        {
            string yaml =
                "fileFormatVersion: 2\n" +
                "guid: " + guid + "\n" +
                "folderAsset: yes\n" +
                "DefaultImporter:\n" +
                "  externalObjects: {}\n" +
                "  userData: \n" +
                "  assetBundleName: \n" +
                "  assetBundleVariant: \n";
            return StrictUtf8.GetBytes(yaml);
        }

        private void RestoreVersionInfo(Journal owner)
        {
            VersionInfoRecord version = owner.versionInfo;
            if (string.Equals(version.state, VersionStatePreparing, StringComparison.Ordinal))
            {
                VerifyOriginalFileOrAbsence(version.asset);
                VerifyOriginalFileOrAbsence(version.meta);
                DeleteTransactionStage(version.stageAssetPath, version.stageAsset, "VersionInfoData staging asset");
                DeleteTransactionStage(version.stageMetaPath, version.stageMeta, "VersionInfoData staging meta file");
                DeleteGeneratedAssetDirectories(version);
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
            DeleteGeneratedAssetDirectories(version);
            VerifyVersionOriginalState(version);
        }

        private void ValidateVersionFilesystemForRecovery(VersionInfoRecord version)
        {
            if (string.Equals(version.state, VersionStatePreparing, StringComparison.Ordinal)
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
            if (string.Equals(
                    original.relativePath,
                    PlayerSettingsRelativePath,
                    StringComparison.Ordinal))
            {
                beforePlayerSettingsRestoreReplaceForTests?.Invoke();
            }
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
            bool merged = (ReferenceEquals(state, journal) && activePlayerSettingsMerged)
                || (ReferenceEquals(state, pendingRecoveryJournal) && pendingPlayerSettingsMerged);
            if (merged)
            {
                EnsureOwnedPlayerSettingsEqual(
                    CaptureOwnedPlayerSettings(
                        GetPlayerSettingsAssetForMerge(),
                        state.requestedBuildTarget),
                    state.originalPlayerSettings,
                    "Merged PlayerSettings restoration verification");
            }
            else
            {
                VerifyOriginalFileOrAbsence(state.playerSettings);
            }

            VerifyOriginalFileOrAbsence(state.editorBuildSettings);

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
            VerifyGeneratedAssetDirectoriesAbsent(version);
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
                AddExpectedSnapshot(
                    completed.editorBuildSettings,
                    expectedSnapshots);
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
                formatVersion = FormatVersion,
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
                || envelope.formatVersion != FormatVersion
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
            if (candidate.formatVersion != FormatVersion
                || !IsGuidN(candidate.transactionId)
                || candidate.sequence <= 0
                || !IsKnownGlobalPhase(candidate.phase))
            {
                throw new IOException("Global-state journal header is invalid.");
            }

            ValidateOwnedPlayerSettingsRecord(
                candidate.originalPlayerSettings,
                "original PlayerSettings");
            bool requiresAppliedState = string.Equals(
                    candidate.phase,
                    GlobalPhaseActive,
                    StringComparison.Ordinal)
                || string.Equals(
                    candidate.phase,
                    GlobalPhaseRestored,
                    StringComparison.Ordinal);
            if (requiresAppliedState)
            {
                ValidateOwnedPlayerSettingsRecord(
                    candidate.appliedPlayerSettings,
                    "applied PlayerSettings");
            }

            if (!BuildCommandLine.IsSupportedBuildTarget(
                    (BuildTarget)candidate.originalActiveBuildTarget)
                || !BuildCommandLine.IsSupportedBuildTarget(
                    (BuildTarget)candidate.requestedBuildTarget))
            {
                throw new IOException(
                    "Global-state journal contains an unsupported build target.");
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

            ValidateFileRecord(
                candidate.editorBuildSettings,
                requireExistingRecord: true,
                candidate.transactionDirectory);
            if (!string.Equals(
                    candidate.editorBuildSettings.relativePath,
                    EditorBuildSettingsRelativePath,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "Global-state journal references an unexpected EditorBuildSettings path.");
            }

            candidate.transientPlayerSettings = NormalizeOptionalIdentity(candidate.transientPlayerSettings);
            if (candidate.transientPlayerSettings != null)
            {
                ValidateIdentity(candidate.transientPlayerSettings, candidate.playerSettings.relativePath);
            }

            candidate.transientEditorBuildSettings = NormalizeOptionalIdentity(
                candidate.transientEditorBuildSettings);
            if (candidate.transientEditorBuildSettings != null)
            {
                ValidateIdentity(
                    candidate.transientEditorBuildSettings,
                    candidate.editorBuildSettings.relativePath);
            }

            bool phaseRequiresTransient = string.Equals(candidate.phase, GlobalPhaseActive, StringComparison.Ordinal)
                || string.Equals(candidate.phase, GlobalPhaseRestored, StringComparison.Ordinal);
            if (phaseRequiresTransient != (candidate.transientPlayerSettings != null))
            {
                throw new IOException(
                    "Global-state journal phase and transient PlayerSettings identity are inconsistent.");
            }

            bool phaseForbidsEditorTransient = string.Equals(
                    candidate.phase,
                    GlobalPhasePreparing,
                    StringComparison.Ordinal)
                || string.Equals(
                    candidate.phase,
                    GlobalPhasePrepared,
                    StringComparison.Ordinal);
            if ((phaseRequiresTransient
                    && candidate.transientEditorBuildSettings == null)
                || (phaseForbidsEditorTransient
                    && candidate.transientEditorBuildSettings != null))
            {
                throw new IOException(
                    "Global-state journal phase and transient EditorBuildSettings identity are inconsistent.");
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

            ValidateGeneratedAssetDirectoryRecords(version, parent);
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

            if (string.Equals(
                    relativePath,
                    EditorBuildSettingsRelativePath,
                    StringComparison.Ordinal))
            {
                return transactionDirectory +
                    "/editor-build-settings.snapshot";
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

        private void ValidateGeneratedAssetDirectoryRecords(
            VersionInfoRecord version,
            string targetParentRelativePath)
        {
            GeneratedAssetDirectoryRecord[] directories = version.generatedDirectories;
            if (directories == null || directories.Length > MaximumGeneratedAssetDirectories)
            {
                throw new IOException(
                    "Global-state journal contains an invalid generated Unity folder inventory.");
            }

            string previousPath = null;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (GeneratedAssetDirectoryRecord directory in directories)
            {
                if (directory == null
                    || string.IsNullOrWhiteSpace(directory.relativePath)
                    || !directory.relativePath.StartsWith("Assets/", StringComparison.Ordinal)
                    || !IsGuidN(directory.guid)
                    || !IsSha256(directory.metaSha256)
                    || !paths.Add(directory.relativePath))
                {
                    throw new IOException(
                        "Global-state journal contains an invalid generated Unity folder record.");
                }

                string normalized = NormalizeAndValidateProjectRelativePath(
                    projectRoot,
                    directory.relativePath,
                    "generated Unity folder path");
                if (!string.Equals(normalized, directory.relativePath, StringComparison.Ordinal))
                {
                    throw new IOException(
                        "Global-state journal contains a non-canonical generated Unity folder path.");
                }

                if (previousPath != null)
                {
                    string expectedParent = Path.GetDirectoryName(directory.relativePath)
                        ?.Replace('\\', '/');
                    if (!string.Equals(expectedParent, previousPath, StringComparison.Ordinal))
                    {
                        throw new IOException(
                            "Global-state journal generated Unity folders do not form one contiguous parent chain.");
                    }
                }

                byte[] recordedBytes = DecodeFolderMetaBytes(directory);
                byte[] expectedBytes = CreateFolderMetaBytes(directory.guid);
                if (!ByteArraysEqual(recordedBytes, expectedBytes)
                    || !FixedTimeEquals(ComputeSha256(recordedBytes), directory.metaSha256))
                {
                    throw new IOException(
                        "Global-state journal generated Unity folder meta identity is invalid.");
                }

                ResolveProjectRelativePath(
                    projectRoot,
                    directory.relativePath,
                    allowMissingLeaf: true);
                previousPath = directory.relativePath;
            }

            if (directories.Length > 0
                && !string.Equals(previousPath, targetParentRelativePath, StringComparison.Ordinal))
            {
                throw new IOException(
                    "Global-state journal generated Unity folder chain does not end at the VersionInfoData parent.");
            }
        }

        private static void ValidateOwnedPlayerSettingsRecord(
            OwnedPlayerSettingsRecord record,
            string label)
        {
            if (record == null)
            {
                throw new IOException(
                    $"Global-state journal {label} is missing.");
            }

            var backend = (ScriptingImplementation)record.scriptingBackend;
            if (backend != ScriptingImplementation.Mono2x
                && backend != ScriptingImplementation.IL2CPP)
            {
                throw new IOException(
                    $"Global-state journal {label} contains an unsupported scripting backend.");
            }

            if (record.androidBundleVersionCode <= 0)
            {
                throw new IOException(
                    $"Global-state journal {label} contains an invalid Android bundle version code.");
            }

            ValidateBoundedJournalString(record.companyName, label + " company name");
            ValidateBoundedJournalString(record.productName, label + " product name");
            ValidateBoundedJournalString(record.bundleVersion, label + " bundle version");
            ValidateBoundedJournalString(record.applicationIdentifier, label + " application identifier");
            ValidateBoundedJournalString(record.iosBuildNumber, label + " iOS build number");
            if (record.editorBuildScenes == null
                || record.editorBuildScenes.Length > 1024)
            {
                throw new IOException(
                    $"Global-state journal {label} contains an invalid Editor build scene sequence.");
            }

            for (int index = 0; index < record.editorBuildScenes.Length; index++)
            {
                EditorBuildSceneRecord scene = record.editorBuildScenes[index];
                if (scene == null)
                {
                    throw new IOException(
                        $"Global-state journal {label} contains a null Editor build scene entry.");
                }

                ValidateBoundedJournalString(
                    scene.path,
                    label + " Editor build scene path");
            }
            try
            {
                PlayerSettingsPreloadedAssetPolicy.ValidateIdentifiers(
                    record.preloadedAssetIds,
                    label + " preloaded assets");
            }
            catch (InvalidOperationException exception)
            {
                throw new IOException(exception.Message, exception);
            }
        }

        private static OwnedPlayerSettingsRecord ToRecord(
            PlayerSettingsOwnedState state)
        {
            return new OwnedPlayerSettingsRecord
            {
                scriptingBackend = state.ScriptingBackend,
                companyName = state.CompanyName,
                productName = state.ProductName,
                bundleVersion = state.BundleVersion,
                applicationIdentifier = state.ApplicationIdentifier,
                androidBundleVersionCode = state.AndroidBundleVersionCode,
                iosBuildNumber = state.IosBuildNumber,
                exportAndroidProject = state.ExportAndroidProject,
                developmentBuild = state.DevelopmentBuild,
                editorBuildScenes = ToRecords(state.EditorBuildScenes),
                showSplashScreen = state.Splash.ShowSplashScreen,
                showUnityLogo = state.Splash.ShowUnityLogo,
                preloadedAssetIds = (string[])state.PreloadedAssetIds.Clone()
            };
        }

        private static EditorBuildSceneRecord[] ToRecords(
            IReadOnlyList<EditorBuildSceneState> states)
        {
            var records = new EditorBuildSceneRecord[states.Count];
            for (int index = 0; index < states.Count; index++)
            {
                records[index] = new EditorBuildSceneRecord
                {
                    path = states[index].Path,
                    enabled = states[index].Enabled
                };
            }

            return records;
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

        private static bool ByteArraysEqual(byte[] first, byte[] second)
        {
            if (first == null || second == null || first.Length != second.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < first.Length; index++)
            {
                difference |= first[index] ^ second[index];
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
        private const string VersionStatePrepared = "Prepared";
        private const string VersionStateStageReady = "StageReady";
        private const string VersionStateInstalling = "Installing";
        private const string VersionStateInstalled = "Installed";
        private const string VersionStateRestored = "Restored";

        [Serializable]
        private sealed class JournalEnvelope
        {
            public int formatVersion;
            public string payloadBase64;
            public string sha256;
        }

        [Serializable]
        private sealed class Journal
        {
            public int formatVersion;
            public string transactionId;
            public string projectRoot;
            public string transactionDirectory;
            public string phase;
            public long sequence;
            public int originalActiveBuildTarget;
            public int requestedBuildTarget;
            public OwnedPlayerSettingsRecord originalPlayerSettings;
            public OwnedPlayerSettingsRecord appliedPlayerSettings;
            public FileRecord playerSettings;
            public FileIdentity transientPlayerSettings;
            public FileRecord editorBuildSettings;
            public FileIdentity transientEditorBuildSettings;
            public bool hasVersionInfo;
            public VersionInfoRecord versionInfo;
        }

        [Serializable]
        private sealed class OwnedPlayerSettingsRecord
        {
            public int scriptingBackend;
            public string companyName;
            public string productName;
            public string bundleVersion;
            public string applicationIdentifier;
            public int androidBundleVersionCode;
            public string iosBuildNumber;
            public bool exportAndroidProject;
            public bool developmentBuild;
            public EditorBuildSceneRecord[] editorBuildScenes;
            public bool showSplashScreen;
            public bool showUnityLogo;
            public string[] preloadedAssetIds;
        }

        [Serializable]
        private sealed class EditorBuildSceneRecord
        {
            public string path;
            public bool enabled;
        }

        [Serializable]
        private sealed class VersionInfoRecord
        {
            public string state;
            public FileRecord asset;
            public FileRecord meta;
            public string stageAssetPath;
            public string stageMetaPath;
            public FileIdentity stageAsset;
            public FileIdentity stageMeta;
            public FileIdentity installedAsset;
            public FileIdentity installedMeta;
            public GeneratedAssetDirectoryRecord[] generatedDirectories;
        }

        [Serializable]
        private sealed class GeneratedAssetDirectoryRecord
        {
            public string relativePath;
            public string guid;
            public string metaBase64;
            public string metaSha256;
        }

        internal sealed class PlayerSettingsPersistenceToken
        {
            internal long Length { get; }
            internal string Sha256 { get; }
            internal long EditorBuildSettingsLength { get; }
            internal string EditorBuildSettingsSha256 { get; }

            internal PlayerSettingsPersistenceToken(
                long length,
                string sha256,
                long editorBuildSettingsLength,
                string editorBuildSettingsSha256)
            {
                Length = length;
                Sha256 = sha256 ?? string.Empty;
                EditorBuildSettingsLength = editorBuildSettingsLength;
                EditorBuildSettingsSha256 =
                    editorBuildSettingsSha256 ?? string.Empty;
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

}
