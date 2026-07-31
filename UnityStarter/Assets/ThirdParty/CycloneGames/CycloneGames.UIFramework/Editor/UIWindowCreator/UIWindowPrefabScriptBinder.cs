using CycloneGames.Logging;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    internal static class UIWindowPrefabScriptBinder
    {
        private static readonly LogChannel Log = UIFrameworkEditorLog.Channel;

        public static bool AddScriptComponentToPrefab(string prefabPath, System.Type scriptType, string scriptName)
        {
            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (savedPrefab == null || scriptType == null)
            {
                Log.Warning($"Cannot add {scriptName} component: prefab or script type is missing. PrefabPath='{prefabPath}'.");
                return false;
            }

            if (savedPrefab.GetComponent(scriptType) != null)
            {
                Log.Info($"{scriptName} component already exists on prefab '{prefabPath}'.");
                return true;
            }

            string prefabPathFull = AssetDatabase.GetAssetPath(savedPrefab);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPathFull);

            if (prefabRoot == null)
            {
                Log.Warning($"Failed to load prefab contents for '{prefabPathFull}'.");
                return false;
            }

            try
            {
                UIWindow placeholder = prefabRoot.GetComponent<UIWindow>();
                if (placeholder != null && placeholder.GetType() == typeof(UIWindow) && scriptType != typeof(UIWindow))
                {
                    Object.DestroyImmediate(placeholder, true);
                }

                prefabRoot.AddComponent(scriptType);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPathFull);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            AssetDatabase.Refresh();

            savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (savedPrefab != null && savedPrefab.GetComponent(scriptType) != null)
            {
                Log.Info($"Successfully added {scriptName} component to prefab '{prefabPath}'.");
                return true;
            }

            Log.Warning($"Failed to add {scriptName} component to prefab '{prefabPath}'.");
            return false;
        }
    }
}
