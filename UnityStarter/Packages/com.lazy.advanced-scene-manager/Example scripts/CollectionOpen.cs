using System.Collections;
using AdvancedSceneManager.Callbacks.Events;
using AdvancedSceneManager.Core.Callbacks;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.ExampleScripts
{

    /// <summary>Contains examples for opening collections.</summary>
    public class CollectionOpen : MonoBehaviour
    {

        public SceneCollection collectionToOpen;

        #region Open

        public void Open()
        {
            collectionToOpen.Open();
            //Equivalent to:
            //SceneManager.runtime.Open(collectionToOpen);
        }

        #endregion
        #region Open with user data

        public void OpenWithUserData(ScriptableObject scriptableObject)
        {
            //Note: Overrides data set from scene manager window
            collectionToOpen.userData = scriptableObject;
            collectionToOpen.Open();
        }

        #endregion
        #region Open with loading screen

        //Overrides loading screen
        public void OpenWithLoadingScreen(Scene loadingScreen)
        {

            if (!loadingScreen)
            {
                //LoadingScreenUtility.fade will be null if default fade loading screen scene has been deleted or otherwise un-imported from ASM.
                loadingScreen = LoadingScreenUtility.fade;
            }

            collectionToOpen.Open().
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
            collectionToOpen.Open().
                With(LoadPriority.High).      //Sets Application.backgroundLoadingPriority for the duration of the operation
                UnloadUsedAssets().             //Calls Resources.UnloadUnusedAssets() after all scenes have been loaded / unloaded
                RegisterCallback<LoadingScreenOpenPhaseEvent>(e => Debug.Log("Loading screen opened."), When.After).
                RegisterCallback<SceneOpenPhaseEvent>(e => e.WaitFor(DoStuffInCoroutine), When.After);

        }

        IEnumerator DoStuffInCoroutine()
        {
            //ASM will wait for this coroutine to finish before continuing normal operation
            yield return new WaitForSeconds(1);
        }

        #endregion

        public void ToggleOpen() => collectionToOpen.ToggleOpen();

    }

}
