using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    /// <summary>
    /// Module-level settings for CycloneGames.DataTable.
    /// <para>
    /// Currently configures the Luban code-generation pipeline. Future versions
    /// may add backend selection (Luban / MessagePack / Custom) similar to how
    /// <c>BuildData</c> selects asset management backends.
    /// </para>
    /// <para>
    /// This asset is auto-discovered via AssetDatabase. You can place it anywhere
    /// under <c>Assets/</c>. If none exists, a default one is created at
    /// <c>Assets/Editor/DataTableSettings.asset</c> on first access.
    /// Only ONE instance should exist in the project.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        menuName = "CycloneGames/DataTable/Settings",
        fileName = "DataTableSettings")]
    public class DataTableSettings : ScriptableObject
    {
        private const string DefaultAssetDir = "Assets/Editor";
        private const string DefaultAssetName = "DataTableSettings.asset";

        /// <summary>
        /// Path to the Luban project directory, relative to the repository root
        /// (the folder containing the Unity project folder).
        /// Use "../" prefix because the repo root is one level above the Unity project.
        /// </summary>
        [Tooltip("Luban project directory, relative to repo root. Use '../' to go above the Unity project folder.")]
        public string DataTableProjectDir = "../DataTable";

        /// <summary>
        /// Name of the Luban code-generation script without file extension.
        /// The runner appends .bat (Windows) or .sh (macOS/Linux).
        /// </summary>
        [Tooltip("Luban script name without extension (.bat/.sh appended automatically).")]
        public string ScriptName = "gen_code_bin_to_project_lazyload";

        /// <summary>
        /// Whether to call AssetDatabase.Refresh() after a successful Luban run.
        /// This only refreshes the asset database; it does NOT trigger the Luban build.
        /// </summary>
        [Tooltip("Automatically refresh the AssetDatabase after a successful Luban build.")]
        public bool AutoRefreshAssets = true;

        /// <summary>
        /// Discover and return the single DataTableSettings asset in the project.
        /// Creates a default one if none exists. Logs a warning if duplicates are found.
        /// Result is cached; call <see cref="InvalidateCache"/> to force a re-scan.
        /// </summary>
        public static DataTableSettings GetOrCreate()
        {
            if (_cached != null)
                return _cached;

            var guids = AssetDatabase.FindAssets("t:DataTableSettings");

            if (guids.Length == 0)
            {
                _cached = CreateDefault();
                return _cached;
            }

            if (guids.Length > 1)
            {
                var paths = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .ToArray();

                Debug.LogWarning(
                    $"[DataTable] Found {guids.Length} DataTableSettings assets in the project. " +
                    $"Using the first one:\n  → {paths[0]}\n" +
                    $"All instances:\n  • {string.Join("\n  • ", paths)}\n" +
                    "Remove duplicates to avoid ambiguous configuration.");
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            _cached = AssetDatabase.LoadAssetAtPath<DataTableSettings>(assetPath);

            if (_cached == null)
            {
                Debug.LogError(
                    $"[DataTable] DataTableSettings GUID resolved to '{assetPath}' but the asset " +
                    "could not be loaded. Creating a new default config as fallback.");
                _cached = CreateDefault();
            }

            return _cached;
        }

        /// <summary>
        /// Invalidate the cached reference. Call after modifying or replacing the
        /// settings asset to force the next <see cref="GetOrCreate"/> to re-scan.
        /// </summary>
        public static void InvalidateCache()
        {
            _cached = null;
        }

        /// <summary>
        /// Current settings asset path on disk, or null if not yet resolved.
        /// </summary>
        public static string AssetPath
        {
            get
            {
                var settings = GetOrCreate();
                if (settings == null) return null;
                return AssetDatabase.GetAssetPath(settings);
            }
        }

        private static DataTableSettings _cached;

        private static DataTableSettings CreateDefault()
        {
            EnsureDirectoryExists(DefaultAssetDir);

            var settings = CreateInstance<DataTableSettings>();
            var path = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(DefaultAssetDir, DefaultAssetName));

            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[DataTable] No DataTableSettings found. Created default settings at:\n" +
                $"  {path}\n" +
                "You can move this asset anywhere under Assets/. Edit its fields to customize " +
                "the Luban build script path and name.");

            return settings;
        }

        private static void EnsureDirectoryExists(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir))
                return;

            var parent = Path.GetDirectoryName(dir)?.Replace('\\', '/');
            var folder = Path.GetFileName(dir);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureDirectoryExists(parent);

            AssetDatabase.CreateFolder(parent ?? "Assets", folder);
        }
    }
}
