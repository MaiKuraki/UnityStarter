using System.IO;
using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logging;

namespace CycloneGames.GameplayAbilities.Editor
{
    [CustomEditor(typeof(GASOverlayConfig))]
    public class GASOverlayConfigEditor : UnityEditor.Editor
    {
        private static readonly LogChannel Log = GameplayAbilitiesEditorLog.Channel;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "GAS Debug Overlay Configuration\n" +
                "This asset can live anywhere in the project. Bind it with GASDebugOverlay.ActiveConfiguration, " +
                "or pass it to Initialize(enableAtStart, dontDestroyOnLoad, configuration).\n" +
                "When no configuration is supplied, the overlay uses GASOverlayConfig defaults.\n" +
                "Toggle overlay in Play Mode: Tools > CycloneGames > GameplayAbilities > Overlay > Toggle In Play Mode",
                MessageType.Info);

            EditorGUILayout.Space(4);
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Preview Tag Colors"))
            {
                var config = (GASOverlayConfig)target;
                Log.Info($"GAS overlay config: {config.SemanticTagClassifications.Count} semantic tag classification(s), " +
                         $"{config.DebuffTagSubstrings.Count} debuff substring(s), " +
                         $"Panel alpha={config.PanelAlpha:F2}, MaxPanels={config.MaxPanels}, " +
                         $"TrackWorld={config.TrackWorldPosition}");
            }

            serializedObject.ApplyModifiedProperties();
        }

        [MenuItem(GameplayAbilitiesEditorMenuPaths.OverlayConfig)]
        public static void SelectOrCreateConfig()
        {
            // Select an existing configuration regardless of where it is stored.
            var guids = AssetDatabase.FindAssets("t:GASOverlayConfig");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<GASOverlayConfig>(path);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                return;
            }

            string folder = ResolveCreationFolder();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/GASOverlayConfig.asset");
            var config = ScriptableObject.CreateInstance<GASOverlayConfig>();
            AssetDatabase.CreateAsset(config, assetPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            Log.Info($"Created GAS overlay config at: {assetPath}. " +
                     "Assign it to GASDebugOverlay.ActiveConfiguration or pass it to Initialize(...).");
        }

        private static string ResolveCreationFolder()
        {
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path)) return path;

                string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent))
                {
                    return parent;
                }
            }

            const string defaultFolder = "Assets/Settings";
            if (!AssetDatabase.IsValidFolder(defaultFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }
            return defaultFolder;
        }
    }
}
