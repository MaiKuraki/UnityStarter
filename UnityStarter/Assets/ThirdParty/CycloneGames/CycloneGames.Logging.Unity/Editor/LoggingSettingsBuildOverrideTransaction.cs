#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityLoggingSettings = CycloneGames.Logging.Unity.LoggingSettings;

namespace CycloneGames.Logging.Unity.Editor
{
    internal enum LoggingSettingsBuildFolderCheckpoint : byte
    {
        IntentPersisted = 0,
        FolderCreated = 1,
        GuidResolved = 2,
        AppliedPersisted = 3,
        FolderMoved = 4,
        GuidPersisted = 5
    }

    internal enum LoggingSettingsBuildRecoveryCheckpoint : byte
    {
        RecoveryAnchorPersisted = 0,
        OriginalCandidatesPruned = 1
    }

    /// <summary>
    /// Owns the temporary Resources override and its crash-recovery evidence.
    /// Recovery is deliberately explicit; normal builds only inspect and fail closed.
    /// </summary>
    internal sealed class LoggingSettingsBuildOverrideTransaction : IDisposable
    {
        internal const string StateDirectoryRelativePath = ".buildpipeline/transactions/logging-settings";
        internal const string GeneratedSettingsAssetPath =
            "Assets/Generated/CycloneGames.Logging.Unity/Resources/CycloneGames.Logging.Unity/LoggingSettingsBuildOverride.asset";
        internal const int MaximumJournalFileBytes = 64 * 1024;
        internal const long MaximumGeneratedAssetBytes = 1024 * 1024;

        private const int JournalSchemaVersion = 4;
        private const int MaximumStateEntries = 8;
        private const int MaximumFolderRecords = 8;
        private const string JournalFileName = "journal.json";
        private const string TemporaryJournalFileName = "journal.json.tmp";
        private const string BackupJournalFileName = "journal.json.bak";
        private const string RecoveryJournalFileName = "journal.recovery.json";
        private const string LockFileName = "transaction.lock";
        private const string PhasePrepared = "Prepared";
        private const string PhaseActive = "Active";
        private const string PhaseCleanupPrepared = "CleanupPrepared";
        private const string PhaseAssetDeleted = "AssetDeleted";
        private const string FolderPhaseIntent = "Intent";
        private const string FolderPhaseApplied = "Applied";
        private const string FolderPhaseIdentified = "Identified";
        private const string StagingFolderNamePrefix = "__CycloneGamesLoggingBuild_";
        private const string GeneratedContainerFolderPath = "Assets/Generated";
        private const string GeneratedRootFolderPath = GeneratedContainerFolderPath + "/CycloneGames.Logging.Unity";
        private const string GeneratedResourcesFolderPath = GeneratedRootFolderPath + "/Resources";
        private const string GeneratedSettingsFolderPath = GeneratedResourcesFolderPath + "/CycloneGames.Logging.Unity";

        private static readonly string[] KnownStateFileNames =
        {
            JournalFileName,
            TemporaryJournalFileName,
            BackupJournalFileName,
            RecoveryJournalFileName,
            LockFileName
        };

        internal static Action<string, string, LoggingSettingsBuildFolderCheckpoint>
            FolderCheckpointForTests;
        internal static Action<LoggingSettingsBuildRecoveryCheckpoint> RecoveryCheckpointForTests;

        private readonly string _projectRoot;
        private readonly string _stateDirectory;
        private FileStream _lockStream;
        private LoggingSettingsBuildJournal _journal;
        private bool _completed;

        private LoggingSettingsBuildOverrideTransaction(
            string projectRoot,
            string stateDirectory,
            FileStream lockStream,
            LoggingSettingsBuildJournal journal)
        {
            _projectRoot = projectRoot;
            _stateDirectory = stateDirectory;
            _lockStream = lockStream;
            _journal = journal;
        }

