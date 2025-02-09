#if NETCODE && UNITY_2021_1_OR_NEWER

using System.Collections;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Models;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace AdvancedSceneManager.PackageSupport.Netcode
{

    class SceneLoader : Core.SceneLoader
    {

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod]
        static void Initialize() =>
        SceneManager.OnInitialized(() =>
            {
                SceneValidator.Initialize();
                SceneManager.runtime.AddSceneLoader<SceneLoader>();
            });

        public override string sceneToggleText => "Netcode";
        public override Indicator indicator => new() { useFontAwesome = true, text = "" };
        public override bool isGlobal => false;

        // tells asm to load/unload this scene with loader. 
        // we want default scene loader to unload a scene if the netcode scenemanager is null.
        public override bool CanHandleScene(Scene scene) => IsNetworkManagerInitialized;

        static bool IsNetworkManagerInitialized =>
            !(NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null);

        public override IEnumerator LoadScene(Scene scene, SceneLoadArgs e)
        {
            // it should not reach this as can open triggers first, but incase.
            if (!IsNetworkManagerInitialized)
            {
                e.SetError("Could not load scene, netcode is not initialized.");
                yield break;
            }

            //Logs error and calls e.NotifyComplete(handled: true)
            //if scene is not actually included in build,
            //which means we can just break then.
            //Remove this if the scene isn't supposed to be in build list, like addressable scenes
            if (!e.CheckIsIncluded())
                yield break;

            bool canContinue = false;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += SceneManager_OnSceneEvent;
            _ = NetworkManager.Singleton.SceneManager.LoadScene(e.scene, UnityEngine.SceneManagement.LoadSceneMode.Additive);

            yield return new WaitUntil(() => canContinue);

            NetworkManager.Singleton.SceneManager.OnSceneEvent -= SceneManager_OnSceneEvent;

            //Notify that we're complete, required
            //If handled: false, then normal ASM action will run
            e.SetCompleted(e.GetOpenedScene());

            void SceneManager_OnSceneEvent(SceneEvent e1)
            {
                if (e1.SceneEventType == SceneEventType.LoadEventCompleted)
                {
                    scene.isSynced = true;
                    SceneManager.runtime.Track(scene);
                    canContinue = true;
                }
            }

        }

        public override IEnumerator UnloadScene(Scene scene, SceneUnloadArgs e)
        {
            // it should not reach this as can open triggers first, but incase.
            if (!IsNetworkManagerInitialized)
            {
                e.SetError("Could not load scene, netcode is not initialized.");
                yield break;
            }

            bool canContinue = false;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += SceneManager_OnSceneEvent;
            _ = NetworkManager.Singleton.SceneManager.UnloadScene(scene);

            yield return new WaitUntil(() => canContinue);

            NetworkManager.Singleton.SceneManager.OnSceneEvent -= SceneManager_OnSceneEvent;

            //Scene is probably closed, but hierarchy might still display it,
            //so lets wait for it to update for good measure
            yield return null;

            void SceneManager_OnSceneEvent(SceneEvent e1)
            {
                if (e1.SceneEventType == SceneEventType.UnloadEventCompleted)
                {
                    scene.isSynced = false;
                    SceneManager.runtime.Untrack(scene);
                    canContinue = true;
                }
            }

            e.SetCompleted();
        }

        // not sure where this was used before, but we keep it incase.
        string GetFriendlyErrorMessage(SceneEventProgressStatus status) =>
            status switch
            {
                SceneEventProgressStatus.SceneNotLoaded => "Netcode: The scene could not be unloaded, since it was not loaded to begin with.",
                SceneEventProgressStatus.SceneEventInProgress => "Netcode: Only one scene can be loaded / unloaded at any given time.",
                SceneEventProgressStatus.InvalidSceneName => "Netcode: Invalid scene",
                SceneEventProgressStatus.SceneFailedVerification => "Netcode: Scene verification failed",
                SceneEventProgressStatus.InternalNetcodeError => "Netcode: Internal error",
                _ => null,
            };
    }
}

#endif
