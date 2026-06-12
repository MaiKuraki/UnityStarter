using CycloneGames.Logger;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    internal static class UIWindowPrefabScriptBinder
    {
        private const string LogCategory = "UIWindowCreator";

        public static bool AddScriptComponentToPrefab(string prefabPath, System.Type scriptType, string scriptName)
        {
            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (savedPrefab == null || scriptType == null)
            {
                CLogger.LogWarning($"Cannot add {scriptName} component: prefab or script type is missing. PrefabPath='{prefabPath}'.", LogCategory);
                return false;
            }

            if (savedPrefab.GetComponent(scriptType) != null)
            {
                CLogger.LogInfo($"{scriptName} component already exists on prefab '{prefabPath}'.", LogCategory);
                return true;
            }

            string prefabPathFull = AssetDatabase.GetAssetPath(savedPrefab);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPathFull);

            if (prefabRoot == null)
            {
                CLogger.LogWarning($"Failed to load prefab contents for '{prefabPathFull}'.", LogCategory);
                return false;
            }

            try
            {
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
                CLogger.LogInfo($"Successfully added {scriptName} component to prefab '{prefabPath}'.", LogCategory);
                return true;
            }

            CLogger.LogWarning($"Failed to add {scriptName} component to prefab '{prefabPath}'.", LogCategory);
            return false;
        }
    }
}
