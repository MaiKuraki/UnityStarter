#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using RuntimeLoggerSettings = CycloneGames.Logger.LoggerSettings;

namespace CycloneGames.Logger.Editor
{
    internal sealed class LoggerSettingsBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string ResourcesFolderPath = "Assets/Resources";
        private const string BackupPath = "Library/CycloneGames.Logger/LoggerSettingsBuildOverride.json";
        private const string LogPrefix = "[CLogger Build]";
        private static readonly string SettingsAssetPath = "Assets/Resources/" + RuntimeLoggerSettings.SettingsResourcePath + ".asset";
        private static readonly string SettingsFolderPath = GetAssetDirectory(SettingsAssetPath);
        private static readonly string SettingsFolderName = GetAssetName(SettingsFolderPath);

        public int callbackOrder => -850;

        [InitializeOnLoadMethod]
        private static void RestoreStaleBuildOverride()
        {
            RestoreFromBackupIfNeeded(false);
        }

        [MenuItem("Tools/CycloneGames/Logger/Create Default LoggerSettings", priority = 100)]
        private static void CreateDefaultSettings()
        {
            var settings = EnsureSettingsAsset(out _, out _, out _);
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            Debug.Log($"{LogPrefix} LoggerSettings is ready at {SettingsAssetPath}.");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            RestoreFromBackupIfNeeded(false);

            var options = LoggerBuildCommandLineOptions.Resolve();
            if (!options.HasOverrides) return;

            var settings = EnsureSettingsAsset(
                out bool settingsAssetExisted,
                out bool settingsFolderExisted,
                out bool resourcesFolderExisted);

            var backup = new LoggerSettingsBuildBackup
            {
                settingsAssetExisted = settingsAssetExisted,
                settingsFolderExisted = settingsFolderExisted,
                resourcesFolderExisted = resourcesFolderExisted,
                settingsJson = settingsAssetExisted ? EditorJsonUtility.ToJson(settings) : string.Empty
            };
            SaveBackup(backup);

            options.ApplyTo(settings);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(SettingsAssetPath, ImportAssetOptions.ForceUpdate);

            Debug.Log($"{LogPrefix} Applied build logger settings: {options.Describe(settings)}");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            RestoreFromBackupIfNeeded(true);
        }

        private static RuntimeLoggerSettings EnsureSettingsAsset(
            out bool settingsAssetExisted,
            out bool settingsFolderExisted,
            out bool resourcesFolderExisted)
        {
            resourcesFolderExisted = AssetDatabase.IsValidFolder(ResourcesFolderPath);
            settingsFolderExisted = AssetDatabase.IsValidFolder(SettingsFolderPath);

            if (!resourcesFolderExisted)
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            if (!settingsFolderExisted)
            {
                AssetDatabase.CreateFolder(ResourcesFolderPath, SettingsFolderName);
            }

            var settings = AssetDatabase.LoadAssetAtPath<RuntimeLoggerSettings>(SettingsAssetPath);
            settingsAssetExisted = settings != null;
            if (settings != null) return settings;

            settings = ScriptableObject.CreateInstance<RuntimeLoggerSettings>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.ImportAsset(SettingsAssetPath, ImportAssetOptions.ForceUpdate);
            return settings;
        }

        private static void SaveBackup(LoggerSettingsBuildBackup backup)
        {
            string directory = Path.GetDirectoryName(BackupPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(BackupPath, JsonUtility.ToJson(backup), new System.Text.UTF8Encoding(false));
        }

        private static void RestoreFromBackupIfNeeded(bool logResult)
        {
            if (!File.Exists(BackupPath)) return;

            string json = File.ReadAllText(BackupPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                File.Delete(BackupPath);
                return;
            }

            var backup = JsonUtility.FromJson<LoggerSettingsBuildBackup>(json);
            if (backup == null)
            {
                File.Delete(BackupPath);
                return;
            }

            if (backup.settingsAssetExisted)
            {
                var settings = EnsureSettingsAsset(out _, out _, out _);
                if (!string.IsNullOrEmpty(backup.settingsJson))
                {
                    EditorJsonUtility.FromJsonOverwrite(backup.settingsJson, settings);
                    EditorUtility.SetDirty(settings);
                }
            }
            else if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SettingsAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(SettingsAssetPath);
            }

            CleanupFolderIfBuildCreated(SettingsFolderPath, backup.settingsFolderExisted);
            CleanupFolderIfBuildCreated(ResourcesFolderPath, backup.resourcesFolderExisted);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            File.Delete(BackupPath);
            ScheduleDelayedPhysicalCleanup(backup);

            if (logResult)
            {
                Debug.Log($"{LogPrefix} Restored LoggerSettings after build.");
            }
        }

