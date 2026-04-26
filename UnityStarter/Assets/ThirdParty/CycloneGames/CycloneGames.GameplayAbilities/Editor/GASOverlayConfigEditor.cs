using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Editor
{
    [CustomEditor(typeof(GASOverlayConfig))]
    public class GASOverlayConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "GAS Debug Overlay Configuration\n" +
                "Place this asset in a Resources folder (named 'GASOverlayConfig') for auto-loading.\n" +
                "Toggle overlay in Play Mode: Tools > CycloneGames > GAS Overlay",
                MessageType.Info);

            EditorGUILayout.Space(4);
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Preview Tag Colors"))
            {
                var config = (GASOverlayConfig)target;
                Debug.Log($"[GAS Overlay Config] {config.SemanticTagClassifications.Count} semantic tag classification(s), " +
                          $"{config.DebuffTagSubstrings.Count} debuff substring(s), " +
                          $"Panel alpha={config.PanelAlpha:F2}, MaxPanels={config.MaxPanels}, " +
                          $"TrackWorld={config.TrackWorldPosition}");
            }

            serializedObject.ApplyModifiedProperties();
        }

        [MenuItem("Tools/CycloneGames/GameplayAbilities/GAS Overlay Config")]
        public static void SelectOrCreateConfig()
        {
            // Try find existing asset in Resources
            var guids = AssetDatabase.FindAssets("t:GASOverlayConfig");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<GASOverlayConfig>(path);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                return;
            }

            // Create new one in a Resources folder near the Runtime assembly
            string dir = "Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime/Resources";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                string parent = "Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime";
                AssetDatabase.CreateFolder(parent, "Resources");
            }

            var config = ScriptableObject.CreateInstance<GASOverlayConfig>();
            string assetPath = dir + "/GASOverlayConfig.asset";
            AssetDatabase.CreateAsset(config, assetPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            Debug.Log($"[GAS] Created overlay config at: {assetPath}");
        }
    }
}
