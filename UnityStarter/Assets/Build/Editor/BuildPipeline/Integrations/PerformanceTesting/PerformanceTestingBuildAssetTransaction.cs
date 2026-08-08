using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace Build.Pipeline.Editor
{
    public enum PerformanceTestingBuildAssetReadinessStatus
    {
        Clean,
        RecoveryRequired,
        Blocked
    }

    public sealed class PerformanceTestingBuildAssetReadiness
    {
        internal PerformanceTestingBuildAssetReadiness(
            PerformanceTestingBuildAssetReadinessStatus status,
            bool canRecover,
            string message,
            string evidencePath)
        {
            Status = status;
            CanRecover = canRecover;
            Message = message ?? string.Empty;
            EvidencePath = evidencePath ?? string.Empty;
        }

        public PerformanceTestingBuildAssetReadinessStatus Status { get; }
        public bool CanRecover { get; }
        public string Message { get; }
        public string EvidencePath { get; }
    }

    internal enum PerformanceTestingPackageGateStatus
    {
        Missing,
        Supported,
        Unsupported
    }

    internal readonly struct PerformanceTestingPackageGateResult
    {
        public PerformanceTestingPackageGateResult(
            PerformanceTestingPackageGateStatus status,
            string version,
            string message)
        {
            Status = status;
            Version = version ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public PerformanceTestingPackageGateStatus Status { get; }
        public string Version { get; }
        public string Message { get; }
    }

    internal static class PerformanceTestingPackageGate
    {
        internal const string PackageName = "com.unity.test-framework.performance";

        public static PerformanceTestingPackageGateResult InspectInstalledPackage()
        {
            PackageManagerInfo[] packages = PackageManagerInfo.GetAllRegisteredPackages();
            PackageManagerInfo package = packages?.SingleOrDefault(candidate =>
                string.Equals(candidate.name, PackageName, StringComparison.Ordinal));
            return EvaluateVersion(package?.version);
        }

        internal static PerformanceTestingPackageGateResult EvaluateVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
            {
                return new PerformanceTestingPackageGateResult(
                    PerformanceTestingPackageGateStatus.Missing,
                    string.Empty,
                    "Unity Performance Testing is not installed; its build-asset guard is inactive.");
            }

            string coreVersion = version;
            int suffixIndex = coreVersion.IndexOfAny(new[] { '-', '+' });
            if (suffixIndex >= 0)
            {
                coreVersion = coreVersion.Substring(0, suffixIndex);
            }

            string[] segments = coreVersion.Split('.');
            if (segments.Length == 3
                && int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
                && int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
                && int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out _)
                && major == 3
                && minor == 5)
            {
                return new PerformanceTestingPackageGateResult(
                    PerformanceTestingPackageGateStatus.Supported,
                    version,
                    $"Unity Performance Testing {version} uses the guarded 3.5.x build-asset contract.");
            }

            return new PerformanceTestingPackageGateResult(
                PerformanceTestingPackageGateStatus.Unsupported,
                version,
                $"Installed Unity Performance Testing version '{version}' is outside the audited 3.5.x range. " +
                "The build is blocked until this guard is reviewed for that package version.");
        }
    }

    internal interface IPerformanceTestingPreferenceStore
    {
        bool HasKey(string key);
        bool GetBool(string key);
        void SetBool(string key, bool value);
        void DeleteKey(string key);
    }

    internal sealed class EditorPerformanceTestingPreferenceStore : IPerformanceTestingPreferenceStore
    {
        public bool HasKey(string key) => EditorPrefs.HasKey(key);
        public bool GetBool(string key) => EditorPrefs.GetBool(key);
        public void SetBool(string key, bool value) => EditorPrefs.SetBool(key, value);
        public void DeleteKey(string key) => EditorPrefs.DeleteKey(key);
    }

    internal static class PerformanceTestingBuildAssetBuildSession
    {
        public static bool OwnsCurrentBuild { get; private set; }

        public static void Reset()
        {
            OwnsCurrentBuild = false;
        }

        public static void MarkStarted()
        {
            OwnsCurrentBuild = true;
        }
    }

    [BuildRecoveryRegistration(ParticipantId, 100)]
    public sealed class PerformanceTestingBuildAssetRecoveryParticipant : IBuildRecoveryParticipant
    {
        public const string ParticipantId = "PerformanceTestingBuildAssets";
        private static readonly string[] StatePaths =
        {
            PerformanceTestingBuildAssetTransaction.StateRelativePath
        };

        public string Id => ParticipantId;
        public int Priority => 100;
        public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

        public void Recover(string projectRoot)
        {
            PerformanceTestingBuildAssetTransaction.Recover(projectRoot);
        }
    }

    /// <summary>
    /// Runs before Unity Performance Testing 3.5.x creates its temporary Resources assets.
    /// </summary>
    public sealed class PerformanceTestingBuildAssetEarlyProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            PerformanceTestingBuildAssetBuildSession.Reset();
            PerformanceTestingPackageGateResult gate =
                PerformanceTestingPackageGate.InspectInstalledPackage();
            if (gate.Status == PerformanceTestingPackageGateStatus.Missing)
            {
                return;
            }

            if (gate.Status != PerformanceTestingPackageGateStatus.Supported)
            {
                throw new BuildFailedException(gate.Message);
            }

            try
            {
                PerformanceTestingBuildAssetTransaction.Begin(
                    PerformanceTestingBuildAssetTransaction.GetCurrentProjectRoot(),
                    gate.Version);
                PerformanceTestingBuildAssetBuildSession.MarkStarted();
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    "Unity Performance Testing build-asset protection could not start. " +
                    exception.Message);
            }
        }
    }

    /// <summary>
    /// Adopts the package-generated image after its order-zero preprocess callback,
    /// then restores the exact pre-build state after its order-zero cleanup callback.
    /// </summary>
    public sealed class PerformanceTestingBuildAssetLateProcessor :
        IPreprocessBuildWithReport,
        IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            PerformanceTestingPackageGateResult gate =
                PerformanceTestingPackageGate.InspectInstalledPackage();
            if (gate.Status == PerformanceTestingPackageGateStatus.Missing)
            {
                return;
            }

            if (gate.Status != PerformanceTestingPackageGateStatus.Supported)
            {
                throw new BuildFailedException(gate.Message);
            }

            if (!PerformanceTestingBuildAssetBuildSession.OwnsCurrentBuild)
            {
                throw new BuildFailedException(
                    "The Performance Testing early protection callback did not establish ownership for this build. " +
                    "Any durable transaction evidence requires explicit recovery before retrying.");
            }

            try
            {
                PerformanceTestingBuildAssetTransaction.AdoptGeneratedImage(
                    PerformanceTestingBuildAssetTransaction.GetCurrentProjectRoot());
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    "Unity Performance Testing generated an unsafe or unexpected build-asset image. " +
                    exception.Message);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!PerformanceTestingBuildAssetBuildSession.OwnsCurrentBuild)
            {
                return;
            }

            string projectRoot = PerformanceTestingBuildAssetTransaction.GetCurrentProjectRoot();
            if (!PerformanceTestingBuildAssetTransaction.HasPendingEvidence(projectRoot))
            {
                PerformanceTestingBuildAssetBuildSession.Reset();
                return;
            }

            try
            {
                PerformanceTestingBuildAssetTransaction.RestoreAndComplete(projectRoot);
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    "Unity Performance Testing build assets could not be restored safely. " +
                    "Durable recovery evidence was retained. " + exception.Message);
            }
            finally
            {
                PerformanceTestingBuildAssetBuildSession.Reset();
            }
        }
    }

    /// <summary>
    /// Protects the two temporary Resources JSON assets written and deleted by
    /// Unity Performance Testing 3.5.x. Normal builds never recover prior evidence;
    /// recovery is an explicit workspace operation.
    /// </summary>
    public static class PerformanceTestingBuildAssetTransaction
    {
        internal const string StateRelativePath =
            ".buildpipeline/transactions/performance-testing";
        internal const string CleanupPreferenceKey = "PT_ResourcesCleanup";

        private const int FormatVersion = 1;
        private const string PreparingPhase = "Preparing";
        private const string SnapshottedPhase = "Snapshotted";
        private const string ActivePhase = "Active";
        private const string AdoptedPhase = "Adopted";
        private const string RestoredPhase = "Restored";
        private const string JournalFileName = "active.json";
        private const string JournalTemporaryFileName = "active.json.tmp";
        private const string JournalBackupFileName = "active.json.bak";
        private const string LockFileName = "build.lock";
        private const string OwnerFileName = "transaction.owner";
        private const int MaximumJournalBytes = 256 * 1024;
        private const int MaximumTargetFileBytes = 1024 * 1024;
        private const int MaximumStateEntries = 16;
        private const int BufferSize = 64 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly string[] TargetRelativePaths =
        {
            "Assets/Resources/PerformanceTestRunInfo.json",
            "Assets/Resources/PerformanceTestRunInfo.json.meta",
            "Assets/Resources/PerformanceTestRunSettings.json",
            "Assets/Resources/PerformanceTestRunSettings.json.meta",
            "Assets/Resources.meta"
        };

        public static PerformanceTestingBuildAssetReadiness InspectReadiness(string projectRoot)
        {
            return InspectReadiness(
                projectRoot,
                new EditorPerformanceTestingPreferenceStore());
        }

        public static void Recover(string projectRoot)
        {
            RestoreAndComplete(
                projectRoot,
                new EditorPerformanceTestingPreferenceStore(),
                ShouldRefreshAssetDatabase(projectRoot));
        }

        internal static void Begin(string projectRoot, string packageVersion)
        {
            Begin(
                projectRoot,
                packageVersion,
                new EditorPerformanceTestingPreferenceStore(),
                ShouldRefreshAssetDatabase(projectRoot));
        }

        internal static void Begin(
            string projectRoot,
            string packageVersion,
            IPerformanceTestingPreferenceStore preferences,
            bool refreshAssets)
        {
            if (preferences == null)
            {
                throw new ArgumentNullException(nameof(preferences));
            }

            PerformanceTestingPackageGateResult gate =
                PerformanceTestingPackageGate.EvaluateVersion(packageVersion);
            if (gate.Status != PerformanceTestingPackageGateStatus.Supported)
            {
                throw new InvalidOperationException(gate.Message);
            }

            string root = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(root);
            ValidatePaths(root, stateRoot);
            Directory.CreateDirectory(stateRoot);
            EnsurePathHasNoReparsePoints(root, stateRoot, allowMissingLeaf: false);

            using (FileStream buildLock = AcquireLock(stateRoot))
            {
                EnsureNoPendingEvidence(stateRoot);

                string transactionId = Guid.NewGuid().ToString("N");
                bool cleanupPreferenceOriginallyExisted =
                    preferences.HasKey(CleanupPreferenceKey);
                var journal = new Journal
                {
                    formatVersion = FormatVersion,
                    transactionId = transactionId,
                    projectRoot = NormalizePortablePath(root),
                    packageVersion = packageVersion,
                    phase = PreparingPhase,
                    transactionDirectoryName = "transaction-" + transactionId,
                    resourcesDirectoryOriginallyExisted = Directory.Exists(GetResourcesDirectory(root)),
                    cleanupPreferenceOriginallyExisted = cleanupPreferenceOriginallyExisted,
                    cleanupPreferenceOriginalValue = cleanupPreferenceOriginallyExisted
                        && preferences.GetBool(CleanupPreferenceKey),
                    generatedResourcesGuid = Guid.NewGuid().ToString("N"),
                    records = CaptureOriginalRecords(root)
                };

                if (!journal.resourcesDirectoryOriginallyExisted
                    && journal.records[4].originalExisted)
                {
                    throw new InvalidOperationException(
                        "Assets/Resources.meta exists without Assets/Resources. " +
                        "Refusing to claim or replace an orphaned user asset meta file.");
                }

                journal.generatedResourcesMetaBase64 = Convert.ToBase64String(
                    CreateResourcesMetaBytes(journal.generatedResourcesGuid));
                journal.generatedResourcesMetaSha256 = ComputeSha256(
                    Convert.FromBase64String(journal.generatedResourcesMetaBase64));
                ValidateJournal(root, stateRoot, journal);

                string journalPath = GetJournalPath(stateRoot);
                WriteJournal(journalPath, journal, createNew: true);
                string transactionDirectory = GetTransactionDirectory(stateRoot, journal);
                Directory.CreateDirectory(transactionDirectory);
                WriteDurably(
                    Path.Combine(transactionDirectory, OwnerFileName),
                    StrictUtf8.GetBytes(transactionId + "\n"),
                    createNew: true);
                WriteOriginalSnapshots(root, transactionDirectory, journal);
                VerifyOriginalRecords(root, journal);
                journal.phase = SnapshottedPhase;
                WriteJournal(journalPath, journal, createNew: false);

                preferences.SetBool(CleanupPreferenceKey, false);
                EnsureResourcesDirectory(root, journal);
                if (refreshAssets)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }

                ValidateProtectedPrelude(root, journal, preferences);
                journal.phase = ActivePhase;
                WriteJournal(journalPath, journal, createNew: false);
            }
        }

        internal static void AdoptGeneratedImage(string projectRoot)
        {
            AdoptGeneratedImage(
                projectRoot,
                new EditorPerformanceTestingPreferenceStore());
        }

        internal static void AdoptGeneratedImage(
            string projectRoot,
            IPerformanceTestingPreferenceStore preferences)
        {
            if (preferences == null)
            {
                throw new ArgumentNullException(nameof(preferences));
            }

            string root = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(root);
            using (FileStream buildLock = AcquireExistingLock(stateRoot))
            {
                Journal journal = ReadRequiredJournal(root, stateRoot, readOnly: false);
                if (!string.Equals(journal.phase, ActivePhase, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Performance Testing transaction cannot adopt assets in phase '{journal.phase}'.");
                }

                ValidateProtectedPrelude(root, journal, preferences);
                for (int index = 0; index < 4; index++)
                {
                    string path = ResolveTargetPath(root, journal.records[index].relativePath);
                    FileIdentity identity = CaptureOptionalIdentity(path, "Performance Testing generated asset");
                    if ((index == 0 || index == 2) && !identity.exists)
                    {
                        throw new InvalidOperationException(
                            $"Expected Unity Performance Testing output is missing: '{path}'.");
                    }

                    journal.records[index].postImage = identity;
                }

                journal.phase = AdoptedPhase;
                WriteJournal(GetJournalPath(stateRoot), journal, createNew: false);
            }
        }

        internal static void RestoreAndComplete(string projectRoot)
        {
            RestoreAndComplete(
                projectRoot,
                new EditorPerformanceTestingPreferenceStore(),
                ShouldRefreshAssetDatabase(projectRoot));
        }

        internal static void RestoreAndComplete(
            string projectRoot,
            IPerformanceTestingPreferenceStore preferences,
            bool refreshAssets)
        {
            if (preferences == null)
            {
                throw new ArgumentNullException(nameof(preferences));
            }

            string root = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(root);
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            using (FileStream buildLock = AcquireExistingLock(stateRoot))
            {
                ReconcileJournalScratch(root, stateRoot);
                if (!File.Exists(GetJournalPath(stateRoot)))
                {
                    EnsureNoDetachedEvidence(stateRoot);
                    return;
                }

                Journal journal = ReadRequiredJournal(root, stateRoot, readOnly: false);
                if (string.Equals(journal.phase, PreparingPhase, StringComparison.Ordinal))
                {
                    VerifyOriginalRecords(root, journal);
                    VerifyOriginalPreference(preferences, journal);
                    DeleteTransactionDirectory(stateRoot, journal, allowIncomplete: true);
                    DeleteFileStrict(GetJournalPath(stateRoot));
                    EnsureNoDetachedEvidence(stateRoot);
                    return;
                }

                if (!string.Equals(journal.phase, RestoredPhase, StringComparison.Ordinal))
                {
                    ValidateRestorePreconditions(root, stateRoot, journal, preferences);
                    RestoreRecords(root, stateRoot, journal);
                    RestoreResourcesDirectory(root, journal);
                    RestorePreference(preferences, journal);
                    journal.phase = RestoredPhase;
                    WriteJournal(GetJournalPath(stateRoot), journal, createNew: false);
                }

                VerifyRestoredState(root, journal, preferences);
                if (refreshAssets)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    VerifyRestoredState(root, journal, preferences);
                }

                DeleteTransactionDirectory(stateRoot, journal, allowIncomplete: false);
                DeleteFileStrict(GetJournalPath(stateRoot));
                DeleteFileStrict(Path.Combine(stateRoot, JournalTemporaryFileName));
                DeleteFileStrict(Path.Combine(stateRoot, JournalBackupFileName));
                EnsureNoDetachedEvidence(stateRoot);
            }
        }

        internal static bool HasPendingEvidence(string projectRoot)
        {
            string root = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(root);
            if (!Directory.Exists(stateRoot))
            {
                return false;
            }

            return Directory.EnumerateFileSystemEntries(stateRoot)
                .Any(path => !string.Equals(
                    Path.GetFileName(path),
                    LockFileName,
                    StringComparison.Ordinal));
        }

        internal static string GetCurrentProjectRoot()
        {
            string assetsPath = Path.GetFullPath(Application.dataPath);
            DirectoryInfo parent = Directory.GetParent(assetsPath);
            if (parent == null)
            {
                throw new InvalidOperationException(
                    $"Unity Assets path has no project parent: '{assetsPath}'.");
            }

            return parent.FullName;
        }

        internal static PerformanceTestingBuildAssetReadiness InspectReadiness(
            string projectRoot,
            IPerformanceTestingPreferenceStore preferences)
        {
            string root;
            string stateRoot;
            try
            {
                root = NormalizeProjectRoot(projectRoot);
                stateRoot = GetStateRoot(root);
                if (!Directory.Exists(stateRoot))
                {
                    return CleanReadiness();
                }

                EnsurePathHasNoReparsePoints(root, stateRoot, allowMissingLeaf: false);
                if (!HasPendingEvidence(root))
                {
                    return CleanReadiness();
                }

                using (IDisposable buildLock = AcquireReadOnlyInspectionLock(stateRoot))
                {
                    Journal journal = ReadBestJournalCandidate(root, stateRoot);
                    ValidateReadOnlyInventory(stateRoot, journal);
                    ValidateRecoveryReadiness(root, stateRoot, journal, preferences);
                    return new PerformanceTestingBuildAssetReadiness(
                        PerformanceTestingBuildAssetReadinessStatus.RecoveryRequired,
                        true,
                        "A Performance Testing 3.5.x build-asset transaction requires explicit recovery.",
                        GetJournalEvidencePath(stateRoot));
                }
            }
            catch (Exception exception)
            {
                string evidence = string.Empty;
                try
                {
                    evidence = string.IsNullOrEmpty(projectRoot)
                        ? string.Empty
                        : GetStateRoot(Path.GetFullPath(projectRoot));
                }
                catch
                {
                    // Preserve the primary inspection failure.
                }

                return new PerformanceTestingBuildAssetReadiness(
                    PerformanceTestingBuildAssetReadinessStatus.Blocked,
                    false,
                    exception.Message,
                    evidence);
            }
        }

        private static PerformanceTestingBuildAssetReadiness CleanReadiness()
        {
            return new PerformanceTestingBuildAssetReadiness(
                PerformanceTestingBuildAssetReadinessStatus.Clean,
                false,
                "No Performance Testing build-asset recovery is pending.",
                string.Empty);
        }

        private static FileRecord[] CaptureOriginalRecords(string projectRoot)
        {
            var records = new FileRecord[TargetRelativePaths.Length];
            long totalBytes = 0;
            for (int index = 0; index < TargetRelativePaths.Length; index++)
            {
                string relativePath = TargetRelativePaths[index];
                string path = ResolveTargetPath(projectRoot, relativePath);
                FileIdentity identity = CaptureOptionalIdentity(
                    path,
                    "Performance Testing protected asset");
                totalBytes = checked(totalBytes + identity.length);
                if (totalBytes > MaximumTargetFileBytes * 4L)
                {
                    throw new IOException(
                        "Performance Testing protected assets exceed the aggregate snapshot budget.");
                }

                records[index] = new FileRecord
                {
                    relativePath = relativePath,
                    snapshotFileName = index.ToString("D2", CultureInfo.InvariantCulture) + ".snapshot",
                    originalExisted = identity.exists,
                    original = identity
                };
            }

            return records;
        }

        private static void WriteOriginalSnapshots(
            string projectRoot,
            string transactionDirectory,
            Journal journal)
        {
            for (int index = 0; index < journal.records.Length; index++)
            {
                FileRecord record = journal.records[index];
                if (!record.originalExisted)
                {
                    continue;
                }

                string sourcePath = ResolveTargetPath(projectRoot, record.relativePath);
                string snapshotPath = Path.Combine(transactionDirectory, record.snapshotFileName);
                byte[] bytes = ReadBoundedFile(
                    sourcePath,
                    MaximumTargetFileBytes,
                    "Performance Testing protected asset");
                if (bytes.LongLength != record.original.length
                    || !FixedTimeEquals(ComputeSha256(bytes), record.original.sha256))
                {
                    throw new IOException(
                        $"Protected asset changed while it was snapshotted: '{sourcePath}'.");
                }

                WriteDurably(snapshotPath, bytes, createNew: true);
                VerifySnapshot(snapshotPath, record);
            }
        }

        private static void EnsureResourcesDirectory(string projectRoot, Journal journal)
        {
            string resourcesDirectory = GetResourcesDirectory(projectRoot);
            string resourcesMetaPath = resourcesDirectory + ".meta";
            if (journal.resourcesDirectoryOriginallyExisted)
            {
                EnsureDirectory(resourcesDirectory, "Assets/Resources");
                return;
            }

            if (Directory.Exists(resourcesDirectory))
            {
                throw new InvalidOperationException(
                    $"Assets/Resources appeared after the transaction snapshot: '{resourcesDirectory}'.");
            }

            if (File.Exists(resourcesMetaPath))
            {
                throw new InvalidOperationException(
                    $"Assets/Resources.meta appeared after the transaction snapshot: '{resourcesMetaPath}'.");
            }

            byte[] metaBytes = Convert.FromBase64String(journal.generatedResourcesMetaBase64);
            WriteDurably(resourcesMetaPath, metaBytes, createNew: true);
            Directory.CreateDirectory(resourcesDirectory);
            EnsureDirectory(resourcesDirectory, "transaction-created Assets/Resources");
            VerifyGeneratedResourcesMeta(resourcesMetaPath, journal);
        }

        private static void ValidateProtectedPrelude(
            string projectRoot,
            Journal journal,
            IPerformanceTestingPreferenceStore preferences)
        {
            if (!preferences.HasKey(CleanupPreferenceKey)
                || preferences.GetBool(CleanupPreferenceKey))
            {
                throw new InvalidOperationException(
                    $"EditorPrefs key '{CleanupPreferenceKey}' must remain false while the protected build is active.");
            }

            string resourcesDirectory = GetResourcesDirectory(projectRoot);
            EnsureDirectory(resourcesDirectory, "Assets/Resources");
            if (!journal.resourcesDirectoryOriginallyExisted)
            {
                VerifyGeneratedResourcesMeta(resourcesDirectory + ".meta", journal);
            }
            else
            {
                VerifyRecordUnchanged(projectRoot, journal.records[4]);
            }
        }

        private static void ValidateRestorePreconditions(
            string projectRoot,
            string stateRoot,
            Journal journal,
            IPerformanceTestingPreferenceStore preferences)
        {
            bool allowPostImage = string.Equals(
                journal.phase,
                AdoptedPhase,
                StringComparison.Ordinal);
            if (!allowPostImage
                && !string.Equals(journal.phase, SnapshottedPhase, StringComparison.Ordinal)
                && !string.Equals(journal.phase, ActivePhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unsupported Performance Testing recovery phase '{journal.phase}'.");
            }

            ValidatePreferenceForRestore(preferences, journal);
            for (int index = 0; index < 4; index++)
            {
                ValidateRecordForRestore(
                    projectRoot,
                    stateRoot,
                    journal,
                    journal.records[index],
                    allowPostImage);
            }

            string resourcesDirectory = GetResourcesDirectory(projectRoot);
            if (journal.resourcesDirectoryOriginallyExisted)
            {
                EnsureDirectory(resourcesDirectory, "original Assets/Resources");
                ValidateRecordForRestore(
                    projectRoot,
                    stateRoot,
                    journal,
                    journal.records[4],
                    allowPostImage: false);
            }
            else
            {
                ValidateTransactionCreatedResourcesDirectory(
                    projectRoot,
                    stateRoot,
                    journal);
            }
        }

        private static void ValidatePreferenceForRestore(
            IPerformanceTestingPreferenceStore preferences,
            Journal journal)
        {
            bool exists = preferences.HasKey(CleanupPreferenceKey);
            if (!exists)
            {
                if (journal.cleanupPreferenceOriginallyExisted)
                {
                    throw new InvalidOperationException(
                        $"EditorPrefs key '{CleanupPreferenceKey}' was removed by an unknown writer.");
                }

                return;
            }

            bool value = preferences.GetBool(CleanupPreferenceKey);
            bool matchesProtectedValue = !value;
            bool matchesOriginal = journal.cleanupPreferenceOriginallyExisted
                && value == journal.cleanupPreferenceOriginalValue;
            if (!matchesProtectedValue && !matchesOriginal)
            {
                throw new InvalidOperationException(
                    $"EditorPrefs key '{CleanupPreferenceKey}' has an unknown concurrent value.");
            }
        }

        private static void ValidateRecordForRestore(
            string projectRoot,
            string stateRoot,
            Journal journal,
            FileRecord record,
            bool allowPostImage)
        {
            string path = ResolveTargetPath(projectRoot, record.relativePath);
            EnsureRecordCurrentImageIsOwned(path, record, allowPostImage);

            string scratchPath = GetRestoreScratchPath(path, journal.transactionId);
            if (File.Exists(scratchPath))
            {
                if (!record.originalExisted)
                {
                    throw new InvalidOperationException(
                        $"Unexpected restore scratch exists for an originally absent asset: '{scratchPath}'.");
                }

                VerifyOriginalContent(scratchPath, record, "Performance Testing restore scratch");
            }

            if (record.originalExisted)
            {
                VerifySnapshot(
                    Path.Combine(GetTransactionDirectory(stateRoot, journal), record.snapshotFileName),
                    record);
            }
        }

        private static void ValidateTransactionCreatedResourcesDirectory(
            string projectRoot,
            string stateRoot,
            Journal journal)
        {
            string resourcesDirectory = GetResourcesDirectory(projectRoot);
            string resourcesMetaPath = resourcesDirectory + ".meta";
            if (!Directory.Exists(resourcesDirectory))
            {
                if (File.Exists(resourcesMetaPath))
                {
                    VerifyGeneratedResourcesMeta(resourcesMetaPath, journal);
                }

                return;
            }

            EnsureDirectory(resourcesDirectory, "transaction-created Assets/Resources");
            VerifyGeneratedResourcesMeta(resourcesMetaPath, journal);

            var allowed = new HashSet<string>(PathComparer);
            for (int index = 0; index < 4; index++)
            {
                string path = ResolveTargetPath(projectRoot, journal.records[index].relativePath);
                allowed.Add(Path.GetFullPath(path));
                allowed.Add(Path.GetFullPath(GetRestoreScratchPath(path, journal.transactionId)));
            }

            string[] entries = Directory.GetFileSystemEntries(
                resourcesDirectory,
                "*",
                SearchOption.TopDirectoryOnly);
            if (entries.Length > MaximumStateEntries)
            {
                throw new InvalidOperationException(
                    "Transaction-created Assets/Resources exceeds its guarded entry budget.");
            }

            foreach (string entry in entries)
            {
                if (!allowed.Contains(Path.GetFullPath(entry)))
                {
                    throw new InvalidOperationException(
                        $"Transaction-created Assets/Resources contains an unknown concurrent entry and will not be deleted: '{entry}'.");
                }

                RejectReparsePoint(entry, "Assets/Resources entry");
            }
        }

        private static void RestoreRecords(
            string projectRoot,
            string stateRoot,
            Journal journal)
        {
            int recordCount = journal.resourcesDirectoryOriginallyExisted ? 5 : 4;
            for (int index = 0; index < recordCount; index++)
            {
                RestoreRecord(projectRoot, stateRoot, journal, journal.records[index]);
            }
        }

        private static void RestoreRecord(
            string projectRoot,
            string stateRoot,
            Journal journal,
            FileRecord record)
        {
            string targetPath = ResolveTargetPath(projectRoot, record.relativePath);
            string scratchPath = GetRestoreScratchPath(targetPath, journal.transactionId);
            bool allowPostImage = string.Equals(
                journal.phase,
                AdoptedPhase,
                StringComparison.Ordinal);
            EnsureRecordCurrentImageIsOwned(targetPath, record, allowPostImage);
            if (!record.originalExisted)
            {
                DeleteFileStrict(targetPath);
                DeleteFileStrict(scratchPath);
                return;
            }

            FileIdentity current = CaptureOptionalIdentity(
                targetPath,
                "Performance Testing protected asset");
            if (current.exists && SameIdentity(current, record.original))
            {
                ApplyOriginalMetadata(targetPath, record.original);
                DeleteFileStrict(scratchPath);
                return;
            }

            string snapshotPath = Path.Combine(
                GetTransactionDirectory(stateRoot, journal),
                record.snapshotFileName);
            VerifySnapshot(snapshotPath, record);
            if (!File.Exists(scratchPath))
            {
                byte[] bytes = ReadBoundedFile(
                    snapshotPath,
                    MaximumTargetFileBytes,
                    "Performance Testing snapshot");
                WriteDurably(scratchPath, bytes, createNew: true);
            }

            VerifyOriginalContent(scratchPath, record, "Performance Testing restore scratch");
            DeleteFileStrict(targetPath);
            File.Move(scratchPath, targetPath);
            ApplyOriginalMetadata(targetPath, record.original);
            if (!SameIdentity(
                    CaptureOptionalIdentity(targetPath, "restored Performance Testing asset"),
                    record.original))
            {
                throw new IOException(
                    $"Protected asset does not match its original image after restoration: '{targetPath}'.");
            }
        }

        private static void RestoreResourcesDirectory(string projectRoot, Journal journal)
        {
            if (journal.resourcesDirectoryOriginallyExisted)
            {
                return;
            }

            string resourcesDirectory = GetResourcesDirectory(projectRoot);
            string resourcesMetaPath = resourcesDirectory + ".meta";
            if (Directory.Exists(resourcesDirectory))
            {
                if (Directory.EnumerateFileSystemEntries(resourcesDirectory).Any())
                {
                    throw new InvalidOperationException(
                        $"Transaction-created Resources directory is not empty and will not be deleted: '{resourcesDirectory}'.");
                }

                VerifyGeneratedResourcesMeta(resourcesMetaPath, journal);
                Directory.Delete(resourcesDirectory, recursive: false);
                if (Directory.Exists(resourcesDirectory))
                {
                    throw new IOException(
                        $"Transaction-created Resources directory still exists after deletion: '{resourcesDirectory}'.");
                }
            }

            if (File.Exists(resourcesMetaPath))
            {
                VerifyGeneratedResourcesMeta(resourcesMetaPath, journal);
                DeleteFileStrict(resourcesMetaPath);
            }
        }

        private static void RestorePreference(
            IPerformanceTestingPreferenceStore preferences,
            Journal journal)
        {
            ValidatePreferenceForRestore(preferences, journal);
            if (journal.cleanupPreferenceOriginallyExisted)
            {
                preferences.SetBool(
                    CleanupPreferenceKey,
                    journal.cleanupPreferenceOriginalValue);
            }
            else
            {
                preferences.DeleteKey(CleanupPreferenceKey);
            }
        }

        private static void VerifyRestoredState(
            string projectRoot,
            Journal journal,
            IPerformanceTestingPreferenceStore preferences)
        {
            VerifyOriginalRecords(projectRoot, journal);
            string resourcesDirectory = GetResourcesDirectory(projectRoot);
            if (Directory.Exists(resourcesDirectory)
                != journal.resourcesDirectoryOriginallyExisted)
            {
                throw new IOException(
                    "Assets/Resources directory existence was not restored exactly.");
            }

            VerifyOriginalPreference(preferences, journal);
            for (int index = 0; index < 5; index++)
            {
                string scratchPath = GetRestoreScratchPath(
                    ResolveTargetPath(projectRoot, journal.records[index].relativePath),
                    journal.transactionId);
                if (File.Exists(scratchPath))
                {
                    throw new IOException(
                        $"Performance Testing restore scratch remains after recovery: '{scratchPath}'.");
                }
            }
        }

        private static void VerifyOriginalRecords(string projectRoot, Journal journal)
        {
            foreach (FileRecord record in journal.records)
            {
                VerifyRecordUnchanged(projectRoot, record);
            }
        }

        private static void VerifyRecordUnchanged(string projectRoot, FileRecord record)
        {
            string path = ResolveTargetPath(projectRoot, record.relativePath);
            FileIdentity current = CaptureOptionalIdentity(path, "Performance Testing protected asset");
            if (current.exists != record.originalExisted
                || (current.exists && !SameIdentity(current, record.original)))
            {
                throw new IOException(
                    $"Protected asset does not match its original image: '{path}'.");
            }
        }

        private static void VerifyOriginalPreference(
            IPerformanceTestingPreferenceStore preferences,
            Journal journal)
        {
            bool exists = preferences.HasKey(CleanupPreferenceKey);
            if (exists != journal.cleanupPreferenceOriginallyExisted
                || (exists
                    && preferences.GetBool(CleanupPreferenceKey)
                    != journal.cleanupPreferenceOriginalValue))
            {
                throw new IOException(
                    $"EditorPrefs key '{CleanupPreferenceKey}' was not restored exactly.");
            }
        }

        private static void ValidateRecoveryReadiness(
            string projectRoot,
            string stateRoot,
            Journal journal,
            IPerformanceTestingPreferenceStore preferences)
        {
            if (string.Equals(journal.phase, PreparingPhase, StringComparison.Ordinal))
            {
                VerifyOriginalRecords(projectRoot, journal);
                VerifyOriginalPreference(preferences, journal);
                return;
            }

            if (string.Equals(journal.phase, RestoredPhase, StringComparison.Ordinal))
            {
                VerifyRestoredState(projectRoot, journal, preferences);
                return;
            }

            ValidateRestorePreconditions(
                projectRoot,
                stateRoot,
                journal,
                preferences);
        }

        private static void ValidateReadOnlyInventory(string stateRoot, Journal journal)
        {
            string[] entries = Directory.GetFileSystemEntries(
                stateRoot,
                "*",
                SearchOption.TopDirectoryOnly);
            if (entries.Length > MaximumStateEntries)
            {
                throw new InvalidDataException(
                    "Performance Testing transaction state exceeds its entry budget.");
            }

            string transactionDirectory = GetTransactionDirectory(stateRoot, journal);
            foreach (string entry in entries)
            {
                RejectReparsePoint(entry, "Performance Testing transaction state entry");
                string fileName = Path.GetFileName(entry);
                bool known = string.Equals(fileName, LockFileName, StringComparison.Ordinal)
                    || string.Equals(fileName, JournalFileName, StringComparison.Ordinal)
                    || string.Equals(fileName, JournalTemporaryFileName, StringComparison.Ordinal)
                    || string.Equals(fileName, JournalBackupFileName, StringComparison.Ordinal)
                    || PathsEqual(entry, transactionDirectory);
                if (!known)
                {
                    throw new InvalidDataException(
                        $"Unknown Performance Testing transaction evidence: '{entry}'.");
                }
            }

            ValidateTransactionDirectoryInventory(
                stateRoot,
                journal,
                allowIncomplete: string.Equals(
                    journal.phase,
                    PreparingPhase,
                    StringComparison.Ordinal));
        }

        private static Journal ReadBestJournalCandidate(string projectRoot, string stateRoot)
        {
            string journalPath = GetJournalPath(stateRoot);
            string backupPath = Path.Combine(stateRoot, JournalBackupFileName);
            string temporaryPath = Path.Combine(stateRoot, JournalTemporaryFileName);
            if (File.Exists(journalPath))
            {
                return ReadAndValidateJournal(journalPath, projectRoot, stateRoot);
            }

            if (File.Exists(backupPath))
            {
                return ReadAndValidateJournal(backupPath, projectRoot, stateRoot);
            }

            if (File.Exists(temporaryPath))
            {
                return ReadAndValidateJournal(temporaryPath, projectRoot, stateRoot);
            }

            throw new InvalidDataException(
                $"Detached Performance Testing transaction evidence has no journal: '{stateRoot}'.");
        }

        private static Journal ReadRequiredJournal(
            string projectRoot,
            string stateRoot,
            bool readOnly)
        {
            if (!readOnly)
            {
                ReconcileJournalScratch(projectRoot, stateRoot);
            }

            string journalPath = GetJournalPath(stateRoot);
            if (!File.Exists(journalPath))
            {
                throw new InvalidDataException(
                    $"Performance Testing transaction journal is missing: '{journalPath}'.");
            }

            return ReadAndValidateJournal(journalPath, projectRoot, stateRoot);
        }

        private static void ReconcileJournalScratch(string projectRoot, string stateRoot)
        {
            string journalPath = GetJournalPath(stateRoot);
            string backupPath = Path.Combine(stateRoot, JournalBackupFileName);
            string temporaryPath = Path.Combine(stateRoot, JournalTemporaryFileName);
            if (File.Exists(journalPath))
            {
                ReadAndValidateJournal(journalPath, projectRoot, stateRoot);
                DeleteFileStrict(temporaryPath);
                DeleteFileStrict(backupPath);
                return;
            }

            if (File.Exists(backupPath))
            {
                ReadAndValidateJournal(backupPath, projectRoot, stateRoot);
                File.Move(backupPath, journalPath);
                DeleteFileStrict(temporaryPath);
                return;
            }

            if (File.Exists(temporaryPath))
            {
                ReadAndValidateJournal(temporaryPath, projectRoot, stateRoot);
                File.Move(temporaryPath, journalPath);
            }
        }

        private static Journal ReadAndValidateJournal(
            string path,
            string projectRoot,
            string stateRoot)
        {
            byte[] bytes = ReadBoundedFile(
                path,
                MaximumJournalBytes,
                "Performance Testing transaction journal");
            string text = DecodeStrictUtf8(bytes);
            JournalEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<JournalEnvelope>(text);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Performance Testing journal envelope is invalid: '{path}'.",
                    exception);
            }

            if (envelope == null
                || envelope.formatVersion != FormatVersion
                || string.IsNullOrEmpty(envelope.payloadBase64)
                || !IsSha256(envelope.sha256))
            {
                throw new InvalidDataException(
                    $"Performance Testing journal envelope is incomplete: '{path}'.");
            }

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(envelope.payloadBase64);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    $"Performance Testing journal payload is not valid Base64: '{path}'.",
                    exception);
            }

            if (!FixedTimeEquals(ComputeSha256(payload), envelope.sha256))
            {
                throw new InvalidDataException(
                    $"Performance Testing journal checksum does not match: '{path}'.");
            }

            Journal journal;
            try
            {
                journal = JsonUtility.FromJson<Journal>(DecodeStrictUtf8(payload));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Performance Testing journal payload is invalid: '{path}'.",
                    exception);
            }

            ValidateJournal(projectRoot, stateRoot, journal);
            return journal;
        }

        private static void ValidateJournal(
            string projectRoot,
            string stateRoot,
            Journal journal)
        {
            if (journal == null
                || journal.formatVersion != FormatVersion
                || !IsGuidN(journal.transactionId)
                || !string.Equals(
                    journal.projectRoot,
                    NormalizePortablePath(projectRoot),
                    PathComparison)
                || PerformanceTestingPackageGate.EvaluateVersion(journal.packageVersion).Status
                    != PerformanceTestingPackageGateStatus.Supported
                || !string.Equals(
                    journal.transactionDirectoryName,
                    "transaction-" + journal.transactionId,
                    StringComparison.Ordinal)
                || !IsKnownPhase(journal.phase)
                || journal.records == null
                || journal.records.Length != TargetRelativePaths.Length
                || !IsGuidN(journal.generatedResourcesGuid)
                || string.IsNullOrEmpty(journal.generatedResourcesMetaBase64)
                || !IsSha256(journal.generatedResourcesMetaSha256))
            {
                throw new InvalidDataException(
                    "Performance Testing transaction journal is invalid or unsupported.");
            }

            byte[] generatedMeta;
            try
            {
                generatedMeta = Convert.FromBase64String(journal.generatedResourcesMetaBase64);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    "Generated Resources meta snapshot is invalid Base64.",
                    exception);
            }

            if (generatedMeta.Length > 4096
                || !FixedTimeEquals(
                    ComputeSha256(generatedMeta),
                    journal.generatedResourcesMetaSha256)
                || !ByteArraysEqual(
                    generatedMeta,
                    CreateResourcesMetaBytes(journal.generatedResourcesGuid)))
            {
                throw new InvalidDataException(
                    "Generated Resources meta ownership proof is invalid.");
            }

            for (int index = 0; index < journal.records.Length; index++)
            {
                FileRecord record = journal.records[index];
                string expectedSnapshot = index.ToString("D2", CultureInfo.InvariantCulture)
                    + ".snapshot";
                if (record == null
                    || !string.Equals(
                        record.relativePath,
                        TargetRelativePaths[index],
                        StringComparison.Ordinal)
                    || !string.Equals(
                        record.snapshotFileName,
                        expectedSnapshot,
                        StringComparison.Ordinal)
                    || record.original == null
                    || record.original.exists != record.originalExisted
                    || !IsValidIdentity(record.original)
                    || (record.postImage != null && !IsValidIdentity(record.postImage)))
                {
                    throw new InvalidDataException(
                        $"Performance Testing file record {index} is invalid.");
                }

                ResolveTargetPath(projectRoot, record.relativePath);
            }

            GetTransactionDirectory(stateRoot, journal);
        }

        private static void WriteJournal(string path, Journal journal, bool createNew)
        {
            string payloadJson = JsonUtility.ToJson(journal, prettyPrint: false);
            byte[] payload = StrictUtf8.GetBytes(payloadJson);
            var envelope = new JournalEnvelope
            {
                formatVersion = FormatVersion,
                payloadBase64 = Convert.ToBase64String(payload),
                sha256 = ComputeSha256(payload)
            };
            byte[] bytes = StrictUtf8.GetBytes(JsonUtility.ToJson(envelope, prettyPrint: false));
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalBytes)
            {
                throw new IOException(
                    "Performance Testing transaction journal exceeds its byte budget.");
            }

            string temporaryPath = path + ".tmp";
            string backupPath = path + ".bak";
            if (createNew)
            {
                if (File.Exists(path) || File.Exists(temporaryPath) || File.Exists(backupPath))
                {
                    throw new InvalidOperationException(
                        $"Pending Performance Testing journal evidence exists: '{path}'.");
                }

                WriteDurably(path, bytes, createNew: true);
                return;
            }

            DeleteFileStrict(temporaryPath);
            WriteDurably(temporaryPath, bytes, createNew: true);
            DeleteFileStrict(backupPath);
            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path);
                return;
            }

            File.Replace(temporaryPath, path, backupPath);
            DeleteFileStrict(backupPath);
        }

        private static void EnsureNoPendingEvidence(string stateRoot)
        {
            string[] evidence = Directory.GetFileSystemEntries(stateRoot)
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    LockFileName,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (evidence.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Pending Performance Testing recovery must be completed explicitly before another build: '{evidence[0]}'.");
            }
        }

        private static void EnsureNoDetachedEvidence(string stateRoot)
        {
            EnsureNoPendingEvidence(stateRoot);
        }

        private static void DeleteTransactionDirectory(
            string stateRoot,
            Journal journal,
            bool allowIncomplete)
        {
            string transactionDirectory = GetTransactionDirectory(stateRoot, journal);
            if (!Directory.Exists(transactionDirectory))
            {
                if (allowIncomplete)
                {
                    return;
                }

                throw new DirectoryNotFoundException(
                    $"Performance Testing transaction snapshot directory is missing: '{transactionDirectory}'.");
            }

            ValidateTransactionDirectoryInventory(stateRoot, journal, allowIncomplete);
            foreach (string entry in Directory.GetFileSystemEntries(transactionDirectory))
            {
                DeleteFileStrict(entry);
            }

            Directory.Delete(transactionDirectory, recursive: false);
            if (Directory.Exists(transactionDirectory))
            {
                throw new IOException(
                    $"Performance Testing transaction directory still exists after deletion: '{transactionDirectory}'.");
            }
        }

        private static void ValidateTransactionDirectoryInventory(
            string stateRoot,
            Journal journal,
            bool allowIncomplete)
        {
            string transactionDirectory = GetTransactionDirectory(stateRoot, journal);
            if (!Directory.Exists(transactionDirectory))
            {
                if (allowIncomplete)
                {
                    return;
                }

                throw new DirectoryNotFoundException(
                    $"Performance Testing transaction directory is missing: '{transactionDirectory}'.");
            }

            EnsurePathHasNoReparsePoints(stateRoot, transactionDirectory, allowMissingLeaf: false);
            string[] entries = Directory.GetFileSystemEntries(transactionDirectory);
            if (entries.Length > TargetRelativePaths.Length + 1)
            {
                throw new InvalidDataException(
                    "Performance Testing transaction directory exceeds its entry budget.");
            }

            var allowedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                OwnerFileName
            };
            foreach (FileRecord record in journal.records)
            {
                if (record.originalExisted)
                {
                    allowedNames.Add(record.snapshotFileName);
                }
            }

            foreach (string entry in entries)
            {
                RejectReparsePoint(entry, "Performance Testing transaction snapshot");
                if (Directory.Exists(entry)
                    || !allowedNames.Contains(Path.GetFileName(entry)))
                {
                    throw new InvalidDataException(
                        $"Unknown Performance Testing transaction snapshot entry: '{entry}'.");
                }
            }

            string ownerPath = Path.Combine(transactionDirectory, OwnerFileName);
            if (!File.Exists(ownerPath))
            {
                if (allowIncomplete)
                {
                    return;
                }

                throw new InvalidDataException(
                    $"Performance Testing transaction owner is missing: '{ownerPath}'.");
            }

            byte[] expectedOwner = StrictUtf8.GetBytes(journal.transactionId + "\n");
            byte[] actualOwner = ReadBoundedFile(ownerPath, 128, "Performance Testing transaction owner");
            if (!ByteArraysEqual(expectedOwner, actualOwner))
            {
                throw new InvalidDataException(
                    "Performance Testing transaction ownership anchor does not match its journal.");
            }

            if (!allowIncomplete)
            {
                foreach (FileRecord record in journal.records.Where(record => record.originalExisted))
                {
                    VerifySnapshot(
                        Path.Combine(transactionDirectory, record.snapshotFileName),
                        record);
                }
            }
        }

        private static FileIdentity CaptureOptionalIdentity(string path, string label)
        {
            if (!File.Exists(path))
            {
                if (Directory.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"{label} resolves to a directory: '{path}'.");
                }

                return new FileIdentity { exists = false };
            }

            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidOperationException(
                    $"{label} is not a regular file: '{path}'.");
            }

            byte[] bytes = ReadBoundedFile(path, MaximumTargetFileBytes, label);
            if (File.GetLastWriteTimeUtc(path) != lastWriteTimeUtc
                || File.GetAttributes(path) != attributes)
            {
                throw new IOException($"{label} changed while it was captured: '{path}'.");
            }

            return new FileIdentity
            {
                exists = true,
                length = bytes.LongLength,
                sha256 = ComputeSha256(bytes),
                lastWriteTimeUtcTicks = lastWriteTimeUtc.Ticks,
                attributes = (int)attributes
            };
        }

        private static void EnsureRecordCurrentImageIsOwned(
            string path,
            FileRecord record,
            bool allowPostImage)
        {
            FileIdentity current = CaptureOptionalIdentity(
                path,
                "Performance Testing protected asset");
            bool accepted = !current.exists
                || (record.originalExisted && SameIdentity(current, record.original))
                || (allowPostImage
                    && record.postImage != null
                    && record.postImage.exists
                    && SameIdentity(current, record.postImage));
            if (!accepted)
            {
                throw new InvalidOperationException(
                    $"Protected asset has an unknown concurrent image and will not be replaced or deleted: '{path}'.");
            }
        }

        private static void VerifySnapshot(string path, FileRecord record)
        {
            if (!record.originalExisted)
            {
                throw new InvalidOperationException(
                    "Originally absent records do not own snapshot files.");
            }

            VerifyOriginalContent(path, record, "Performance Testing snapshot");
        }

        private static void VerifyOriginalContent(string path, FileRecord record, string label)
        {
            byte[] bytes = ReadBoundedFile(path, MaximumTargetFileBytes, label);
            if (bytes.LongLength != record.original.length
                || !FixedTimeEquals(ComputeSha256(bytes), record.original.sha256))
            {
                throw new InvalidDataException(
                    $"{label} does not match its journal identity: '{path}'.");
            }
        }

        private static void VerifyGeneratedResourcesMeta(string path, Journal journal)
        {
            byte[] bytes = ReadBoundedFile(path, 4096, "transaction-created Resources meta");
            if (!FixedTimeEquals(ComputeSha256(bytes), journal.generatedResourcesMetaSha256)
                || !ByteArraysEqual(
                    bytes,
                    Convert.FromBase64String(journal.generatedResourcesMetaBase64)))
            {
                throw new InvalidOperationException(
                    $"Assets/Resources.meta no longer matches the transaction-owned GUID '{journal.generatedResourcesGuid}': '{path}'.");
            }
        }

        private static byte[] CreateResourcesMetaBytes(string guid)
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

        private static void ApplyOriginalMetadata(string path, FileIdentity original)
        {
            File.SetAttributes(path, (FileAttributes)original.attributes);
            File.SetLastWriteTimeUtc(
                path,
                new DateTime(original.lastWriteTimeUtcTicks, DateTimeKind.Utc));
        }

        private static byte[] ReadBoundedFile(string path, int maximumBytes, string label)
        {
            RejectReparsePoint(path, label);
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       BufferSize,
                       FileOptions.SequentialScan))
            {
                if (stream.Length < 0 || stream.Length > maximumBytes)
                {
                    throw new IOException(
                        $"{label} exceeds its {maximumBytes}-byte budget: '{path}'.");
                }

                var bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException(
                            $"{label} ended while it was read: '{path}'.");
                    }

                    offset += read;
                }

                if (stream.ReadByte() != -1)
                {
                    throw new IOException($"{label} grew while it was read: '{path}'.");
                }

                return bytes;
            }
        }

        private static void WriteDurably(string path, byte[] bytes, bool createNew)
        {
            string parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                throw new DirectoryNotFoundException(
                    $"Durable write parent directory is missing: '{parent}'.");
            }

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

        private static void DeleteFileStrict(string path)
        {
            if (!File.Exists(path))
            {
                if (Directory.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"Refusing to delete a directory as a transaction file: '{path}'.");
                }

                return;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to delete a non-regular transaction file: '{path}'.");
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
            if (File.Exists(path))
            {
                throw new IOException($"Transaction file still exists after deletion: '{path}'.");
            }
        }

        private static FileStream AcquireLock(string stateRoot)
        {
            return OpenLock(stateRoot, FileMode.OpenOrCreate);
        }

        private static FileStream AcquireExistingLock(string stateRoot)
        {
            if (!Directory.Exists(stateRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Performance Testing transaction state root is missing: '{stateRoot}'.");
            }

            return OpenLock(stateRoot, FileMode.OpenOrCreate);
        }

        private static IDisposable AcquireReadOnlyInspectionLock(string stateRoot)
        {
            string lockPath = Path.Combine(stateRoot, LockFileName);
            if (!File.Exists(lockPath))
            {
                return NoopDisposable.Instance;
            }

            RejectReparsePoint(lockPath, "Performance Testing transaction lock");
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None,
                    1,
                    FileOptions.SequentialScan);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    $"Another process owns the Performance Testing build-asset transaction lock: '{lockPath}'.",
                    exception);
            }
        }

        private static FileStream OpenLock(string stateRoot, FileMode mode)
        {
            string lockPath = Path.Combine(stateRoot, LockFileName);
            if (Directory.Exists(lockPath))
            {
                throw new InvalidOperationException(
                    $"Performance Testing transaction lock resolves to a directory: '{lockPath}'.");
            }

            if (File.Exists(lockPath))
            {
                RejectReparsePoint(lockPath, "Performance Testing transaction lock");
            }

            try
            {
                return new FileStream(
                    lockPath,
                    mode,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    $"Another process owns the Performance Testing build-asset transaction lock: '{lockPath}'.",
                    exception);
            }
        }

        private static void ValidatePaths(string projectRoot, string stateRoot)
        {
            EnsurePathHasNoReparsePoints(projectRoot, Path.Combine(projectRoot, "Assets"), false);
            EnsurePathHasNoReparsePoints(projectRoot, stateRoot, allowMissingLeaf: true);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                stateRoot,
                "Performance Testing transaction state root");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                GetJournalPath(stateRoot),
                "Performance Testing transaction journal",
                ".bak".Length);
            foreach (string relativePath in TargetRelativePaths)
            {
                BuildPathPolicy.EnsureWin32MaxPathBudget(
                    ResolveTargetPath(projectRoot, relativePath),
                    "Performance Testing protected asset",
                    64);
            }
        }

        private static string NormalizeProjectRoot(string projectRoot)
        {
            string root = Path.GetFullPath(
                    projectRoot ?? throw new ArgumentNullException(nameof(projectRoot)))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException(
                    $"Unity project root does not exist: '{root}'.");
            }

            string assetsPath = Path.Combine(root, "Assets");
            if (!Directory.Exists(assetsPath))
            {
                throw new DirectoryNotFoundException(
                    $"Unity project Assets directory does not exist: '{assetsPath}'.");
            }

            RejectReparsePoint(root, "Unity project root");
            RejectReparsePoint(assetsPath, "Unity Assets directory");
            return root;
        }

        private static string ResolveTargetPath(string projectRoot, string relativePath)
        {
            if (!TargetRelativePaths.Contains(relativePath, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unrecognized Performance Testing protected path: '{relativePath}'.");
            }

            string path = Path.GetFullPath(Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!BuildPathPolicy.IsStrictDescendant(projectRoot, path))
            {
                throw new InvalidDataException(
                    $"Performance Testing protected path escaped the project root: '{relativePath}'.");
            }

            EnsurePathHasNoReparsePoints(projectRoot, path, allowMissingLeaf: true);
            return path;
        }

        private static string GetStateRoot(string projectRoot)
        {
            string path = Path.GetFullPath(Path.Combine(
                projectRoot,
                StateRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!BuildPathPolicy.IsStrictDescendant(projectRoot, path))
            {
                throw new InvalidOperationException(
                    "Performance Testing transaction state root escaped the Unity project.");
            }

            return path;
        }

        private static string GetJournalPath(string stateRoot)
        {
            return Path.Combine(stateRoot, JournalFileName);
        }

        private static string GetJournalEvidencePath(string stateRoot)
        {
            string journalPath = GetJournalPath(stateRoot);
            if (File.Exists(journalPath))
            {
                return journalPath;
            }

            string backupPath = Path.Combine(stateRoot, JournalBackupFileName);
            return File.Exists(backupPath)
                ? backupPath
                : Path.Combine(stateRoot, JournalTemporaryFileName);
        }

        private static string GetTransactionDirectory(string stateRoot, Journal journal)
        {
            string path = Path.GetFullPath(Path.Combine(
                stateRoot,
                journal.transactionDirectoryName));
            if (!BuildPathPolicy.IsStrictDescendant(stateRoot, path))
            {
                throw new InvalidDataException(
                    "Performance Testing transaction directory escaped its state root.");
            }

            return path;
        }

        private static string GetResourcesDirectory(string projectRoot)
        {
            return Path.Combine(projectRoot, "Assets", "Resources");
        }

        private static string GetRestoreScratchPath(string targetPath, string transactionId)
        {
            return targetPath + ".bp-performance-" + transactionId + ".restore.tmp";
        }

        private static void EnsurePathHasNoReparsePoints(
            string root,
            string path,
            bool allowMissingLeaf)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path);
            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!PathsEqual(normalizedRoot, normalizedPath)
                && !normalizedPath.StartsWith(prefix, PathComparison))
            {
                throw new InvalidOperationException(
                    $"Guarded path escaped its root: '{normalizedPath}'.");
            }

            string current = normalizedRoot;
            RejectReparsePoint(current, "guarded path");
            if (PathsEqual(current, normalizedPath))
            {
                return;
            }

            string relative = normalizedPath.Substring(prefix.Length);
            string[] segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < segments.Length; index++)
            {
                current = Path.Combine(current, segments[index]);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    if (!allowMissingLeaf && index == segments.Length - 1)
                    {
                        throw new FileNotFoundException("Guarded path is missing.", current);
                    }

                    return;
                }

                RejectReparsePoint(current, "guarded path component");
            }
        }

        private static void RejectReparsePoint(string path, string label)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"{label} cannot be a symbolic link or reparse point: '{path}'.");
            }
        }

        private static void EnsureDirectory(string path, string label)
        {
            if (!Directory.Exists(path) || File.Exists(path))
            {
                throw new DirectoryNotFoundException($"{label} is missing: '{path}'.");
            }

            RejectReparsePoint(path, label);
        }

        private static bool ShouldRefreshAssetDatabase(string projectRoot)
        {
            try
            {
                return PathsEqual(NormalizeProjectRoot(projectRoot), GetCurrentProjectRoot());
            }
            catch
            {
                return false;
            }
        }

        private static bool IsKnownPhase(string phase)
        {
            return string.Equals(phase, PreparingPhase, StringComparison.Ordinal)
                || string.Equals(phase, SnapshottedPhase, StringComparison.Ordinal)
                || string.Equals(phase, ActivePhase, StringComparison.Ordinal)
                || string.Equals(phase, AdoptedPhase, StringComparison.Ordinal)
                || string.Equals(phase, RestoredPhase, StringComparison.Ordinal);
        }

        private static bool IsValidIdentity(FileIdentity identity)
        {
            if (identity == null)
            {
                return false;
            }

            if (!identity.exists)
            {
                return identity.length == 0
                    && string.IsNullOrEmpty(identity.sha256)
                    && identity.lastWriteTimeUtcTicks == 0
                    && identity.attributes == 0;
            }

            FileAttributes attributes = (FileAttributes)identity.attributes;
            return identity.length >= 0
                && identity.length <= MaximumTargetFileBytes
                && IsSha256(identity.sha256)
                && identity.lastWriteTimeUtcTicks > 0
                && (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }

        private static bool SameIdentity(FileIdentity left, FileIdentity right)
        {
            return left != null
                && right != null
                && left.exists == right.exists
                && (!left.exists
                    || (left.length == right.length
                        && left.lastWriteTimeUtcTicks == right.lastWriteTimeUtcTicks
                        && left.attributes == right.attributes
                        && FixedTimeEquals(left.sha256, right.sha256)));
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string DecodeStrictUtf8(byte[] bytes)
        {
            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
            {
                throw new InvalidDataException("Transaction JSON must be UTF-8 without BOM.");
            }

            return StrictUtf8.GetString(bytes);
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

            foreach (char character in value)
            {
                if (!((character >= '0' && character <= '9')
                      || (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                PathComparison);
        }

        private static string NormalizePortablePath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private static StringComparer PathComparer =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

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
            public string packageVersion;
            public string phase;
            public string transactionDirectoryName;
            public bool resourcesDirectoryOriginallyExisted;
            public bool cleanupPreferenceOriginallyExisted;
            public bool cleanupPreferenceOriginalValue;
            public string generatedResourcesGuid;
            public string generatedResourcesMetaBase64;
            public string generatedResourcesMetaSha256;
            public FileRecord[] records;
        }

        [Serializable]
        private sealed class FileRecord
        {
            public string relativePath;
            public string snapshotFileName;
            public bool originalExisted;
            public FileIdentity original;
            public FileIdentity postImage;
        }

        [Serializable]
        private sealed class FileIdentity
        {
            public bool exists;
            public long length;
            public string sha256;
            public long lastWriteTimeUtcTicks;
            public int attributes;
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new NoopDisposable();

            public void Dispose()
            {
            }
        }
    }
}