        private static void CleanupFolderIfBuildCreated(string assetFolderPath, bool existedBeforeBuild)
        {
            if (existedBeforeBuild) return;
            if (!AssetDatabase.IsValidFolder(assetFolderPath)) return;

            string absolutePath = AssetPathToAbsolutePath(assetFolderPath);
            if (Directory.Exists(absolutePath))
            {
                using (var enumerator = Directory.EnumerateFileSystemEntries(absolutePath).GetEnumerator())
                {
                    if (enumerator.MoveNext()) return;
                }
            }

            AssetDatabase.DeleteAsset(assetFolderPath);
        }

        private static void ScheduleDelayedPhysicalCleanup(LoggerSettingsBuildBackup backup)
        {
            if (backup.settingsAssetExisted && backup.settingsFolderExisted && backup.resourcesFolderExisted) return;

            EditorApplication.delayCall += () =>
            {
                DeletePhysicalAssetIfCreated(SettingsAssetPath, backup.settingsAssetExisted);
                DeletePhysicalFolderIfCreated(SettingsFolderPath, backup.settingsFolderExisted);
                DeletePhysicalFolderIfCreated(ResourcesFolderPath, backup.resourcesFolderExisted);
                AssetDatabase.Refresh();
            };
        }

        private static void DeletePhysicalAssetIfCreated(string assetPath, bool existedBeforeBuild)
        {
            if (existedBeforeBuild) return;

            string absolutePath = AssetPathToAbsolutePath(assetPath);
            DeleteFileIfExists(absolutePath);
            DeleteFileIfExists(absolutePath + ".meta");
        }

        private static void DeletePhysicalFolderIfCreated(string assetFolderPath, bool existedBeforeBuild)
        {
            if (existedBeforeBuild) return;

            string absolutePath = AssetPathToAbsolutePath(assetFolderPath);
            if (Directory.Exists(absolutePath))
            {
                using (var enumerator = Directory.EnumerateFileSystemEntries(absolutePath).GetEnumerator())
                {
                    if (enumerator.MoveNext()) return;
                }

                Directory.Delete(absolutePath);
            }

            DeleteFileIfExists(absolutePath + ".meta");
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{LogPrefix} Refusing to clean path outside project: {assetPath}");
            }

            return absolutePath;
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string GetAssetDirectory(string assetPath)
        {
            int index = assetPath.LastIndexOf('/');
            return index < 0 ? string.Empty : assetPath.Substring(0, index);
        }

        private static string GetAssetName(string assetPath)
        {
            int index = assetPath.LastIndexOf('/');
            return index < 0 ? assetPath : assetPath.Substring(index + 1);
        }

        [Serializable]
        private sealed class LoggerSettingsBuildBackup
        {
            public bool settingsAssetExisted;
            public bool settingsFolderExisted;
            public bool resourcesFolderExisted;
            public string settingsJson;
        }

        private sealed class LoggerBuildCommandLineOptions
        {
            private LoggerBuildMode? _mode;
            private string _profilePath;
            private bool? _registerUnityLogger;
            private bool? _registerFileLogger;
            private bool? _usePersistentDataPath;
            private string _fileName;
            private string _customFilePath;
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
                var options = new LoggerBuildCommandLineOptions();
                options.ApplyEnvironment();
                options.ApplyCommandLine(Environment.GetCommandLineArgs());
                return options;
            }

            public void ApplyTo(RuntimeLoggerSettings settings)
            {
                if (settings == null) throw new ArgumentNullException(nameof(settings));

                if (!string.IsNullOrEmpty(_profilePath))
                {
                    var profile = LoadProfile(_profilePath);
                    EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(profile), settings);
                }

                if (_mode.HasValue)
                {
                    ApplyMode(settings, _mode.Value);
                }

