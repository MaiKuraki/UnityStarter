using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Utility;

namespace AdvancedSceneManager.Core
{

    partial class SceneOperation
    {

        /// <summary>Specifies description for operation coroutine.</summary>
        public SceneOperation WithFriendlyText(string text) =>
            Set(() =>
            {
                description = text;
                if (operationCoroutine is not null)
                    operationCoroutine.description = description;
            });

        /// <summary>Opens the scene, and makes sure it is activated afterwards.</summary>
        public SceneOperation OpenAndActivate(Scene scene)
        {
            Open(scene);
            Activate(scene);
            return this;
        }

        /// <summary>Sets focus to the specified scene. Overrides selected scene in collections.</summary>
        /// <remarks>No effect if preloading.</remarks>
        public SceneOperation Focus(Scene scene) =>
            Set(() => focus = scene);

        /// <summary>Sets focus to the specified scene. Overrides selected scene in collections. If <see langword="null"/>, then the first scene opened will be set as active.</summary>
        /// <remarks>No effect if preloading.</remarks>
        public SceneOperation Activate(Scene scene = null)
        {

            if (scene)
                focus = scene;
            else
                focusSingleScene = true;

            return this;

        }

        //Collection

        /// <summary>Specifies an associated collection.</summary>
        public SceneOperation With(SceneCollection collection, bool setActiveScene = false) =>
            Set(() => { this.collection = collection; setActiveCollectionScene = setActiveScene; });

        /// <inheritdoc cref="Runtime.Open(SceneOperation, SceneCollection, bool)"/>
        public SceneOperation Open(SceneCollection collection, bool openAll = false) =>
            SceneManager.runtime.Open(this, collection, openAll);

        /// <inheritdoc cref="Runtime.Close(SceneOperation, SceneCollection)"/>
        public SceneOperation Close(SceneCollection collection) =>
            SceneManager.runtime.Close(this, collection);

        //Lists

        /// <summary>Specifies the scenes to open.</summary>
        /// <remarks>Can be called multiple times to add more scenes.</remarks>
        public SceneOperation Open(params Scene[] scenes) => Open(scenes.AsEnumerable());
        public SceneOperation PrependOpen(params Scene[] scenes) => PrependOpen(scenes.AsEnumerable(), false);

        /// <summary>Specifies the scenes to close.</summary>
        /// <remarks>Can be called multiple times to add more scenes.</remarks>
        public SceneOperation Close(params Scene[] scenes) => Close(scenes.AsEnumerable());

        ///// <summary>Specifies user callbacks.</summary>
        //public SceneOperation Callback(params Callback[] callbacks) => Callback(callbacks.AsEnumerable());

        /// <summary>Specifies scenes to preload.</summary>
        /// <remarks>A scene specified to preload cannot also be added to open or close lists.</remarks>
        public SceneOperation Preload(params Scene[] scenes) => Set(() => Preload(scenes.AsEnumerable()));

        /// <summary>Specifies scenes to preload.</summary>
        /// <remarks>A scene specified to preload cannot also be added to open or close lists.</remarks>
        public SceneOperation Preload(IEnumerable<Scene> scenes) => Set(() => preload = preload.Concat(scenes));

        /// <inheritdoc cref="Open(Scene[])"/>
		public SceneOperation Open(IEnumerable<Scene> scenes) => Set(() => open = open.Concat(scenes));
        public SceneOperation PrependOpen(IEnumerable<Scene> scenes, bool ignoreForActivation)
        {
            open = scenes.Concat(open);
            if (ignoreForActivation)
                IgnoreForActivation(scenes);
            return this;
        }

        /// <inheritdoc cref="Close(Scene[])"/>
        public SceneOperation Close(IEnumerable<Scene> scenes) => Set(() => close = close.Concat(scenes));

        ///// <inheritdoc cref="Callback(Core.Callback[])"/>
        //public SceneOperation Callback(IEnumerable<Callback> callbacks) => Set(() => this.callbacks = this.callbacks.Concat(callbacks));

        /// <summary>Specifies scenes that should not be activated.</summary>
        public SceneOperation IgnoreForActivation(IEnumerable<Scene> scenes) =>
            Set(() => ignoreForActivation = ignoreForActivation.Concat(scenes));

        //Loading

        /// <summary>Specifies loading screen to use.</summary>
        /// <remarks><paramref name="loadingScene"/> has no effect if <see cref="useLoadingScene"/> is <see langword="false"/>.</remarks>
        public SceneOperation With(Scene loadingScene, bool useLoadingScene = true) => Set(() =>
        {
            this.loadingScene = loadingScene;
            this.useLoadingScene = useLoadingScene;
        });

