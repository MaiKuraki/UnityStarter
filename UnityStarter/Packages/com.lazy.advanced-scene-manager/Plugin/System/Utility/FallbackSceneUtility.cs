using UnityEngine;
using scene = UnityEngine.SceneManagement.Scene;
using sceneManager = UnityEngine.SceneManagement.SceneManager;
using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Internal;
using UnityEngine.SceneManagement;
using UnityEditor;




#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using editorSceneManager = UnityEditor.SceneManagement.EditorSceneManager;
#endif

namespace AdvancedSceneManager.Utility
{

    [InitializeInEditor]
    /// <summary>An utility class that manages the default scene, called 'AdvancedSceneManager'.</summary>
    /// <remarks>The default scene allows us to more easily close all scenes when we need to, since unity requires at least one scene to be open at any time.</remarks>
    public static class FallbackSceneUtility
    {

        static FallbackSceneUtility() =>
            SceneManager.OnInitialized(() => sceneManager.activeSceneChanged += PreventFallbackSceneActivation);

        #region Active check

        static void PreventFallbackSceneActivation(scene previousScene, scene newScene)
        {

            if (!IsFallbackScene(newScene))
                return;

            if (SceneUtility.GetAllOpenUnityScenes().Count(IsValidScene) == 0)
                return;

            SceneManager.runtime.Activate(SceneManager.openScenes.LastOrDefault());

        }

        static bool IsValidScene(scene scene) =>
            !scene.isLoaded && IsSpecialScene(scene);

        static bool IsSpecialScene(scene scene) =>
            IsFallbackScene(scene) ||
            (SceneManager.runtime.dontDestroyOnLoad && SceneManager.runtime.dontDestroyOnLoad.internalScene?.handle == scene.handle);

        #endregion
        #region Startup scene

        public const string Name = "ASM - Fallback scene";

        public static bool isActive => IsFallbackScene(sceneManager.GetActiveScene());

        internal static void EnsureOpen()
        {
#if UNITY_EDITOR
            var fallbackScene = GetScene();
            if (!fallbackScene.isLoaded)
                Load(GetPath());
#endif
        }

        /// <summary>Gets whatever the default scene is open.</summary>
        internal static bool isOpen =>
            GetScene().isLoaded;

        /// <summary>Gets whatever the specified scene is the default scene.</summary>
        public static bool IsFallbackScene(scene scene) =>
            scene.IsValid() && scene.buildIndex == 0;

        /// <summary>Gets the fallback scene.</summary>
        /// <remarks>This would be the scene at build index 0. If this method returns the wrong scene, then please verify build scene list, fallback scene should be automatically inserted at the top.</remarks>
        public static scene GetScene()
        {
            return sceneManager.GetSceneByBuildIndex(0);
        }

        /// <summary>Gets the fallback scene.</summary>
        /// <remarks>This would be the scene at build index 0. If this method returns the wrong scene, then please verify build scene list, fallback scene should be automatically inserted at the top.</remarks>
        public static string GetPath()
        {
            var scene = sceneManager.GetSceneByBuildIndex(0);
#if UNITY_EDITOR
            if (!scene.IsValid())
            {
                if (EditorBuildSettings.scenes.Length == 0)
                    throw new System.InvalidOperationException("Cannot retrieve fallback scene path.");
                return EditorBuildSettings.scenes.ElementAtOrDefault(0).path;
            }
#endif

            return scene.path;
        }

#if UNITY_EDITOR

        /// <summary>Close the default scene.</summary>
        ///<remarks>Only available in editor.</remarks>
        internal static void Close()
        {

            if (SceneUtility.GetAllOpenUnityScenes().Where(s => s.isLoaded).Count() == 1 && isOpen)
                return;

            var fallbackScene = GetScene();
            if (fallbackScene.isLoaded)
                Unload(fallbackScene);

        }

#endif

        static void Load(string path)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                editorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                return;
            }
#endif
            sceneManager.LoadScene(path, LoadSceneMode.Additive);
        }

        static void Unload(scene scene)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                editorSceneManager.CloseScene(scene, true);
                return;
            }
#endif
            sceneManager.UnloadSceneAsync(scene);
        }

        internal static string GetStartupScene() =>
            Profile.current && Profile.current.startupScene
            ? Profile.current.startupScene.path
            : Assets.fallbackScenePath;

        internal static void EnsureNotActive()
        {

            if (!isActive)
                return;

            var scene = SceneUtility.GetAllOpenUnityScenes().FirstOrDefault(s => !IsFallbackScene(s));
            if (scene.isLoaded)
                sceneManager.SetActiveScene(scene);
            else
                sceneManager.sceneLoaded += SceneManager_sceneLoaded;

            void SceneManager_sceneLoaded(scene arg0, UnityEngine.SceneManagement.LoadSceneMode arg1)
            {
                sceneManager.sceneLoaded -= SceneManager_sceneLoaded;
                sceneManager.SetActiveScene(arg0);
            }

        }

        #endregion

    }

}
