#nullable enable

using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.Core
{

    /// <summary>Provides extensions for scene loading.</summary>
    /// <remarks>This provides access to direct scene loading / unloading, which bypasses many checks that .Open() / .Close() has. Make sure to test thoroughly.</remarks>
    public static class SceneLoaderExtensions
    {

        /// <inheritdoc cref="Load(Scene, bool, SceneOperation?, SceneCollection?, bool, ThreadPriority?, Action?, Action{string}?)" />
        public static IEnumerator Load(this Scene scene, SceneLoadArgs e)
        {
            yield return scene.Load(e.isPreload, e.operation, e.collection, e.reportProgress);
        }

        /// <inheritdoc cref="Unload(Scene, SceneOperation?, SceneCollection?, bool, ThreadPriority?, Action?, Action{string}?)" />
        public static IEnumerator Unload(this Scene scene, SceneUnloadArgs e)
        {
            yield return scene.Unload(e.operation, e.collection, e.reportProgress);
        }

        /// <summary>Loads the scene using a scene loader.</summary>
        /// <remarks>This loads directly, bypassing many checks that .Open() has.</remarks>
        /// <param name="scene">The scene to load.</param>
        /// <param name="isPreload">Specifies whatever scene should be preloaded.</param>
        /// <param name="operation">Specifies what operation this load is part of, if any.</param>
        /// <param name="collection">Specifies if this is part of a collection open / close operation.</param>
        /// <param name="reportsProgress">Specifies if progress should be reported.</param>
        /// <param name="onLoaded">Callback for when scene is successfully loaded.</param>
        /// <param name="onError">Callback for when an error occurs.</param>
        public static IEnumerator Load(this Scene scene, bool isPreload = false, SceneOperation? operation = null, SceneCollection? collection = null, bool reportsProgress = true, ThreadPriority? loadPriority = null, Action? onLoaded = null, Action<string>? onError = null)
        {

            if (!scene || scene.isOpen)
                yield break;

            var e = new SceneLoadArgs()
            {
                operation = operation,
                scene = scene,
                collection = collection,
                isPreload = isPreload,
                reportProgress = reportsProgress,
            };

            yield return RunSceneLoader(e, loadPriority);

            if (Validate(e, out var errorReason))
            {

                SceneManager.runtime.Track(scene);
                if (e.isPreload && e.preloadCallback is not null)
                    SceneManager.runtime.TrackPreload(scene, e.preloadCallback);

                scene.openedBy = collection;
                onLoaded?.Invoke();

            }
            else
            {
                e.scene.internalScene = null;
                onError?.Invoke(errorReason);
            }

        }

        /// <summary>Unloads the scene using a scene loader.</summary>
        /// <remarks>This loads directly, bypassing many checks that .Close() has.</remarks>
        /// <param name="scene">The scene to unload.</param>
        /// <param name="operation">Specifies what operation this unload is part of, if any.</param>
        /// <param name="collection">Specifies if this is part of a collection open / close operation.</param>
        /// <param name="reportsProgress">Specifies if progress should be reported.</param>
        /// <param name="onUnloaded">Callback for when scene is successfully unloaded.</param>
        /// <param name="onError">Callback for when an error occurs.</param>
        public static IEnumerator Unload(this Scene scene, SceneOperation? operation = null, SceneCollection? collection = null, bool reportsProgress = true, ThreadPriority? loadPriority = null, Action? onUnloaded = null, Action<string>? onError = null)
        {

            if (!scene || !scene.isOpen)
                yield break;

            var e = new SceneUnloadArgs()
            {
                operation = operation,
                scene = scene,
                collection = collection,
                reportProgress = reportsProgress,
            };

            yield return RunSceneLoader(e, loadPriority);

            if (Validate(e, out var errorReason))
            {
                _ = SceneManager.runtime.Untrack(scene);
                onUnloaded?.Invoke();
            }
            else
            {
                e.scene.internalScene = null;
                onError?.Invoke(errorReason);
            }

        }

        static IEnumerator RunSceneLoader(SceneLoaderArgsBase e, ThreadPriority? loadPriority = null)
        {

            var loader = e.scene.GetEffectiveSceneLoader();
            if (loader is null)
                yield break;

            var savedPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = loadPriority ?? Application.backgroundLoadingPriority;

            if (e is SceneLoadArgs loadArgs)
            {
                LogUtility.LogLoaded(loader, loadArgs);
                yield return loader.LoadScene(e.scene, loadArgs);
            }
            else if (e is SceneUnloadArgs unloadArgs)
            {
                LogUtility.LogUnloaded(loader, unloadArgs);
                yield return loader.UnloadScene(e.scene, unloadArgs);
            }

            Application.backgroundLoadingPriority = savedPriority;

        }

        static bool Validate(SceneLoaderArgsBase e, [NotNullWhen(false)] out string? errorReason)
        {

            var operation = e is SceneLoadArgs ? "load" : "unload";

            if (e.isError)
                errorReason = e.errorMessage;
            else if (!e.isHandled)
                errorReason = $"Could not find a scene loader to {operation} scene:\n{e.scene.path}";
            else if (e is SceneLoadArgs && e.noSceneWasLoaded)
            {
                errorReason = $"Scene loader returned that is was completed, but no scene loaded:\n{e.scene.path}";
                return true;
            }
            else
                errorReason = null;

            var isValid = e.scene.internalScene?.IsValid() ?? false;

            if (e is SceneLoadArgs && !isValid)
                errorReason = "Could not find loaded scene.";
            else if (e is SceneUnloadArgs && isValid)
                errorReason = "Could not unload scene.";

            return string.IsNullOrEmpty(errorReason);

        }

    }

}
