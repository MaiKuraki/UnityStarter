#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using scene = UnityEngine.SceneManagement.Scene;

namespace AdvancedSceneManager.Utility.CrossSceneReferences
{

    /// <summary>Manages editor functionality.</summary>
    static class Editor
    {

        static Editor()
        {
            HierarchyGUIUtility.AddSceneGUI(OnSceneGUI, index: -int.MaxValue);
            HierarchyGUIUtility.AddGameObjectGUI(OnGameObjectGUI, index: -int.MaxValue);

            SceneManager.OnInitialized(() =>
            {
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
            });
        }

        static void Update()
        {
            if (EditorSceneManager.preventCrossSceneReferences && SceneManager.settings.project.enableCrossSceneReferences)
                EditorSceneManager.preventCrossSceneReferences = false;
        }

        internal static void OnEnable()
        {

            OnDisable();

            EditorSceneManager.preventCrossSceneReferences = false;

            AssemblyReloadEvents.afterAssemblyReload += AssemblyReloadEvents_afterAssemblyReload;

            EditorSceneManager.sceneSaving += EditorSceneManager_sceneSaving;
            EditorSceneManager.sceneSaved += EditorSceneManager_sceneSaved;
            EditorSceneManager.sceneOpened += EditorSceneManager_sceneOpened;
            EditorSceneManager.sceneClosed += EditorSceneManager_sceneClosed;
            BuildUtility.preBuild += BuildEventsUtility_preBuild;

            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ResolveScenes();

        }

        internal static void OnDisable()
        {

            EditorSceneManager.preventCrossSceneReferences = true;

            AssemblyReloadEvents.afterAssemblyReload -= AssemblyReloadEvents_afterAssemblyReload;

            EditorSceneManager.sceneSaving -= EditorSceneManager_sceneSaving;
            EditorSceneManager.sceneSaved -= EditorSceneManager_sceneSaved;
            EditorSceneManager.sceneOpened -= EditorSceneManager_sceneOpened;
            EditorSceneManager.sceneClosed -= EditorSceneManager_sceneClosed;
            BuildUtility.preBuild -= BuildEventsUtility_preBuild;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        }

        #region Hierarchy indicator

        static GUIStyle hierarchyIconStyle;

        static GUIContent sceneHasLinksContent;
        static GUIContent sceneHasBrokenLinksContent;

        static GUIContent objHasLinksContent;
        static GUIContent objHasBrokenLinksContent;

        static void SetupGUI()
        {

            if (hierarchyIconStyle == null)
                hierarchyIconStyle = new GUIStyle(EditorStyles.iconButton);

            var iconName = "d_Linked";

            if (sceneHasLinksContent == null)
                sceneHasLinksContent = new GUIContent(EditorGUIUtility.IconContent(iconName).image, "This scene contains cross-scene references.\n\nPress to open cross-scene references debugger.");

            if (objHasLinksContent == null)
                objHasLinksContent = new GUIContent(EditorGUIUtility.IconContent(iconName).image);

            iconName = "d_Unlinked";

            if (sceneHasBrokenLinksContent == null)
                sceneHasBrokenLinksContent = new GUIContent(EditorGUIUtility.IconContent(iconName).image, "One or more broken cross-scene references were found, references will not be saved until all are valid.\n\nPress to open cross-scene references debugger.");

            if (objHasBrokenLinksContent == null)
                objHasBrokenLinksContent = new GUIContent(EditorGUIUtility.IconContent(iconName).image);

        }

        static void OnSceneGUI(scene scene)
        {

            if (!SceneManager.settings.project.enableCrossSceneReferences)
                return;

            var references = CrossSceneReferenceUtility.GetResolvedReferences(scene).ToArray();

            if (!references.Any())
                return;

            SetupGUI();

            var content =
                references.Any(r => r.result != ResolveStatus.Succeeded)
                ? sceneHasBrokenLinksContent
                : sceneHasLinksContent;

            if (GUILayout.Button(content, hierarchyIconStyle))
                CrossSceneDebugger.Open();

        }

        static void OnGameObjectGUI(GameObject obj)
        {

            if (!SceneManager.settings.project.enableCrossSceneReferences)
                return;

            SetupGUI();

            OnVariable(CrossSceneReferenceUtility.GetResolvedReferences(obj));
            OnValue(CrossSceneReferenceUtility.GetResolvedReferencesValue(obj));

        }

        static void OnVariable(IEnumerable<ResolvedCrossReference> references)
        {

            var content =
                references.Any(r => r.result != ResolveStatus.Succeeded)
                ? objHasBrokenLinksContent
                : objHasLinksContent;

            content.tooltip =
                string.Join("\n\n", references.Select(r => r.ToString())) +
                (references.Any(r => r.result != ResolveStatus.Succeeded) ? null : "\n\nPress to view linked object.");

            if (references.Any())
                if (GUILayout.Button(content, hierarchyIconStyle))
                {

                    var o = references.First().value.resolve.gameObject;

                    if (Selection.activeGameObject != o)
                        EditorGUIUtility.PingObject(o);

                    Selection.activeObject = o;

                }

        }

