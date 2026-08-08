#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using CycloneGames.Logging.Pipeline;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityLoggingSettings = CycloneGames.Logging.Unity.LoggingSettings;

[assembly: InternalsVisibleTo("CycloneGames.Logging.Unity.Tests.Editor")]

namespace CycloneGames.Logging.Unity.Editor
{
    internal sealed class LoggingSettingsBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        internal const string CanonicalSettingsAssetPath = "Assets/Resources/" + UnityLoggingSettings.SettingsResourcePath + ".asset";
        internal const string GeneratedSettingsAssetPath = LoggingSettingsBuildOverrideTransaction.GeneratedSettingsAssetPath;

        private const string LogPrefix = "[LogPipeline Build]";
        private static readonly char[] PortableInvalidFileNameCharacters = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        private static LoggingSettingsBuildOverrideTransaction _activeTransaction;

        public int callbackOrder => -850;

        [MenuItem("Tools/CycloneGames/Logging/Create Default Settings", priority = 100)]
        private static void CreateDefaultSettings()
        {
            var settings = EnsureCanonicalSettingsAsset();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            UnityConsoleOutput.Write(LogType.Log, $"{LogPrefix} LoggingSettings is ready at {CanonicalSettingsAssetPath}.");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (_activeTransaction != null)
            {
                throw new BuildFailedException($"{LogPrefix} A LoggingSettings build transaction is already active.");
            }

            string projectRoot = GetProjectRoot();
            try
            {
                LoggingSettingsBuildOverrideTransaction.ThrowIfPendingEvidence(projectRoot);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    $"{LogPrefix} Build cannot start until LoggingSettings recovery succeeds: {exception.Message}");
            }

            var options = LoggingBuildCommandLineOptions.Resolve();
            if (!options.HasOverrides)
            {
                ValidateCanonicalSettings();
                return;
            }

            _activeTransaction = CreateGeneratedBuildOverride(projectRoot, options);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            LoggingSettingsBuildOverrideTransaction transaction = _activeTransaction;
            _activeTransaction = null;
            if (transaction == null)
            {
                try
                {
                    LoggingSettingsBuildOverrideTransaction.ThrowIfPendingEvidence(GetProjectRoot());
                    return;
                }
                catch (OutOfMemoryException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new BuildFailedException(
                        $"{LogPrefix} Post-build verification found a pending LoggingSettings transaction: {exception.Message}");
                }
            }