        /// <summary>Specifies loading screen to use.</summary>
        /// <remarks><paramref name="loadingScene"/> has no effect if <see cref="useLoadingScene"/> is <see langword="false"/>.</remarks>
        public SceneOperation WithLoadingScreen(Scene loadingScene, bool useLoadingScene = true) => Set(() =>
        {
            this.loadingScene = loadingScene;
            this.useLoadingScene = useLoadingScene;
        });

        /// <summary>Specifies loading screen to use.</summary>
        /// <remarks><see cref="loadingScene"/> has no effect if <see cref="useLoadingScene"/> is <see langword="false"/>.</remarks>
        public SceneOperation With(bool useLoadingScene = true) => Set(() => this.useLoadingScene = useLoadingScene);

        /// <summary>Specifies a callback when loading screen is opened, before <see cref="LoadingScreen.OnOpen"/> is called.</summary>
        public SceneOperation With(Action<LoadingScreen> loadingScreenCallback) => Set(() => this.loadingScreenCallback = loadingScreenCallback);

        /// <summary>Specifies whatever loading screen should be used.</summary>
        public SceneOperation WithoutLoadingScreen(bool useLoadingScene = false) => Set(() => this.useLoadingScene = useLoadingScene);

        /// <summary>Specifies whatever loading screen should be used.</summary>
        public SceneOperation WithLoadingScreen(bool useLoadingScene = true) => Set(() => this.useLoadingScene = useLoadingScene);

        /// <summary>Adds a callback when progress changed.</summary>
        public SceneOperation OnProgressChanged(Action<float> callback) =>
            Set(() => progressScope.OnProgressChanged(callback));

        /// <summary>Removes a callback when progress changed.</summary>
		public SceneOperation RemoveOnProgressChangedCallback(Action<float> callback) =>
            Set(() => progressScope.RemoveOnProgressChangedCallback(callback));

        /// <summary>Disables progress reporting for this operation.</summary>
        /// <remarks>This will disable scene loaders from reporting progress to <see cref="LoadingScreenUtility.ReportProgress(ILoadProgressData)"/>. Loading screens and other listeners will not receive reports.</remarks>
        public SceneOperation DisableProgressReporting() =>
            Set(() => reportsProgress = false);

        //Options

        /// <summary>Specifies whatever <see cref="Resources.UnloadUnusedAssets"/> should be called at the end (before loading screen).</summary>
        public SceneOperation UnloadUsedAssets() => Set(() => unloadUnusedAssets = true);

        /// <summary>Sets the <see cref="LoadPriority"/> this operation will use.</summary>
        /// <remarks>Overrides <see cref="SceneCollection.loadPriority"/> and <see cref="Scene.loadPriority"/> if set.</remarks>
        public SceneOperation With(LoadPriority loadPriority) =>
            Set(() => this.loadPriority = loadPriority);

        /// <summary>Sets the operation flags this operation will use.</summary>
        /// <remarks>Use this to disable steps of operation for better performance.</remarks>
        public SceneOperation With(SceneOperationFlags flags) =>
            Set(() => this.flags = flags);

        //Convenience

        /// <summary>Closes all scenes prior to opening any scenes.</summary>
        public SceneOperation CloseAll(params Scene[] except) =>
            CloseAll(except: except);

        /// <summary>Closes all non-persistent scenes prior to opening any scenes.</summary>
        public SceneOperation CloseAllNonPersistent(params Scene[] except) =>
            CloseAll(persistent: false, except: except);

        /// <summary>Closes all scenes prior to opening any scenes.</summary>
        public SceneOperation CloseAll(bool loadingScene = true, bool splashScreen = true, bool persistent = true, IEnumerable<Scene> except = null)
        {

            var scenes = SceneManager.runtime.openScenes;
            if (!loadingScene)
                scenes = scenes.Where(s => !s.isLoadingScreen);

            if (!splashScreen)
                scenes = scenes.Where(s => !s.isSplashScreen);

            if (!persistent)
                scenes = scenes.Where(s => !s.isPersistent);

            if (except is not null)
                scenes = scenes.Except(except);

            //scenes.Log(logWithNoItems: true);

            return Close(scenes);

        }

        /// <summary>Specifies whatever scene callbacks should run outside of play mode.</summary>
        public SceneOperation RunSceneCallbacksOutsidePlayMode(bool value) =>
            Set(() => runSceneCallbacksOutsideOfPlayMode = value);

        bool isFrozen;
        SceneOperation Set(Action action)
        {

            if (isFrozen)
                throw new InvalidOperationException("Cannot change properties when frozen.");

            action.Invoke();
            return this;

        }

        void Freeze() => isFrozen = true;

    }

}
