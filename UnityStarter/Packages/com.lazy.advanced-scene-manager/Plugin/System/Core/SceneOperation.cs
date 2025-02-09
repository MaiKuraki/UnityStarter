using System.Collections;
using System.Linq;
using AdvancedSceneManager.Callbacks.Events;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.Core
{

    /// <summary>A scene operation is a queueable operation that can open or close scenes. See also: <see cref="SceneAction"/>.</summary>
    public partial class SceneOperation : CustomYieldInstruction, IQueueable
    {

        bool hasRun;
        IEnumerator Run()
        {

            wasCancelled = false;

            yield return PrepareForExecution();

            if (wasCancelled)
                yield break;

            yield return ShowLoadingScreen();

            yield return CloseScenes();
            yield return OpenScenes();
            yield return PreloadScenes();

            yield return PerformFinalSteps();
            yield return HideLoadingScreen();

            yield return FinalizeExecution();

        }

        private IEnumerator PerformFinalSteps()
        {

            if (!SceneManager.runtime.preloadedScenes.Any())
                yield return UnloadUnusedAssets();

            //If user attempts to open a scene in Start(), Awake(), OnEnable(), ISceneOpen, ICollectionOpen, then we want this operation to wait for it
            foreach (var operation in waitFor.ToArray())
                yield return operation;

        }

        private IEnumerator FinalizeExecution()
        {

            //Fallback scene should not have focus / activation
            FallbackSceneUtility.EnsureNotActive();

            yield return InvokeCallback<EndPhaseEvent>(new());

            hasRun = true;

            LogUtility.LogEnd(this);

#if UNITY_EDITOR
            LogOperationEnd();
#endif

        }

        #region Logging

#if UNITY_EDITOR

        long timestamp;
        private void LogOperationStart()
        {
            timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        private void LogOperationEnd()
        {
            if (openedScenes.Any() && SceneManager.settings.user.logOperation)
            {
                string loadingMode = SceneManager.settings.project.allowLoadingScenesInParallel ? "[Parallel]" : "[Sequential]";
                string collectionInfo = $"({this.collection})";

                Debug.Log($"{loadingMode} {collectionInfo} Scenes loaded in: {StopwatchUtility.GetElapsedTime(timestamp)}s");
            }
        }

#endif

        #endregion

    }

}