        internal static LoggingSettingsBuildOverrideTransaction Begin(
            string projectRoot,
            UnityLoggingSettings configuredSettings)
        {
            if (configuredSettings == null)
            {
                throw new ArgumentNullException(nameof(configuredSettings));
            }

            string normalizedRoot = NormalizeCurrentProjectRoot(projectRoot);
            string stateDirectory = GetStateDirectory(normalizedRoot);
            EnsureSafeStateDirectory(normalizedRoot, stateDirectory, createIfMissing: true);
            FileStream lockStream = AcquireExclusiveLock(stateDirectory);
            LoggingSettingsBuildOverrideTransaction transaction = null;
            UnityLoggingSettings generatedSettings = null;

            try
            {
                ThrowIfPendingEvidenceUnderLock(normalizedRoot, stateDirectory);

                generatedSettings = UnityEngine.Object.Instantiate(configuredSettings);
                generatedSettings.name = "LoggingSettingsBuildOverride";

                var journal = new LoggingSettingsBuildJournal
                {
                    schemaVersion = JournalSchemaVersion,
                    revision = 1,
                    transactionId = Guid.NewGuid().ToString("N"),
                    projectToken = Guid.NewGuid().ToString("N"),
                    phase = PhasePrepared,
                    assetPath = GeneratedSettingsAssetPath,
                    payloadSha256 = string.Empty,
                    assetGuid = string.Empty,
                    assetSha256 = string.Empty,
                    assetBytes = 0,
                    createdFolders = Array.Empty<LoggingSettingsBuildFolderRecord>()
                };

                string payloadHash = ComputePayloadHash(generatedSettings);
                journal.payloadSha256 = payloadHash;
                generatedSettings.SetBuildOverrideProvenance(
                    journal.transactionId,
                    journal.projectToken,
                    payloadHash);

                transaction = new LoggingSettingsBuildOverrideTransaction(
                    normalizedRoot,
                    stateDirectory,
                    lockStream,
                    journal);
                lockStream = null;
                transaction.WriteJournal();
                transaction.EnsureGeneratedAssetFolder();

                AssetDatabase.CreateAsset(generatedSettings, GeneratedSettingsAssetPath);
                generatedSettings = null;

                UnityLoggingSettings persistedSettings =
                    AssetDatabase.LoadAssetAtPath<UnityLoggingSettings>(GeneratedSettingsAssetPath);
                if (persistedSettings == null)
                {
                    throw new InvalidOperationException(
                        "The generated LoggingSettings override could not be loaded after creation.");
                }

                AssetDatabase.SaveAssetIfDirty(persistedSettings);
                AssetDatabase.ImportAsset(
                    GeneratedSettingsAssetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                string assetGuid = AssetDatabase.AssetPathToGUID(GeneratedSettingsAssetPath);
                if (!IsValidUnityGuid(assetGuid))
                {
                    throw new InvalidOperationException(
                        "Unity did not assign a valid GUID to the generated LoggingSettings override.");
                }

                string assetAbsolutePath = GetGeneratedAssetAbsolutePath(normalizedRoot);
                FileIdentity assetIdentity = ComputeBoundedFileIdentity(
                    assetAbsolutePath,
                    MaximumGeneratedAssetBytes,
                    "generated LoggingSettings override");

                journal.assetGuid = assetGuid;
                journal.assetSha256 = assetIdentity.Sha256;
                journal.assetBytes = assetIdentity.Bytes;
                journal.phase = PhaseActive;
                transaction.WriteJournal();
                transaction.ValidateOwnedAsset(requireActiveFileIdentity: true);
                return transaction;
            }
            catch
            {
                if (generatedSettings != null && !AssetDatabase.Contains(generatedSettings))
                {
                    UnityEngine.Object.DestroyImmediate(generatedSettings);
                }

                if (transaction != null)
                {
                    transaction.Dispose();
                }
                else
                {
                    lockStream?.Dispose();
                }

                throw;
            }
        }

        internal static void ThrowIfPendingEvidence(string projectRoot)
        {
            string normalizedRoot = NormalizeCurrentProjectRoot(projectRoot);
            string stateDirectory = GetStateDirectory(normalizedRoot);
            if (Directory.Exists(stateDirectory))
            {
                EnsureSafeStateDirectory(normalizedRoot, stateDirectory, createIfMissing: false);
                StateInventory inventory = ReadStateInventory(stateDirectory);
                if (inventory.HasJournalCandidates || inventory.HasLockFile)
                {
                    throw new InvalidOperationException(
                        "A LoggingSettings build transaction requires explicit recovery at " +
                        StateDirectoryRelativePath + ".");
                }
            }

            if (DoesGeneratedAssetExist(normalizedRoot))
            {
                throw new InvalidOperationException(
                    "The generated LoggingSettings override path is occupied without a clean active transaction: " +
                    GeneratedSettingsAssetPath + ".");
            }
        }

        internal static void Recover(string projectRoot)
        {
            string normalizedRoot = NormalizeCurrentProjectRoot(projectRoot);
            string stateDirectory = GetStateDirectory(normalizedRoot);

            if (!Directory.Exists(stateDirectory))
            {
                if (DoesGeneratedAssetExist(normalizedRoot))
                {
                    throw new InvalidOperationException(
                        "Recovery refused because the generated override exists without transaction evidence.");
                }

                return;
            }

            EnsureSafeStateDirectory(normalizedRoot, stateDirectory, createIfMissing: false);
            FileStream lockStream = AcquireExclusiveLock(stateDirectory);
            bool recovered = false;
            try
            {
                StateInventory inventory = ReadStateInventory(stateDirectory);
                if (!inventory.HasJournalCandidates)
                {
                    if (DoesGeneratedAssetExist(normalizedRoot))
                    {
                        throw new InvalidOperationException(
                            "Recovery refused because the generated override exists without a journal.");
                    }

                    recovered = true;
                    return;
                }

                LoggingSettingsBuildJournal journal = SelectAuthoritativeJournal(inventory.JournalCandidates);
                var transaction = new LoggingSettingsBuildOverrideTransaction(
                    normalizedRoot,
                    stateDirectory,
                    lockStream,
                    journal);
                lockStream = null;
                try
                {
                    transaction.ValidateRecoveryStartingState();
                    transaction.NormalizeJournalFilesForRecovery();
                    transaction.ReconcileFolderOwnershipForRecovery();
                    transaction.RecoverUnderLock();
                    transaction._completed = true;
                    transaction.ReleaseLock();
                    DeleteKnownStateFiles(stateDirectory);
                    DeleteDirectoryIfEmpty(stateDirectory);
                    recovered = true;
                }
                finally
                {
                    transaction.Dispose();
                }
            }
            finally
            {
                lockStream?.Dispose();
                if (recovered)
                {
                    DeleteFileIfPresent(Path.Combine(stateDirectory, LockFileName));
                    DeleteDirectoryIfEmpty(stateDirectory);
                }
            }
        }

        internal void Complete()
        {
            if (_completed)
            {
                return;
            }

            EnsureLockHeld();
            LoggingSettingsBuildJournal authoritative =
                SelectAuthoritativeJournal(ReadStateInventory(_stateDirectory).JournalCandidates);
            EnsureSameTransaction(_journal, authoritative);
            _journal = authoritative;

            if (!string.Equals(_journal.phase, PhaseActive, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The LoggingSettings transaction is not active and cannot be completed normally.");
            }

            ValidateOwnedAsset(requireActiveFileIdentity: true);
            _journal.phase = PhaseCleanupPrepared;
            WriteJournal();
            DeleteOwnedGeneratedAsset();
            _journal.phase = PhaseAssetDeleted;
            WriteJournal();
            CleanupOwnedFolders();

            DeleteKnownJournalFiles(_stateDirectory);
            _completed = true;
            ReleaseLock();
            DeleteFileIfPresent(Path.Combine(_stateDirectory, LockFileName));
            DeleteDirectoryIfEmpty(_stateDirectory);
        }

        public void Dispose()
        {
            ReleaseLock();
        }

        internal static string ReadBoundedJournalForTests(string path)
        {
            return ReadBoundedUtf8(path, MaximumJournalFileBytes, "LoggingSettings build journal");
        }

        internal static string ComputePayloadHashForTests(UnityLoggingSettings settings)
        {
            return ComputePayloadHash(settings);
        }

        internal static bool ValidateProvenanceForTests(
            UnityLoggingSettings settings,
            string transactionId,
            string projectToken,
            string payloadHash,
            out string error)
        {
            return TryValidateProvenance(settings, transactionId, projectToken, payloadHash, out error);
        }

        private void RecoverUnderLock()
        {
            EnsureLockHeld();
            bool assetExists = DoesGeneratedAssetExist(_projectRoot);

            switch (_journal.phase)
            {
                case PhasePrepared:
                    if (assetExists)
                    {
                        ValidateOwnedAsset(requireActiveFileIdentity: false);
                        CaptureActiveAssetIdentity();
                        _journal.phase = PhaseCleanupPrepared;
                        WriteJournal();
                        DeleteOwnedGeneratedAsset();
                        _journal.phase = PhaseAssetDeleted;
                        WriteJournal();
                    }
                    break;

                case PhaseActive:
                    if (!assetExists)
                    {
                        throw new InvalidOperationException(
                            "Recovery refused because an active journal no longer has its tracked asset.");
                    }

                    ValidateOwnedAsset(requireActiveFileIdentity: true);
                    _journal.phase = PhaseCleanupPrepared;
                    WriteJournal();
                    DeleteOwnedGeneratedAsset();
                    _journal.phase = PhaseAssetDeleted;
                    WriteJournal();
                    break;

                case PhaseCleanupPrepared:
                    if (assetExists)
                    {
                        ValidateOwnedAsset(requireActiveFileIdentity: true);
                        DeleteOwnedGeneratedAsset();
                    }

                    _journal.phase = PhaseAssetDeleted;
                    WriteJournal();
                    break;

                case PhaseAssetDeleted:
                    if (assetExists)
                    {
                        throw new InvalidOperationException(
                            "Recovery refused because an AssetDeleted journal still has an asset at the tracked path.");
                    }
                    break;

                default:
                    throw new InvalidDataException("The LoggingSettings build journal has an unsupported phase.");
            }

            CleanupOwnedFolders();
            DeleteKnownJournalFiles(_stateDirectory);
        }

        private void ValidateRecoveryStartingState()
        {
            bool assetExists = DoesGeneratedAssetExist(_projectRoot);
            switch (_journal.phase)
            {
                case PhasePrepared:
                    if (assetExists)
                    {
                        ValidateOwnedAsset(requireActiveFileIdentity: false);
                    }
                    break;

                case PhaseActive:
                    if (!assetExists)
                    {
                        throw new InvalidOperationException(
                            "Recovery refused because an active journal no longer has its tracked asset.");
                    }

                    ValidateOwnedAsset(requireActiveFileIdentity: true);
                    break;

                case PhaseCleanupPrepared:
                    if (assetExists)
                    {
                        ValidateOwnedAsset(requireActiveFileIdentity: true);
                    }
                    break;

                case PhaseAssetDeleted:
                    if (assetExists)
                    {
                        throw new InvalidOperationException(
                            "Recovery refused because an AssetDeleted journal still has an asset at the tracked path.");
                    }
                    break;

                default:
                    throw new InvalidDataException("The LoggingSettings build journal has an unsupported phase.");
            }
        }

        private void NormalizeJournalFilesForRecovery()
        {
            EnsureLockHeld();
            string journalPath = Path.Combine(_stateDirectory, JournalFileName);
            string temporaryPath = Path.Combine(_stateDirectory, TemporaryJournalFileName);
            string backupPath = Path.Combine(_stateDirectory, BackupJournalFileName);
            string recoveryPath = Path.Combine(_stateDirectory, RecoveryJournalFileName);

            bool recoveryAnchorMatches = false;
            if (File.Exists(recoveryPath))
            {
                try
                {
                    LoggingSettingsBuildJournal recoveryJournal = DeserializeAndValidateJournal(
                        ReadBoundedUtf8(
                            recoveryPath,
                            MaximumJournalFileBytes,
                            "LoggingSettings recovery journal"),
                        recoveryPath);
                    recoveryAnchorMatches = string.Equals(
                        JsonUtility.ToJson(recoveryJournal),
                        JsonUtility.ToJson(_journal),
                        StringComparison.Ordinal);
                }
                catch (InvalidDataException)
                {
                    recoveryAnchorMatches = false;
                }
            }

            if (!recoveryAnchorMatches)
            {
                // The selected authoritative candidate still exists while an old or interrupted
                // recovery anchor is replaced, so this publication cannot erase the last evidence.
                DeleteFileIfPresent(recoveryPath);
                WriteJournalFileCreateNew(recoveryPath, _journal);
            }

            RecoveryCheckpointForTests?.Invoke(
                LoggingSettingsBuildRecoveryCheckpoint.RecoveryAnchorPersisted);

            // Keep the flushed recovery anchor until every older candidate has been removed.
            DeleteFileIfPresent(journalPath);
            DeleteFileIfPresent(temporaryPath);
            DeleteFileIfPresent(backupPath);
            RecoveryCheckpointForTests?.Invoke(
                LoggingSettingsBuildRecoveryCheckpoint.OriginalCandidatesPruned);

            File.Move(recoveryPath, journalPath);
        }

        private void EnsureGeneratedAssetFolder()
        {
            EnsureAssetFolder(GeneratedSettingsFolderPath);
        }

        private void EnsureAssetFolder(string folderPath)
        {
            if (!IsAllowedGeneratedFolder(folderPath))
            {
                throw new InvalidOperationException("Refusing to create an unowned generated folder: " + folderPath);
            }

            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                string absolutePath = AssetPathToAbsolutePath(_projectRoot, next);
                EnsureNoReparsePointsBelowRoot(_projectRoot, absolutePath);
                if (!AssetDatabase.IsValidFolder(next))
                {
                    if (Directory.Exists(absolutePath) ||
                        File.Exists(absolutePath) ||
                        File.Exists(absolutePath + ".meta"))
                    {
                        throw new InvalidOperationException(
                            "The generated folder path is occupied outside AssetDatabase: " + next);
                    }

                    LoggingSettingsBuildFolderRecord folder = AddFolderIntent(next);
                    string stagingAbsolutePath =
                        AssetPathToAbsolutePath(_projectRoot, folder.stagingAssetPath);
                    EnsureNoReparsePointsBelowRoot(_projectRoot, stagingAbsolutePath);
                    if (AssetDatabase.IsValidFolder(folder.stagingAssetPath) ||
                        Directory.Exists(stagingAbsolutePath) ||
                        File.Exists(stagingAbsolutePath) ||
                        File.Exists(stagingAbsolutePath + ".meta"))
                    {
                        throw new InvalidOperationException(
                            "The transaction staging folder path is already occupied: " +
                            folder.stagingAssetPath);
                    }

                    WriteJournal();
                    InvokeFolderCheckpoint(
                        folder.assetPath,
                        folder.stagingAssetPath,
                        LoggingSettingsBuildFolderCheckpoint.IntentPersisted);

                    string stagingFolderName = GetAssetName(folder.stagingAssetPath);
                    string createdGuid = AssetDatabase.CreateFolder(current, stagingFolderName);
                    if (!AssetDatabase.IsValidFolder(folder.stagingAssetPath) ||
                        !Directory.Exists(stagingAbsolutePath))
                    {
                        throw new InvalidOperationException(
                            "Failed to create generated transaction staging folder: " +
                            folder.stagingAssetPath);
                    }

                    EnsureNoReparsePointsBelowRoot(_projectRoot, stagingAbsolutePath);
                    InvokeFolderCheckpoint(
                        folder.assetPath,
                        folder.stagingAssetPath,
                        LoggingSettingsBuildFolderCheckpoint.FolderCreated);

                    string resolvedGuid = AssetDatabase.AssetPathToGUID(folder.stagingAssetPath);
                    if (!IsValidUnityGuid(createdGuid) ||
                        !string.Equals(createdGuid, resolvedGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Unity did not provide a stable GUID for generated transaction staging folder: " +
                            folder.stagingAssetPath);
                    }

                    InvokeFolderCheckpoint(
                        folder.assetPath,
                        folder.stagingAssetPath,
                        LoggingSettingsBuildFolderCheckpoint.GuidResolved);

                    folder.assetGuid = createdGuid;
                    folder.phase = FolderPhaseApplied;
                    WriteJournal();
                    InvokeFolderCheckpoint(
                        folder.assetPath,
                        folder.stagingAssetPath,
                        LoggingSettingsBuildFolderCheckpoint.AppliedPersisted);

                    MoveAppliedFolderToFinalPath(folder);
                    InvokeFolderCheckpoint(
                        folder.assetPath,
                        folder.stagingAssetPath,
                        LoggingSettingsBuildFolderCheckpoint.FolderMoved);

                    folder.phase = FolderPhaseIdentified;
                    WriteJournal();
                    InvokeFolderCheckpoint(
                        folder.assetPath,
                        folder.stagingAssetPath,
                        LoggingSettingsBuildFolderCheckpoint.GuidPersisted);
                }

                current = next;
            }
        }

        private LoggingSettingsBuildFolderRecord AddFolderIntent(string assetPath)
        {
            LoggingSettingsBuildFolderRecord[] existing =
                _journal.createdFolders ?? Array.Empty<LoggingSettingsBuildFolderRecord>();
            if (existing.Length >= MaximumFolderRecords)
            {
                throw new InvalidOperationException("The LoggingSettings transaction exceeded its folder budget.");
            }

            var updated = new LoggingSettingsBuildFolderRecord[existing.Length + 1];
            Array.Copy(existing, updated, existing.Length);
            var record = new LoggingSettingsBuildFolderRecord
            {
                assetPath = assetPath,
                stagingAssetPath = BuildStagingFolderAssetPath(
                    assetPath,
                    _journal.transactionId,
                    existing.Length),
                phase = FolderPhaseIntent,
                assetGuid = string.Empty
            };
            updated[updated.Length - 1] = record;
            _journal.createdFolders = updated;
            return record;
        }

        private void ReconcileFolderOwnershipForRecovery()
        {
            LoggingSettingsBuildFolderRecord[] folders =
                _journal.createdFolders ?? Array.Empty<LoggingSettingsBuildFolderRecord>();
            for (int i = 0; i < folders.Length; i++)
            {
                LoggingSettingsBuildFolderRecord folder = folders[i];
                string finalAbsolutePath = AssetPathToAbsolutePath(_projectRoot, folder.assetPath);
                string stagingAbsolutePath =
                    AssetPathToAbsolutePath(_projectRoot, folder.stagingAssetPath);
                bool finalExists = GetConsistentFolderExistence(
                    folder.assetPath,
                    finalAbsolutePath);
                bool stagingExists = GetConsistentFolderExistence(
                    folder.stagingAssetPath,
                    stagingAbsolutePath);

                if (string.Equals(folder.phase, FolderPhaseIntent, StringComparison.Ordinal))
                {
                    if (!stagingExists)
                    {
                        if (finalExists)
                        {
                            throw new InvalidOperationException(
                                "An intent-only folder has an unverified object at its final path: " +
                                folder.assetPath);
                        }

                        continue;
                    }

                    if (finalExists || !IsDirectoryEmptyBounded(stagingAbsolutePath, 1))
                    {
                        throw new InvalidOperationException(
                            "An intent-only transaction staging folder cannot be adopted safely: " +
                            folder.stagingAssetPath);
                    }

                    string resolvedGuid = AssetDatabase.AssetPathToGUID(folder.stagingAssetPath);
                    if (!IsValidUnityGuid(resolvedGuid))
                    {
                        throw new InvalidOperationException(
                            "The intent-only transaction staging folder has no valid Unity GUID: " +
                            folder.stagingAssetPath);
                    }

                    folder.assetGuid = resolvedGuid;
                    folder.phase = FolderPhaseApplied;
                    WriteJournal();
                }

                if (string.Equals(folder.phase, FolderPhaseApplied, StringComparison.Ordinal))
                {
                    if (stagingExists)
                    {
                        if (finalExists)
                        {
                            throw new InvalidOperationException(
                                "Both staging and final generated folders exist for one transaction record: " +
                                folder.assetPath);
                        }

                        MoveAppliedFolderToFinalPath(folder);
                        finalExists = true;
                        stagingExists = false;
                    }
                    else if (finalExists)
                    {
                        ValidateFolderGuid(folder.assetPath, folder.assetGuid);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "An applied generated folder is missing from both staging and final paths: " +
                            folder.assetPath);
                    }

                    folder.phase = FolderPhaseIdentified;
                    WriteJournal();
                }

                if (stagingExists)
                {
                    throw new InvalidOperationException(
                        "An identified generated folder still has a transaction staging path: " +
                        folder.stagingAssetPath);
                }

                if (finalExists)
                {
                    ValidateFolderGuid(folder.assetPath, folder.assetGuid);
                }
            }
        }

