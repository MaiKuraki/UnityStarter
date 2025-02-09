#if ENABLE_INPUT_SYSTEM && INPUTSYSTEM

using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Models;
using UnityEngine;
using InputButton = AdvancedSceneManager.Models.InputButton;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;



#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AdvancedSceneManager.Utility
{

    /// <summary>Provides utility functions relating to scene bindings.</summary>
    /// <remarks>Only available if input system is installed.</remarks>
    public static class SceneBindingUtility
    {

        static SceneCollection m_openCollection;
        static List<Scene> m_standaloneScenes = new();

        /// <summary>Gets if <paramref name="collection"/> was opened by a binding.</summary>
        public static bool WasOpenedByBinding(SceneCollection collection) =>
            collection && collection == m_openCollection && SceneManager.openCollection == collection;

        /// <summary>Gets if the scene was opened by a binding.</summary>
        public static bool WasOpenedByBinding(Scene scene)
        {

            if (!scene)
                return false;

            if (!Profile.current)
                return false;

            if (m_standaloneScenes.Contains(scene))
                return true;
            else if (m_openCollection && m_openCollection.Contains(scene))
                return true;
            else
                return false;

        }

        #region Tracking

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#endif

        [RuntimeInitializeOnLoadMethod]
        static void SetupTracking() =>
            SceneManager.OnInitialized(() =>
            {

                RestoreTrackedItems();

                SceneManager.runtime.sceneClosed += (s) =>
                {
                    m_standaloneScenes.Remove(s);
                    Persist();
                };

                SceneManager.runtime.collectionClosed += (c) =>
                {
                    if (c == m_openCollection)
                    {
                        m_openCollection = null;
                        Persist();
                    }
                };

            });

        static void Track(Scene scene)
        {
            m_standaloneScenes.Add(scene);
            Persist();
        }

        static void Track(SceneCollection collection)
        {
            m_openCollection = collection;
            Persist();
        }

        static void RestoreTrackedItems()
        {

#if UNITY_EDITOR
            EditorApplication.delayCall += () =>
            {

                var collection = SceneManager.assets.collections.Find(EditorPrefs.GetString("ASM.SceneBindings.Collection"));

                if (SceneManager.openCollection)
                    m_openCollection = collection;

                var ids = EditorPrefs.GetString("ASM.SceneBindings.Scenes").Split("\n");
                var scenes = ids?.Select(id => SceneManager.assets.scenes.Find(id))?.NonNull() ?? Enumerable.Empty<Scene>();

                m_standaloneScenes = scenes.Where(s => s.isOpen).ToList();

            };
#endif

        }

        static void Persist()
        {
#if UNITY_EDITOR
            EditorPrefs.SetString("ASM.SceneBindings.Collection", m_openCollection ? m_openCollection.id : "");
            EditorPrefs.SetString("ASM.SceneBindings.Scenes", string.Join("\n", m_standaloneScenes.Select(s => s.id)));
#endif
        }

        #endregion
        #region Enumerate

        /// <summary>Gets all bindings in the project.</summary>
        public static IEnumerable<(SceneCollection collection, Scene scene, Models.InputBinding binding)> GetBindings()
        {

            if (!Profile.current)
                yield break;

            var collections = Profile.current.collections.Where(c => c && (c.inputBindings.Any(b => b.isValid)));
            var scenes = Profile.current.standaloneScenes.NonNull().Where(s => s.inputBindings.Any(b => b.isValid));

            foreach (var collection in collections)
                foreach (var binding in collection.inputBindings)
                    yield return (collection, null, binding);

            foreach (var scene in scenes)
                foreach (var binding in scene.inputBindings)
                    yield return (null, scene, binding);

        }

        static IEnumerable<(SceneCollection collection, Scene scene, Models.InputBinding binding, InputAction action)> GetActions()
        {
            var bindings = GetBindings();
            foreach (var binding in bindings)
            {

                var name = "ASM:";
                if (binding.scene)
                    name += binding.scene.name;
                else if (binding.collection)
                    name += binding.collection.name;
                else
                    continue;

                var action = new InputAction(name);

                if (binding.binding.buttons.Count == 0)
                {
                    //Do nothing
                }
                else if (binding.binding.buttons.Count == 1)
                {
                    action.AddBinding(binding.binding.buttons[0].path);
                }
                else if (binding.binding.buttons.Count == 2)
                {
                    action.
                        AddCompositeBinding("OneModifier").
                        With("Binding", binding.binding.buttons[1].path).
                        With("Modifier", binding.binding.buttons[0].path);
                }
                else if (binding.binding.buttons.Count == 3)
                {
                    action.
                        AddCompositeBinding("TwoModifiers").
                        With("Button", binding.binding.buttons[2].path).
                        With("Modifier1", binding.binding.buttons[0].path).
                        With("Modifier1", binding.binding.buttons[1].path);
                }

                yield return (binding.collection, binding.scene, binding.binding, action);

            }
        }

        #endregion
        #region Listener

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void OnLoadEditor() =>
            SceneManager.OnInitialized(() =>
            {
                EditorApplication.playModeStateChanged += (e) =>
                {
                    if (e == PlayModeStateChange.ExitingPlayMode)
                        DisableActions();
                };
            });
#endif

        [RuntimeInitializeOnLoadMethod]
        [InitializeInEditorMethod]
        static void StartListener() =>
            SceneManager.OnInitialized(() =>
            {
                if (Application.isPlaying)
                    EnableActions();
            });

        #region Input actions

        static readonly Dictionary<InputAction, (SceneCollection collection, Scene scene, Models.InputBinding binding)> activeActions = new();

        static void RestartActions()
        {
            DisableActions();
            EnableActions();
        }

        static void EnableActions()
        {
            var actions = GetActions().ToArray();
            foreach (var action in actions)
            {
                action.action.Enable();
                action.action.started += OnInputStarted;
                action.action.canceled += OnInputCancelled;
                activeActions.Add(action.action, (action.collection, action.scene, action.binding));
            }
        }

        static void DisableActions()
        {
            foreach (var action in activeActions.Keys)
            {
                action.started -= OnInputStarted;
                action.canceled -= OnInputCancelled;
                action.Disable();
                action.Dispose();
            }
            activeActions.Clear();
        }

        #endregion
        #region Button press

        static void OnInputStarted(InputAction.CallbackContext e)
        {
            if (activeActions.TryGetValue(e.action, out var binding))
            {
                if (binding.collection)
                    OnPress(binding.collection, binding.binding);
                else if (binding.scene)
                    OnPress(binding.scene, binding.binding);
            }
        }

        static void OnPress(SceneCollection collection, Models.InputBinding binding)
        {

            if (!CanTrigger(collection))
                return;

            if (!collection.isOpen && CheckPreload())
            {
                Track(collection);
                if (binding.openCollectionAsAdditive)
                    collection._OpenAdditive();
                else
                    collection._Open();
            }
            else if (collection.isOpen && binding.interactionType == InputBindingInteractionType.Toggle && CheckPreload())
                collection._Close();

        }

        static void OnPress(Scene scene, Models.InputBinding binding)
        {

            if (!CanTrigger(scene))
                return;

            if (!scene.isOpen && CheckPreload())
            {
                Track(scene);
                scene._OpenWithLoadingScreen(scene.inputBindingsLoadingScene);
            }
            else if (scene.isOpen && binding.interactionType == InputBindingInteractionType.Toggle && CheckPreload())
                scene._CloseWithLoadingScreen(scene.inputBindingsLoadingScene);

        }

        static bool CanTrigger(Scene scene)
        {
            if (scene.ignoreInputBindingsForScenes.NonNull().Any(SceneManager.runtime.openScenes.Contains))
                return false;

            return true;
        }

        static bool CanTrigger(SceneCollection collection)
        {
            if (collection.ignoreInputBindingsForScenes.NonNull().Any(SceneManager.runtime.openScenes.Contains))
                return false;

            return true;
        }

        #endregion
        #region Button release

        static void OnInputCancelled(InputAction.CallbackContext e)
        {
            if (activeActions.TryGetValue(e.action, out var binding))
            {
                if (binding.collection)
                    OnRelease(binding.collection, binding.binding);
                else if (binding.scene)
                    OnRelease(binding.scene, binding.binding);
            }
        }

        static void OnRelease(SceneCollection collection, Models.InputBinding binding)
        {
            if (collection.isOpen && binding.interactionType == InputBindingInteractionType.Hold && CheckPreload())
                collection.Close();
        }

        static void OnRelease(Scene scene, Models.InputBinding binding)
        {
            if (scene.isOpen && binding.interactionType == InputBindingInteractionType.Hold && CheckPreload())
                scene.Close();
        }

        static bool CheckPreload()
        {

            if (SceneManager.runtime.preloadedScenes.Any())
            {
                Debug.LogError($"Cannot open scene / collection using binding, because a scene is currently being preloaded.");
                return false;
            }

            return true;

        }

        #endregion

        #endregion
        #region Listen and return input

        /// <summary>Gets if the binding is assigned to multiple scenes / collections.</summary>
        public static bool IsDuplicate(InputButton binding) =>
            IsDuplicate(binding.path);

        static bool IsDuplicate(string bindingPath) =>
            GetBindings().IsDuplicate(bindingPath);

        static bool IsDuplicate(this IEnumerable<(SceneCollection collection, Scene scene, Models.InputBinding binding)> list, string bindingPath)
        {

            if (!Profile.current)
                return false;

            var count = list.SelectMany(b => b.binding.buttons).Count(b => b.path == bindingPath);
            return count > 1;

        }

        #endregion

    }

}

#endif
