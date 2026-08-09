using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    [CreateAssetMenu(
        menuName = "CycloneGames/DataTable/Luban Pipeline Settings",
        fileName = "DataTableLubanSettings")]
    public sealed class DataTableLubanSettings : ScriptableObject
    {
        private enum LoadStatus
        {
            Loaded,
            Missing,
            Ambiguous,
            Invalid,
        }

        public const int CurrentSchemaVersion = 1;
        public const int DefaultMaximumCapturedOutputCharacters = 1024 * 1024;
        public const string DefaultBuildConfigurationPath = "../DataTable/Luban/build_config.ini";
        public const string DefaultProfileName = "client";
        public const string DefaultAssetPath = "Assets/Editor/DataTable/DataTableLubanSettings.asset";

        [SerializeField]
        [HideInInspector]
        private int schemaVersion = CurrentSchemaVersion;

        [SerializeField]
        [Tooltip("Path to build_config.ini, relative to the Unity project root.")]
        private string buildConfigurationPath = DefaultBuildConfigurationPath;

        [SerializeField]
        [Tooltip("Default named [profile.<name>] section used by the Unity Editor.")]
        private string defaultProfileName = DefaultProfileName;

        [SerializeField]
        [Tooltip("Refresh the AssetDatabase after a successful generate or recovery operation.")]
        private bool refreshAssetsAfterSuccess = true;

        [SerializeField]
        [Range(4096, 16 * 1024 * 1024)]
        [Tooltip("Maximum combined stdout and stderr characters retained by an Editor operation.")]
        private int maximumCapturedOutputCharacters = DefaultMaximumCapturedOutputCharacters;

        public int SchemaVersion => schemaVersion;
        public string BuildConfigurationPath => buildConfigurationPath ?? string.Empty;
        public string SelectedProfileName => defaultProfileName ?? string.Empty;
        public bool RefreshAssetsAfterSuccess => refreshAssetsAfterSuccess;
        public int MaximumCapturedOutputCharacters => maximumCapturedOutputCharacters;

        internal string ResolveBuildConfigurationPath()
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(
                unityProjectRoot,
                buildConfigurationPath ?? string.Empty));
        }

        internal static string[] FindAssetPaths()
        {
            return AssetDatabase.FindAssets("t:DataTableLubanSettings")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(static path => !string.IsNullOrEmpty(path))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        }

        public static bool TryLoad(out DataTableLubanSettings settings, out string error)
        {
            return Load(out settings, out error) == LoadStatus.Loaded;
        }

        private static LoadStatus Load(out DataTableLubanSettings settings, out string error)
        {
            string[] paths = FindAssetPaths();
            if (paths.Length == 0)
            {
                settings = null;
                error =
                    "No DataTableLubanSettings asset exists. Create one explicitly with " +
                    "Tools > CycloneGames > DataTable > Create Default Settings.";
                return LoadStatus.Missing;
            }

            if (paths.Length != 1)
            {
                settings = null;
                error = "Exactly one DataTableLubanSettings asset is required. Found:\n" +
                        string.Join("\n", paths);
                return LoadStatus.Ambiguous;
            }

            settings = AssetDatabase.LoadAssetAtPath<DataTableLubanSettings>(paths[0]);
            if (settings == null)
            {
                error = "The DataTableLubanSettings asset could not be loaded: " + paths[0];
                return LoadStatus.Invalid;
            }

            error = string.Empty;
            return LoadStatus.Loaded;
        }

        public static DataTableLubanSettings GetRequired()
        {
            if (!TryLoad(out DataTableLubanSettings settings, out string error))
            {
                throw new InvalidOperationException(error);
            }

            return settings;
        }

        [MenuItem("Tools/CycloneGames/DataTable/Create Default Settings", priority = 2110)]
        public static DataTableLubanSettings CreateDefaultAsset()
        {
            try
            {
                return CreateDefaultAssetCore();
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                DataTableEditorDiagnostics.PublishException(
                    DataTableDiagnosticLevel.Error,
                    exception,
                    "DataTable Luban settings asset creation failed at '" +
                    DefaultAssetPath + "'.");
                throw;
            }
        }

        private static DataTableLubanSettings CreateDefaultAssetCore()
        {
            LoadStatus status = Load(out DataTableLubanSettings existing, out string error);
            if (status == LoadStatus.Loaded)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Info,
                    "DataTable Luban settings already exists at '" +
                    AssetDatabase.GetAssetPath(existing) + "'.");
                return existing;
            }

            if (status != LoadStatus.Missing)
            {
                throw new InvalidOperationException(error);
            }

            EnsureAssetDirectory(Path.GetDirectoryName(DefaultAssetPath)?.Replace('\\', '/'));
            if (AssetDatabase.LoadMainAssetAtPath(DefaultAssetPath) != null)
            {
                throw new InvalidOperationException(
                    "Default settings path is already occupied by another asset: " + DefaultAssetPath);
            }

            var settings = CreateInstance<DataTableLubanSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            Undo.RegisterCreatedObjectUndo(settings, "Create DataTable Luban Settings");
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            DataTableEditorDiagnostics.Publish(
                DataTableDiagnosticLevel.Info,
                "DataTable Luban settings created and saved at '" +
                DefaultAssetPath + "'.");
            return settings;
        }

        private void OnValidate()
        {
            schemaVersion = CurrentSchemaVersion;
            maximumCapturedOutputCharacters = Mathf.Clamp(
                maximumCapturedOutputCharacters,
                4096,
                16 * 1024 * 1024);
        }

        private static void EnsureAssetDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureAssetDirectory(parent);
            string folderName = Path.GetFileName(path);
            string guid = AssetDatabase.CreateFolder(parent ?? "Assets", folderName);
            if (string.IsNullOrEmpty(guid))
            {
                throw new InvalidOperationException("Failed to create settings directory: " + path);
            }
        }
    }
}
