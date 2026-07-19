#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using RuntimeLoggerSettings = CycloneGames.Logger.LoggerSettings;

[assembly: InternalsVisibleTo("CycloneGames.Logger.Tests.Editor")]

namespace CycloneGames.Logger.Editor
{
    internal sealed class LoggerSettingsBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        internal const string CanonicalSettingsAssetPath = "Assets/Resources/" + RuntimeLoggerSettings.SettingsResourcePath + ".asset";
        internal const string GeneratedSettingsAssetPath = "Assets/Generated/CycloneGames.Logger/Resources/CycloneGames.Logger/LoggerSettingsBuildOverride.asset";

        private const int MarkerSchemaVersion = 1;
        private const string GeneratedRootFolderPath = "Assets/Generated/CycloneGames.Logger";
        private const string GeneratedResourcesFolderPath = GeneratedRootFolderPath + "/Resources";
        private const string GeneratedSettingsFolderPath = GeneratedResourcesFolderPath + "/CycloneGames.Logger";
        private const string MarkerDirectoryRelativePath = "Library/CycloneGames.Logger";
        private const string MarkerFileName = "LoggerSettingsBuildOverride.marker.json";
        private const string MarkerPhasePrepared = "Prepared";
        private const string MarkerPhaseActive = "Active";
        private const string LogPrefix = "[CLogger Build]";
        private static readonly char[] PortableInvalidFileNameCharacters = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        public int callbackOrder => -850;

