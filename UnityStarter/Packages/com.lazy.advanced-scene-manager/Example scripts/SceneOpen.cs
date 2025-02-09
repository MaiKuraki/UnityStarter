using System.Collections;
using AdvancedSceneManager.Callbacks.Events;
using AdvancedSceneManager.Core.Callbacks;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.ExampleScripts
{

    /// <summary>Contains examples for opening scenes.</summary>
    public class SceneOpen : MonoBehaviour
    {

        public Scene sceneToOpen;

        #region Open as standalone

        //Open as standalone (not associated with a collection)
        public void OpenStandalone()
        {
            sceneToOpen.Open();
            //Equivalent to:
            //SceneManager.standalone.Open(sceneToOpen);
        }

        #endregion
        #region Open single

        //Close all open scenes, and collection, and then open this scene
        public void OpenSingle() =>
            SceneManager.runtime.CloseAll().Open();

        #endregion
        #region Loading screen

        public void OpenWithLoadingScreen(Scene loadingScreen)
        {

            if (!loadingScreen)
            {
                //LoadingScreenUtility.fade will be null if default fade loading screen scene has been deleted / un-included from build
                loadingScreen = LoadingScreenUtility.fade;
            }

            sceneToOpen.Open().
                With(loadingScreen).
                WithoutLoadingScreen(). //Disables loading screen if needed
                WithLoadingScreen(); //Enables it again

        }

        #endregion
        #region Fluent api / Chaining

        public void ChainingExample()
        {

            //Open(), and other similar ASM methods, return SceneOperation.
            //SceneOperation has a fluent api that can configure it within exactly one frame of it starting (note that operations are queued, so: queue time + 1 frame). 
            sceneToOpen.Open().
                With(LoadPriority.High). //Sets Application.backgroundLoadingPriority for the duration of the operation
                UnloadUsedAssets(). //Calls Resources.UnloadUnusedAssets() after all scenes have been loaded / unloaded
                RegisterCallback<LoadingScreenOpenPhaseEvent>(e => Debug.Log("Loading screen opened."), When.After).
                RegisterCallback<SceneOpenPhaseEvent>(e => e.WaitFor(DoStuffInCoroutine), When.After);

        }

        IEnumerator DoStuffInCoroutine()
        {
            //ASM will wait for this coroutine to finish before continuing normal operation
            yield return new WaitForSeconds(1);
        }

        #endregion

        public void Toggle() => sceneToOpen.ToggleOpen();

    }

}
