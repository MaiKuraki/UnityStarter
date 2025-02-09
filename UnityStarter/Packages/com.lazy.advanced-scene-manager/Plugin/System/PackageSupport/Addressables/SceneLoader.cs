#if ADDRESSABLES

using System.Collections;
using System.Collections.Generic;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AdvancedSceneManager.PackageSupport.Addressables
{

    class SceneLoader : Core.SceneLoader
    {

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod]
        static void OnLoad() =>
            SceneManager.OnInitialized(() =>
            {
                sceneInstances.Clear();
                SceneManager.runtime.AddSceneLoader<SceneLoader>();
            });

        static readonly Dictionary<Scene, SceneInstance> sceneInstances = new();

        public override string sceneToggleText => "Addressable";
        public override Indicator indicator => new() { useFontAwesome = true, text = "" };

        public override bool isGlobal => false;
        public override bool addScenesToBuildSettings => false;

        public override IEnumerator LoadScene(Scene scene, SceneLoadArgs e)
        {

            if (!e.scene.isAddressable)
                yield break;

            var address = scene.address;
            if (string.IsNullOrWhiteSpace(address))
            {
                Debug.LogError("Could not find address for scene: " + e.scene.name);
                yield break;
            }

            var async = UnityEngine.AddressableAssets.Addressables.
                LoadSceneAsync(address, loadMode: UnityEngine.SceneManagement.LoadSceneMode.Additive, activateOnLoad: !e.isPreload);

            if (e.reportProgress)
                yield return async.ReportProgress(SceneOperationKind.Load, scene, e.operation);

            yield return async;

            if (async.OperationException != null)
            {
                Debug.LogError(async.OperationException);
                e.SetCompleted(default);
                yield break;
            }
            else
            {
                sceneInstances.Set(e.scene, async.Result);
                if (e.isPreload)
                    e.SetCompleted(e.GetOpenedScene(), ActivatePreloadedScene);
                else
                    e.SetCompleted(e.GetOpenedScene());
            }

            IEnumerator ActivatePreloadedScene()
            {
                yield return async.Result.ActivateAsync();
            }

        }

        public override IEnumerator UnloadScene(Scene scene, SceneUnloadArgs e)
        {

            if (!e.scene)
                yield break;

            if (!sceneInstances.TryGetValue(e.scene, out var instance))
                yield break;
            _ = sceneInstances.Remove(e.scene);

            var async = UnityEngine.AddressableAssets.Addressables.UnloadSceneAsync(instance);

            if (e.reportProgress)
                async.ReportProgress(SceneOperationKind.Unload, scene, e.operation);

            yield return async;

            e.SetCompleted();

        }

    }

}
#endif
