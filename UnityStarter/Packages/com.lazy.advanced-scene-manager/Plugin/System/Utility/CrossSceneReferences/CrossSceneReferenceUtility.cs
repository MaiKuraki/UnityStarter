using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;
using UnityEngine.Events;
using System.Reflection;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Core;
using Component = UnityEngine.Component;
using scene = UnityEngine.SceneManagement.Scene;
using System;
using AdvancedSceneManager.Core.Callbacks;
using AdvancedSceneManager.Callbacks.Events;



#if UNITY_EDITOR
using UnityEditor;
using AdvancedSceneManager.Editor.Utility;
#endif

namespace AdvancedSceneManager.Utility.CrossSceneReferences
{

    /// <summary>An utility for saving and restoring cross-scene references.</summary>
    public static class CrossSceneReferenceUtility
    {

#pragma warning disable CS0067
        internal static event Action OnSaved;
        internal static event Action OnSceneStatusChanged;
#pragma warning restore CS0067

        #region Initialize

        static CrossSceneReferenceUtility() =>
            SceneManager.OnInitialized(() =>
            {
                if (SceneManager.settings.project)
                    SceneManager.settings.project.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(SceneManager.settings.project.enableCrossSceneReferences))
                            OnLoad();
                    };
            });

        [RuntimeInitializeOnLoadMethod]
        [InitializeInEditorMethod]
        static void OnLoad() =>
            SceneManager.OnInitialized(Initialize);

        /// <summary>Initializes cross-scene references, if it is enabled in settings.</summary>
        public static void Initialize() => Initialize(null);

        /// <summary>Initializes cross-scene references, if it is enabled in settings.</summary>
        public static void Initialize(bool? enabled = null)
        {

            if (!SceneManager.settings.project)
                return;

            if (enabled.HasValue)
                SceneManager.settings.project.enableCrossSceneReferences = enabled.Value;

            if (SceneManager.settings.project.enableCrossSceneReferences)
                Enable();
            else
                Disable();

        }

        static void Enable()
        {

            SceneOperation.RegisterGlobalCallback<ScenePreloadPhaseEvent>(SceneOperation_ScenePreloadPhaseEvent, When.After);
            SceneManager.runtime.scenePreloadFinished -= Runtime_scenePreloadFinished;
            SceneManager.runtime.scenePreloadFinished += Runtime_scenePreloadFinished;

            ResetAllScenes();
            resolvedReferences.Clear();
            sceneStatus.Clear();

            ResetAllScenes();

#if UNITY_EDITOR

            Editor.OnEnable();

            if (SessionState.GetBool("cross-scene-ref-warning", true))
                Debug.Log("Cross-scene references enabled. Note that you may receive warnings from unity that they are not supported, these are unnecessarily difficult to get rid of, so while ASM has prevented a bunch of them, you may still receive some, they are safe to ignore.");
            SessionState.SetBool("cross-scene-ref-warning", false);

#endif

        }

        static void Disable()
        {
            SceneOperation.UnregisterGlobalCallback<ScenePreloadPhaseEvent>(SceneOperation_ScenePreloadPhaseEvent, When.After);
            SceneManager.runtime.scenePreloadFinished -= Runtime_scenePreloadFinished;
            ResetAllScenes();

#if UNITY_EDITOR
            Editor.OnDisable();
#endif

        }

        private static void Runtime_scenePreloadFinished(Scene _) =>
            ResolveAllScenes();

        static void SceneOperation_ScenePreloadPhaseEvent(ScenePreloadPhaseEvent e) =>
            ResolveAllScenes();

        #endregion
        #region Resolve status

        static readonly Dictionary<string, ResolvedCrossReference> resolvedReferences = new Dictionary<string, ResolvedCrossReference>();

        static void AddResolvedReference(ResolvedCrossReference reference) =>
            _ = resolvedReferences.Set(reference.reference.id, reference);

        /// <summary>Gets all references for all scenes.</summary>
        public static IEnumerable<ResolvedCrossReference> GetResolvedReferences() =>
            resolvedReferences.Values;

        /// <summary>Gets all references for this scene.</summary>
        public static IEnumerable<ResolvedCrossReference> GetResolvedReferences(scene scene) =>
            resolvedReferences.Values.Where(r => r.variable.resolve.scene == scene);

        /// <summary>Gets all references for this game object.</summary>
        public static IEnumerable<ResolvedCrossReference> GetResolvedReferences(GameObject obj) =>
            resolvedReferences.Values.Where(r => r.variable.resolve.gameObject == obj);

        /// <summary>Gets all references for this game object.</summary>
        public static IEnumerable<ResolvedCrossReference> GetResolvedReferencesValue(GameObject obj) =>
            resolvedReferences.Values.Where(r => r.value.resolve.gameObject == obj);

        /// <summary>Gets if the cross-scene references can be saved.</summary>
        /// <remarks>This would be if status: <see cref="SceneStatus.Restored"/> and no resolve errors.</remarks>
        public static bool CanSceneBeSaved(scene scene) =>
            GetResolvedReferences(scene).All(r => r.result == ResolveStatus.Succeeded);

        /// <summary>Get the resolve result for a cross scene reference, if it has been resolved.</summary>
        public static bool GetResolved(CrossSceneReference reference, out ResolvedCrossReference? resolved)
        {
            resolved = default;
            if (resolvedReferences.ContainsKey(reference.id))
                resolved = resolvedReferences[reference.id];
            return resolved.HasValue;
        }

        /// <summary>Get the resolve result for a cross scene reference, if it has been resolved.</summary>
        public static ResolvedCrossReference GetResolved(CrossSceneReference reference)
        {
            if (resolvedReferences.ContainsKey(reference.id))
                return resolvedReferences[reference.id];
            return default;
        }

        static void ClearStatusForScene(scene scene)
        {
            var references = GetResolvedReferences(scene);
            foreach (var reference in references.ToArray())
                if (reference.variable.resolve.scene == scene)
                    _ = resolvedReferences.Remove(reference.reference.id);
            OnSceneStatusChanged?.Invoke();
        }

        static readonly Dictionary<scene, SceneStatus> sceneStatus = new Dictionary<scene, SceneStatus>();

        static void SetSceneStatus(scene scene, SceneStatus state)
        {

            _ = sceneStatus.Set(scene, state);
            OnSceneStatusChanged?.Invoke();

#if UNITY_EDITOR
            HierarchyGUIUtility.Repaint();
#endif

        }

        public static SceneStatus GetSceneStatus(scene scene)
        {
            if (sceneStatus.ContainsKey(scene))
                return sceneStatus[scene];
            else
                return default;
        }

        #endregion
        #region Resolve / reset

        /// <summary>Resolves all scenes.</summary>
        public static void ResolveAllScenes()
        {
            foreach (var scene in SceneUtility.GetAllOpenUnityScenes())
                _ = ResolveScene(scene).ToArray();
        }

        /// <summary>Resolves cross-scene references in the scene.</summary>
        public static IEnumerable<ResolvedCrossReference> ResolveScene(scene scene)
        {

            if (scene.ASMScene(out var s))
                foreach (var reference in s.crossSceneReferences)
                {

                    if (resolvedReferences.ContainsKey(reference.id))
                    {
                        var r = resolvedReferences[reference.id];
                        if (resolvedReferences.Remove(reference.id) && r.result == ResolveStatus.Succeeded)
                            ObjectReference.ResetValue(r.variable.resolve);
                    }

                    var variable = reference.variable.Resolve();
                    var value = reference.value.Resolve();
                    var result = ObjectReference.SetValue(variable, value);

                    var resolved = new ResolvedCrossReference(variable, value, reference, result);
                    AddResolvedReference(resolved);
                    yield return resolved;

                };

            SetSceneStatus(scene, SceneStatus.Restored);

        }

        /// <summary>Resets all cross-scene references in all scenes.</summary>
        public static void ResetAllScenes()
        {
            foreach (var scene in SceneUtility.GetAllOpenUnityScenes())
                ResetScene(scene);
        }

        /// <summary>Resets all cross-scene references in scene.</summary>
        public static void ResetScene(scene scene)
        {

            foreach (var reference in GetResolvedReferences(scene).ToArray())
            {
                ObjectReference.ResetValue(reference.variable.resolve);
                _ = resolvedReferences.Remove(reference.reference.id);
            }

            SetSceneStatus(scene, SceneStatus.Cleared);

        }

        #endregion
        #region Find

        /// <summary>Finds all cross-scene references in the scenes.</summary>
        public static IEnumerable<CrossSceneReference> FindCrossSceneReferences(params scene[] scenes)
        {

            var components = FindComponents(scenes).
                Where(s => s.obj && s.scene.IsValid()).
                Select(c => (c.scene, c.obj, fields: c.obj.GetType()._GetFields().Where(IsSerialized).ToArray())).
                ToArray();

            foreach (var (scene, obj, fields) in components)
            {

                foreach (var field in fields.ToArray())
                {

                    var o = field.GetValue(obj);

                    if (o != null)
                    {

                        if (typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                        {
                            for (int i = 0; i < ((UnityEventBase)o).GetPersistentEventCount(); i++)
                            {
                                if (GetCrossSceneReference(o, scene, out var reference, unityEventIndex: i))
                                {
                                    var source = GetSourceCrossSceneReference(scene, obj, field, unityEventIndex: i);
                                    yield return new CrossSceneReference(source, reference);
                                }
                            }
                        }
                        else if (typeof(IList).IsAssignableFrom(field.FieldType))
                        {
                            for (int i = 0; i < ((IList)o).Count; i++)
                            {
                                if (GetCrossSceneReference(o, scene, out var reference, arrayIndex: i))
                                {
                                    var source = GetSourceCrossSceneReference(scene, obj, field, arrayIndex: i);
                                    yield return new CrossSceneReference(source, reference);
                                }
                            }
                        }
                        else if (GetCrossSceneReference(o, scene, out var reference))
                            yield return new CrossSceneReference(GetSourceCrossSceneReference(scene, obj, field), reference);

                    }

                }
            }
        }

        static bool IsSerialized(FieldInfo field) =>
             (field?.IsPublic ?? false) || field?.GetCustomAttribute<SerializeField>() != null;

        static IEnumerable<(scene scene, Component obj)> FindComponents(params scene[] scenes)
        {
            foreach (var scene in scenes)
                if (scene.isLoaded)
                    foreach (var rootObj in scene.GetRootGameObjects())
                        foreach (var obj in rootObj.GetComponentsInChildren<Component>(includeInactive: true))
                            yield return (scene, obj);
        }

        static bool GetCrossSceneReference(object obj, scene sourceScene, out ObjectReference reference, int unityEventIndex = -1, int arrayIndex = -1)
        {

            reference = null;

            if (obj is GameObject go && go && IsCrossScene(sourceScene.path, go.scene.path))
                reference = new ObjectReference(go.scene, GuidReferenceUtility.GetOrAddPersistent(go));

            else if (obj is Component c && c && c.gameObject && IsCrossScene(sourceScene.path, c.gameObject.scene.path))
                reference = new ObjectReference(c.gameObject.scene, GuidReferenceUtility.GetOrAddPersistent(c.gameObject)).With(c);

            else if (obj is UnityEvent ev)
                return GetCrossSceneReference(ev.GetPersistentTarget(unityEventIndex), sourceScene, out reference);

            else if (obj is IList list)
                return GetCrossSceneReference(list[arrayIndex], sourceScene, out reference);

            return reference != null;

        }

        static bool IsCrossScene(string srcScene, string scenePath)
        {
            var isPrefab = string.IsNullOrWhiteSpace(scenePath);
            var isDifferentScene = scenePath != srcScene;
            return isDifferentScene && !isPrefab;
        }

        static ObjectReference GetSourceCrossSceneReference(scene scene, Component obj, FieldInfo field, int? unityEventIndex = null, int? arrayIndex = null) =>
            new ObjectReference(scene, GuidReferenceUtility.GetOrAddPersistent(obj.gameObject), field).With(obj).With(unityEventIndex, arrayIndex);

        #endregion

    }

}