        static void OnValue(IEnumerable<ResolvedCrossReference> references)
        {

            var content =
                references.Any(r => r.result != ResolveStatus.Succeeded)
                ? objHasBrokenLinksContent
                : objHasLinksContent;

            content.tooltip =
                string.Join("\n\n", references.Select(r => r.ToString())) +
                (references.Any(r => r.result != ResolveStatus.Succeeded) ? null : "\n\nPress to view linked object.");

            if (references.Any())
                if (GUILayout.Button(content, hierarchyIconStyle))
                {

                    var o = references.First().variable.resolve.gameObject;

                    if (Selection.activeGameObject != o)
                        EditorGUIUtility.PingObject(o);

                    Selection.activeObject = o;

                }

        }

        #endregion
        #region Triggers / unity callbacks

        static void BuildEventsUtility_preBuild(BuildReport report)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                foreach (var scene in SceneUtility.GetAllOpenUnityScenes().ToArray())
                    CrossSceneReferenceUtility.ResetScene(scene);
        }

        static void AssemblyReloadEvents_afterAssemblyReload() =>
            ResolveScenes();

        [PostProcessBuild]
        static void PostProcessBuild(BuildTarget target, string path) =>
            ResolveScenes();

        static void OnPlayModeChanged(PlayModeStateChange mode)
        {

            EditorSceneManager.preventCrossSceneReferences = false;

            CrossSceneReferenceUtility.ResetAllScenes();

            if (mode == PlayModeStateChange.EnteredPlayMode || mode == PlayModeStateChange.EnteredEditMode)
                ResolveScenes();

        }

        static void EditorSceneManager_sceneOpened(scene scene, OpenSceneMode mode) =>
            ResolveScenes();

        static void EditorSceneManager_sceneClosed(scene scene) =>
            ResolveScenes();

        static readonly List<string> scenesToIgnore = new List<string>();

        /// <summary>Ignores the specified scene.</summary>
        public static void Ignore(string scenePath, bool ignore)
        {
            if (ignore && !scenesToIgnore.Contains(scenePath))
                scenesToIgnore.Add(scenePath);
            else if (!ignore)
                _ = scenesToIgnore.Remove(scenePath);
        }

        static bool isAdding;
        static void EditorSceneManager_sceneSaving(scene scene, string path)
        {

            EditorSceneManager.preventCrossSceneReferences = false;
            if (isAdding || BuildPipeline.isBuildingPlayer || scenesToIgnore.Contains(path) || !scene.ASMScene(out var s))
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                var references = CrossSceneReferenceUtility.GetResolvedReferences(scene).Where(r => r.variable.reference.Resolve().result != ResolveStatus.Succeeded).ToArray();
                foreach (var reference in references)
                    s.RemoveCrossSceneReference(reference.reference);
            }

            isAdding = true;

            var l = new List<CrossSceneReference>();
            var newReferences = CrossSceneReferenceUtility.FindCrossSceneReferences(scene).ToArray();

            l.AddRange(newReferences);

            var l1 = l.GroupBy(r => r.variable).
                Select(g => (oldRef: g.ElementAtOrDefault(0), newRef: g.ElementAtOrDefault(1))).
                Where(g =>
                {

                    //This is a bit confusing, but oldRef is newRef when no actual oldRef exist,
                    //we should probably improve this to be more readable
                    if (newReferences.Contains(g.oldRef))
                        g = (oldRef: null, newRef: g.oldRef);

                    //This is a new reference, or has been updated
                    if (g.newRef != null)
                        return true;

                    //This reference has not been updated to a new cross-scene target,
                    //but we still don't know if it has been set to null or to same scene,
                    //lets check if it is still valid (beyond unloaded target scene)
                    var shouldCarryOver = (g.oldRef?.value?.IsValid(returnTrueWhenSceneIsUnloaded: true) ?? false);
                    return shouldCarryOver;

                }).
                Select(g => g.newRef ?? g.oldRef).ToArray();

            CrossSceneReferenceUtility.ResetAllScenes();
            s.crossSceneReferences = l1;

            isAdding = false;

        }

        static void EditorSceneManager_sceneSaved(scene scene)
        {
            ResolveScenes();
        }

        #endregion

        static void ResolveScenes()
        {
            CoroutineUtility.Run(
                CrossSceneReferenceUtility.ResolveAllScenes,
                when: () => !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer && SceneUtility.hasAnyScenes && Profile.current);
        }

    }

}
#endif
