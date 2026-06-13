using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    /// <summary>
    /// Project-visible settings for the Luban-based DataTable generation workflow.
    /// </summary>
    [CreateAssetMenu(
        menuName = "CycloneGames/DataTable/Luban Settings",
        fileName = "DataTableLubanSettings")]
    public class DataTableLubanSettings : ScriptableObject
    {
        private const string DefaultAssetDir = "Assets/Editor/DataTable";
        private const string DefaultAssetName = "DataTableLubanSettings.asset";

        private static readonly Dictionary<Type, DataTableLubanSettings> CachedSettings =
            new Dictionary<Type, DataTableLubanSettings>(4);

        /// <summary>
        /// Path to the Luban project directory, relative to the Unity project root.
        /// Use "../" when the Luban project lives next to the Unity project folder.
        /// </summary>
        [Tooltip("Luban project directory, relative to the Unity project root. Use '../' to go above the Unity project folder.")]
        public string LubanProjectDir = "../DataTable";

        /// <summary>
        /// Name of the Luban code-generation script without file extension.
        /// The runner appends .bat on Windows and .sh on macOS/Linux.
        /// </summary>
        [Tooltip("Luban script name without extension (.bat/.sh appended automatically).")]
        public string LubanScriptName = "gen_code_bin_to_project_lazyload";

        /// <summary>
        /// Optional command-line arguments passed after the generated script path.
        /// </summary>
        [Tooltip("Optional command-line arguments appended after the Luban script path.")]
        public string LubanScriptArguments;

        /// <summary>
        /// Maximum seconds to wait for the external process. Zero or negative means no timeout.
        /// </summary>
        [Tooltip("Maximum seconds to wait for the external process. Zero or negative means no timeout.")]
        public int LubanTimeoutSeconds = 0;

        /// <summary>
        /// Whether to call AssetDatabase.Refresh() after a successful Luban run.
        /// </summary>
        [Tooltip("Automatically refresh the AssetDatabase after a successful Luban build.")]
        public bool RefreshAssetsAfterLubanBuild = true;

        /// <summary>
        /// Discover and return the default DataTableLubanSettings asset.
        /// </summary>
        public static DataTableLubanSettings GetOrCreate()
        {
            return GetOrCreate<DataTableLubanSettings>();
        }

        /// <summary>
        /// Discover and return a settings asset of the requested type.
        /// External projects can derive from DataTableLubanSettings and call this from custom tooling.
        /// </summary>
        public static TSettings GetOrCreate<TSettings>()
            where TSettings : DataTableLubanSettings
        {
            var type = typeof(TSettings);
            if (CachedSettings.TryGetValue(type, out var cached) &&
                cached != null &&
                AssetDatabase.Contains(cached))
            {
                return (TSettings)cached;
            }

            var guids = FindSettingsGuids(type);
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
        public static string AssetPath => GetAssetPath<DataTableLubanSettings>();

        /// <summary>
        /// Current settings asset path for the requested derived settings type.
        /// </summary>
        public static string GetAssetPath<TSettings>()
            where TSettings : DataTableLubanSettings
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
        /// Derived settings can override this to use custom Luban project paths or profiles.
        /// </summary>
        public virtual string GetLubanProjectDir()
        {
            return LubanProjectDir;
        }

        /// <summary>
        /// Derived settings can override this to select different Luban generation scripts per platform or build target.
        /// </summary>
        public virtual string GetLubanScriptName()
        {
            return LubanScriptName;
        }

        /// <summary>
        /// Derived settings can override this to provide dynamic Luban CI/build-profile arguments.
        /// </summary>
        public virtual string GetLubanScriptArguments()
        {
            return LubanScriptArguments;
        }

        /// <summary>
        /// Derived settings can override this to use a custom Luban script extension.
        /// </summary>
        public virtual string GetLubanScriptExtension()
        {
            return Application.platform == RuntimePlatform.WindowsEditor ? ".bat" : ".sh";
        }

        /// <summary>
        /// Derived settings can override this to control Luban post-build asset refresh policy.
        /// </summary>
        public virtual bool ShouldRefreshAssetsAfterLubanBuild()
        {
            return RefreshAssetsAfterLubanBuild;
        }

        /// <summary>
        /// Derived settings can override this to supply a Luban timeout from a custom profile.
        /// </summary>
        public virtual int GetLubanTimeoutMilliseconds()
        {
            if (LubanTimeoutSeconds <= 0)
            {
                return 0;
            }

            return LubanTimeoutSeconds >= int.MaxValue / 1000
                ? int.MaxValue
                : LubanTimeoutSeconds * 1000;
        }

        /// <summary>
        /// Derived settings can override this to build a custom request while still using the default runner.
        /// </summary>
        public virtual DataTableLubanRunRequest CreateLubanRunRequest()
        {
            return DataTableLubanRunner.CreateRequest(this);
        }

        private static TSettings CreateDefault<TSettings>()
            where TSettings : DataTableLubanSettings
        {
            EnsureDirectoryExists(DefaultAssetDir);

            var settings = CreateInstance<TSettings>();
            var assetName = typeof(TSettings) == typeof(DataTableLubanSettings)
                ? DefaultAssetName
                : typeof(TSettings).Name + ".asset";
            var path = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(DefaultAssetDir, assetName).Replace('\\', '/'));

            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[DataTable] No {typeof(TSettings).Name} found. Created default settings at:\n" +
                $"  {path}\n" +
                "You can move this asset anywhere under Assets/.");

            return settings;
        }

        private static string[] FindSettingsGuids(Type settingsType)
        {
            var query = "t:" + settingsType.Name;
            var guids = AssetDatabase.FindAssets(query);
            if (guids.Length == 0 && settingsType == typeof(DataTableLubanSettings))
            {
                guids = AssetDatabase.FindAssets("t:DataTableLubanSettings");
            }

            if (guids.Length <= 1)
            {
                return guids;
            }

            var paths = new List<string>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<DataTableLubanSettings>(path);
                if (asset != null && settingsType.IsInstanceOfType(asset))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);

            var sortedGuids = new string[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                sortedGuids[i] = AssetDatabase.AssetPathToGUID(paths[i]);
            }

            return sortedGuids;
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
            if (string.IsNullOrEmpty(dir) || AssetDatabase.IsValidFolder(dir))
            {
                return;
            }

            if (dir == "Assets")
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
