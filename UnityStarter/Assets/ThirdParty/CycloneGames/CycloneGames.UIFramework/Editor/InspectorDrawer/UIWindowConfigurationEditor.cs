using UnityEditor;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.UIFramework.Runtime
{
    [CustomEditor(typeof(UIWindowConfiguration))]
    public sealed class UIWindowConfigurationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("layer"));

            var sourceProp = serializedObject.FindProperty("source");
            EditorGUILayout.PropertyField(sourceProp);

            var src = (UIWindowConfiguration.PrefabSource)sourceProp.enumValueIndex;
            if (src == UIWindowConfiguration.PrefabSource.PrefabReference)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("windowPrefab"));
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("prefabLocation"));
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Validate Location", GUILayout.Width(160)))
                    {
                        ValidateLocation();
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Select the PrefabSource to avoid ambiguous configuration. Use PrefabReference for direct prefab, or Location for asset-system loading.", MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }

        private void ValidateLocation()
        {
            var cfg = target as UIWindowConfiguration;
            if (cfg == null) return;
            if (string.IsNullOrEmpty(cfg.PrefabLocation))
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration", "PrefabLocation is empty.", "OK");
                return;
            }
            var pkg = AssetManagementLocator.DefaultPackage;
            if (pkg == null)
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration", "DefaultPackage is null. Initialize AssetManagement and assign DefaultPackage.", "OK");
                return;
            }
            // Weak validation: try to start an async load and immediately check error after first yield (Editor only hint)
            var handle = pkg.LoadAssetAsync<GameObject>(cfg.PrefabLocation);
            EditorApplication.delayCall += () =>
            {
                if (!handle.IsDone)
                {
                    // Not finished yet: schedule another check; keep it light to avoid blocking editor
                    EditorApplication.delayCall += () => FinalizeValidation(handle, cfg.PrefabLocation);
                }
                else
                {
                    FinalizeValidation(handle, cfg.PrefabLocation);
                }
            };
        }

        private void FinalizeValidation(IAssetHandle<GameObject> handle, string location)
        {
            if (handle == null)
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration", "Handle null during validation.", "OK");
                return;
            }
            string msg = string.IsNullOrEmpty(handle.Error) && handle.Asset != null
                ? $"Location OK: {location}"
                : $"Location FAILED: {location}\nError: {handle.Error}";
            handle.Dispose();
            EditorUtility.DisplayDialog("UIWindowConfiguration", msg, "OK");
        }
    }
}