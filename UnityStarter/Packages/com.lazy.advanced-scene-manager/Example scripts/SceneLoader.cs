using System.Collections;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;
using scene = UnityEngine.SceneManagement.Scene;
using sceneManager = UnityEngine.SceneManagement.SceneManager;

namespace AdvancedSceneManager.ExampleScripts
{

    /// <summary>Contains examples of how to override scene loading.</summary>
    public static class SceneLoader
    {

        [RuntimeInitializeOnLoadMethod]
        static void RegisterOverride()
        {

            //Register scene loader.

            //Conflicts can occur if multiple global ones are enabled at the same time.

            //Global:
            //SceneManager.runtime.AddSceneLoader<AsyncLoader>();
            //SceneManager.runtime.AddSceneLoader<SyncLoader>();

            //Scene specific:
            //SceneManager.runtime.AddSceneLoader<SpecificSceneLoader>();

        }

        #region Async

        //This is how ASM currently implements scene loading
        //Load scenes in async
        class AsyncLoader : Core.SceneLoader
        {

            public override IEnumerator LoadScene(Scene scene, SceneLoadArgs e)
            {

                //Logs error and calls e.NotifyComplete(handled: true)
                //if scene is not actually included in build,
                //which means we can just break then.
                //Remove this if the scene isn't supposed to be in build list, like addressable scenes
                if (!e.CheckIsIncluded())
                    yield break;

                //Load scene additively
                //If non-additive is needed, then keep in mind your collections are setup for this, ASM will get weird otherwise
                var async = sceneManager.LoadSceneAsync(scene.path, UnityEngine.SceneManagement.LoadSceneMode.Additive);

                if (e.reportProgress)
                    yield return async.ReportProgress(SceneOperationKind.Load, e.operation, scene); //Reports progress to loading screens

                yield return async;

                //Get the scene that was opened with UnityEngine.SceneManagement.LoadScene,
                //since unity does not support this for some reason
                var uScene = e.GetOpenedScene();

                //Notify that we're complete, if we don't call this
                //then ASM will run its regular action
                e.SetCompleted(uScene);

            }

            public override IEnumerator UnloadScene(Scene scene, SceneUnloadArgs e)
            {

                var async = sceneManager.UnloadSceneAsync(scene.internalScene.Value);

                if (e.reportProgress)
                    yield return async.ReportProgress(SceneOperationKind.Unload, e.operation, scene);

                yield return async;
                e.SetCompleted();

            }

        }

        #endregion
        #region Sync

        //Loads scenes sync
        //Note that unity unload method is deprecated
        class SyncLoader : Core.SceneLoader
        {

            public override IEnumerator LoadScene(Scene scene, SceneLoadArgs e)
            {

                //Logs error and calls e.NotifyComplete(handled: true)
                //if scene is not actually included in build,
                //which means we can just break then.
                //Remove this if the scene isn't supposed to be in build list, like addressable scenes.
                if (!e.CheckIsIncluded())
                    yield break;

                //Setup event handler in order to retrieve scene once loaded
                scene? loadedScene = null;
                sceneManager.sceneLoaded += SceneManager_sceneLoaded;
                void SceneManager_sceneLoaded(scene scene, UnityEngine.SceneManagement.LoadSceneMode _)
                {
                    loadedScene = scene;
                    sceneManager.sceneLoaded -= SceneManager_sceneLoaded;
                }

                //Load scene additively
                //If non-additive is needed, then keep in mind your collections are setup for this, ASM will get weird otherwise
                sceneManager.LoadScene(scene.path, UnityEngine.SceneManagement.LoadSceneMode.Additive);

                //Keep in mind that this will never return if there is an error occurs when loading scene
                yield return new WaitUntil(() => loadedScene.HasValue);

                //Notify that we're complete, if we don't call this
                //then ASM will run its regular action
                e.SetCompleted(loadedScene.Value);

            }

#pragma warning disable CS0618 //UnityEngine.SceneManagement.SceneManager.UnloadScene() is deprecated

            public override IEnumerator UnloadScene(Scene scene, SceneUnloadArgs e)
            {

                //Setup event handler in order to make sure we wait until scene is actually unloaded.
                //ASM does not really care whatever scene is unloaded or not, but loading screens might
                //disappear to early if we don't
                bool hasUnloaded = false;
                sceneManager.sceneUnloaded += SceneManager_sceneUnloaded;
                void SceneManager_sceneUnloaded(scene scene)
                {
                    hasUnloaded = true;
                    sceneManager.sceneUnloaded -= SceneManager_sceneUnloaded;
                }

                //Unload the scene
                if (!sceneManager.UnloadScene(scene.internalScene.Value))
                    Debug.LogError("Scene could not be unloaded.");

                //Keep in mind that this will never return if there is an error occurs when loading scene
                yield return new WaitUntil(() => hasUnloaded);

                //Scene is probably closed, but hierarchy might still display it,
                //so lets wait for it to update for good measure
                yield return null;

                //Notify ASM that we have completed, and normal action should not run
                e.SetCompleted();

            }

#pragma warning restore CS0618

        }


        #endregion
        #region Specific scene loader

        class SpecificSceneLoader : Core.SceneLoader
        {

            //Specifies that this loader can be used outside of play mode
            public override bool activeOutsideOfPlayMode => false;

            //Displays a toggle will be displayed in scene popup to flag a scene for use with this loader.
            //Scene.SetSceneLoader<SpecificSceneLoader>() can also be used.
            public override string sceneToggleText => "Custom loader";

            //Specifies that this loader can be used for all scenes
            //Must be false for this loader to only apply to certain scenes
            public override bool isGlobal => false;

            public override IEnumerator LoadScene(Scene scene, SceneLoadArgs e)
            {

                Debug.Log("Custom loader: Load: " + scene.name);

                //TODO: Implement functionality

                yield break;

            }

            public override IEnumerator UnloadScene(Scene scene, SceneUnloadArgs e)
            {

                Debug.Log("Custom loader: Unload: " + scene.name);

                //TODO: Implement functionality

                yield break;

            }

        }

        #endregion

    }

}
