using System;
using System.Collections.Generic;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;

namespace AdvancedSceneManager.DependencyInjection
{

    /// <inheritdoc cref="SceneManager.runtime"/>
    public interface IRuntime : ISceneManager
    { }

    /// <inheritdoc cref="SceneManager.runtime"/>
    public interface ISceneManager : DependencyInjectionUtility.IInjectable
    {

        /// <inheritdoc cref="Runtime.activeScene"/>
        Scene activeScene { get; }

        /// <inheritdoc cref="Runtime.currentOperation"/>
        SceneOperation currentOperation { get; }

        /// <inheritdoc cref="Runtime.dontDestroyOnLoad"/>
        Scene dontDestroyOnLoad { get; }

        /// <inheritdoc cref="Runtime.isBusy"/>
        bool isBusy { get; }

        /// <inheritdoc cref="Runtime.openAdditiveCollections"/>
        IEnumerable<SceneCollection> openAdditiveCollections { get; }

        /// <inheritdoc cref="Runtime.openCollection"/>
        SceneCollection openCollection { get; }

        /// <inheritdoc cref="Runtime.openScenes"/>
        IEnumerable<Scene> openScenes { get; }

        /// <inheritdoc cref="Runtime.preloadedScenes"/>
        IEnumerable<Scene> preloadedScenes { get; }

        /// <inheritdoc cref="Runtime.queuedOperations"/>
        IEnumerable<SceneOperation> queuedOperations { get; }

        /// <inheritdoc cref="Runtime.runningOperations"/>
        IEnumerable<SceneOperation> runningOperations { get; }

        /// <inheritdoc cref="Runtime.collectionClosed"/>
        event Action<SceneCollection> collectionClosed;

        /// <inheritdoc cref="Runtime.collectionOpened"/>
        event Action<SceneCollection> collectionOpened;

        /// <inheritdoc cref="Runtime.sceneClosed"/>
        event Action<Scene> sceneClosed;

        /// <inheritdoc cref="Runtime.sceneOpened"/>
        event Action<Scene> sceneOpened;

        /// <inheritdoc cref="Runtime.scenePreloaded"/>
        event Action<Scene> scenePreloaded;

        /// <inheritdoc cref="Runtime.scenePreloadFinished"/>
        event Action<Scene> scenePreloadFinished;

        /// <inheritdoc cref="Runtime.startedWorking"/>
        event Action startedWorking;

        /// <inheritdoc cref="Runtime.stoppedWorking"/>
        event Action stoppedWorking;

        /// <inheritdoc cref="Runtime.AddSceneLoader"/>
        void AddSceneLoader<T>() where T : SceneLoader, new();

        /// <inheritdoc cref="Runtime.Close"/>
        SceneOperation Close(IEnumerable<Scene> scenes);

        /// <inheritdoc cref="Runtime.Close"/>
        SceneOperation Close(params Scene[] scenes);

        /// <inheritdoc cref="Runtime.Close"/>
        SceneOperation Close(Scene scene);

        /// <inheritdoc cref="Runtime.Close"/>
        SceneOperation Close(SceneCollection collection);

        /// <inheritdoc cref="Runtime.CloseAll"/>
        SceneOperation CloseAll(bool exceptLoadingScreens = true, bool exceptUnimported = true, params Scene[] except);

        /// <inheritdoc cref="Runtime.DiscardPreload"/>
        SceneOperation DiscardPreload();

        /// <inheritdoc cref="Runtime.CancelPreload"/>
        SceneOperation CancelPreload();

        /// <inheritdoc cref="Runtime.FinishPreload"/>
        SceneOperation FinishPreload();

        /// <inheritdoc cref="Runtime.GetLoaderForScene"/>
        SceneLoader GetLoaderForScene(Scene scene);

        /// <inheritdoc cref="Runtime.GetState"/>
        SceneState GetState(Scene scene);

        /// <inheritdoc cref="Runtime.GetToggleableSceneLoaders"/>
        IEnumerable<SceneLoader> GetToggleableSceneLoaders();

        /// <inheritdoc cref="Runtime.IsTracked"/>
        bool IsTracked(Scene scene);

        /// <inheritdoc cref="Runtime.IsTracked"/>
        bool IsTracked(SceneCollection collection);

        /// <inheritdoc cref="Runtime.Open"/>
        SceneOperation Open(IEnumerable<Scene> scenes);

        /// <inheritdoc cref="Runtime.Open"/>
        SceneOperation Open(params Scene[] scenes);

        /// <inheritdoc cref="Runtime.Open"/>
        SceneOperation Open(Scene scene);

        /// <inheritdoc cref="Runtime.Open"/>
        SceneOperation Open(SceneCollection collection, bool openAll = false);

        /// <inheritdoc cref="Runtime.OpenAdditive"/>
        SceneOperation OpenAdditive(IEnumerable<SceneCollection> collections, SceneCollection activeCollection = null, Scene loadingScene = null);

        /// <inheritdoc cref="Runtime.OpenAdditive"/>
        SceneOperation OpenAdditive(SceneCollection collection, bool openAll = false);

        /// <inheritdoc cref="Runtime.OpenAndActivate"/>
        SceneOperation OpenAndActivate(Scene scene);

        /// <inheritdoc cref="Runtime.OpenWithLoadingScreen"/>
        SceneOperation OpenWithLoadingScreen(IEnumerable<Scene> scene, Scene loadingScreen);

        /// <inheritdoc cref="Runtime.OpenWithLoadingScreen"/>
        SceneOperation OpenWithLoadingScreen(Scene scene, Scene loadingScreen);

        /// <inheritdoc cref="Runtime.Preload"/>
        SceneOperation Preload(Scene scene, Action onPreloaded = null);

        /// <inheritdoc cref="Runtime.RemoveSceneLoader"/>
        void RemoveSceneLoader<T>();

        /// <inheritdoc cref="Runtime.SetActive"/>
        void SetActive(Scene scene);

        /// <inheritdoc cref="Runtime.ToggleOpen"/>
        SceneOperation ToggleOpen(Scene scene);

        /// <inheritdoc cref="Runtime.ToggleOpen"/>
        SceneOperation ToggleOpen(SceneCollection collection, bool openAll = false);

        /// <inheritdoc cref="Runtime.Track"/>
        void Track(Scene scene);

        /// <inheritdoc cref="Runtime.Track"/>
        void Track(Scene scene, UnityEngine.SceneManagement.Scene unityScene);

        /// <inheritdoc cref="Runtime.Track"/>
        void Track(SceneCollection collection, bool isAdditive = false);

        /// <inheritdoc cref="Runtime.Untrack"/>
        bool Untrack(Scene scene);

        /// <inheritdoc cref="Runtime.Untrack"/>
        void Untrack(SceneCollection collection, bool isAdditive = false);

        /// <inheritdoc cref="Runtime.UntrackCollections"/>
        void UntrackCollections();

        /// <inheritdoc cref="Runtime.UntrackScenes"/>
        void UntrackScenes();

    }

}