            try
            {
                transaction.Complete();
                UnityConsoleOutput.Write(
                    LogType.Log,
                    $"{LogPrefix} Removed the verified generated LoggingSettings build override.");
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    $"{LogPrefix} Post-build LoggingSettings cleanup failed; recovery evidence was retained: {exception.Message}");
            }
            finally
            {
                transaction.Dispose();
            }
        }

        internal static bool ApplyOptionsForTests(
            UnityLoggingSettings settings,
            Func<string, string> environmentReader,
            string[] commandLineArgs)
        {
            var options = LoggingBuildCommandLineOptions.Resolve(environmentReader, commandLineArgs);
            options.ApplyTo(settings);
            ValidateSettings(settings);
            return options.HasOverrides;
        }

        private static LoggingSettingsBuildOverrideTransaction CreateGeneratedBuildOverride(
            string projectRoot,
            LoggingBuildCommandLineOptions options)
        {
            UnityLoggingSettings generatedSettings = null;
            try
            {
                generatedSettings = CloneCanonicalSettings();
                generatedSettings.name = "LoggingSettingsBuildOverride";
                options.ApplyTo(generatedSettings);
                ValidateSettings(generatedSettings);
                LoggingSettingsBuildOverrideTransaction transaction =
                    LoggingSettingsBuildOverrideTransaction.Begin(projectRoot, generatedSettings);
                UnityLoggingSettings generatedAsset =
                    AssetDatabase.LoadAssetAtPath<UnityLoggingSettings>(GeneratedSettingsAssetPath);
                UnityConsoleOutput.Write(
                    LogType.Log,
                    $"{LogPrefix} Generated isolated build override at {GeneratedSettingsAssetPath}: {options.Describe(generatedAsset)}");
                return transaction;
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (exception is BuildFailedException)
                {
                    throw;
                }

                throw new BuildFailedException($"{LogPrefix} Failed to create the generated LoggingSettings build override: {exception.Message}");
            }
            finally
            {
                if (generatedSettings != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatedSettings);
                }
            }
        }

        private static UnityLoggingSettings CloneCanonicalSettings()
        {
            var canonical = AssetDatabase.LoadAssetAtPath<UnityLoggingSettings>(CanonicalSettingsAssetPath);
            if (canonical != null)
            {
                return UnityEngine.Object.Instantiate(canonical);
            }

            if (DoesAssetPathExist(CanonicalSettingsAssetPath))
            {
                throw new BuildFailedException($"{LogPrefix} Canonical settings path exists but is not a LoggingSettings asset: {CanonicalSettingsAssetPath}");
            }

            return ScriptableObject.CreateInstance<UnityLoggingSettings>();
        }

        private static void ValidateCanonicalSettings()
        {
            UnityLoggingSettings settings = CloneCanonicalSettings();
            try
            {
                ValidateSettings(settings);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        private static UnityLoggingSettings EnsureCanonicalSettingsAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityLoggingSettings>(CanonicalSettingsAssetPath);
            if (settings != null)
            {
                return settings;
            }

            if (DoesAssetPathExist(CanonicalSettingsAssetPath))
            {
                throw new InvalidOperationException($"{LogPrefix} Canonical settings path exists but is not a LoggingSettings asset: {CanonicalSettingsAssetPath}");
            }

            EnsureAssetFolder(GetAssetDirectory(CanonicalSettingsAssetPath));
            settings = ScriptableObject.CreateInstance<UnityLoggingSettings>();
            AssetDatabase.CreateAsset(settings, CanonicalSettingsAssetPath);
            AssetDatabase.SaveAssetIfDirty(settings);
            AssetDatabase.ImportAsset(CanonicalSettingsAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return settings;
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

        internal static void ValidateSettings(UnityLoggingSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            ValidateDefinedEnum(settings.executionMode, nameof(settings.executionMode));
            ValidateDefinedEnum(settings.overflowPolicy, nameof(settings.overflowPolicy));
            ValidateDefinedEnum(settings.unityConsoleOverflowPolicy, nameof(settings.unityConsoleOverflowPolicy));
            ValidateDefinedEnum(settings.criticalSeverity, nameof(settings.criticalSeverity));
            ValidateDefinedEnum(settings.minimumSeverity, nameof(settings.minimumSeverity));
            ValidateDefinedEnum(settings.categoryFilter, nameof(settings.categoryFilter));
            ValidateDefinedEnum(settings.fileMaintenanceMode, nameof(settings.fileMaintenanceMode));
            ValidateDefinedEnum(settings.fileSourcePathMode, nameof(settings.fileSourcePathMode));

            if (settings.criticalSeverity == LogSeverity.None)
            {
                throw InvalidSettings(nameof(settings.criticalSeverity), "must identify a logging severity");
            }

            try
            {
                LogPipelineOptions pipelineOptions = LogPipelineOptions.CreateValidated(new LogPipelineOptions
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
                    ShutdownDrainTimeoutMs = settings.shutdownDrainTimeoutMs,
                    EnqueueBlockTimeoutMs = settings.enqueueBlockTimeoutMs,
                    MaintenanceIntervalMs = settings.maintenanceIntervalMs,
                    SinkFailureThreshold = settings.sinkFailureThreshold,
                    OverflowPolicy = settings.overflowPolicy,
                    CriticalSeverity = settings.criticalSeverity
                });

                UnityConsoleLogSinkOptions.CreateValidated(new UnityConsoleLogSinkOptions
                {
                    MaxQueuedMessages = settings.unityConsoleMaxQueuedMessages,
                    MaxQueuedCharacters = settings.unityConsoleMaxQueuedCharacters,
                    MaximumRetainedEntryCharacters = UnityConsoleLogSinkOptions.EstimateRetainedCharacters(
                        pipelineOptions.MaxMessageCharacters,
                        pipelineOptions.MaxCategoryCharacters,
                        pipelineOptions.MaxSourcePathCharacters),
                    ReservedCriticalMessages = pipelineOptions.ReservedCriticalMessages,
                    ReservedCriticalCharacters = pipelineOptions.ReservedCriticalCharacters,
                    OverflowPolicy = settings.unityConsoleOverflowPolicy,
                    CriticalSeverity = pipelineOptions.CriticalSeverity
                });
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                throw InvalidSettings("executionMode", exception.Message);
            }

            try
            {
                FileLogSinkOptions.CreateValidated(new FileLogSinkOptions
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
                throw InvalidSettings("fileSink", exception.Message);
            }

            ValidatePortableFileName(settings.fileName);
            if (!string.IsNullOrEmpty(settings.customFilePath))
            {
                ValidateCustomFilePath(settings.customFilePath);
            }

            if (settings.registerFileLogSink &&
                !settings.usePersistentDataPath &&
                (!settings.allowCustomFilePath || string.IsNullOrWhiteSpace(settings.customFilePath)))
            {
                throw InvalidSettings(nameof(settings.customFilePath), "requires allowCustomFilePath and a value when the file sink does not use Application.persistentDataPath");
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
                throw InvalidSettings(nameof(UnityLoggingSettings.fileName), "must not be empty");
            }

            if (fileName == "." || fileName == ".." ||
                fileName.IndexOfAny(PortableInvalidFileNameCharacters) >= 0 ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                IsWindowsReservedFileName(fileName) ||
                fileName.EndsWith(".", StringComparison.Ordinal) ||
                fileName.EndsWith(" ", StringComparison.Ordinal))
            {
                throw InvalidSettings(nameof(UnityLoggingSettings.fileName), "must be a portable leaf file name without traversal or directory separators");
            }

            for (int i = 0; i < fileName.Length; i++)
            {
                if (char.IsControl(fileName[i]))
                {
                    throw InvalidSettings(nameof(UnityLoggingSettings.fileName), "must not contain control characters");
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
                throw InvalidSettings(nameof(UnityLoggingSettings.customFilePath), "must not contain only whitespace");
            }

            if (!Path.IsPathFullyQualified(customFilePath))
            {
                throw InvalidSettings(nameof(UnityLoggingSettings.customFilePath), "must be a rooted absolute path");
            }

            for (int i = 0; i < customFilePath.Length; i++)
            {
                if (char.IsControl(customFilePath[i]))
                {
                    throw InvalidSettings(nameof(UnityLoggingSettings.customFilePath), "must not contain control characters");
                }
            }

            string normalized = customFilePath.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "..")
                {
                    throw InvalidSettings(nameof(UnityLoggingSettings.customFilePath), "must not contain parent-directory traversal segments");
                }
            }
        }

        private static BuildFailedException InvalidSettings(string fieldName, string reason)
        {
            return new BuildFailedException($"{LogPrefix} Invalid LoggingSettings.{fieldName}: {reason}.");
        }

        private static string GetAssetDirectory(string assetPath)
        {
            int index = assetPath.LastIndexOf('/');
            return index < 0 ? string.Empty : assetPath.Substring(0, index);
        }

        private sealed class LoggingBuildCommandLineOptions
        {
            private LoggingBuildMode? _mode;
            private string _profilePath;
            private bool? _registerUnityConsoleLogSink;
            private bool? _registerConsoleLogSink;
            private bool? _registerFileLogSink;
            private bool? _usePersistentDataPath;
            private string _fileName;
            private string _customFilePath;
            private bool _customFilePathSpecified;
            private LogSeverity? _minimumSeverity;
            private LogCategoryFilterMode? _categoryFilter;
            private UnityLoggingSettings.ExecutionMode? _executionMode;
            private int? _maxQueuedMessages;
            private int? _unityConsoleMaxQueuedMessages;
            private int? _shutdownDrainTimeoutMs;
            private LogQueueOverflowPolicy? _overflowPolicy;
            private LogSeverity? _criticalSeverity;

            public bool HasOverrides { get; private set; }

            public static LoggingBuildCommandLineOptions Resolve()
            {
                return Resolve(Environment.GetEnvironmentVariable, Environment.GetCommandLineArgs());
            }

            public static LoggingBuildCommandLineOptions Resolve(Func<string, string> environmentReader, string[] commandLineArgs)
            {
                if (environmentReader == null)
                {
                    throw new ArgumentNullException(nameof(environmentReader));
                }

                var options = new LoggingBuildCommandLineOptions();
                options.ApplyEnvironment(environmentReader);
                options.ApplyCommandLine(commandLineArgs ?? Array.Empty<string>());
                return options;
            }

            public void ApplyTo(UnityLoggingSettings settings)
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

                if (_registerUnityConsoleLogSink.HasValue)
                {
                    settings.registerUnityConsoleLogSink = _registerUnityConsoleLogSink.Value;
                }

                if (_registerConsoleLogSink.HasValue)
                {
                    settings.registerConsoleLogSink = _registerConsoleLogSink.Value;
                }

                if (_registerFileLogSink.HasValue)
                {
                    settings.registerFileLogSink = _registerFileLogSink.Value;
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

                if (_minimumSeverity.HasValue)
                {
                    settings.minimumSeverity = _minimumSeverity.Value;
                }

                if (_categoryFilter.HasValue)
                {
                    settings.categoryFilter = _categoryFilter.Value;
                }

                if (_executionMode.HasValue)
                {
                    settings.executionMode = _executionMode.Value;
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

                if (_criticalSeverity.HasValue)
                {
                    settings.criticalSeverity = _criticalSeverity.Value;
                }
            }

            public string Describe(UnityLoggingSettings settings)
            {
                return $"Unity={settings.registerUnityConsoleLogSink}, Console={settings.registerConsoleLogSink}, File={settings.registerFileLogSink}, Severity={settings.minimumSeverity}, FileName={settings.fileName}";
            }

            private void ApplyEnvironment(Func<string, string> environmentReader)
            {
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_SETTINGS", value => TrySetRequiredString(value, parsed => _profilePath = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_MODE", TrySetMode);
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_UNITY", value => TrySetBool(value, parsed => _registerUnityConsoleLogSink = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_CONSOLE", value => TrySetBool(value, parsed => _registerConsoleLogSink = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_FILE", value => TrySetBool(value, parsed => _registerFileLogSink = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_USE_PERSISTENT_DATA_PATH", value => TrySetBool(value, parsed => _usePersistentDataPath = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_MINIMUM_SEVERITY", value => TrySetEnum<LogSeverity>(value, parsed => _minimumSeverity = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_CATEGORY_FILTER", value => TrySetEnum<LogCategoryFilterMode>(value, parsed => _categoryFilter = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_EXECUTION_MODE", value => TrySetEnum<UnityLoggingSettings.ExecutionMode>(value, parsed => _executionMode = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_FILE_NAME", value => TrySetRequiredString(value, parsed => _fileName = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_CUSTOM_FILE_PATH", TrySetOptionalCustomPath);
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_MAX_QUEUED_MESSAGES", value => TrySetPositiveInt(value, parsed => _maxQueuedMessages = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_UNITY_CONSOLE_MAX_QUEUED_MESSAGES", value => TrySetPositiveInt(value, parsed => _unityConsoleMaxQueuedMessages = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_SHUTDOWN_DRAIN_TIMEOUT_MS", value => TrySetNonNegativeInt(value, parsed => _shutdownDrainTimeoutMs = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_OVERFLOW_POLICY", value => TrySetEnum<LogQueueOverflowPolicy>(value, parsed => _overflowPolicy = parsed));
                ApplyEnvironmentValue(environmentReader, "CG_LOGGING_CRITICAL_SEVERITY", value => TrySetEnum<LogSeverity>(value, parsed => _criticalSeverity = parsed));
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
                    if (TryMatchValue(args, ref i, arg, "-loggingSettings", value => TrySetRequiredString(value, parsed => _profilePath = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingMode", TrySetMode))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingUnity", value => TrySetBool(value, parsed => _registerUnityConsoleLogSink = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingConsole", value => TrySetBool(value, parsed => _registerConsoleLogSink = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingFile", value => TrySetBool(value, parsed => _registerFileLogSink = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingUsePersistentDataPath", value => TrySetBool(value, parsed => _usePersistentDataPath = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingFileName", value => TrySetRequiredString(value, parsed => _fileName = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingCustomFilePath", TrySetOptionalCustomPath))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingMinimumSeverity", value => TrySetEnum<LogSeverity>(value, parsed => _minimumSeverity = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingCategoryFilter", value => TrySetEnum<LogCategoryFilterMode>(value, parsed => _categoryFilter = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingExecutionMode", value => TrySetEnum<UnityLoggingSettings.ExecutionMode>(value, parsed => _executionMode = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingMaxQueuedMessages", value => TrySetPositiveInt(value, parsed => _maxQueuedMessages = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingUnityConsoleMaxQueuedMessages", value => TrySetPositiveInt(value, parsed => _unityConsoleMaxQueuedMessages = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingShutdownDrainTimeoutMs", value => TrySetNonNegativeInt(value, parsed => _shutdownDrainTimeoutMs = parsed)))
                    {
                        continue;
                    }

                    if (TryMatchValue(args, ref i, arg, "-loggingOverflowPolicy", value => TrySetEnum<LogQueueOverflowPolicy>(value, parsed => _overflowPolicy = parsed)))
                    {
                        continue;
                    }

                    TryMatchValue(args, ref i, arg, "-loggingCriticalSeverity", value => TrySetEnum<LogSeverity>(value, parsed => _criticalSeverity = parsed));
                }
            }

            private static UnityLoggingSettings LoadProfile(string profilePath)
            {
                string assetPath = NormalizeProfileAssetPath(profilePath);
                if (string.Equals(assetPath, GeneratedSettingsAssetPath, StringComparison.Ordinal))
                {
                    throw new BuildFailedException($"{LogPrefix} The generated build override cannot be used as a source profile.");
                }

                var profile = AssetDatabase.LoadAssetAtPath<UnityLoggingSettings>(assetPath);
                if (profile == null)
                {
                    throw new BuildFailedException($"{LogPrefix} LoggingSettings profile not found or has the wrong type: {profilePath}");
                }

                return profile;
            }

            private static string NormalizeProfileAssetPath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new BuildFailedException($"{LogPrefix} LoggingSettings profile path must not be empty.");
                }

                try
                {
                    string projectRoot = GetProjectRoot();
                    string fullPath = Path.IsPathRooted(path)
                        ? Path.GetFullPath(path)
                        : Path.GetFullPath(Path.Combine(projectRoot, path));
                    if (!IsPathWithinRoot(fullPath, projectRoot))
                    {
                        throw new BuildFailedException($"{LogPrefix} LoggingSettings profile must be inside the current Unity project.");
                    }

                    string normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string relativePath = fullPath.Substring(normalizedRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    if (!relativePath.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        throw new BuildFailedException($"{LogPrefix} LoggingSettings profile must be an asset under Assets/.");
                    }

                    return relativePath;
                }
                catch (BuildFailedException)
                {
                    throw;
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    throw new BuildFailedException($"{LogPrefix} LoggingSettings profile path is invalid: {exception.Message}");
                }
            }

            private static void ApplyMode(UnityLoggingSettings settings, LoggingBuildMode mode)
            {
                switch (mode)
                {
                    case LoggingBuildMode.Settings:
                        break;
                    case LoggingBuildMode.Off:
                        settings.registerUnityConsoleLogSink = false;
                        settings.registerConsoleLogSink = false;
                        settings.registerFileLogSink = false;
                        break;
                    case LoggingBuildMode.Unity:
                        settings.registerUnityConsoleLogSink = true;
                        settings.registerConsoleLogSink = false;
                        settings.registerFileLogSink = false;
                        break;
                    case LoggingBuildMode.File:
                        settings.registerUnityConsoleLogSink = false;
                        settings.registerConsoleLogSink = false;
                        settings.registerFileLogSink = true;
                        break;
                    case LoggingBuildMode.UnityAndFile:
                        settings.registerUnityConsoleLogSink = true;
                        settings.registerConsoleLogSink = false;
                        settings.registerFileLogSink = true;
                        break;
                    default:
                        throw new BuildFailedException($"{LogPrefix} Undefined logging build mode: {mode}");
                }
            }

            private bool TrySetMode(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                string normalized = value.Replace("-", string.Empty).Replace("_", string.Empty);
                if (!Enum.TryParse(normalized, true, out LoggingBuildMode mode) ||
                    !Enum.IsDefined(typeof(LoggingBuildMode), mode))
                {
                    return false;
                }

                _mode = mode;
                if (mode != LoggingBuildMode.Settings)
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

        private enum LoggingBuildMode
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
