using System;
using System.Collections;
using UnityEngine;

namespace AdvancedSceneManager.Core
{

    /// <summary>Specifies arguments for <see cref="SceneLoader.LoadScene(Models.Scene, SceneLoadArgs)"/>.</summary>
    public class SceneLoadArgs : SceneLoaderArgsBase
    {

        /// <summary>Specifies if the scene should be preloaded.</summary>
        public bool isPreload { get; internal set; }
        internal Func<IEnumerator> preloadCallback { get; set; }

        /// <summary>Notifies ASM that the load is done.</summary>
        /// <param name="scene">The opened scene.</param>
        public void SetCompleted(UnityEngine.SceneManagement.Scene scene)
        {
            this.scene.internalScene = scene;
            isHandled = true;
        }

        /// <inheritdoc cref="SetCompleted(UnityEngine.SceneManagement.Scene)"/>
        /// <param name="scene">The opened scene.</param>
        /// <param name="preloadCallback">Specifies a callback that will be called when it is time to activate preloaded scene.</param>
        public void SetCompleted(UnityEngine.SceneManagement.Scene scene, Func<IEnumerator> preloadCallback)
        {
            this.preloadCallback = preloadCallback;
            SetCompleted(scene);
        }

        /// <summary>Sets this loader as complete even though no scene was loaded.</summary>
        public void SetCompletedWithoutScene()
        {
            isHandled = true;
            noSceneWasLoaded = true;
        }

        /// <summary>Checks if the scene is actually included in build.</summary>
        public bool CheckIsIncluded(bool logError = true)
        {

            if (scene.isIncludedInBuilds)
                return true;
            else
            {
                if (logError)
                    Debug.LogError($"The scene ('{scene.path}') could not be opened because it is not added to build settings.");
                return false;
            }

        }

        /// <summary>Gets the <see cref="UnityEngine.SceneManagement.Scene"/> that was opened by this override.</summary>
        /// <remarks>Will return <see langword="default"/> if not found.</remarks>
        public UnityEngine.SceneManagement.Scene GetOpenedScene()
        {

            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(this.scene.path);
            Debug.Assert(scene.IsValid(), "Could not find unity scene after loading it.");
            return scene;

        }

    }

}
