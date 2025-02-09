using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Callbacks;
using AdvancedSceneManager.Callbacks.Events;
using AdvancedSceneManager.Core.Callbacks;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.Core
{

    partial class SceneOperation
    {

        readonly List<Scene> m_closedScenes = new();
        readonly List<Scene> m_openedScenes = new();

        /// <summary>Gets the scenes that was closed during this operation.</summary>
        public IEnumerable<Scene> closedScenes => m_closedScenes;

        /// <summary>Gets the scenes that was opened during this operation.</summary>
        public IEnumerable<Scene> openedScenes => m_openedScenes;

        IEnumerator CloseScenes()
        {

            yield return InvokeCallback<SceneClosePhaseEvent>(new(), When.Before);

            var scenes = close.NonNull().Distinct().Except(openedLoadingScreen.ASMScene()).ToArray();
            yield return PerformActions<SceneCloseEvent>(UnloadScene, scenes);

            yield return InvokeCallback<SceneClosePhaseEvent>(new(), When.After);

        }

        IEnumerator OpenScenes()
        {

            yield return InvokeCallback<SceneOpenPhaseEvent>(new(), When.Before);

            var scenes = open.NonNull().Distinct().ToArray();
            yield return PerformActions<SceneOpenEvent>(s => LoadScene(s, false), scenes);

            yield return InvokeCallback<SceneOpenPhaseEvent>(new(), When.After);

        }

        IEnumerator PreloadScenes()
        {

            yield return InvokeCallback<ScenePreloadPhaseEvent>(new(), When.Before);

            var scenes = preload.NonNull().Distinct().ToArray();
            yield return PerformActions<ScenePreloadEvent>(s => LoadScene(s, true), scenes);

            yield return InvokeCallback<ScenePreloadPhaseEvent>(new(), When.After);

        }

        IEnumerator PerformActions<TEventType>(Func<Scene, IEnumerator> action, params Scene[] scenes) where TEventType : SceneEvent, new()
        {

            if (SceneManager.settings.project.allowLoadingScenesInParallel || (collection && preload.Any()))
            {
                // Parallel execution for performance
                yield return CoroutineUtility.WaitAll(scenes.Select(ExecuteSceneAction));
            }
            else
            {
                // Sequential execution
                yield return CoroutineUtility.Chain(scenes.Select(ExecuteSceneAction));
            }

            IEnumerator ExecuteSceneAction(Scene scene)
            {
                yield return InvokeCallback<TEventType>(new() { scene = scene }, When.Before);
                yield return action(scene);
                yield return InvokeCallback<TEventType>(new() { scene = scene }, When.After);
            }

        }

        #region Loading Screen

        /// <summary>Gets the loading screen that was opened by this operation.</summary>
        public LoadingScreen openedLoadingScreen { get; private set; }

        IEnumerator ShowLoadingScreen()
        {

            yield return InvokeCallback<LoadingScreenOpenPhaseEvent>(new(), Callbacks.When.Before);

            if (useLoadingScene && flags.HasFlag(SceneOperationFlags.LoadingScreen))
            {
                var async = LoadingScreenUtility.OpenLoadingScreen(this, loadingScreenCallback);
                yield return async;
                openedLoadingScreen = async.value;
            }

            yield return InvokeCallback<LoadingScreenOpenPhaseEvent>(new(), Callbacks.When.After);
            yield return PerformCloseCallbacks(close.NonNull().Distinct().ToArray());

        }

        IEnumerator HideLoadingScreen()
        {

            yield return PerformOpenCallbacks(openedScenes);
            yield return InvokeCallback<LoadingScreenClosePhaseEvent>(new(), Callbacks.When.Before);

            if (openedLoadingScreen)
            {
                yield return LoadingScreenUtility.CloseLoadingScreen(openedLoadingScreen);
                openedLoadingScreen = null;
            }

            yield return InvokeCallback<LoadingScreenClosePhaseEvent>(new(), Callbacks.When.After);

        }

        #endregion
        #region Scene load

        ThreadPriority GetLoadPriority(Scene scene)
        {

            //With(LoadPriority) always overrides any setting on collection or scen
            if (loadPriority != LoadPriority.Auto)
                return (ThreadPriority)loadPriority;

            //Scene overrides collection setting
            if (scene.loadPriority != LoadPriority.Auto)
                return (ThreadPriority)scene.loadPriority;

            //Lastly check collection
            if (collection && collection.loadPriority != LoadPriority.Auto)
                return (ThreadPriority)collection.loadPriority;

            //Fallback to current value
            return Application.backgroundLoadingPriority;

        }

        internal static Scene currentLoadingScene { get; private set; }
        internal static bool isCurrentLoadingScenePreload { get; private set; }

        IEnumerator LoadScene(Scene scene, bool isPreload)
        {

            if (!scene || scene.isOpen)
                yield break;

            currentLoadingScene = scene;
            isCurrentLoadingScenePreload = isPreload;

            yield return scene.Load(isPreload, operation: this, collection, reportsProgress, GetLoadPriority(scene),
                onLoaded: () =>
                {
                    m_openedScenes.Add(scene);
                    SetActiveScene();
                },
                onError: Debug.LogError);

            currentLoadingScene = null;
            isCurrentLoadingScenePreload = false;

        }

        IEnumerator UnloadScene(Scene scene)
        {

            if (!scene || !scene.internalScene.HasValue || !scene.internalScene.Value.IsValid())
                yield break;

            SetActiveScene(scene);
            yield return scene.Unload(operation: this, collection, reportsProgress, GetLoadPriority(scene),
                onUnloaded: () => m_closedScenes.Add(scene),
                onError: Debug.LogError);

        }

        #endregion
        #region Scene callbacks

        /// <summary>Gets if this operation is currently executing open callbacks. If so, sub operations is temporarily accepted.</summary>
        /// <remarks>See <see cref="WaitFor(SceneOperation)"/></remarks>
        public bool acceptsSubOperations { get; private set; }

        readonly List<SceneOperation> waitFor = new();

        /// <summary>Add an operation to wait for. This means that this operation will wait until the new one is done, before proceeding to the close loading screen phase.</summary>
        public void WaitFor(SceneOperation operation) =>
            waitFor.Add(operation);

        bool ShouldRunCallbacks => !wasCancelled && (runSceneCallbacksOutsideOfPlayMode || Application.isPlaying);

        IEnumerator PerformCloseCallbacks(IEnumerable<Scene> scenes)
        {

            if (wasCancelled)
                yield break;

            if (ShouldRunCallbacks && collection && flags.HasFlag(SceneOperationFlags.CollectionCallbacks))
                yield return CallbackUtility.DoCollectionCloseCallbacks(SceneManager.openCollection);

            if (flags.HasFlag(SceneOperationFlags.SceneCallbacks))
                foreach (var scene in scenes)
                    if (ShouldRunCallbacks)
                        yield return CallbackUtility.DoSceneCloseCallbacks(scene);

        }

        IEnumerator PerformOpenCallbacks(IEnumerable<Scene> scenes)
        {

            if (wasCancelled)
                yield break;

            acceptsSubOperations = true;

            if (flags.HasFlag(SceneOperationFlags.SceneCallbacks))
                foreach (var scene in scenes)
                {
                    if (ShouldRunCallbacks)
                        yield return CallbackUtility.DoSceneOpenCallbacks(scene);

                }

            if (ShouldRunCallbacks && collection && flags.HasFlag(SceneOperationFlags.CollectionCallbacks))
                yield return CallbackUtility.DoCollectionOpenCallbacks(collection);

            acceptsSubOperations = false;

        }

        #endregion
        #region Active scene

        /// <summary>Attempts to set active scene.</summary>
        /// <param name="except">Specifies a scene that should not be activated.</param>
        void SetActiveScene(Scene except = null)
        {

            var scenesToIgnore = ignoreForActivation.Concat(except);

            if (preload.Any())
                return;

            var scene = focus;

            if (!scene && setActiveCollectionScene && collection && collection.activeScene && !scenesToIgnore.Contains(collection.activeScene))
                scene = collection.activeScene;

            if (!scene && focusSingleScene)
                scene = openedScenes.Except(scenesToIgnore).FirstOrDefault();

            if (scene)
                scene.Activate();

        }

        #endregion
        #region Unload unused assets

        IEnumerator UnloadUnusedAssets()
        {
            if (unloadUnusedAssets ?? false ||
                (collection && collection.unloadUnusedAssets) ||
                (!collection && Profile.current.unloadUnusedAssetsForStandalone))
                yield return Resources.UnloadUnusedAssets();
        }

        #endregion

    }
}
