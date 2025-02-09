#if UNITY_EDITOR

using System;
using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AdvancedSceneManager.Editor.Utility
{

    /// <summary>A utility for locking scenes and collections from modification.</summary>
    /// <remarks>Only modification from within unity is prevented.</remarks>
    public static partial class LockUtility
    {

        #region API

        static void CheckEnabled(ILockable obj)
        {

            if (obj is Scene && !SceneManager.settings.project.allowSceneLocking)
                throw new InvalidOperationException("Cannot lock/unlock a scene when locking is disabled.");

            if (obj is SceneCollection && !SceneManager.settings.project.allowCollectionLocking)
                throw new InvalidOperationException("Cannot lock/unlock a collection when locking is disabled.");

        }

        /// <summary>Locks the object.</summary>
        public static void Lock(this ILockable obj, string message = null, bool prompt = false)
        {

            CheckEnabled(obj);

            if (prompt && !PromptUtility.PromptString("Locking scene...", "Lock reason:", out message, message))
                return;

            obj.lockMessage = message;
            obj.isLocked = true;
            obj.Save();

        }

        /// <summary>Unlocks the object.</summary>
        public static void Unlock(this ILockable obj, bool prompt = false)
        {

            CheckEnabled(obj);

            if (prompt && !PromptUtility.Prompt("Unlocking scene...", string.IsNullOrWhiteSpace(obj.lockMessage) ? "No message" : obj.lockMessage))
                return;

            obj.lockMessage = null;
            obj.isLocked = false;
            obj.Save();

        }

        /// <summary>Toggles lock status of the object.</summary>
        public static void Toggle(ILockable obj, bool prompt = false)
        {
            if (obj.isLocked)
                obj.Unlock(prompt);
            else
                obj.Lock(null, prompt);
        }

        #endregion
        #region Scene lock

        [InitializeOnLoadMethod]
        static void AddSceneContextMenuItems() =>
            SceneManager.OnInitialized(() =>
            {
                SceneHierarchyHooks.addItemsToSceneHeaderContextMenu += (menu, e) =>
                {

                    if (!e.ASMScene(out var scene))
                        return;

                    menu.AddSeparator("");
                    if (scene.isLocked)
                        menu.AddItem(new GUIContent("Unlock scene..."), false, () => Unlock(scene, true));
                    else
                        menu.AddItem(new GUIContent("Lock scene..."), false, () => Lock(scene, prompt: true));

                };
            });

        internal static void OnSave(Scene scene)
        {
            if (PromptUtility.Prompt("Saving locked scene...", $"The scene {scene.path} is locked, which means it cannot be saved.\n\n{scene.lockMessage}", "Discard", "Save as...", out var discard, out var saveAs))
            {

                if (discard)
                    Discard(scene);

                else if (saveAs && SaveAs(scene))
                    Discard(scene);

            }
        }

        static bool SaveAs(Scene scene)
        {

            var path = EditorUtility.SaveFilePanelInProject("Save scene as...", scene.name, "unity", "");
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (!EditorSceneManager.SaveScene(scene.internalScene.Value, path, true))
            {
                Debug.LogError("An error occurred when saving scene.");
                return false;
            }

            var newScene = SceneImportUtility.Import(path);
            SceneManager.runtime.Track(newScene, EditorSceneManager.OpenScene(newScene.path, OpenSceneMode.Additive));
            EditorSceneManager.MoveSceneAfter(newScene.internalScene.Value, scene.internalScene.Value);

            return true;

        }

        static void Discard(Scene scene) =>
            EditorApplication.delayCall += () =>
            {

                var scenes = SceneUtility.GetAllOpenUnityScenes().ToArray();
                var index = scenes.Select((s, i) => (s, i)).First(s => s.s.handle == scene.internalScene.Value.handle).i;
                var sceneAbove = scenes.ElementAtOrDefault(index - 1);

                FallbackSceneUtility.EnsureOpen();
                EditorSceneManager.CloseScene(scene.internalScene.Value, true);
                var newScene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Additive);
                FallbackSceneUtility.Close();
                scene.internalScene = newScene;

                if (sceneAbove == default)
                    EditorSceneManager.MoveSceneBefore(newScene, UnityEngine.SceneManagement.SceneManager.GetSceneAt(0));
                else
                    EditorSceneManager.MoveSceneAfter(newScene, sceneAbove);

            };

        #endregion

    }

    class LockedSceneProcessor : AssetModificationProcessor
    {

        static string currentScene;
        static string[] OnWillSaveAssets(string[] paths)
        {

            if (!SceneManager.settings.project || !SceneManager.settings.project.allowSceneLocking)
                return paths;

            var lockedScenes = paths.
                Except(currentScene).
                Where(SceneImportUtility.StringExtensions.IsScene).
                Select(SceneManager.assets.scenes.Find).
                NonNull().
                Where(s => s.isLocked);

            foreach (var scene in lockedScenes)
            {
                currentScene = scene;
                LockUtility.OnSave(scene);
                currentScene = null;
            }

            return paths.Except(lockedScenes.Select(s => s.path)).ToArray();

        }

    }

}

#endif
