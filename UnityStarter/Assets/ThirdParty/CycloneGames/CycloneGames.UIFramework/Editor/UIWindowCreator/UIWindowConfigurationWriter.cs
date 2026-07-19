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
        private const string WindowIdPropertyName = "windowId";
        private const string SourcePropertyName = "source";
        private const string WindowPrefabPropertyName = "windowPrefab";
        private const string PrefabAssetRefPropertyName = "prefabAssetRef";
        private const string PrefabLocationPropertyName = "prefabLocation";
        private const string LayerPropertyName = "layer";
        private const string AssetRefLocationPropertyName = "location";
        private const string AssetRefGuidPropertyName = "editorGuid";

        public static string Create(
            string configPath,
            GameObject prefab,
            UILayerConfiguration layer,
            UIWindowConfiguration.PrefabSource sourceMode,
            bool autoFillLocation,
            string runtimeLocation)
        {
            EnsureAssetDirectoryExists(configPath);

            if (prefab == null)
            {
                throw new System.ArgumentNullException(nameof(prefab));
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject reloadedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (reloadedPrefab == null)
            {
                throw new System.InvalidOperationException(
                    $"Failed to reload UIWindow prefab at '{prefabPath}'.");
            }

            if (!UIWindowCreationValidator.TryEnsureOutputAvailable(
                    configPath,
                    ".asset",
                    out _,
                    out string initialCollisionError))
            {
                throw new IOException(
                    $"Configuration output is unavailable: {initialCollisionError}");
            }

            UIWindowConfiguration config = ScriptableObject.CreateInstance<UIWindowConfiguration>();
            string temporaryPath = string.Empty;
            string configGuid = string.Empty;
            bool movedToFinalPath = false;
            try
            {
                SerializedObject serializedConfig = new SerializedObject(config);
                Apply(serializedConfig, reloadedPrefab, layer, sourceMode, autoFillLocation, runtimeLocation);

                temporaryPath = AllocateTemporaryAssetPath(configPath);
                AssetDatabase.CreateAsset(config, temporaryPath);
                configGuid = AssetDatabase.AssetPathToGUID(temporaryPath);
                VerifyUnityAssetIdentity(temporaryPath, configGuid, "Temporary configuration");

                if (!UIWindowCreationValidator.TryEnsureOutputAvailable(
                        configPath,
                        ".asset",
                        out _,
                        out string collisionError))
                {
                    throw new IOException(
                        $"Configuration output became unavailable before commit: {collisionError}");
                }

                string moveError = AssetDatabase.MoveAsset(temporaryPath, configPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    throw new IOException(
                        $"Failed to commit configuration without replacing another asset: {moveError}");
                }

                movedToFinalPath = true;
                VerifyUnityAssetIdentity(configPath, configGuid, "Committed configuration");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                UIWindowConfiguration loadedConfig =
                    AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(configPath);
                if (loadedConfig == null)
                {
                    throw new System.InvalidOperationException(
                        $"Failed to create UIWindowConfiguration at '{configPath}'.");
                }

                string details = loadedConfig.Source == UIWindowConfiguration.PrefabSource.PrefabReference
                    ? $"prefab='{loadedConfig.WindowPrefab?.name ?? "null"}'"
                    : $"location='{loadedConfig.EffectiveAssetReference.Location}'";
                CLogger.LogInfo(
                    $"Created UIWindowConfiguration '{configPath}' with source mode: {loadedConfig.Source} ({details}).",
                    LogCategory);
                return configGuid;
            }
            catch (System.Exception exception)
            {
                string ownedPath = movedToFinalPath ? configPath : temporaryPath;
                if (!string.IsNullOrEmpty(ownedPath) &&
                    TryGetExistingAssetFile(ownedPath, out _))
                {
                    string cleanupError = "Configuration GUID was not captured.";
                    if (string.IsNullOrEmpty(configGuid) ||
                        !TryDeleteOwnedUnityAsset(ownedPath, configGuid, out cleanupError))
                    {
                        throw new UIWindowCreatorAssetCommitException(
                            exception.Message +
                            $" Cleanup preserved unverified configuration residual '{ownedPath}': {cleanupError}",
                            exception,
                            ownedPath);
                    }
                }

                throw;
            }
            finally
            {
                if (config != null && !EditorUtility.IsPersistent(config))
                {
                    Object.DestroyImmediate(config);
                }
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
            bool autoFillLocation,
            string runtimeLocation)
        {
            SerializedProperty sourceProperty = serializedConfig.FindProperty(SourcePropertyName);
            SerializedProperty windowIdProperty = serializedConfig.FindProperty(WindowIdPropertyName);
            SerializedProperty prefabRefProperty = serializedConfig.FindProperty(WindowPrefabPropertyName);
            SerializedProperty assetRefProperty = serializedConfig.FindProperty(PrefabAssetRefPropertyName);
            SerializedProperty locationProperty = serializedConfig.FindProperty(PrefabLocationPropertyName);
            SerializedProperty layerProperty = serializedConfig.FindProperty(LayerPropertyName);

            if (sourceProperty == null || windowIdProperty == null || prefabRefProperty == null ||
                assetRefProperty == null || locationProperty == null || layerProperty == null)
            {
                throw new System.InvalidOperationException(
                    "UIWindowConfiguration serialized schema does not match the creator.");
            }

            sourceProperty.enumValueIndex = (int)sourceMode;
            windowIdProperty.stringValue = prefab.name;

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
                    SetAssetRef(assetRefProperty, runtimeLocation?.Trim() ?? string.Empty, guid);
                    locationProperty.stringValue = string.Empty;
                    break;

                case UIWindowConfiguration.PrefabSource.PathLocation:
                    prefabRefProperty.objectReferenceValue = null;
                    ClearAssetRef(assetRefProperty);
                    locationProperty.stringValue = autoFillLocation
                        ? prefabAssetPath
                        : runtimeLocation?.Trim() ?? string.Empty;
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

        private static string AllocateTemporaryAssetPath(string finalAssetPath)
        {
            string directory = Path.GetDirectoryName(finalAssetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
            {
                throw new System.InvalidOperationException(
                    $"Asset path '{finalAssetPath}' has no parent folder.");
            }

            for (int attempt = 0; attempt < 32; attempt++)
            {
                string candidate =
                    directory + "/__UIWindowCreator_" + System.Guid.NewGuid().ToString("N") + ".asset";
                if (UIWindowCreationValidator.TryEnsureOutputAvailable(
                        candidate,
                        ".asset",
                        out _,
                        out _))
                {
                    return candidate;
                }
            }

            throw new IOException(
                $"Could not allocate a unique temporary asset beside '{finalAssetPath}'.");
        }

        private static void VerifyUnityAssetIdentity(
            string assetPath,
            string expectedGuid,
            string label)
        {
            string currentGuid = AssetDatabase.AssetPathToGUID(assetPath);
            string resolvedPath = AssetDatabase.GUIDToAssetPath(expectedGuid);
            if (string.IsNullOrEmpty(expectedGuid) ||
                !string.Equals(currentGuid, expectedGuid, System.StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resolvedPath, assetPath, System.StringComparison.Ordinal))
            {
                throw new System.InvalidOperationException(
                    $"{label} identity validation failed. Expected GUID '{expectedGuid}' at " +
                    $"'{assetPath}', current GUID='{currentGuid}', current path='{resolvedPath}'.");
            }
        }

        private static bool TryDeleteOwnedUnityAsset(
            string assetPath,
            string expectedGuid,
            out string error)
        {
            error = string.Empty;
            string currentGuid = AssetDatabase.AssetPathToGUID(assetPath);
            string resolvedPath = AssetDatabase.GUIDToAssetPath(expectedGuid);
            if (string.IsNullOrEmpty(expectedGuid) ||
                !string.Equals(currentGuid, expectedGuid, System.StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resolvedPath, assetPath, System.StringComparison.Ordinal))
            {
                error =
                    $"Ownership mismatch. Expected GUID '{expectedGuid}', current GUID='{currentGuid}', " +
                    $"current path='{resolvedPath}'.";
                return false;
            }

            try
            {
                if (!AssetDatabase.DeleteAsset(assetPath))
                {
                    error = "AssetDatabase refused to delete the owned asset.";
                    return false;
                }
            }
            catch (System.Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (TryGetExistingAssetFile(assetPath, out _) ||
                string.Equals(
                    AssetDatabase.GUIDToAssetPath(expectedGuid),
                    assetPath,
                    System.StringComparison.Ordinal))
            {
                error = "Owned asset still exists after deletion.";
                return false;
            }

            return true;
        }

        private static bool TryGetExistingAssetFile(string assetPath, out string absolutePath)
        {
            return UIWindowCreationValidator.TryGetAbsoluteAssetPath(
                       assetPath,
                       out absolutePath,
                       out _) &&
                   (File.Exists(absolutePath) || File.Exists(absolutePath + ".meta"));
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
