using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    /// <summary>
    /// Module-level settings for CycloneGames.DataTable.
    /// </summary>
    [CreateAssetMenu(
        menuName = "CycloneGames/DataTable/Settings",
        fileName = "DataTableSettings")]
    public class DataTableSettings : ScriptableObject
    {
        private const string DefaultAssetDir = "Assets/Editor";
        private const string DefaultAssetName = "DataTableSettings.asset";

        private static readonly Dictionary<Type, DataTableSettings> CachedSettings =
            new Dictionary<Type, DataTableSettings>(4);

        /// <summary>
        /// Path to the Luban project directory, relative to the Unity project root.
        /// Use "../" when the Luban project lives next to the Unity project folder.
        /// </summary>
        [Tooltip("Luban project directory, relative to the Unity project root. Use '../' to go above the Unity project folder.")]
        public string DataTableProjectDir = "../DataTable";

        /// <summary>
        /// Name of the Luban code-generation script without file extension.
        /// The runner appends .bat on Windows and .sh on macOS/Linux.
        /// </summary>
        [Tooltip("Luban script name without extension (.bat/.sh appended automatically).")]
        public string ScriptName = "gen_code_bin_to_project_lazyload";

        /// <summary>
        /// Optional command-line arguments passed after the generated script path.
        /// </summary>
        [Tooltip("Optional command-line arguments appended after the Luban script path.")]
        public string ScriptArguments;

        /// <summary>
        /// Maximum seconds to wait for the external process. Zero or negative means no timeout.
        /// </summary>
        [Tooltip("Maximum seconds to wait for the external process. Zero or negative means no timeout.")]
        public int TimeoutSeconds = 0;

        /// <summary>
        /// Whether to call AssetDatabase.Refresh() after a successful Luban run.
        /// </summary>
        [Tooltip("Automatically refresh the AssetDatabase after a successful Luban build.")]
        public bool AutoRefreshAssets = true;

        /// <summary>
        /// Discover and return the default DataTableSettings asset.
        /// </summary>
        public static DataTableSettings GetOrCreate()
        {
            return GetOrCreate<DataTableSettings>();
        }

        /// <summary>
        /// Discover and return a settings asset of the requested type.
        /// External projects can derive from DataTableSettings and call this from custom tooling.
        /// </summary>
        public static TSettings GetOrCreate<TSettings>()
            where TSettings : DataTableSettings
        {
            var type = typeof(TSettings);
            if (CachedSettings.TryGetValue(type, out var cached) && cached != null)
            {
                return (TSettings)cached;
            }

            var query = "t:" + type.Name;
            var guids = AssetDatabase.FindAssets(query);
            if (guids.Length == 0 && type == typeof(DataTableSettings))
            {
                guids = AssetDatabase.FindAssets("t:DataTableSettings");
            }

            if (guids.Length == 0)
            {
                var created = CreateDefault<TSettings>();
                CachedSettings[type] = created;
                return created;
            }

            if (guids.Length > 1)
            {
                LogDuplicateSettingsWarning(type, guids);
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var settings = AssetDatabase.LoadAssetAtPath<TSettings>(assetPath);
            if (settings == null)
            {
                Debug.LogError(
                    $"[DataTable] {type.Name} GUID resolved to '{assetPath}' but the asset could not be loaded. " +
                    "Creating a default config as fallback.");
                settings = CreateDefault<TSettings>();
            }

            CachedSettings[type] = settings;
            return settings;
        }

        /// <summary>
        /// Invalidate every cached settings reference.
        /// </summary>
        public static void InvalidateCache()
        {
            CachedSettings.Clear();
        }

        /// <summary>
        /// Current default settings asset path on disk, or null if not yet resolved.
        /// </summary>
        public static string AssetPath => GetAssetPath<DataTableSettings>();

        /// <summary>
        /// Current settings asset path for the requested derived settings type.
        /// </summary>
        public static string GetAssetPath<TSettings>()
            where TSettings : DataTableSettings
        {
            var settings = GetOrCreate<TSettings>();
            return settings == null ? null : AssetDatabase.GetAssetPath(settings);
        }

        /// <summary>
        /// Derived settings can override this to redirect project-root resolution without replacing the runner.
        /// </summary>
        public virtual string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        /// <summary>
        /// Derived settings can override this to use custom platform-specific paths or profiles.
        /// </summary>
        public virtual string GetDataTableProjectDir()
        {
            return DataTableProjectDir;
        }

        /// <summary>
        /// Derived settings can override this to select different generation scripts per platform or build target.
        /// </summary>
        public virtual string GetScriptName()
        {
            return ScriptName;
        }

        /// <summary>
        /// Derived settings can override this to provide dynamic CI/build-profile arguments.
        /// </summary>
        public virtual string GetScriptArguments()
        {
            return ScriptArguments;
        }

        /// <summary>
        /// Derived settings can override this to use a custom script extension.
        /// </summary>
        public virtual string GetScriptExtension()
        {
            return Application.platform == RuntimePlatform.WindowsEditor ? ".bat" : ".sh";
        }

        /// <summary>
        /// Derived settings can override this to control post-build asset refresh policy.
        /// </summary>
        public virtual bool ShouldRefreshAssets()
        {
            return AutoRefreshAssets;
        }

        /// <summary>
        /// Derived settings can override this to supply a timeout from a custom profile.
        /// </summary>
        public virtual int GetTimeoutMilliseconds()
        {
            return TimeoutSeconds <= 0 ? 0 : TimeoutSeconds * 1000;
        }

        /// <summary>
        /// Derived settings can override this to build a custom request while still using the default runner.
        /// </summary>
        public virtual DataTableLubanRunRequest CreateLubanRunRequest()
        {
            return DataTableLubanRunner.CreateRequest(this);
        }

        private static TSettings CreateDefault<TSettings>()
            where TSettings : DataTableSettings
        {
            EnsureDirectoryExists(DefaultAssetDir);

            var settings = CreateInstance<TSettings>();
            var assetName = typeof(TSettings) == typeof(DataTableSettings)
                ? DefaultAssetName
                : typeof(TSettings).Name + ".asset";
            var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(DefaultAssetDir, assetName));

            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[DataTable] No {typeof(TSettings).Name} found. Created default settings at:\n" +
                $"  {path}\n" +
                "You can move this asset anywhere under Assets/.");

            return settings;
        }

        private static void LogDuplicateSettingsWarning(Type settingsType, string[] guids)
        {
            var message = $"[DataTable] Found {guids.Length} {settingsType.Name} assets. Using the first one:";
            for (int i = 0; i < guids.Length; i++)
            {
                message += "\n  " + AssetDatabase.GUIDToAssetPath(guids[i]);
            }

            Debug.LogWarning(message + "\nRemove duplicates or use a project-specific settings type.");
        }

        private static void EnsureDirectoryExists(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir))
            {
                return;
            }

            var parent = Path.GetDirectoryName(dir)?.Replace('\\', '/');
            var folder = Path.GetFileName(dir);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureDirectoryExists(parent);
            }

            AssetDatabase.CreateFolder(parent ?? "Assets", folder);
        }
    }
}
