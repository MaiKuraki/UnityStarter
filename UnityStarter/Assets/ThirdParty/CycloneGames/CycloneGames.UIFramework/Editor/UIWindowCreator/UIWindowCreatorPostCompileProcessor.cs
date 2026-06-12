using System;
using CycloneGames.Logger;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;

namespace CycloneGames.UIFramework.Editor
{
    [InitializeOnLoad]
    internal static class UIWindowCreatorPostCompileProcessor
    {
        private const string LogCategory = "UIWindowCreator";
        private const string PendingKey = "CycloneGames.UIFramework.UIWindowCreator.Pending";
        private const string ScriptNameKey = "CycloneGames.UIFramework.UIWindowCreator.ScriptName";
        private const string NamespaceNameKey = "CycloneGames.UIFramework.UIWindowCreator.NamespaceName";
        private const string PrefabPathKey = "CycloneGames.UIFramework.UIWindowCreator.PrefabPath";
        private const string ConfigPathKey = "CycloneGames.UIFramework.UIWindowCreator.ConfigPath";
        private const string SourceModeKey = "CycloneGames.UIFramework.UIWindowCreator.SourceMode";
        private const string AttemptsKey = "CycloneGames.UIFramework.UIWindowCreator.Attempts";
        private const int MaxAttempts = 100;
        private const double CheckIntervalSeconds = 0.2d;

        private static double nextCheckTime;

        static UIWindowCreatorPostCompileProcessor()
        {
            EditorApplication.delayCall += ResumeIfPending;
        }

        public static void Schedule(
            string scriptName,
            string namespaceName,
            string prefabPath,
            string configPath,
            UIWindowConfiguration.PrefabSource sourceMode)
        {
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(ScriptNameKey, scriptName ?? string.Empty);
            SessionState.SetString(NamespaceNameKey, namespaceName ?? string.Empty);
            SessionState.SetString(PrefabPathKey, prefabPath ?? string.Empty);
            SessionState.SetString(ConfigPathKey, configPath ?? string.Empty);
            SessionState.SetInt(SourceModeKey, (int)sourceMode);
            SessionState.SetInt(AttemptsKey, 0);

            CLogger.LogInfo($"Scheduled post-compile binding for UIWindow '{scriptName}'. Prefab='{prefabPath}', Config='{configPath}', Source={sourceMode}.", LogCategory);
            StartPolling();
        }

        private static void ResumeIfPending()
        {
            if (!SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            CLogger.LogInfo("Resuming pending UIWindow post-compile binding after editor reload.", LogCategory);
            StartPolling();
        }

        private static void StartPolling()
        {
            EditorApplication.update -= OnEditorUpdate;
            nextCheckTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            if (!SessionState.GetBool(PendingKey, false))
            {
                EditorApplication.update -= OnEditorUpdate;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime < nextCheckTime)
            {
                return;
            }

            nextCheckTime = currentTime + CheckIntervalSeconds;

            int attempts = SessionState.GetInt(AttemptsKey, 0) + 1;
            SessionState.SetInt(AttemptsKey, attempts);

            string scriptName = SessionState.GetString(ScriptNameKey, string.Empty);
            string namespaceName = SessionState.GetString(NamespaceNameKey, string.Empty);
            string prefabPath = SessionState.GetString(PrefabPathKey, string.Empty);
            string configPath = SessionState.GetString(ConfigPathKey, string.Empty);
            var sourceMode = (UIWindowConfiguration.PrefabSource)SessionState.GetInt(
                SourceModeKey,
                (int)UIWindowConfiguration.PrefabSource.PrefabReference);

            Type scriptType = FindScriptType(scriptName, namespaceName);
            if (scriptType != null)
            {
                CompletePendingBinding(scriptName, prefabPath, configPath, sourceMode, scriptType);
                return;
            }

            if (attempts >= MaxAttempts)
            {
                EditorApplication.update -= OnEditorUpdate;
                ClearPending();
                CLogger.LogWarning($"Timed out while waiting for generated UIWindow script '{scriptName}'. Check Console compile errors, then add the component manually if needed.", LogCategory);
            }
        }

        private static void CompletePendingBinding(
            string scriptName,
            string prefabPath,
            string configPath,
            UIWindowConfiguration.PrefabSource sourceMode,
            Type scriptType)
        {
            EditorApplication.update -= OnEditorUpdate;

            bool scriptAdded = UIWindowPrefabScriptBinder.AddScriptComponentToPrefab(prefabPath, scriptType, scriptName);
            if (scriptAdded && sourceMode == UIWindowConfiguration.PrefabSource.PrefabReference)
            {
                bool referenceUpdated = UIWindowConfigurationWriter.UpdatePrefabReference(configPath, prefabPath);
                if (!referenceUpdated)
                {
                    CLogger.LogWarning($"UIWindowConfiguration prefab reference was not updated for '{scriptName}'. Config='{configPath}', Prefab='{prefabPath}'.", LogCategory);
                }
            }

            if (scriptAdded)
            {
                CLogger.LogInfo($"Completed post-compile binding for UIWindow '{scriptName}'.", LogCategory);
            }

            ClearPending();
            AssetDatabase.Refresh();
        }

        private static Type FindScriptType(string scriptName, string namespaceName)
        {
            if (string.IsNullOrEmpty(scriptName))
            {
                return null;
            }

            string fullTypeName = string.IsNullOrEmpty(namespaceName) ? scriptName : namespaceName + "." + scriptName;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type scriptType = assemblies[i].GetType(fullTypeName);
                if (scriptType != null)
                {
                    return scriptType;
                }
            }

            return null;
        }

        private static void ClearPending()
        {
            SessionState.EraseBool(PendingKey);
            SessionState.EraseString(ScriptNameKey);
            SessionState.EraseString(NamespaceNameKey);
            SessionState.EraseString(PrefabPathKey);
            SessionState.EraseString(ConfigPathKey);
            SessionState.EraseInt(SourceModeKey);
            SessionState.EraseInt(AttemptsKey);
        }
    }
}
