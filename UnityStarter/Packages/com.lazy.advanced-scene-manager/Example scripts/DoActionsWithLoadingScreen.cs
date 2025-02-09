using System.Collections;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.ExampleScripts
{

    /// <summary>Contains examples for doing <see cref="System.Action"/> with loading screen.</summary>
    public class DoActionsWithLoadingScreen
    {

        //Note that fade loading screen might be null, if not included in build, or moved / removed.
        public Scene loadingScreen = LoadingScreenUtility.fade;

        #region Action

        public void DoActionWithLoadingScreen()
        {

            //Can't show loading screen, if we do not have one
            if (!loadingScreen)
                return;

            //Start loading screen, which will then:
            //1. Open loading screen,
            //2. Run action,
            //3. Close loading screen.
            LoadingScreenUtility.DoAction(loadingScreen, () => Debug.Log("Performing action..."));

        }

        #endregion
        #region Coroutine

        public void DoCoroutineWithLoadingScreen()
        {

            //Can't show loading screen, if we do not have one
            if (!loadingScreen)
                return;

            //Start loading screen, which will then:
            //1. Open loading screen,
            //2. Run action,
            //3. Close loading screen.
            //Note that coroutine needs to be passed as a Func<IEnumerator>, because otherwise coroutine will start too early.
            LoadingScreenUtility.DoAction(loadingScreen, Coroutine);

        }

        IEnumerator Coroutine()
        {
            Debug.Log("Starting coroutine...");
            yield return new WaitForSecondsRealtime(1);
            Debug.Log("Completed coroutine...");
        }

        #endregion

    }

}
