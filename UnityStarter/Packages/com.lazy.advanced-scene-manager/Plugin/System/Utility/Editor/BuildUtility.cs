#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AdvancedSceneManager.Editor.Utility
{

    /// <summary>Provides functions for building, and build events.</summary>
    /// <remarks>Only available in editor.</remarks>
    public static class BuildUtility
    {

        /// <summary>Specifies a reason as to why a scene is included or excluded from build.</summary>
        public enum Reason
        {
            Default, InvalidScene, NotIncludedInProfile, IncludedInProfile, DynamicScene, Addressable, SceneLoaderOverride
        }

        internal static void Initialize()
        {

            if (!Application.isPlaying)
                BuildPlayerWindow.RegisterBuildPlayerHandler(e => _ = DoBuild(e));

            EditorBuildSettings.sceneListChanged += OnBuildSettingsChanged;
            SceneImportUtility.scenesChanged += UpdateSceneList;

            UpdateSceneList();

        }

        #region Scene list

        #region On scene list changed

        static readonly List<object> isUpdatingBuildSettings = new();
        static void OnBuildSettingsChanged()
        {

            if (isUpdatingBuildSettings.Any())
                return;

            if (!Profile.current)
            {
                Debug.Log("Please do not modify build list manually when no profile is active.");
                UpdateSceneList();
                return;
            }

            var oldScenes = GetOrderedList().Select(s => s.buildScene);
            var newScenes = EditorBuildSettings.scenes;

            GetDiff(oldScenes, newScenes, out var added, out var removed, out var modified);

            //added.Select(s => s.path).Log("added:");
            //removed.Select(s => s.path).Log("removed:");
            //modified.Select(s => s.path).Log("modified:");

            foreach (var scene in added)
                if (Scene.TryFind(scene.path, out var s))
                    Profile.current.standaloneScenes.Add(s);

            foreach (var scene in modified)
                if (Scene.TryFind(scene.path, out var s))
                    if (scene.enabled)
                        Profile.current.standaloneScenes.Add(s);

            UpdateSceneList();

        }

        /// <summary>Gets the scenes that was modified.</summary>
        static void GetDiff(IEnumerable<EditorBuildSettingsScene> oldScenes, IEnumerable<EditorBuildSettingsScene> newScenes, out IEnumerable<EditorBuildSettingsScene> added, out IEnumerable<EditorBuildSettingsScene> removed, out IEnumerable<EditorBuildSettingsScene> modified)
        {
            added = newScenes.Where(oldScenes.DoesNotHaveIt);
            removed = oldScenes.Where(newScenes.DoesNotHaveIt);
            modified = newScenes.Where(oldScenes.HasButDifferentToggle);
        }

        static bool HasButDifferentToggle(this IEnumerable<EditorBuildSettingsScene> list, EditorBuildSettingsScene scene) =>
            list.Any(s => s.path == scene.path && s.enabled != scene.enabled);

        static bool DoesNotHaveIt(this IEnumerable<EditorBuildSettingsScene> list, EditorBuildSettingsScene scene) =>
            !list.Any(s => s.path == scene.path);

        #endregion

        /// <summary>Updates the scene build settings.</summary>
        /// <param name="force">If <see langword="true"/>, then <see cref="ASMSettings.autoUpdateBuildScenes"/> will be ignored.</param>
        public static void UpdateSceneList() =>
            UpdateSceneList(false);

        /// <summary>Updates the scene build settings.</summary>
        /// <param name="force">If <see langword="true"/>, then <see cref="ASMSettings.autoUpdateBuildScenes"/> will be ignored.</param>
        public static void UpdateSceneList(bool ignorePlaymodeCheck, bool force = false)
        {

            if (!Profile.current)
                return;

            if (!ignorePlaymodeCheck && Application.isPlaying)
                return;

            if (!SceneManager.settings.project.autoUpdateBuildScenes && !force)
                return;

            var o = new object();
            isUpdatingBuildSettings.Add(o);

            var buildScenes = GetOrderedList().Select(s => s.buildScene).ToList();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(FallbackSceneUtility.GetStartupScene()))
                buildScenes.Insert(0, new(FallbackSceneUtility.GetStartupScene(), true));

            var list = buildScenes.GroupBy(s => s.path).Select(g => g.First()).ToArray();
            if (!EditorBuildSettings.scenes.SequenceEqual(list))
            {
                LogUtility.LogBuildScenes(list);
                EditorBuildSettings.scenes = list;
            }

            _ = isUpdatingBuildSettings.Remove(o);

        }

        /// <summary>Get an ordered list of all scenes that would be set as scene build settings.</summary>
        public static IEnumerable<(EditorBuildSettingsScene buildScene, Reason reason)> GetOrderedList()
        {

            if (!Profile.current)
                return Array.Empty<(EditorBuildSettingsScene buildScene, Reason reason)>();

            var scenes = Profile.current.scenes.
                Where(s => s.sceneAsset || s.isDefaultASMScene).
                Select(s => (scene: s, isIncluded: IsIncluded(s, out _))).
                Where(s => s.isIncluded).
                Select(s => s.scene.path).
                Concat(Profile.current.dynamicCollections.SelectMany(s => s.scenePaths)).
                Distinct();

            var buildScenes =
                scenes.
                Select(path =>
                {

                    var enabled = IsEnabled(path, out var reason);
                    return (
                        buildScene: new EditorBuildSettingsScene(path, enabled),
                        reason);

                }).
                OrderByDescending(s => s.buildScene.enabled);

            return buildScenes;

        }

        /// <summary>Gets the inclusion and enabled state of a scene.</summary>
        public static bool IsIncluded(Scene scene, out Reason reason)
        {

            if (!scene || string.IsNullOrWhiteSpace(scene.path))
            {
                reason = Reason.InvalidScene;
                return false;
            }
            else if (!scene.GetSceneLoader()?.addScenesToBuildSettings ?? false)
            {
                reason = Reason.SceneLoaderOverride;
                return false;
            }
            else if (Profile.current && Profile.current.scenes.Contains(scene))
            {
                reason = Reason.IncludedInProfile;
                return true;
            }
            else
            {
                reason = Reason.NotIncludedInProfile;
                return false;
            }

        }

        public static bool IsEnabled(string path, out Reason reason)
        {

            if (string.IsNullOrWhiteSpace(path))
            {
                reason = Reason.InvalidScene;
                return false;
            }

            reason = Reason.Default;
            return true;

        }

        #endregion
        #region Build

        /// <summary>Performs a build of your project.</summary>
        /// <param name="path">Specifies the target path of the .exe to build.</param>
        /// <param name="attachProfiler">Specifies whatever we should attach the profiler.</param>
        /// <param name="runGameWhenBuilt">Specifies whatever to run the game after it has been built.</param>
        public static BuildReport DoBuild(string path, bool attachProfiler = false, bool runGameWhenBuilt = false, bool dev = true, BuildOptions customOptions = BuildOptions.None)
        {

            var options =
                customOptions |
                (dev ? BuildOptions.Development : BuildOptions.None) |
                (runGameWhenBuilt ? BuildOptions.AutoRunPlayer : BuildOptions.None) |
                (attachProfiler ? BuildOptions.ConnectWithProfiler : BuildOptions.None);

            return DoBuild(new BuildPlayerOptions()
            {
                locationPathName = path,
                options = options,
                target = EditorUserBuildSettings.activeBuildTarget,
                targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup,
                scenes = EditorBuildSettings.scenes.Select(s => s.path).ToArray()
            });

        }

        /// <inheritdoc cref="BuildPipeline.BuildPlayer(BuildPlayerOptions)"/>
        public static BuildReport DoBuild(BuildPlayerOptions options)
        {

            if (BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Cannot start build when already building.");

            var report = BuildPipeline.BuildPlayer(options);
            BuildEvents.instance.OnAfterASMBuild(report);
            return report;

        }

        #endregion
        #region Build events

        /// <summary>Occurs before build.</summary>
        public static event Action<BuildReport> preBuild;

        /// <summary>Occurs after build.</summary>
        public static event Action<PostBuildEventArgs> postBuild;

        /// <summary>Represents a post build summary.</summary>
        public struct PostBuildEventArgs
        {

            /// <summary>Gets the report generated from the build.</summary>
            public BuildReport report;

            /// <summary>Gets the warnings that occured during the build.</summary>
            public (string condition, string stacktrace)[] warning;

            /// <summary>Gets the errors that occured during the build.</summary>
            public (string condition, string stacktrace)[] error;

            public PostBuildEventArgs(BuildReport report, (string condition, string stacktrace)[] warning, (string condition, string stacktrace)[] error)
            {
                this.report = report;
                this.error = error;
                this.warning = warning;
            }

        }

        class BuildEvents : IPreprocessBuildWithReport, IPostprocessBuildWithReport
        {

            public static BuildEvents instance;

            public BuildEvents() => instance = this;

            public int callbackOrder => int.MinValue;

            public void OnPreprocessBuild(BuildReport e)
            {
                SetupListener();
                preBuild?.Invoke(e);
            }

            public void OnPostprocessBuild(BuildReport e)
            {
                StopListener(out var warnings, out var errors);
                postBuild?.Invoke(new(e, warnings, errors));
            }

            public void OnAfterASMBuild(BuildReport report)
            {
                if (hasListener)
                    OnPostprocessBuild(report);
            }

            #region Log listener

            bool hasListener;
            readonly List<(string condition, string stacktrace, LogType type)> logs = new();

            void SetupListener()
            {
                hasListener = true;
                Application.logMessageReceived += LogMessageReceived;
            }

            void StopListener(out (string condition, string stacktrace)[] warnings, out (string condition, string stacktrace)[] errors)
            {

                hasListener = false;
                Application.logMessageReceived -= LogMessageReceived;

                warnings = logs.Where(l => l.type == LogType.Warning).Select(l => (l.condition, l.stacktrace)).ToArray();
                errors = logs.Where(l => l.type == LogType.Error).Select(l => (l.condition, l.stacktrace)).ToArray();
                logs.Clear();

            }

            void LogMessageReceived(string condition, string stacktrace, LogType type) =>
                logs.Add((condition, stacktrace, type));

            #endregion

        }

        #endregion

    }
}
#endif
