#if NETCODE && UNITY_2021_1_OR_NEWER

using System.Collections;
using System.Linq;
using Lazy.Utility;
using Unity.Netcode;
using UnityEngine;

namespace AdvancedSceneManager.PackageSupport.Netcode
{

    static class SceneValidator
    {
        private static NetworkManager networkManager;

        public static void Initialize()
        {

            WaitForNetworkManager().StartCoroutine();

            SceneManager.settings.project.PropertyChanged += (s, e) => UpdateSceneValidationEnabled();
            UpdateSceneValidationEnabled();

        }

        static void UpdateSceneValidationEnabled()
        {
            if (Application.isPlaying && SceneManager.settings.project.isNetcodeValidationEnabled)
                OnEnable();
            else
                OnDisable();
        }

        static void OnEnable()
        {

            OnDisable();

            if (networkManager && networkManager.SceneManager is not null)
            {
                networkManager.SceneManager.SetClientSynchronizationMode(UnityEngine.SceneManagement.LoadSceneMode.Additive);
                networkManager.SceneManager.ActiveSceneSynchronizationEnabled = true;
                networkManager.SceneManager.VerifySceneBeforeLoading += Validate;
                networkManager.SceneManager.OnSceneEvent += OnSceneEvent;
            }

        }

        static void OnDisable()
        {
            if (networkManager && networkManager.SceneManager is not null)
            {
                networkManager.SceneManager.VerifySceneBeforeLoading -= Validate;
                networkManager.SceneManager.OnSceneEvent -= OnSceneEvent;
            }
        }

        private static IEnumerator WaitForNetworkManager()
        {

            yield return new WaitUntil(() => NetworkManager.Singleton);

            networkManager = NetworkManager.Singleton;
            networkManager.OnServerStarted += OnServerStarted;

        }

        private static void OnServerStarted() =>
            UpdateSceneValidationEnabled();

        // just me debugging
        private static void OnSceneEvent(SceneEvent sceneEvent)
        {
            //Debug.Log("---");
            //Debug.Log(sceneEvent.ClientId != NetworkManager.ServerClientId ? "Client" : "Server");
            //Debug.Log($"Index: {sceneEvent.Scene.buildIndex}, Name: {sceneEvent.Scene.name}, LoadMode: {sceneEvent.LoadSceneMode}");
        }


        // validate tells server what to sync, currently its just netcode marked scenes, perhaps make it so it can be overwritten?
        private static bool Validate(int sceneIndex, string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode)
        {
            // complained out of scope, fix.
            var scenes = SceneManager.assets.scenes.Where(s => s.isNetcode);
            return scenes.Any(x => x.name == sceneName);
        }
    }

}

#endif