        private void MoveAppliedFolderToFinalPath(LoggingSettingsBuildFolderRecord folder)
        {
            string finalAbsolutePath = AssetPathToAbsolutePath(_projectRoot, folder.assetPath);
            string stagingAbsolutePath =
                AssetPathToAbsolutePath(_projectRoot, folder.stagingAssetPath);
            if (!GetConsistentFolderExistence(folder.stagingAssetPath, stagingAbsolutePath))
            {
                throw new InvalidOperationException(
                    "The generated transaction staging folder is missing: " +
                    folder.stagingAssetPath);
            }

            if (GetConsistentFolderExistence(folder.assetPath, finalAbsolutePath))
            {
                throw new InvalidOperationException(
                    "The generated transaction final folder path is already occupied: " +
                    folder.assetPath);
            }

            ValidateFolderGuid(folder.stagingAssetPath, folder.assetGuid);
            if (!IsDirectoryEmptyBounded(stagingAbsolutePath, 1))
            {
                throw new InvalidOperationException(
                    "The transaction staging folder contains unverified entries: " +
                    folder.stagingAssetPath);
            }

            string moveError = AssetDatabase.MoveAsset(folder.stagingAssetPath, folder.assetPath);
            if (!string.IsNullOrEmpty(moveError))
            {
                throw new InvalidOperationException(
                    "Unity refused to publish the generated transaction folder: " + moveError);
            }

            if (!GetConsistentFolderExistence(folder.assetPath, finalAbsolutePath) ||
                GetConsistentFolderExistence(folder.stagingAssetPath, stagingAbsolutePath))
            {
                throw new InvalidOperationException(
                    "The generated transaction folder move did not reach a consistent final state: " +
                    folder.assetPath);
            }

            ValidateFolderGuid(folder.assetPath, folder.assetGuid);
        }