                if (_registerUnityLogger.HasValue) settings.registerUnityLogger = _registerUnityLogger.Value;
                if (_registerFileLogger.HasValue) settings.registerFileLogger = _registerFileLogger.Value;
                if (_usePersistentDataPath.HasValue) settings.usePersistentDataPath = _usePersistentDataPath.Value;
                if (!string.IsNullOrEmpty(_fileName)) settings.fileName = _fileName;
                if (!string.IsNullOrEmpty(_customFilePath)) settings.customFilePath = _customFilePath;
                if (_defaultLevel.HasValue) settings.defaultLevel = _defaultLevel.Value;
                if (_defaultFilter.HasValue) settings.defaultFilter = _defaultFilter.Value;
                if (_processing.HasValue) settings.processing = _processing.Value;
                if (_maxQueuedMessages.HasValue) settings.maxQueuedMessages = _maxQueuedMessages.Value;
                if (_unityConsoleMaxQueuedMessages.HasValue) settings.unityConsoleMaxQueuedMessages = _unityConsoleMaxQueuedMessages.Value;
                if (_shutdownDrainTimeoutMs.HasValue) settings.shutdownDrainTimeoutMs = _shutdownDrainTimeoutMs.Value;
                if (_overflowPolicy.HasValue) settings.overflowPolicy = _overflowPolicy.Value;
                if (_guaranteedLevel.HasValue) settings.guaranteedLevel = _guaranteedLevel.Value;
            }

            public string Describe(RuntimeLoggerSettings settings)
            {
                return $"Unity={settings.registerUnityLogger}, File={settings.registerFileLogger}, Level={settings.defaultLevel}, FileName={settings.fileName}";
            }

            private void ApplyEnvironment()
            {
                TrySetString(Environment.GetEnvironmentVariable("CG_LOGGER_SETTINGS"), value => _profilePath = value);
                TrySetMode(Environment.GetEnvironmentVariable("CG_LOGGER_MODE"));
                TrySetBool(Environment.GetEnvironmentVariable("CG_LOGGER_UNITY"), value => _registerUnityLogger = value);
                TrySetBool(Environment.GetEnvironmentVariable("CG_LOGGER_FILE"), value => _registerFileLogger = value);
                TrySetBool(Environment.GetEnvironmentVariable("CG_LOGGER_USE_PERSISTENT_DATA_PATH"), value => _usePersistentDataPath = value);
                TrySetEnum<LogLevel>(Environment.GetEnvironmentVariable("CG_LOGGER_LEVEL"), value => _defaultLevel = value);
                TrySetEnum<LogFilter>(Environment.GetEnvironmentVariable("CG_LOGGER_FILTER"), value => _defaultFilter = value);
                TrySetEnum<RuntimeLoggerSettings.ProcessingMode>(Environment.GetEnvironmentVariable("CG_LOGGER_PROCESSING"), value => _processing = value);
                TrySetString(Environment.GetEnvironmentVariable("CG_LOGGER_FILE_NAME"), value => _fileName = value);
                TrySetString(Environment.GetEnvironmentVariable("CG_LOGGER_CUSTOM_FILE_PATH"), value => _customFilePath = value);
                TrySetPositiveInt(Environment.GetEnvironmentVariable("CG_LOGGER_MAX_QUEUED_MESSAGES"), value => _maxQueuedMessages = value);
                TrySetPositiveInt(Environment.GetEnvironmentVariable("CG_LOGGER_UNITY_CONSOLE_MAX_QUEUED_MESSAGES"), value => _unityConsoleMaxQueuedMessages = value);
                TrySetNonNegativeInt(Environment.GetEnvironmentVariable("CG_LOGGER_SHUTDOWN_DRAIN_TIMEOUT_MS"), value => _shutdownDrainTimeoutMs = value);
                TrySetEnum<LogQueueOverflowPolicy>(Environment.GetEnvironmentVariable("CG_LOGGER_OVERFLOW_POLICY"), value => _overflowPolicy = value);
                TrySetEnum<LogLevel>(Environment.GetEnvironmentVariable("CG_LOGGER_GUARANTEED_LEVEL"), value => _guaranteedLevel = value);
            }

            private void ApplyCommandLine(string[] args)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (TryMatchValue(args, ref i, arg, "-loggerSettings", value => TrySetString(value, parsed => _profilePath = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerMode", TrySetMode)) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerUnity", value => TrySetBool(value, parsed => _registerUnityLogger = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerFile", value => TrySetBool(value, parsed => _registerFileLogger = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerUsePersistentDataPath", value => TrySetBool(value, parsed => _usePersistentDataPath = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerFileName", value => TrySetString(value, parsed => _fileName = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerCustomFilePath", value => TrySetString(value, parsed => _customFilePath = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerLevel", value => TrySetEnum<LogLevel>(value, parsed => _defaultLevel = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerFilter", value => TrySetEnum<LogFilter>(value, parsed => _defaultFilter = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerProcessing", value => TrySetEnum<RuntimeLoggerSettings.ProcessingMode>(value, parsed => _processing = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerMaxQueuedMessages", value => TrySetPositiveInt(value, parsed => _maxQueuedMessages = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerUnityConsoleMaxQueuedMessages", value => TrySetPositiveInt(value, parsed => _unityConsoleMaxQueuedMessages = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerShutdownDrainTimeoutMs", value => TrySetNonNegativeInt(value, parsed => _shutdownDrainTimeoutMs = parsed))) continue;
                    if (TryMatchValue(args, ref i, arg, "-loggerOverflowPolicy", value => TrySetEnum<LogQueueOverflowPolicy>(value, parsed => _overflowPolicy = parsed))) continue;
                    TryMatchValue(args, ref i, arg, "-loggerGuaranteedLevel", value => TrySetEnum<LogLevel>(value, parsed => _guaranteedLevel = parsed));
                }
            }

            private static RuntimeLoggerSettings LoadProfile(string profilePath)
            {
                string assetPath = NormalizeAssetPath(profilePath);
                var profile = AssetDatabase.LoadAssetAtPath<RuntimeLoggerSettings>(assetPath);
                if (profile == null)
                {
                    throw new BuildFailedException($"{LogPrefix} LoggerSettings profile not found: {profilePath}");
                }

                return profile;
            }

            private static string NormalizeAssetPath(string path)
            {
                if (string.IsNullOrEmpty(path)) return path;
                path = path.Replace('\\', '/');
                if (path.StartsWith("Assets/", StringComparison.Ordinal)) return path;

                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
                string fullPath = Path.GetFullPath(path).Replace('\\', '/');
                if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                return fullPath.Substring(projectRoot.Length + 1);
            }

            private static void ApplyMode(RuntimeLoggerSettings settings, LoggerBuildMode mode)
            {
                switch (mode)
                {
                    case LoggerBuildMode.Off:
                        settings.registerUnityLogger = false;
                        settings.registerFileLogger = false;
                        break;
                    case LoggerBuildMode.Unity:
                        settings.registerUnityLogger = true;
                        settings.registerFileLogger = false;
                        break;
                    case LoggerBuildMode.File:
                        settings.registerUnityLogger = false;
                        settings.registerFileLogger = true;
                        break;
                    case LoggerBuildMode.UnityAndFile:
                        settings.registerUnityLogger = true;
                        settings.registerFileLogger = true;
                        break;
                }
            }

            private bool TrySetMode(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return false;
                string normalized = value.Replace("-", string.Empty).Replace("_", string.Empty);
                if (!Enum.TryParse(normalized, true, out LoggerBuildMode mode)) return false;
                _mode = mode;
                if (mode != LoggerBuildMode.Settings)
                {
                    HasOverrides = true;
                }
                return true;
            }

            private bool TrySetString(string value, Action<string> setter)
            {
                if (string.IsNullOrEmpty(value)) return false;
                setter(value);
                HasOverrides = true;
                return true;
            }

            private bool TrySetBool(string value, Action<bool> setter)
            {
                if (!TryParseBool(value, out bool parsed)) return false;
                setter(parsed);
                HasOverrides = true;
                return true;
            }

            private bool TrySetPositiveInt(string value, Action<int> setter)
            {
                if (!int.TryParse(value, out int parsed) || parsed < 1) return false;
                setter(parsed);
                HasOverrides = true;
                return true;
            }

            private bool TrySetNonNegativeInt(string value, Action<int> setter)
            {
                if (!int.TryParse(value, out int parsed) || parsed < 0) return false;
                setter(parsed);
                HasOverrides = true;
                return true;
            }

            private bool TrySetEnum<T>(string value, Action<T> setter)
                where T : struct, Enum
            {
                if (!Enum.TryParse(value, true, out T parsed)) return false;
                setter(parsed);
                HasOverrides = true;
                return true;
            }

            private bool TryMatchValue(string[] args, ref int index, string arg, string expected, Func<string, bool> setter)
            {
                if (!string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase)) return false;
                if (index + 1 >= args.Length)
                {
                    throw new BuildFailedException($"{LogPrefix} Missing value for {expected}.");
                }

                string value = args[index + 1];
                if (!setter(value))
                {
                    throw new BuildFailedException($"{LogPrefix} Invalid value for {expected}: {value}");
                }

                index++;
                return true;
            }

            private static bool TryParseBool(string value, out bool parsed)
            {
                parsed = false;
                if (string.IsNullOrWhiteSpace(value)) return false;

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