        [InitializeOnLoadMethod]
        private static void CleanupStaleBuildOverride()
        {
            try
            {
                CleanupTrackedOverride(false);
                EnsureGeneratedAssetPathIsClear();
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix} Stale override cleanup failed closed. No generated asset was deleted unless its marker identity matched exactly. {exception.Message}");
            }
        }

        [MenuItem("Tools/CycloneGames/Logger/Create Default LoggerSettings", priority = 100)]
        private static void CreateDefaultSettings()
        {
            var settings = EnsureCanonicalSettingsAsset();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            Debug.Log($"{LogPrefix} LoggerSettings is ready at {CanonicalSettingsAssetPath}.");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            CleanupTrackedOverride(false);
            EnsureGeneratedAssetPathIsClear();

            var options = LoggerBuildCommandLineOptions.Resolve();
            if (!options.HasOverrides)
            {
                ValidateCanonicalSettings();
                return;
            }

            CreateGeneratedBuildOverride(options);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            CleanupTrackedOverride(true);
            EnsureGeneratedAssetPathIsClear();
        }

        internal static string ComputeProjectIdentityForTests(string dataPath)
        {
            return ComputeProjectIdentity(dataPath);
        }

        internal static bool ValidateMarkerForTests(
            string json,
            string expectedProjectIdentity,
            string actualAssetGuid,
            out string error)
        {
            if (!TryDeserializeMarker(json, out var marker, out error))
            {
                return false;
            }

            return ValidateCompleteMarker(marker, expectedProjectIdentity, actualAssetGuid, out error);
        }

        internal static bool CanCleanupPreparedMarkerForTests(
            string json,
            string expectedProjectIdentity,
            bool generatedAssetExists,
            out string error)
        {
            if (!TryDeserializeMarker(json, out var marker, out error))
            {
                return false;
            }

            return CanCleanupPreparedMarker(marker, expectedProjectIdentity, generatedAssetExists, out error);
        }

        internal static bool ApplyOptionsForTests(
            RuntimeLoggerSettings settings,
            Func<string, string> environmentReader,
            string[] commandLineArgs)
        {
            var options = LoggerBuildCommandLineOptions.Resolve(environmentReader, commandLineArgs);
            options.ApplyTo(settings);
            ValidateSettings(settings);
            return options.HasOverrides;
        }

        private static void CreateGeneratedBuildOverride(LoggerBuildCommandLineOptions options)
        {
            EnsureGeneratedAssetPathIsClear();

            var marker = new LoggerSettingsBuildMarker
            {
                schemaVersion = MarkerSchemaVersion,
                transactionId = Guid.NewGuid().ToString("N"),
                projectIdentity = ComputeProjectIdentity(Application.dataPath),
                generatedAssetGuid = string.Empty,
                assetPath = GeneratedSettingsAssetPath,
                phase = MarkerPhasePrepared
            };

            SaveMarkerAtomic(marker);

            RuntimeLoggerSettings generatedSettings = null;
            bool assetCreated = false;
            try
            {
                generatedSettings = CloneCanonicalSettings();
                generatedSettings.name = "LoggerSettingsBuildOverride";
                options.ApplyTo(generatedSettings);
                ValidateSettings(generatedSettings);

                EnsureAssetFolder(GeneratedSettingsFolderPath);
                EnsureGeneratedAssetPathIsClear();
                AssetDatabase.CreateAsset(generatedSettings, GeneratedSettingsAssetPath);
                assetCreated = true;
                generatedSettings = null;

                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(GeneratedSettingsAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                string generatedGuid = AssetDatabase.AssetPathToGUID(GeneratedSettingsAssetPath);
                if (!IsValidAssetGuid(generatedGuid))
                {
                    throw new BuildFailedException($"{LogPrefix} Unity did not assign a valid GUID to the generated LoggerSettings override.");
                }

                marker.generatedAssetGuid = generatedGuid;
                marker.phase = MarkerPhaseActive;
                SaveMarkerAtomic(marker);

                var generatedAsset = AssetDatabase.LoadAssetAtPath<RuntimeLoggerSettings>(GeneratedSettingsAssetPath);
                if (generatedAsset == null)
                {
                    throw new BuildFailedException($"{LogPrefix} Generated LoggerSettings override could not be loaded after import.");
                }

                Debug.Log($"{LogPrefix} Generated isolated build override at {GeneratedSettingsAssetPath}: {options.Describe(generatedAsset)}");
            }
            catch (Exception exception)
            {
                if (generatedSettings != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatedSettings);
                }

                if (!assetCreated && !DoesAssetPathExist(GeneratedSettingsAssetPath))
                {
                    DeleteMarkerFileIfPresent();
                    CleanupGeneratedFolders();
                }

                if (exception is BuildFailedException)
                {
                    throw;
                }

                throw new BuildFailedException($"{LogPrefix} Failed to create the generated LoggerSettings build override: {exception.Message}");
            }
        }

        private static RuntimeLoggerSettings CloneCanonicalSettings()
        {
            var canonical = AssetDatabase.LoadAssetAtPath<RuntimeLoggerSettings>(CanonicalSettingsAssetPath);
            if (canonical != null)
            {
                return UnityEngine.Object.Instantiate(canonical);
            }

            if (DoesAssetPathExist(CanonicalSettingsAssetPath))
            {
                throw new BuildFailedException($"{LogPrefix} Canonical settings path exists but is not a LoggerSettings asset: {CanonicalSettingsAssetPath}");
            }

            return ScriptableObject.CreateInstance<RuntimeLoggerSettings>();
        }

        private static void ValidateCanonicalSettings()
        {
            RuntimeLoggerSettings settings = CloneCanonicalSettings();
            try
            {
                ValidateSettings(settings);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        private static RuntimeLoggerSettings EnsureCanonicalSettingsAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RuntimeLoggerSettings>(CanonicalSettingsAssetPath);
            if (settings != null)
            {
                return settings;
            }

            if (DoesAssetPathExist(CanonicalSettingsAssetPath))
            {
                throw new InvalidOperationException($"{LogPrefix} Canonical settings path exists but is not a LoggerSettings asset: {CanonicalSettingsAssetPath}");
            }

            EnsureAssetFolder(GetAssetDirectory(CanonicalSettingsAssetPath));
            settings = ScriptableObject.CreateInstance<RuntimeLoggerSettings>();
            AssetDatabase.CreateAsset(settings, CanonicalSettingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(CanonicalSettingsAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return settings;
        }

        private static void CleanupTrackedOverride(bool logResult)
        {
            string markerPath = GetMarkerAbsolutePath();
            if (!File.Exists(markerPath))
            {
                return;
            }

            LoggerSettingsBuildMarker marker;
            try
            {
                string json = File.ReadAllText(markerPath, Encoding.UTF8);
                if (!TryDeserializeMarker(json, out marker, out string parseError))
                {
                    throw new BuildFailedException($"{LogPrefix} Build override marker is invalid; cleanup was refused: {parseError}");
                }
            }
            catch (BuildFailedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BuildFailedException($"{LogPrefix} Build override marker could not be read; cleanup was refused: {exception.Message}");
            }

            string expectedProjectIdentity = ComputeProjectIdentity(Application.dataPath);
            bool generatedAssetExists = DoesAssetPathExist(GeneratedSettingsAssetPath);
            if (string.Equals(marker.phase, MarkerPhasePrepared, StringComparison.Ordinal))
            {
                if (!CanCleanupPreparedMarker(marker, expectedProjectIdentity, generatedAssetExists, out string preparedMarkerError))
                {
                    throw new BuildFailedException($"{LogPrefix} Prepared build override marker cleanup was refused: {preparedMarkerError}");
                }

                DeleteMarkerFileIfPresent();
                CleanupGeneratedFolders();
                AssetDatabase.SaveAssets();
                if (logResult)
                {
                    Debug.Log($"{LogPrefix} Removed an interrupted prepared build override marker because no generated asset existed.");
                }

                return;
            }

            string actualGuid = AssetDatabase.AssetPathToGUID(GeneratedSettingsAssetPath);
            if (!ValidateCompleteMarker(marker, expectedProjectIdentity, actualGuid, out string validationError))
            {
                throw new BuildFailedException($"{LogPrefix} Build override marker identity check failed; cleanup was refused: {validationError}");
            }

            if (!AssetDatabase.DeleteAsset(GeneratedSettingsAssetPath))
            {
                throw new BuildFailedException($"{LogPrefix} Unity refused to delete the identity-matched generated settings asset. The marker was preserved.");
            }

            if (DoesAssetPathExist(GeneratedSettingsAssetPath))
            {
                throw new BuildFailedException($"{LogPrefix} Generated settings asset still exists after AssetDatabase.DeleteAsset. The marker was preserved.");
            }

            DeleteMarkerFileIfPresent();
            CleanupGeneratedFolders();
            AssetDatabase.SaveAssets();

            if (logResult)
            {
                Debug.Log($"{LogPrefix} Removed the identity-matched generated LoggerSettings build override.");
            }
        }

        private static void EnsureGeneratedAssetPathIsClear()
        {
            if (!DoesAssetPathExist(GeneratedSettingsAssetPath))
            {
                return;
            }

            throw new BuildFailedException(
                $"{LogPrefix} Generated override path is already occupied without a verified active transaction: {GeneratedSettingsAssetPath}. " +
                "Cleanup was refused to protect the existing asset.");
        }

        private static bool TryDeserializeMarker(string json, out LoggerSettingsBuildMarker marker, out string error)
        {
            marker = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "the marker is empty";
                return false;
            }

            try
            {
                marker = JsonUtility.FromJson<LoggerSettingsBuildMarker>(json);
            }
            catch (Exception exception)
            {
                error = $"JSON parsing failed: {exception.Message}";
                return false;
            }

            if (marker == null)
            {
                error = "JSON did not contain a marker object";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool CanCleanupPreparedMarker(
            LoggerSettingsBuildMarker marker,
            string expectedProjectIdentity,
            bool generatedAssetExists,
            out string error)
        {
            if (marker.schemaVersion != MarkerSchemaVersion)
            {
                error = $"unsupported marker schema {marker.schemaVersion}";
                return false;
            }

            if (!Guid.TryParseExact(marker.transactionId, "N", out _))
            {
                error = "transactionId is missing or invalid";
                return false;
            }

            if (string.IsNullOrEmpty(marker.projectIdentity) ||
                !string.Equals(marker.projectIdentity, expectedProjectIdentity, StringComparison.Ordinal))
            {
                error = "project identity does not match this checkout";
                return false;
            }

            if (!string.Equals(marker.assetPath, GeneratedSettingsAssetPath, StringComparison.Ordinal))
            {
                error = "asset path does not match the Logger-owned generated path";
                return false;
            }

            if (!string.Equals(marker.phase, MarkerPhasePrepared, StringComparison.Ordinal))
            {
                error = $"marker phase is not {MarkerPhasePrepared}";
                return false;
            }

            if (!string.IsNullOrEmpty(marker.generatedAssetGuid))
            {
                error = "a prepared marker must not contain a generated asset GUID";
                return false;
            }

            if (generatedAssetExists)
            {
                error = "the generated asset path still exists, so ownership cannot be proven safely";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool ValidateCompleteMarker(
            LoggerSettingsBuildMarker marker,
            string expectedProjectIdentity,
            string actualAssetGuid,
            out string error)
        {
            if (marker.schemaVersion != MarkerSchemaVersion)
            {
                error = $"unsupported marker schema {marker.schemaVersion}";
                return false;
            }

            if (!Guid.TryParseExact(marker.transactionId, "N", out _))
            {
                error = "transactionId is missing or invalid";
                return false;
            }

            if (string.IsNullOrEmpty(marker.projectIdentity) ||
                !string.Equals(marker.projectIdentity, expectedProjectIdentity, StringComparison.Ordinal))
            {
                error = "project identity does not match this checkout";
                return false;
            }

            if (!string.Equals(marker.assetPath, GeneratedSettingsAssetPath, StringComparison.Ordinal))
            {
                error = "asset path does not match the Logger-owned generated path";
                return false;
            }

            if (!string.Equals(marker.phase, MarkerPhaseActive, StringComparison.Ordinal))
            {
                error = $"marker phase is not {MarkerPhaseActive}";
                return false;
            }

            if (!IsValidAssetGuid(marker.generatedAssetGuid))
            {
                error = "generated asset GUID is missing or invalid";
                return false;
            }

            if (!IsValidAssetGuid(actualAssetGuid) ||
                !string.Equals(marker.generatedAssetGuid, actualAssetGuid, StringComparison.OrdinalIgnoreCase))
            {
                error = "generated asset GUID does not match the asset currently at the tracked path";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static void SaveMarkerAtomic(LoggerSettingsBuildMarker marker)
        {
            string markerPath = GetMarkerAbsolutePath();
            string directory = Path.GetDirectoryName(markerPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException($"{LogPrefix} Marker directory could not be resolved.");
            }

            Directory.CreateDirectory(directory);
            string transactionSuffix = string.IsNullOrEmpty(marker.transactionId) ? Guid.NewGuid().ToString("N") : marker.transactionId;
            string temporaryPath = markerPath + "." + transactionSuffix + ".tmp";
            string backupPath = markerPath + "." + transactionSuffix + ".bak";

            try
            {
                string json = JsonUtility.ToJson(marker);
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(markerPath))
                {
                    File.Replace(temporaryPath, markerPath, backupPath, true);
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, markerPath);
                }
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }

        private static void DeleteMarkerFileIfPresent()
        {
            string markerPath = GetMarkerAbsolutePath();
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }

        private static string GetMarkerAbsolutePath()
        {
            return Path.Combine(GetProjectRoot(), MarkerDirectoryRelativePath, MarkerFileName);
        }

        private static string ComputeProjectIdentity(string dataPath)
        {
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                throw new ArgumentException("A project Assets path is required.", nameof(dataPath));
            }

            string normalizedPath = Path.GetFullPath(dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
            if (Path.DirectorySeparatorChar == '\\')
            {
                normalizedPath = normalizedPath.ToUpperInvariant();
            }

            byte[] pathBytes = Encoding.UTF8.GetBytes(normalizedPath);
            byte[] hash;
            using (var algorithm = SHA256.Create())
            {
                hash = algorithm.ComputeHash(pathBytes);
            }

            const string hex = "0123456789abcdef";
            var result = new char[hash.Length * 2];
            for (int i = 0; i < hash.Length; i++)
            {
                result[i * 2] = hex[hash[i] >> 4];
                result[i * 2 + 1] = hex[hash[i] & 0x0F];
            }

            return new string(result);
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) ||
                (!string.Equals(folderPath, "Assets", StringComparison.Ordinal) &&
                 !folderPath.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"{LogPrefix} Refusing to create a folder outside Assets: {folderPath}");
            }

            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string createdGuid = AssetDatabase.CreateFolder(current, segments[i]);
                    if (string.IsNullOrEmpty(createdGuid) || !AssetDatabase.IsValidFolder(next))
                    {
                        throw new InvalidOperationException($"{LogPrefix} Failed to create generated asset folder: {next}");
                    }
                }

                current = next;
            }
        }

        private static void CleanupGeneratedFolders()
        {
            CleanupEmptyLoggerOwnedFolder(GeneratedSettingsFolderPath);
            CleanupEmptyLoggerOwnedFolder(GeneratedResourcesFolderPath);
            CleanupEmptyLoggerOwnedFolder(GeneratedRootFolderPath);
        }

        private static void CleanupEmptyLoggerOwnedFolder(string assetFolderPath)
        {
            if (!IsLoggerOwnedGeneratedFolder(assetFolderPath) || !AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            string absolutePath = AssetPathToAbsolutePath(assetFolderPath);
            if (!Directory.Exists(absolutePath))
            {
                return;
            }

            using (var entries = Directory.EnumerateFileSystemEntries(absolutePath).GetEnumerator())
            {
                if (entries.MoveNext())
                {
                    return;
                }
            }

            if (!AssetDatabase.DeleteAsset(assetFolderPath))
            {
                Debug.LogWarning($"{LogPrefix} Unity did not remove empty Logger-owned folder: {assetFolderPath}");
            }
        }

        private static bool IsLoggerOwnedGeneratedFolder(string assetFolderPath)
        {
            return string.Equals(assetFolderPath, GeneratedSettingsFolderPath, StringComparison.Ordinal) ||
                   string.Equals(assetFolderPath, GeneratedResourcesFolderPath, StringComparison.Ordinal) ||
                   string.Equals(assetFolderPath, GeneratedRootFolderPath, StringComparison.Ordinal);
        }

        private static bool DoesAssetPathExist(string assetPath)
        {
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)) ||
                AssetDatabase.LoadMainAssetAtPath(assetPath) != null ||
                AssetDatabase.IsValidFolder(assetPath))
            {
                return true;
            }

            string absolutePath = AssetPathToAbsolutePath(assetPath);
            return File.Exists(absolutePath) || Directory.Exists(absolutePath);
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                (!string.Equals(assetPath, "Assets", StringComparison.Ordinal) &&
                 !assetPath.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"{LogPrefix} Refusing to resolve a path outside Assets: {assetPath}");
            }

            string projectRoot = GetProjectRoot();
            string candidate = Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsPathWithinRoot(candidate, projectRoot))
            {
                throw new InvalidOperationException($"{LogPrefix} Refusing to resolve a path outside the current project: {assetPath}");
            }

            return candidate;
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static bool IsPathWithinRoot(string candidate, string root)
        {
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = Path.GetFullPath(candidate);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return string.Equals(normalizedCandidate, normalizedRoot, comparison) ||
                   normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison) ||
                   normalizedCandidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, comparison);
        }

        private static bool IsValidAssetGuid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool isHex = character >= '0' && character <= '9' ||
                             character >= 'a' && character <= 'f' ||
                             character >= 'A' && character <= 'F';
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        internal static void ValidateSettings(RuntimeLoggerSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            ValidateDefinedEnum(settings.processing, nameof(settings.processing));
            ValidateDefinedEnum(settings.overflowPolicy, nameof(settings.overflowPolicy));
            ValidateDefinedEnum(settings.unityConsoleOverflowPolicy, nameof(settings.unityConsoleOverflowPolicy));
            ValidateDefinedEnum(settings.guaranteedLevel, nameof(settings.guaranteedLevel));
            ValidateDefinedEnum(settings.defaultLevel, nameof(settings.defaultLevel));
            ValidateDefinedEnum(settings.defaultFilter, nameof(settings.defaultFilter));
            ValidateDefinedEnum(settings.fileMaintenanceMode, nameof(settings.fileMaintenanceMode));
            ValidateDefinedEnum(settings.fileSourcePathMode, nameof(settings.fileSourcePathMode));

            if (settings.guaranteedLevel == LogLevel.None)
            {
                throw InvalidSettings(nameof(settings.guaranteedLevel), "must identify a logging severity");
            }

            try
            {
                LoggerProcessingOptions.CreateValidated(new LoggerProcessingOptions
                {
                    MaxQueuedMessages = settings.maxQueuedMessages,
                    MaxQueuedCharacters = settings.maxQueuedCharacters,
                    MaxMessageCharacters = settings.maxMessageCharacters,
                    MaxCategoryCharacters = settings.maxCategoryCharacters,
                    MaxSourcePathCharacters = settings.maxSourcePathCharacters,
                    MaxMemberNameCharacters = settings.maxMemberNameCharacters,
                    MaxFilterCategories = settings.maxFilterCategories,
                    MaxFilterCharacters = settings.maxFilterCharacters,
                    ReservedCriticalMessages = settings.reservedCriticalMessages,
                    ReservedCriticalCharacters = settings.reservedCriticalCharacters,
                    UnityConsoleMaxQueuedMessages = settings.unityConsoleMaxQueuedMessages,
                    UnityConsoleMaxQueuedCharacters = settings.unityConsoleMaxQueuedCharacters,
                    UnityConsoleOverflowPolicy = settings.unityConsoleOverflowPolicy,
                    ShutdownDrainTimeoutMs = settings.shutdownDrainTimeoutMs,
                    EnqueueBlockTimeoutMs = settings.enqueueBlockTimeoutMs,
                    MaintenanceIntervalMs = settings.maintenanceIntervalMs,
                    SinkFailureThreshold = settings.sinkFailureThreshold,
                    OverflowPolicy = settings.overflowPolicy,
                    CriticalLevel = settings.guaranteedLevel
                });
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                throw InvalidSettings("processing", exception.Message);
            }

            try
            {
                FileLoggerOptions.CreateValidated(new FileLoggerOptions
                {
                    MaintenanceMode = settings.fileMaintenanceMode,
                    MaxFileBytes = settings.maxFileBytes,
                    MaxArchiveFiles = settings.maxArchiveFiles,
                    FlushBatchSize = settings.fileFlushBatchSize,
                    FlushIntervalMs = settings.fileFlushIntervalMs,
                    DurableFlushOnFatal = settings.durableFlushOnFatal,
                    SourcePathMode = settings.fileSourcePathMode
                });
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                throw InvalidSettings("fileLogger", exception.Message);
            }

            ValidatePortableFileName(settings.fileName);
            if (!string.IsNullOrEmpty(settings.customFilePath))
            {
                ValidateCustomFilePath(settings.customFilePath);
            }

            if (settings.registerFileLogger &&
                !settings.usePersistentDataPath &&
                (!settings.allowCustomFilePath || string.IsNullOrWhiteSpace(settings.customFilePath)))
            {
                throw InvalidSettings(nameof(settings.customFilePath), "requires allowCustomFilePath and a value when the file logger does not use Application.persistentDataPath");
            }
        }

        private static void ValidateDefinedEnum<T>(T value, string fieldName)
            where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw InvalidSettings(fieldName, $"contains undefined {typeof(T).Name} value {value}");
            }
        }

        private static void ValidatePortableFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw InvalidSettings(nameof(RuntimeLoggerSettings.fileName), "must not be empty");
            }

            if (fileName == "." || fileName == ".." ||
                fileName.IndexOfAny(PortableInvalidFileNameCharacters) >= 0 ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                IsWindowsReservedFileName(fileName) ||
                fileName.EndsWith(".", StringComparison.Ordinal) ||
                fileName.EndsWith(" ", StringComparison.Ordinal))
            {
                throw InvalidSettings(nameof(RuntimeLoggerSettings.fileName), "must be a portable leaf file name without traversal or directory separators");
            }

            for (int i = 0; i < fileName.Length; i++)
            {
                if (char.IsControl(fileName[i]))
                {
                    throw InvalidSettings(nameof(RuntimeLoggerSettings.fileName), "must not contain control characters");
                }
            }
        }

        private static bool IsWindowsReservedFileName(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName).TrimEnd('.', ' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CLOCK$")
            {
                return true;
            }

            return stem.Length == 4 &&
                   (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                   stem[3] >= '1' &&
                   stem[3] <= '9';
        }

        private static void ValidateCustomFilePath(string customFilePath)
        {
            if (string.IsNullOrWhiteSpace(customFilePath))
            {
                throw InvalidSettings(nameof(RuntimeLoggerSettings.customFilePath), "must not contain only whitespace");
            }

            if (!Path.IsPathFullyQualified(customFilePath))
            {
                throw InvalidSettings(nameof(RuntimeLoggerSettings.customFilePath), "must be a rooted absolute path");
            }

            for (int i = 0; i < customFilePath.Length; i++)
            {
                if (char.IsControl(customFilePath[i]))
                {
                    throw InvalidSettings(nameof(RuntimeLoggerSettings.customFilePath), "must not contain control characters");
                }
            }

            string normalized = customFilePath.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "..")
                {
                    throw InvalidSettings(nameof(RuntimeLoggerSettings.customFilePath), "must not contain parent-directory traversal segments");
                }
            }
        }

        private static BuildFailedException InvalidSettings(string fieldName, string reason)
        {
            return new BuildFailedException($"{LogPrefix} Invalid LoggerSettings.{fieldName}: {reason}.");
        }

        private static string GetAssetDirectory(string assetPath)
        {
            int index = assetPath.LastIndexOf('/');
            return index < 0 ? string.Empty : assetPath.Substring(0, index);
        }

        [Serializable]
        private sealed class LoggerSettingsBuildMarker
        {
            public int schemaVersion;
            public string transactionId;
            public string projectIdentity;
            public string generatedAssetGuid;
            public string assetPath;
            public string phase;
        }

        private sealed class LoggerBuildCommandLineOptions
        {
            private LoggerBuildMode? _mode;
            private string _profilePath;
            private bool? _registerUnityLogger;
            private bool? _registerConsoleLogger;
            private bool? _registerFileLogger;
            private bool? _usePersistentDataPath;
            private string _fileName;
            private string _customFilePath;
            private bool _customFilePathSpecified;
            private LogLevel? _defaultLevel;
            private LogFilter? _defaultFilter;
            private RuntimeLoggerSettings.ProcessingMode? _processing;
            private int? _maxQueuedMessages;
            private int? _unityConsoleMaxQueuedMessages;
            private int? _shutdownDrainTimeoutMs;
            private LogQueueOverflowPolicy? _overflowPolicy;
            private LogLevel? _guaranteedLevel;

            public bool HasOverrides { get; private set; }

            public static LoggerBuildCommandLineOptions Resolve()
            {
                return Resolve(Environment.GetEnvironmentVariable, Environment.GetCommandLineArgs());
            }

            public static LoggerBuildCommandLineOptions Resolve(Func<string, string> environmentReader, string[] commandLineArgs)
            {
                if (environmentReader == null)
                {
                    throw new ArgumentNullException(nameof(environmentReader));
                }

                var options = new LoggerBuildCommandLineOptions();
                options.ApplyEnvironment(environmentReader);
                options.ApplyCommandLine(commandLineArgs ?? Array.Empty<string>());
                return options;
            }

            public void ApplyTo(RuntimeLoggerSettings settings)
            {
                if (settings == null)
                {
                    throw new ArgumentNullException(nameof(settings));
                }

                if (!string.IsNullOrEmpty(_profilePath))
                {
                    var profile = LoadProfile(_profilePath);
                    EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(profile), settings);
                }

                if (_mode.HasValue)
                {
                    ApplyMode(settings, _mode.Value);
                }

                if (_registerUnityLogger.HasValue)
                {
                    settings.registerUnityLogger = _registerUnityLogger.Value;
                }

                if (_registerConsoleLogger.HasValue)
                {
                    settings.registerConsoleLogger = _registerConsoleLogger.Value;
                }

                if (_registerFileLogger.HasValue)
                {
                    settings.registerFileLogger = _registerFileLogger.Value;
                }

                if (_usePersistentDataPath.HasValue)
                {
                    settings.usePersistentDataPath = _usePersistentDataPath.Value;
                }

                if (_fileName != null)
                {
                    settings.fileName = _fileName;
                }

                if (_customFilePathSpecified)
                {
                    settings.customFilePath = _customFilePath;
                    settings.allowCustomFilePath = !string.IsNullOrEmpty(_customFilePath);
                }

                if (_defaultLevel.HasValue)
                {
                    settings.defaultLevel = _defaultLevel.Value;
                }

                if (_defaultFilter.HasValue)
                {
                    settings.defaultFilter = _defaultFilter.Value;
                }

                if (_processing.HasValue)
                {
                    settings.processing = _processing.Value;
                }

                if (_maxQueuedMessages.HasValue)
                {
                    settings.maxQueuedMessages = _maxQueuedMessages.Value;
                }

                if (_unityConsoleMaxQueuedMessages.HasValue)
                {
                    settings.unityConsoleMaxQueuedMessages = _unityConsoleMaxQueuedMessages.Value;
                }

                if (_shutdownDrainTimeoutMs.HasValue)
                {
                    settings.shutdownDrainTimeoutMs = _shutdownDrainTimeoutMs.Value;
                }

                if (_overflowPolicy.HasValue)
                {
                    settings.overflowPolicy = _overflowPolicy.Value;
                }

                if (_guaranteedLevel.HasValue)
                {
                    settings.guaranteedLevel = _guaranteedLevel.Value;
                }
            }

            public string Describe(RuntimeLoggerSettings settings)
            {
                return $"Unity={settings.registerUnityLogger}, Console={settings.registerConsoleLogger}, File={settings.registerFileLogger}, Level={settings.defaultLevel}, FileName={settings.fileName}";
            }

            private void ApplyEnvironment(Func<string, string> environmentReader)
            {
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_SETTINGS", value => TrySetRequiredString(value, parsed => _profilePath = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_MODE", TrySetMode);
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_UNITY", value => TrySetBool(value, parsed => _registerUnityLogger = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_CONSOLE", value => TrySetBool(value, parsed => _registerConsoleLogger = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_FILE", value => TrySetBool(value, parsed => _registerFileLogger = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_USE_PERSISTENT_DATA_PATH", value => TrySetBool(value, parsed => _usePersistentDataPath = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_LEVEL", value => TrySetEnum<LogLevel>(value, parsed => _defaultLevel = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_FILTER", value => TrySetEnum<LogFilter>(value, parsed => _defaultFilter = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_PROCESSING", value => TrySetEnum<RuntimeLoggerSettings.ProcessingMode>(value, parsed => _processing = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_FILE_NAME", value => TrySetRequiredString(value, parsed => _fileName = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_CUSTOM_FILE_PATH", TrySetOptionalCustomPath);
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_MAX_QUEUED_MESSAGES", value => TrySetPositiveInt(value, parsed => _maxQueuedMessages = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_UNITY_CONSOLE_MAX_QUEUED_MESSAGES", value => TrySetPositiveInt(value, parsed => _unityConsoleMaxQueuedMessages = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_SHUTDOWN_DRAIN_TIMEOUT_MS", value => TrySetNonNegativeInt(value, parsed => _shutdownDrainTimeoutMs = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_OVERFLOW_POLICY", value => TrySetEnum<LogQueueOverflowPolicy>(value, parsed => _overflowPolicy = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGER_GUARANTEED_LEVEL", value => TrySetEnum<LogLevel>(value, parsed => _guaranteedLevel = parsed));
            }

            private void ApplyEnvironmentValue(Func<string, string> environmentReader, string key, Func<string, bool> parser)
            {
                string value = environmentReader(key);
                if (value == null)
                {
                    return;
                }

                if (!parser(value))
                {
                    throw new BuildFailedException($"{LogPrefix} Invalid explicit environment value for {key}.");
                }
            }

            private void ApplyCommandLine(string[] args)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (TryMatchValue(args, ref i, arg, "-loggerSettings", value => TrySetRequiredString(value, parsed => _profilePath = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerMode", TrySetMode))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerUnity", value => TrySetBool(value, parsed => _registerUnityLogger = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerConsole", value => TrySetBool(value, parsed => _registerConsoleLogger = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerFile", value => TrySetBool(value, parsed => _registerFileLogger = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerUsePersistentDataPath", value => TrySetBool(value, parsed => _usePersistentDataPath = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerFileName", value => TrySetRequiredString(value, parsed => _fileName = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerCustomFilePath", TrySetOptionalCustomPath))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerLevel", value => TrySetEnum<LogLevel>(value, parsed => _defaultLevel = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerFilter", value => TrySetEnum<LogFilter>(value, parsed => _defaultFilter = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerProcessing", value => TrySetEnum<RuntimeLoggerSettings.ProcessingMode>(value, parsed => _processing = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerMaxQueuedMessages", value => TrySetPositiveInt(value, parsed => _maxQueuedMessages = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerUnityConsoleMaxQueuedMessages", value => TrySetPositiveInt(value, parsed => _unityConsoleMaxQueuedMessages = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerShutdownDrainTimeoutMs", value => TrySetNonNegativeInt(value, parsed => _shutdownDrainTimeoutMs = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggerOverflowPolicy", value => TrySetEnum<LogQueueOverflowPolicy>(value, parsed => _overflowPolicy = parsed)))
                    {
                        continue;
                    }

                    TryMatchValue(args, ref i, arg, "-loggerGuaranteedLevel", value => TrySetEnum<LogLevel>(value, parsed => _guaranteedLevel = parsed));
                }
            }

            private static RuntimeLoggerSettings LoadProfile(string profilePath)
            {
                string assetPath = NormalizeProfileAssetPath(profilePath);
                if (string.Equals(assetPath, GeneratedSettingsAssetPath, StringComparison.Ordinal))
                {
                    throw new BuildFailedException($"{LogPrefix} The generated build override cannot be used as a source profile.");
                }

                var profile = AssetDatabase.LoadAssetAtPath<RuntimeLoggerSettings>(assetPath);
                if (profile == null)
                {
                    throw new BuildFailedException($"{LogPrefix} LoggerSettings profile not found or has the wrong type: {profilePath}");
                }

                return profile;
            }

            private static string NormalizeProfileAssetPath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new BuildFailedException($"{LogPrefix} LoggerSettings profile path must not be empty.");
                }

                try
                {
                    string projectRoot = GetProjectRoot();
                    string fullPath = Path.IsPathRooted(path)
                        ? Path.GetFullPath(path)
                        : Path.GetFullPath(Path.Combine(projectRoot, path));
                    if (!IsPathWithinRoot(fullPath, projectRoot))
                    {
                        throw new BuildFailedException($"{LogPrefix} LoggerSettings profile must be inside the current Unity project.");
                    }

                    string normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string relativePath = fullPath.Substring(normalizedRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    if (!relativePath.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        throw new BuildFailedException($"{LogPrefix} LoggerSettings profile must be an asset under Assets/.");
                    }

                    return relativePath;
                }
                catch (BuildFailedException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new BuildFailedException($"{LogPrefix} LoggerSettings profile path is invalid: {exception.Message}");
                }
            }

            private static void ApplyMode(RuntimeLoggerSettings settings, LoggerBuildMode mode)
            {
                switch (mode)
                {
                    case LoggerBuildMode.Settings:
                        break;
                    case LoggerBuildMode.Off:
                        settings.registerUnityLogger = false;
                        settings.registerConsoleLogger = false;
                        settings.registerFileLogger = false;
                        break;
                    case LoggerBuildMode.Unity:
                        settings.registerUnityLogger = true;
                        settings.registerConsoleLogger = false;
                        settings.registerFileLogger = false;
                        break;
                    case LoggerBuildMode.File:
                        settings.registerUnityLogger = false;
                        settings.registerConsoleLogger = false;
                        settings.registerFileLogger = true;
                        break;
                    case LoggerBuildMode.UnityAndFile:
                        settings.registerUnityLogger = true;
                        settings.registerConsoleLogger = false;
                        settings.registerFileLogger = true;
                        break;
                    default:
                        throw new BuildFailedException($"{LogPrefix} Undefined logger build mode: {mode}");
                }
            }

            private bool TrySetMode(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                string normalized = value.Replace("-", string.Empty).Replace("_", string.Empty);
                if (!Enum.TryParse(normalized, true, out LoggerBuildMode mode) ||
                    !Enum.IsDefined(typeof(LoggerBuildMode), mode))
                {
                    return false;
                }

                _mode = mode;
                if (mode != LoggerBuildMode.Settings)
                {
                    HasOverrides = true;
                }

                return true;
            }

            private bool TrySetRequiredString(string value, Action<string> setter)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                setter(value);
                HasOverrides = true;
                return true;
            }

            private bool TrySetOptionalCustomPath(string value)
            {
                if (value == null)
                {
                    return false;
                }

                _customFilePath = value;
                _customFilePathSpecified = true;
                HasOverrides = true;
                return true;
            }

            private bool TrySetBool(string value, Action<bool> setter)
            {
                if (!TryParseBool(value, out bool parsed))
                {
                    return false;
                }

                setter(parsed);
                HasOverrides = true;
                return true;
            }

            private bool TrySetPositiveInt(string value, Action<int> setter)
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
                {
                    return false;
                }

                setter(parsed);
                HasOverrides = true;
                return true;
            }

            private bool TrySetNonNegativeInt(string value, Action<int> setter)
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
                {
                    return false;
                }

                setter(parsed);
                HasOverrides = true;
                return true;
            }

            private bool TrySetEnum<T>(string value, Action<T> setter)
                where T : struct, Enum
            {
                if (!Enum.TryParse(value, true, out T parsed) || !Enum.IsDefined(typeof(T), parsed))
                {
                    return false;
                }

                setter(parsed);
                HasOverrides = true;
                return true;
            }

            private bool TryMatchValue(string[] args, ref int index, string arg, string expected, Func<string, bool> setter)
            {
                if (!string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (index + 1 >= args.Length)
                {
                    throw new BuildFailedException($"{LogPrefix} Missing value for {expected}.");
                }

                string value = args[index + 1];
                if (!setter(value))
                {
                    throw new BuildFailedException($"{LogPrefix} Invalid value for {expected}.");
                }

                index++;
                return true;
            }

            private static bool TryParseBool(string value, out bool parsed)
            {
                parsed = false;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                switch (value.Trim().ToLowerInvariant())
                {
                    case "1":
                    case "true":
                    case "yes":
                    case "on":
                    case "enable":
                    case "enabled":
                        parsed = true;
                        return true;
                    case "0":
                    case "false":
                    case "no":
                    case "off":
                    case "disable":
                    case "disabled":
                        parsed = false;
                        return true;
                    default:
                        return false;
                }
            }
        }

        private enum LoggerBuildMode
        {
            Settings = 0,
            Off = 1,
            Unity = 2,
            File = 3,
            UnityAndFile = 4
        }
    }
}
#endif