        private void ValidateFolderGuid(string assetPath, string expectedGuid)
        {
            string actualGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (!string.Equals(actualGuid, expectedGuid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Generated folder GUID does not match its transaction record: " + assetPath);
            }
        }

        private bool GetConsistentFolderExistence(string assetPath, string absolutePath)
        {
            bool physicallyExists = Directory.Exists(absolutePath);
            bool importedFolderExists = AssetDatabase.IsValidFolder(assetPath);
            bool metadataExists = File.Exists(absolutePath + ".meta");
            if (File.Exists(absolutePath) ||
                physicallyExists != importedFolderExists ||
                physicallyExists != metadataExists)
            {
                throw new InvalidOperationException(
                    "Generated folder state is inconsistent between the filesystem and AssetDatabase: " +
                    assetPath);
            }

            if (physicallyExists)
            {
                EnsureNoReparsePointsBelowRoot(_projectRoot, absolutePath);
            }

            return physicallyExists;
        }

        private void CleanupOwnedFolders()
        {
            LoggingSettingsBuildFolderRecord[] folders =
                _journal.createdFolders ?? Array.Empty<LoggingSettingsBuildFolderRecord>();
            for (int i = folders.Length - 1; i >= 0; i--)
            {
                LoggingSettingsBuildFolderRecord folder = folders[i];
                if (folder == null || !IsAllowedGeneratedFolder(folder.assetPath))
                {
                    throw new InvalidDataException("The journal contains an unowned generated folder path.");
                }

                string stagingAbsolutePath =
                    AssetPathToAbsolutePath(_projectRoot, folder.stagingAssetPath);
                if (GetConsistentFolderExistence(folder.stagingAssetPath, stagingAbsolutePath))
                {
                    throw new InvalidOperationException(
                        "Generated folder cleanup found a remaining transaction staging folder: " +
                        folder.stagingAssetPath);
                }

                string absolutePath = AssetPathToAbsolutePath(_projectRoot, folder.assetPath);
                if (!GetConsistentFolderExistence(folder.assetPath, absolutePath))
                {
                    continue;
                }

                if (!string.Equals(folder.phase, FolderPhaseIdentified, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Generated folder cleanup requires identified ownership: " + folder.assetPath);
                }

                string actualGuid = AssetDatabase.AssetPathToGUID(folder.assetPath);
                if (!string.Equals(actualGuid, folder.assetGuid, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Generated folder cleanup refused a GUID mismatch: " + folder.assetPath);
                }

                if (!IsDirectoryEmptyBounded(absolutePath, 1))
                {
                    throw new InvalidOperationException(
                        "Generated folder cleanup refused a non-empty owned folder: " + folder.assetPath);
                }

                if (!AssetDatabase.DeleteAsset(folder.assetPath) ||
                    GetConsistentFolderExistence(folder.assetPath, absolutePath))
                {
                    throw new InvalidOperationException(
                        "Unity refused to delete an empty Logging-owned generated folder: " + folder.assetPath);
                }
            }
        }

        private void ValidateOwnedAsset(bool requireActiveFileIdentity)
        {
            if (!DoesGeneratedAssetExist(_projectRoot))
            {
                throw new InvalidOperationException("The tracked generated LoggingSettings override is missing.");
            }

            string absolutePath = GetGeneratedAssetAbsolutePath(_projectRoot);
            EnsureNoReparsePointsBelowRoot(_projectRoot, absolutePath);
            if (File.Exists(absolutePath))
            {
                var info = new FileInfo(absolutePath);
                if (info.Length <= 0 || info.Length > MaximumGeneratedAssetBytes)
                {
                    throw new InvalidDataException("The generated LoggingSettings override exceeds its file budget.");
                }
            }

            AssetDatabase.ImportAsset(
                GeneratedSettingsAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            UnityLoggingSettings settings =
                AssetDatabase.LoadAssetAtPath<UnityLoggingSettings>(GeneratedSettingsAssetPath);
            if (settings == null)
            {
                throw new InvalidOperationException(
                    "The tracked generated asset is not a LoggingSettings asset; cleanup was refused.");
            }

            string provenanceError = "identity mismatch";
            if (!TryValidateProvenance(
                    settings,
                    _journal.transactionId,
                    _journal.projectToken,
                    _journal.payloadSha256,
                    out provenanceError))
            {
                throw new InvalidOperationException(
                    "Generated LoggingSettings provenance validation failed; cleanup was refused: " +
                    (provenanceError ?? "identity mismatch"));
            }

            if (!requireActiveFileIdentity)
            {
                return;
            }

            string actualGuid = AssetDatabase.AssetPathToGUID(GeneratedSettingsAssetPath);
            if (!string.Equals(actualGuid, _journal.assetGuid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Generated LoggingSettings GUID does not match the active journal; cleanup was refused.");
            }

            FileIdentity identity = ComputeBoundedFileIdentity(
                absolutePath,
                MaximumGeneratedAssetBytes,
                "generated LoggingSettings override");
            if (identity.Bytes != _journal.assetBytes ||
                !string.Equals(identity.Sha256, _journal.assetSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Generated LoggingSettings content does not match the active journal; cleanup was refused.");
            }
        }

        private void CaptureActiveAssetIdentity()
        {
            string guid = AssetDatabase.AssetPathToGUID(GeneratedSettingsAssetPath);
            if (!IsValidUnityGuid(guid))
            {
                throw new InvalidOperationException(
                    "The prepared LoggingSettings override has no valid Unity GUID.");
            }

            FileIdentity identity = ComputeBoundedFileIdentity(
                GetGeneratedAssetAbsolutePath(_projectRoot),
                MaximumGeneratedAssetBytes,
                "generated LoggingSettings override");
            _journal.assetGuid = guid;
            _journal.assetSha256 = identity.Sha256;
            _journal.assetBytes = identity.Bytes;
        }

        private void DeleteOwnedGeneratedAsset()
        {
            if (!DoesGeneratedAssetExist(_projectRoot))
            {
                throw new InvalidOperationException("The generated LoggingSettings override is already missing.");
            }

            if (!AssetDatabase.DeleteAsset(GeneratedSettingsAssetPath))
            {
                throw new InvalidOperationException(
                    "Unity refused to delete the verified generated LoggingSettings override.");
            }

            if (DoesGeneratedAssetExist(_projectRoot))
            {
                throw new InvalidOperationException(
                    "The generated LoggingSettings override still exists after deletion.");
            }
        }

        private void WriteJournal()
        {
            EnsureLockHeld();
            ValidateJournal(_journal);
            checked
            {
                _journal.revision++;
            }

            WriteJournalAtomic(_stateDirectory, _journal);
        }

        private static void WriteJournalAtomic(string stateDirectory, LoggingSettingsBuildJournal journal)
        {
            string journalPath = Path.Combine(stateDirectory, JournalFileName);
            string temporaryPath = Path.Combine(stateDirectory, TemporaryJournalFileName);
            string backupPath = Path.Combine(stateDirectory, BackupJournalFileName);
            string recoveryPath = Path.Combine(stateDirectory, RecoveryJournalFileName);

            if (File.Exists(temporaryPath) || File.Exists(backupPath) || File.Exists(recoveryPath))
            {
                throw new InvalidOperationException(
                    "An interrupted atomic journal publication requires explicit recovery.");
            }

            WriteJournalFileCreateNew(temporaryPath, journal);

            if (File.Exists(journalPath))
            {
                // Each rename is atomic. If the process stops between them, explicit recovery
                // evaluates both the flushed temporary candidate and the previous backup.
                File.Move(journalPath, backupPath);
                File.Move(temporaryPath, journalPath);
                File.Delete(backupPath);
            }
            else
            {
                File.Move(temporaryPath, journalPath);
            }
        }

        private static void WriteJournalFileCreateNew(
            string path,
            LoggingSettingsBuildJournal journal)
        {
            string json = JsonUtility.ToJson(journal);
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalFileBytes)
            {
                throw new InvalidDataException("The LoggingSettings build journal exceeds its byte budget.");
            }

            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static LoggingSettingsBuildJournal SelectAuthoritativeJournal(
            IReadOnlyList<JournalCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                throw new InvalidDataException("No LoggingSettings build journal candidate is available.");
            }

            LoggingSettingsBuildJournal selected = null;
            string transactionId = null;
            string projectToken = null;
            var revisions = new Dictionary<int, string>();
            var invalidCandidates = new List<string>();

            for (int i = 0; i < candidates.Count; i++)
            {
                JournalCandidate candidate = candidates[i];
                LoggingSettingsBuildJournal journal;
                try
                {
                    if (!string.IsNullOrEmpty(candidate.Error))
                    {
                        throw new InvalidDataException(candidate.Error);
                    }

                    journal = DeserializeAndValidateJournal(candidate.Json, candidate.Path);
                }
                catch (InvalidDataException exception)
                {
                    invalidCandidates.Add(Path.GetFileName(candidate.Path) + ": " + exception.Message);
                    continue;
                }

                if (transactionId == null)
                {
                    transactionId = journal.transactionId;
                    projectToken = journal.projectToken;
                }
                else if (!string.Equals(transactionId, journal.transactionId, StringComparison.Ordinal) ||
                         !string.Equals(projectToken, journal.projectToken, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Journal candidates belong to different LoggingSettings transactions.");
                }

                string normalizedJson = JsonUtility.ToJson(journal);
                if (revisions.TryGetValue(journal.revision, out string existingJson) &&
                    !string.Equals(existingJson, normalizedJson, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Journal candidates conflict at the same transaction revision.");
                }

                revisions[journal.revision] = normalizedJson;
                if (selected == null || journal.revision > selected.revision)
                {
                    selected = journal;
                }
            }

            if (selected == null)
            {
                throw new InvalidDataException(
                    "No valid LoggingSettings journal candidate was found. " +
                    string.Join(" | ", invalidCandidates));
            }

            return selected;
        }

        private static LoggingSettingsBuildJournal DeserializeAndValidateJournal(string json, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("LoggingSettings build journal is empty: " + sourcePath);
            }

            LoggingSettingsBuildJournal journal;
            try
            {
                journal = JsonUtility.FromJson<LoggingSettingsBuildJournal>(json);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                throw new InvalidDataException(
                    "LoggingSettings build journal JSON is invalid: " + sourcePath,
                    exception);
            }

            ValidateJournal(journal);
            return journal;
        }

        private static void ValidateJournal(LoggingSettingsBuildJournal journal)
        {
            if (journal == null)
            {
                throw new InvalidDataException("LoggingSettings build journal is missing.");
            }

            if (journal.schemaVersion != JournalSchemaVersion)
            {
                throw new InvalidDataException(
                    "Unsupported LoggingSettings build journal schema: " + journal.schemaVersion);
            }

            if (journal.revision <= 0 || journal.revision > 100000)
            {
                throw new InvalidDataException("LoggingSettings journal revision is outside its budget.");
            }

            if (!Guid.TryParseExact(journal.transactionId, "N", out _) ||
                !Guid.TryParseExact(journal.projectToken, "N", out _))
            {
                throw new InvalidDataException("LoggingSettings journal transaction identity is invalid.");
            }

            if (!string.Equals(journal.assetPath, GeneratedSettingsAssetPath, StringComparison.Ordinal))
            {
                throw new InvalidDataException("LoggingSettings journal asset path is not owned by this module.");
            }

            bool knownPhase = string.Equals(journal.phase, PhasePrepared, StringComparison.Ordinal) ||
                              string.Equals(journal.phase, PhaseActive, StringComparison.Ordinal) ||
                              string.Equals(journal.phase, PhaseCleanupPrepared, StringComparison.Ordinal) ||
                              string.Equals(journal.phase, PhaseAssetDeleted, StringComparison.Ordinal);
            if (!knownPhase)
            {
                throw new InvalidDataException("LoggingSettings journal phase is invalid.");
            }

            bool requiresFileIdentity = !string.Equals(journal.phase, PhasePrepared, StringComparison.Ordinal);
            if (!IsSha256(journal.payloadSha256))
            {
                throw new InvalidDataException("LoggingSettings journal payload identity is invalid.");
            }

            if (requiresFileIdentity &&
                (!IsValidUnityGuid(journal.assetGuid) ||
                 !IsSha256(journal.assetSha256) ||
                 journal.assetBytes <= 0 ||
                 journal.assetBytes > MaximumGeneratedAssetBytes))
            {
                throw new InvalidDataException("LoggingSettings journal asset identity is invalid.");
            }

            if (!requiresFileIdentity &&
                (!string.IsNullOrEmpty(journal.assetGuid) ||
                 !string.IsNullOrEmpty(journal.assetSha256) ||
                 journal.assetBytes != 0))
            {
                throw new InvalidDataException("Prepared LoggingSettings journal must not contain file identity.");
            }

            LoggingSettingsBuildFolderRecord[] folders =
                journal.createdFolders ?? Array.Empty<LoggingSettingsBuildFolderRecord>();
            if (folders.Length > MaximumFolderRecords)
            {
                throw new InvalidDataException("LoggingSettings journal folder count exceeds its budget.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < folders.Length; i++)
            {
                LoggingSettingsBuildFolderRecord folder = folders[i];
                if (folder == null ||
                    !IsAllowedGeneratedFolder(folder.assetPath) ||
                    !string.Equals(
                        folder.stagingAssetPath,
                        BuildStagingFolderAssetPath(
                            folder.assetPath,
                            journal.transactionId,
                            i),
                        StringComparison.Ordinal) ||
                    !seen.Add(folder.assetPath))
                {
                    throw new InvalidDataException("LoggingSettings journal contains an invalid folder record.");
                }

                bool validIntent = string.Equals(folder.phase, FolderPhaseIntent, StringComparison.Ordinal) &&
                                   string.IsNullOrEmpty(folder.assetGuid);
                bool validApplied = string.Equals(folder.phase, FolderPhaseApplied, StringComparison.Ordinal) &&
                                    IsValidUnityGuid(folder.assetGuid);
                bool validIdentified = string.Equals(folder.phase, FolderPhaseIdentified, StringComparison.Ordinal) &&
                                       IsValidUnityGuid(folder.assetGuid);
                if (!validIntent && !validApplied && !validIdentified)
                {
                    throw new InvalidDataException("LoggingSettings journal contains an invalid folder phase.");
                }
            }
        }

        private static StateInventory ReadStateInventory(string stateDirectory)
        {
            var candidates = new List<JournalCandidate>(3);
            bool hasLock = false;
            int count = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(stateDirectory))
            {
                count++;
                if (count > MaximumStateEntries)
                {
                    throw new InvalidDataException("LoggingSettings transaction state exceeds its entry budget.");
                }

                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                    (attributes & FileAttributes.Directory) != 0)
                {
                    throw new InvalidDataException("LoggingSettings transaction state contains an unsafe entry.");
                }

                string name = Path.GetFileName(entry);
                if (!IsKnownStateFileName(name))
                {
                    throw new InvalidDataException(
                        "LoggingSettings transaction state contains an unknown file: " + name);
                }

                if (string.Equals(name, LockFileName, StringComparison.Ordinal))
                {
                    if (new FileInfo(entry).Length > 1024)
                    {
                        throw new InvalidDataException("LoggingSettings transaction lock exceeds its byte budget.");
                    }

                    hasLock = true;
                    continue;
                }

                try
                {
                    candidates.Add(new JournalCandidate(
                        entry,
                        ReadBoundedUtf8(entry, MaximumJournalFileBytes, "LoggingSettings build journal"),
                        string.Empty));
                }
                catch (InvalidDataException exception)
                {
                    candidates.Add(new JournalCandidate(entry, string.Empty, exception.Message));
                }
            }

            return new StateInventory(candidates, hasLock);
        }

        private static string ReadBoundedUtf8(string path, int maximumBytes, string description)
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    description + " is empty or exceeds the " +
                    maximumBytes.ToString(CultureInfo.InvariantCulture) + "-byte limit.");
            }

            byte[] buffer = new byte[maximumBytes + 1];
            int bytesRead = 0;
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            {
                while (bytesRead < buffer.Length)
                {
                    int read = stream.Read(buffer, bytesRead, buffer.Length - bytesRead);
                    if (read == 0)
                    {
                        break;
                    }

                    bytesRead += read;
                }
            }

            if (bytesRead > maximumBytes)
            {
                throw new InvalidDataException(description + " exceeds its byte limit.");
            }

            return new UTF8Encoding(false, true).GetString(buffer, 0, bytesRead);
        }

        private static bool TryValidateProvenance(
            UnityLoggingSettings settings,
            string expectedTransactionId,
            string expectedProjectToken,
            string expectedPayloadHash,
            out string error)
        {
            error = string.Empty;
            if (settings == null ||
                !settings.TryGetBuildOverrideProvenance(
                    out string transactionId,
                    out string projectToken,
                    out string payloadHash))
            {
                error = "provenance is missing";
                return false;
            }

            if (!string.Equals(transactionId, expectedTransactionId, StringComparison.Ordinal) ||
                !string.Equals(projectToken, expectedProjectToken, StringComparison.Ordinal) ||
                !string.Equals(payloadHash, expectedPayloadHash, StringComparison.Ordinal) ||
                !IsSha256(payloadHash))
            {
                error = "provenance identity does not match the journal";
                return false;
            }

            string actualPayloadHash = ComputePayloadHash(settings);
            if (!string.Equals(actualPayloadHash, payloadHash, StringComparison.Ordinal))
            {
                error = "payload hash does not match the generated settings content";
                return false;
            }

            return true;
        }

        private static string ComputePayloadHash(UnityLoggingSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            UnityLoggingSettings clone = UnityEngine.Object.Instantiate(settings);
            try
            {
                clone.ClearBuildOverrideProvenance();
                string json = EditorJsonUtility.ToJson(clone, false);
                return ComputeSha256(new UTF8Encoding(false).GetBytes(json));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        private static FileIdentity ComputeBoundedFileIdentity(
            string path,
            long maximumBytes,
            string description)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes)
            {
                throw new InvalidDataException(description + " is missing, empty, or exceeds its byte budget.");
            }

            using (var algorithm = SHA256.Create())
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       8192,
                       FileOptions.SequentialScan))
            {
                return new FileIdentity(info.Length, ToLowerHex(algorithm.ComputeHash(stream)));
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
            {
                return ToLowerHex(algorithm.ComputeHash(bytes));
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            const string hex = "0123456789abcdef";
            var characters = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                characters[i * 2] = hex[bytes[i] >> 4];
                characters[i * 2 + 1] = hex[bytes[i] & 0x0F];
            }

            return new string(characters);
        }

        private static string NormalizeCurrentProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A Unity project root is required.", nameof(projectRoot));
            }

            string normalized = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!PathsEqual(normalized, current))
            {
                throw new InvalidOperationException(
                    "LoggingSettings recovery may only target the Unity project open in this Editor.");
            }

