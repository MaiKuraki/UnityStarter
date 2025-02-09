using System.Collections;
using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.ExampleScripts
{

    /// <summary>Contains examples for preloading scenes.</summary>
    public class ScenePreload : MonoBehaviour
    {

        public Scene SceneToPreload;

        #region Coroutine

        //Flag to make sure we don't wait for preloadedScene to be set, when
        //StartPreloadCoroutine() hasn't even been called yet
        bool hasStartedPreload;

        public void StartPreloadCoroutine()
        {

            Coroutine().StartCoroutine();
            IEnumerator Coroutine()
            {

                //Don't preload if we're already started,
                //or scene is already open (ASM does not support duplicate scenes)
                if (hasStartedPreload || SceneToPreload.isOpen)
                    yield break;

                //Flag to let us know in FinishPreloadCoroutine() if we've actually started or not.
                hasStartedPreload = true;

                //Start preload.
                //In order to retrieve preloadedScene, we need a reference to operation,
                //so assigning return value (SceneOperation<PreloadedSceneHelper>) is a must.
                var operation = SceneToPreload.Preload();
                yield return operation;

                Debug.Log("[Preload] Scene preloaded: " + SceneToPreload.isPreloaded);

            }

        }

        public void FinishPreloadCoroutine()
        {

            Coroutine().StartCoroutine();
            IEnumerator Coroutine()
            {

                //Make sure we don't wait for preloadedScene to get a value
                //while preload hasn't even started yet.
                if (!hasStartedPreload)
                    yield break;

                //Wait for preloadedScene to be set, this how we know scene
                //has been preloaded, and is ready.
                yield return new WaitUntil(() => SceneToPreload.isPreloaded);

                //Finish actual preload
                yield return SceneToPreload.FinishPreload();

                //Reset state
                hasStartedPreload = false;

                Debug.Log("[Finish] Scene preloaded: " + SceneToPreload.isPreloaded);
                Debug.Log("[Finish] Scene open: " + SceneToPreload.isOpen);

            }

        }

        public void CancelPreloadCoroutine()
        {

            Coroutine().StartCoroutine();
            IEnumerator Coroutine()
            {

                //Make sure we don't wait for preloadedScene to get a value
                //while preload hasn't even started yet.
                if (!hasStartedPreload)
                    yield break;

                //Wait for preloadedScene to be set, this how we know scene
                //has been preloaded, and is ready.
                yield return new WaitUntil(() => SceneToPreload.isPreloaded);

                //Discard
                yield return SceneToPreload.CancelPreload();

                //Reset state
                hasStartedPreload = false;

                Debug.Log("[Discard] Scene preloaded: " + SceneToPreload.isPreloaded);
                Debug.Log("[Discard] Scene open: " + SceneToPreload.isOpen);

            }

        }

        #endregion
        #region Static

        //Note that PreloadedScene is null when no scene is preloaded, and won't have a value until scene is ready to finish preload.
        //Checking isStillPreloaded might be a bit redundant, since SceneManager.standalone.preloadedScene will be set to null when
        //preload is finished, but lets check it for good measure (it'll be more useful when its set to a local variable).
        public static bool hasPreloadedScene => SceneManager.runtime.preloadedScenes.Any();

        public static void StartPreloadStatically(Scene scene) => scene.Preload();
        public static void FinishPreloadStatically(Scene scene) => scene.FinishPreload();
        public static void CancelPreloadStatically(Scene scene) => scene.CancelPreload();

        #endregion

    }

}