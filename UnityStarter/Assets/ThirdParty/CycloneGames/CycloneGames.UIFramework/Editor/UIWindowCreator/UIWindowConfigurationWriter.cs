using System.IO;
using CycloneGames.Logger;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    internal static class UIWindowConfigurationWriter
    {
        private const string LogCategory = "UIWindowCreator";
        private const string SourcePropertyName = "source";
        private const string WindowPrefabPropertyName = "windowPrefab";
        private const string PrefabAssetRefPropertyName = "prefabAssetRef";
        private const string PrefabLocationPropertyName = "prefabLocation";
        private const string LayerPropertyName = "layer";
        private const string AssetRefLocationPropertyName = "m_Location";
        private const string AssetRefGuidPropertyName = "m_GUID";

        public static void Create(
            string configPath,
            GameObject prefab,
            UILayerConfiguration layer,
            UIWindowConfiguration.PrefabSource sourceMode,
            bool autoFillLocation)
        {
            EnsureAssetDirectoryExists(configPath);

            if (prefab == null)
            {
                CLogger.LogError("Cannot create UIWindowConfiguration: Prefab is null.", LogCategory);
                return;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject reloadedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (reloadedPrefab == null)
            {
                CLogger.LogError($"Cannot create UIWindowConfiguration: Failed to reload prefab at '{prefabPath}'.", LogCategory);
                return;
            }

            UIWindowConfiguration config = ScriptableObject.CreateInstance<UIWindowConfiguration>();
            SerializedObject serializedConfig = new SerializedObject(config);
            Apply(serializedConfig, reloadedPrefab, layer, sourceMode, autoFillLocation);

            AssetDatabase.CreateAsset(config, configPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            UIWindowConfiguration loadedConfig = AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(configPath);
            if (loadedConfig != null)
            {
                string details = loadedConfig.Source == UIWindowConfiguration.PrefabSource.PrefabReference
                    ? $"prefab='{loadedConfig.WindowPrefab?.name ?? "null"}'"
                    : $"location='{loadedConfig.EffectiveLocation}'";
                CLogger.LogInfo($"Created UIWindowConfiguration '{configPath}' with source mode: {loadedConfig.Source} ({details}).", LogCategory);
            }
        }

        public static bool UpdatePrefabReference(string configPath, string prefabPath)
        {
            UIWindowConfiguration config = AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(configPath);
            if (config == null || config.Source != UIWindowConfiguration.PrefabSource.PrefabReference)
            {
                CLogger.LogWarning($"Cannot update UIWindowConfiguration prefab reference. Config missing or not PrefabReference: '{configPath}'.", LogCategory);
                return false;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            UIWindow window = ResolveWindowComponent(prefab);
            if (window == null)
            {
                CLogger.LogWarning($"Cannot update UIWindowConfiguration prefab reference because prefab has no UIWindow component. Prefab='{prefabPath}'.", LogCategory);
                return false;
            }

            SerializedObject serializedConfig = new SerializedObject(config);
            SerializedProperty prefabProperty = serializedConfig.FindProperty(WindowPrefabPropertyName);
            if (prefabProperty == null)
            {
                CLogger.LogWarning($"Cannot update UIWindowConfiguration prefab reference because '{WindowPrefabPropertyName}' was not found. Config='{configPath}'.", LogCategory);
                return false;
            }

            if (prefabProperty.objectReferenceValue == window)
            {
                CLogger.LogInfo($"UIWindowConfiguration prefab reference already up to date for '{configPath}'.", LogCategory);
                return true;
            }

            prefabProperty.objectReferenceValue = window;
            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            CLogger.LogInfo($"Updated UIWindowConfiguration prefab reference. Config='{configPath}', Prefab='{prefabPath}'.", LogCategory);
            return true;
        }

        private static void Apply(
            SerializedObject serializedConfig,
            GameObject prefab,
            UILayerConfiguration layer,
            UIWindowConfiguration.PrefabSource sourceMode,
            bool autoFillLocation)
        {
            SerializedProperty sourceProperty = serializedConfig.FindProperty(SourcePropertyName);
            SerializedProperty prefabRefProperty = serializedConfig.FindProperty(WindowPrefabPropertyName);
            SerializedProperty assetRefProperty = serializedConfig.FindProperty(PrefabAssetRefPropertyName);
            SerializedProperty locationProperty = serializedConfig.FindProperty(PrefabLocationPropertyName);
            SerializedProperty layerProperty = serializedConfig.FindProperty(LayerPropertyName);

            sourceProperty.enumValueIndex = (int)sourceMode;

            string prefabAssetPath = AssetDatabase.GetAssetPath(prefab);
            string guid = AssetDatabase.AssetPathToGUID(prefabAssetPath);

            switch (sourceMode)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    UIWindow window = ResolveWindowComponent(prefab);
                    prefabRefProperty.objectReferenceValue = window;
                    if (window == null)
                    {
                        CLogger.LogInfo($"PrefabReference config created before generated UIWindow script is compiled. Prefab reference will be finalized by the post-compile processor. Prefab='{prefabAssetPath}'.", LogCategory);
                    }
                    ClearAssetRef(assetRefProperty);
                    locationProperty.stringValue = string.Empty;
                    break;

                case UIWindowConfiguration.PrefabSource.AssetReference:
                    prefabRefProperty.objectReferenceValue = null;
                    SetAssetRef(assetRefProperty, autoFillLocation ? prefabAssetPath : string.Empty, autoFillLocation ? guid : string.Empty);
                    locationProperty.stringValue = string.Empty;
                    break;

                case UIWindowConfiguration.PrefabSource.PathLocation:
                    prefabRefProperty.objectReferenceValue = null;
                    ClearAssetRef(assetRefProperty);
                    locationProperty.stringValue = autoFillLocation ? prefabAssetPath : string.Empty;
                    break;
            }

            layerProperty.objectReferenceValue = layer;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
        }

        private static UIWindow ResolveWindowComponent(GameObject prefab)
        {
            return prefab != null ? prefab.GetComponent<UIWindow>() : null;
        }

        private static void ClearAssetRef(SerializedProperty assetRefProperty)
        {
            SetAssetRef(assetRefProperty, string.Empty, string.Empty);
        }

        private static void SetAssetRef(SerializedProperty assetRefProperty, string location, string guid)
        {
            if (assetRefProperty == null)
            {
                return;
            }

            SerializedProperty locationProperty = assetRefProperty.FindPropertyRelative(AssetRefLocationPropertyName);
            SerializedProperty guidProperty = assetRefProperty.FindPropertyRelative(AssetRefGuidPropertyName);
            if (locationProperty != null)
            {
                locationProperty.stringValue = location;
            }
            if (guidProperty != null)
            {
                guidProperty.stringValue = guid;
            }
        }

        private static void EnsureAssetDirectoryExists(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath);
            if (Directory.Exists(directory))
            {
                return;
            }

            string parentDirectory = Path.GetDirectoryName(directory);
            string directoryName = Path.GetFileName(directory);
            if (!string.IsNullOrEmpty(parentDirectory) && AssetDatabase.IsValidFolder(parentDirectory))
            {
                AssetDatabase.CreateFolder(parentDirectory, directoryName);
            }
            else
            {
                Directory.CreateDirectory(directory);
            }

            AssetDatabase.Refresh();
        }
    }
}