            if (!Directory.Exists(Path.Combine(normalized, "Assets")) ||
                !Directory.Exists(Path.Combine(normalized, "ProjectSettings")))
            {
                throw new DirectoryNotFoundException("The requested path is not a Unity project root.");
            }

            FileAttributes rootAttributes = File.GetAttributes(normalized);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("A reparse-point Unity project root is not supported for recovery.");
            }

            return normalized;
        }

        private static string GetStateDirectory(string projectRoot)
        {
            string candidate = Path.GetFullPath(Path.Combine(
                projectRoot,
                StateDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            EnsurePathWithinRoot(projectRoot, candidate);
            return candidate;
        }

        private static string GetGeneratedAssetAbsolutePath(string projectRoot)
        {
            return AssetPathToAbsolutePath(projectRoot, GeneratedSettingsAssetPath);
        }

        private static string AssetPathToAbsolutePath(string projectRoot, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                (!string.Equals(assetPath, "Assets", StringComparison.Ordinal) &&
                 !assetPath.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Refusing to resolve a path outside Assets: " + assetPath);
            }

            string candidate = Path.GetFullPath(Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
            EnsurePathWithinRoot(projectRoot, candidate);
            return candidate;
        }

        private static void EnsureSafeStateDirectory(
            string projectRoot,
            string stateDirectory,
            bool createIfMissing)
        {
            EnsurePathWithinRoot(projectRoot, stateDirectory);
            EnsureNoReparsePointsBelowRoot(projectRoot, stateDirectory);
            if (createIfMissing && !Directory.Exists(stateDirectory))
            {
                Directory.CreateDirectory(stateDirectory);
                EnsureNoReparsePointsBelowRoot(projectRoot, stateDirectory);
            }

            if (Directory.Exists(stateDirectory) &&
                (File.GetAttributes(stateDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("LoggingSettings transaction directory must not be a reparse point.");
            }
        }

        private static void EnsureNoReparsePointsBelowRoot(string root, string candidate)
        {
            EnsurePathWithinRoot(root, candidate);
            string relative = candidate.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = root;
            if (string.IsNullOrEmpty(relative))
            {
                return;
            }

            string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    continue;
                }

                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("Recovery path contains a reparse point: " + current);
                }
            }
        }

        private static void EnsurePathWithinRoot(string root, string candidate)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = Path.GetFullPath(candidate);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(normalizedCandidate, normalizedRoot, comparison) &&
                !normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison) &&
                !normalizedCandidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, comparison))
            {
                throw new InvalidOperationException("Resolved path escapes the Unity project root.");
            }
        }

        private static FileStream AcquireExclusiveLock(string stateDirectory)
        {
            string lockPath = Path.Combine(stateDirectory, LockFileName);
            if (File.Exists(lockPath) || Directory.Exists(lockPath))
            {
                FileAttributes attributes = File.GetAttributes(lockPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                    (attributes & FileAttributes.Directory) != 0)
                {
                    throw new InvalidOperationException(
                        "The LoggingSettings transaction lock path is unsafe.");
                }
            }

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    "Another LoggingSettings build transaction or recovery operation is active.",
                    exception);
            }
        }

        private static void ThrowIfPendingEvidenceUnderLock(string projectRoot, string stateDirectory)
        {
            StateInventory inventory = ReadStateInventory(stateDirectory);
            if (inventory.HasJournalCandidates)
            {
                throw new InvalidOperationException(
                    "A pending LoggingSettings transaction requires explicit recovery before building.");
            }

            if (DoesGeneratedAssetExist(projectRoot))
            {
                throw new InvalidOperationException(
                    "The generated LoggingSettings override path is occupied without transaction evidence.");
            }
        }

        private static bool DoesGeneratedAssetExist(string projectRoot)
        {
            string absolutePath = GetGeneratedAssetAbsolutePath(projectRoot);
            if (File.Exists(absolutePath) || File.Exists(absolutePath + ".meta") || Directory.Exists(absolutePath))
            {
                return true;
            }

            return false;
        }

        private static bool IsAllowedGeneratedFolder(string assetPath)
        {
            return string.Equals(assetPath, GeneratedContainerFolderPath, StringComparison.Ordinal) ||
                   string.Equals(assetPath, GeneratedSettingsFolderPath, StringComparison.Ordinal) ||
                   string.Equals(assetPath, GeneratedResourcesFolderPath, StringComparison.Ordinal) ||
                   string.Equals(assetPath, GeneratedRootFolderPath, StringComparison.Ordinal);
        }

        private static string BuildStagingFolderAssetPath(
            string finalAssetPath,
            string transactionId,
            int folderIndex)
        {
            int separatorIndex = finalAssetPath.LastIndexOf('/');
            if (separatorIndex <= 0)
            {
                throw new InvalidDataException("A generated folder path has no valid parent.");
            }

            return finalAssetPath.Substring(0, separatorIndex + 1) +
                   StagingFolderNamePrefix +
                   transactionId +
                   "_" +
                   folderIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static string GetAssetName(string assetPath)
        {
            int separatorIndex = assetPath.LastIndexOf('/');
            if (separatorIndex < 0 || separatorIndex == assetPath.Length - 1)
            {
                throw new InvalidDataException("An asset path has no valid name.");
            }

            return assetPath.Substring(separatorIndex + 1);
        }

        private static bool IsDirectoryEmptyBounded(string directory, int maximumEntries)
        {
            int count = 0;
            foreach (string unused in Directory.EnumerateFileSystemEntries(directory))
            {
                count++;
                if (count > maximumEntries)
                {
                    return false;
                }
            }

            return count == 0;
        }

        private static bool IsKnownStateFileName(string fileName)
        {
            for (int i = 0; i < KnownStateFileNames.Length; i++)
            {
                if (string.Equals(fileName, KnownStateFileNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidUnityGuid(string value)
        {
            return value != null && value.Length == 32 && IsHex(value);
        }

        private static bool IsSha256(string value)
        {
            return value != null && value.Length == 64 && IsHex(value);
        }

        private static bool IsHex(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool valid = character >= '0' && character <= '9' ||
                             character >= 'a' && character <= 'f' ||
                             character >= 'A' && character <= 'F';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(
                first,
                second,
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }

        private static void InvokeFolderCheckpoint(
            string finalAssetPath,
            string stagingAssetPath,
            LoggingSettingsBuildFolderCheckpoint checkpoint)
        {
            FolderCheckpointForTests?.Invoke(finalAssetPath, stagingAssetPath, checkpoint);
        }

        private static void EnsureSameTransaction(
            LoggingSettingsBuildJournal expected,
            LoggingSettingsBuildJournal actual)
        {
            if (!string.Equals(expected.transactionId, actual.transactionId, StringComparison.Ordinal) ||
                !string.Equals(expected.projectToken, actual.projectToken, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The active LoggingSettings journal identity changed unexpectedly.");
            }
        }

        private static void DeleteKnownJournalFiles(string stateDirectory)
        {
            DeleteFileIfPresent(Path.Combine(stateDirectory, JournalFileName));
            DeleteFileIfPresent(Path.Combine(stateDirectory, TemporaryJournalFileName));
            DeleteFileIfPresent(Path.Combine(stateDirectory, BackupJournalFileName));
            DeleteFileIfPresent(Path.Combine(stateDirectory, RecoveryJournalFileName));
        }

        private static void DeleteKnownStateFiles(string stateDirectory)
        {
            DeleteKnownJournalFiles(stateDirectory);
        }

        private static void DeleteFileIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void DeleteDirectoryIfEmpty(string directory)
        {
            if (Directory.Exists(directory) && IsDirectoryEmptyBounded(directory, 1))
            {
                Directory.Delete(directory, false);
            }
        }

        private void EnsureLockHeld()
        {
            if (_lockStream == null)
            {
                throw new ObjectDisposedException(nameof(LoggingSettingsBuildOverrideTransaction));
            }
        }

        private void ReleaseLock()
        {
            if (_lockStream == null)
            {
                return;
            }

            _lockStream.Dispose();
            _lockStream = null;
        }

        [Serializable]
        private sealed class LoggingSettingsBuildJournal
        {
            public int schemaVersion;
            public int revision;
            public string transactionId;
            public string projectToken;
            public string phase;
            public string assetPath;
            public string payloadSha256;
            public string assetGuid;
            public string assetSha256;
            public long assetBytes;
            public LoggingSettingsBuildFolderRecord[] createdFolders;
        }

        [Serializable]
        private sealed class LoggingSettingsBuildFolderRecord
        {
            public string assetPath;
            public string stagingAssetPath;
            public string phase;
            public string assetGuid;
        }

        private readonly struct FileIdentity
        {
            public FileIdentity(long bytes, string sha256)
            {
                Bytes = bytes;
                Sha256 = sha256;
            }

            public long Bytes { get; }
            public string Sha256 { get; }
        }

        private readonly struct JournalCandidate
        {
            public JournalCandidate(string path, string json, string error)
            {
                Path = path;
                Json = json;
                Error = error;
            }

            public string Path { get; }
            public string Json { get; }
            public string Error { get; }
        }

        private readonly struct StateInventory
        {
            public StateInventory(IReadOnlyList<JournalCandidate> journalCandidates, bool hasLockFile)
            {
                JournalCandidates = journalCandidates;
                HasLockFile = hasLockFile;
            }

            public IReadOnlyList<JournalCandidate> JournalCandidates { get; }
            public bool HasLockFile { get; }
            public bool HasJournalCandidates => JournalCandidates != null && JournalCandidates.Count > 0;
        }
    }
}
#endif
