using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Utility;
using UnityEditor;
using UnityEngine;

namespace AdvancedSceneManager.Utility
{

    static class LogUtility
    {

        #region Startup

        public static string StartupBeginMessage() => "-- Startup: starting --";
        public static string StartupCompleteMessage() => "-- Startup: complete --";

        public static void LogStartupBegin()
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logStartup)
                Debug.Log(StartupBeginMessage());
#endif
        }

        public static void LogStartupEnd()
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logStartup)
                Debug.Log(StartupCompleteMessage());
#endif
        }

        #endregion
        #region Tracked

        public static string TrackedMessage(Scene scene) => $"Tracked: {scene.path}";
        public static string UntrackedMessage(Scene scene) => $"Untracked: {scene.path}";

        public static string TrackedMessage(SceneCollection collection, bool isAdditive) => $"Tracked: {collection.name} {(isAdditive ? "(additive)" : "")}";
        public static string UntrackedMessage(SceneCollection collection, bool isAdditive) => $"Untracked: {collection.name} {(isAdditive ? "(additive)" : "")}";

        public static void LogTracked(Scene scene)
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logTracking)
                Debug.Log(TrackedMessage(scene));
#endif
        }

        public static void LogUntracked(Scene scene)
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logTracking)
                Debug.Log(UntrackedMessage(scene));
#endif
        }

        public static void LogTracked(SceneCollection collection, bool isAdditive = false)
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logTracking)
                Debug.Log(TrackedMessage(collection, isAdditive));
#endif
        }

        public static void LogUntracked(SceneCollection collection, bool isAdditive = false)
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logTracking)
                Debug.Log(UntrackedMessage(collection, isAdditive));
#endif
        }

        #endregion
        #region Loaded

        public static string LoadedMessage(SceneLoader loader, SceneLoadArgs e) =>
            e.isPreload
            ? $"Preloading scene ({loader.GetType().Name}):\n{e.scene.path}"
            : $"Loading scene ({loader.GetType().Name}):\n{e.scene.path}";

        public static string UnloadedMessage(SceneLoader loader, SceneUnloadArgs e) =>
            $"Unloading scene ({loader.GetType().Name}):\n{e.scene.path}";

        public static void LogLoaded(SceneLoader loader, SceneLoadArgs e)
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logLoading)
                Debug.Log(LoadedMessage(loader, e));
#endif
        }

        public static void LogUnloaded(SceneLoader loader, SceneUnloadArgs e)
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logLoading)
                Debug.Log(UnloadedMessage(loader, e));
#endif
        }

        #endregion
        #region Scene import

#if UNITY_EDITOR

        public static void LogImported(IEnumerable<Scene> list)
        {
            if (SceneManager.settings.user.logImport)
                Debug.Log($"Imported {list.Count()} scenes:\n{string.Join("\n", list.Select(s => s.path))}\n");
        }

        public static void LogImported(string path)
        {
            if (SceneManager.settings.user.logImport)
            {
                Debug.Log($"Imported 1 scenes:\n{path}\n");
            }
        }

        public static void LogUnimported(IEnumerable<Scene> list)
        {
            if (SceneManager.settings.user.logImport)
                Debug.Log($"Unimported {list.Count()} scenes:\n{string.Join("\n", list.Select(s => s.path))}\n");
        }

        public static void LogUnimported(string path)
        {
            if (SceneManager.settings.user.logImport)
                Debug.Log($"Unimported 1 scenes:\n{path}\n");
        }

#endif

        #endregion
        #region Asset import

#if UNITY_EDITOR

        static readonly Dictionary<Type, string> displayName = new()
        {
            { typeof(Profile), "profiles" },
            { typeof(SceneCollectionTemplate), "templates" },
            { typeof(SceneCollection), "collections" },
        };

        public static void LogImport<T>(string action, IEnumerable<string> paths)
        {

            if (!SceneManager.settings.user || !SceneManager.settings.user.logImport)
                return;

            //Logging for scenes is done in SceneImportUtility
            if (typeof(T) == typeof(Scene))
                return;

            var name = displayName.GetValueOrDefault(typeof(T), "unknown model types");
            Debug.Log($"{action} {paths.Count()} {name}:\n{string.Join("\n", paths)}\n");

        }

#endif

        #endregion
        #region Scene operation

        public static void LogStart(SceneOperation sceneOperation)
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logOperation)
                Debug.Log($"" +
                $"-- Scene operation started --\n" +
                $"Close:\n{(sceneOperation.close.Any() ? string.Join("\n", sceneOperation.close.Select(s => s.path)) : "none")}\n\n" +
                $"Open:\n{(sceneOperation.open.Any() ? string.Join("\n", sceneOperation.open.Select(s => s.path)) : "none")}\n");
#endif
        }

        public static void LogEnd(SceneOperation sceneOperation)
        {
#if UNITY_EDITOR
            if (SceneManager.settings.user.logOperation)
                Debug.Log($"" +
                $"-- Scene operation {(sceneOperation.wasCancelled ? "cancelled" : "finished")} --\n" +
                $"Close:\n{(sceneOperation.closedScenes.Any() ? string.Join("\n", sceneOperation.closedScenes.Select(s => s.path)) : "none")}\n\n" +
                $"Open:\n{(sceneOperation.openedScenes.Any() ? string.Join("\n", sceneOperation.openedScenes.Select(s => s.path)) : "none")}\n");
#endif
        }

        #endregion
        #region Build scenes

#if UNITY_EDITOR
        public static void LogBuildScenes(IEnumerable<EditorBuildSettingsScene> list)
        {
            if (SceneManager.settings.user.logBuildScenes)
                Debug.Log($"Build scenes updated:\n{string.Join("\n", list.Select(s => (s.enabled ? "[X]" : "[  ]") + s.path))}");
        }
#endif

        #endregion

        public static void Log<T>(this IEnumerable<T> list, string header = null, string separator = "\n", bool logWithNoItems = false)
        {

            var items = list?.ToArray();

            if (items?.Length == 0 && !logWithNoItems)
                return;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(header))
                sb.Append(header);

            if (items is null || items.Length == 0)
                sb.AppendLine("No items");
            else
                sb.Append(string.Join(separator, list));

            Debug.Log(sb.ToString());

        }

    }

}
