using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;

namespace AdvancedSceneManager.Core
{

    [Flags]
    public enum SceneOperationFlags
    {
        None = 0,
        LoadingScreen = 1 << 0,
        CollectionCallbacks = 1 << 2,
        SceneCallbacks = 1 << 3,
        EventCallbacks = 1 << 4,
        All = LoadingScreen | CollectionCallbacks | SceneCallbacks | EventCallbacks
    }

    partial class SceneOperation
    {

        /// <summary>Specifies description for coroutine.</summary>
        public string description { get; protected set; }

        /// <summary>Specifies the collection that is being opened or closed.</summary>
        public SceneCollection collection { get; private set; }

        /// <summary>Specifies whatever active scene should be set when possible.</summary>
        public bool setActiveCollectionScene { get; private set; }

        /// <summary>Sets focus to the specified scene. Overrides selected scene in collections.</summary>
        /// <remarks>No effect if preloading.</remarks>
        public Scene focus { get; private set; }

        /// <summary>Sets the first opened scene as active.</summary>
        public bool focusSingleScene { get; private set; }

        /// <summary>Gets the scenes specified to open.</summary>
        /// <remarks>List will change depending on when its called (i.e. only closed scenes can be opened).</remarks>
        public IEnumerable<Scene> open { get; private set; } = Enumerable.Empty<Scene>();

        /// <summary>Gets the scenes specified to close.</summary>
        /// <remarks>List will change depending on when its called (i.e. only open scenes can be closed).</remarks>
        public IEnumerable<Scene> close { get; private set; } = Enumerable.Empty<Scene>();

        /// <summary>Gets the scenes specified to preload.</summary>
        public IEnumerable<Scene> preload { get; private set; } = Enumerable.Empty<Scene>();

        ///// <summary>Gets the user defined callbacks.</summary>
        //public IEnumerable<Callback> callbacks { get; private set; } = Enumerable.Empty<Callback>();

        /// <summary>Gets the progress scope associated with this operation.</summary>
        public ProgressScope progressScope { get; private set; } = new();

        /// <summary>Gets the total progress of this operation.</summary>
        public float progress => progressScope.totalProgress;

        /// <summary>Gets the scenes that should not be activated.</summary>
        public IEnumerable<Scene> ignoreForActivation { get; private set; } = Enumerable.Empty<Scene>();

        //Loading

        /// <summary>Gets the specified loading screen.</summary>
        public Scene loadingScene { get; private set; }

        /// <summary>Gets the specified loading screen callback.</summary>
        public Action<LoadingScreen> loadingScreenCallback { get; private set; }

        /// <summary>Gets whatever a loading screen should be used.</summary>
        public bool useLoadingScene { get; private set; } = true;

        //Options

        /// <summary>Gets whatever <see cref="Resources.UnloadUnusedAssets"/> should be called at the end (before loading screen).</summary>
        public bool? unloadUnusedAssets { get; private set; }

        ///// <summary>The phase the this scene operation is currently in.</summary>
        //public Phase phase { get; private set; }

        /// <summary>Gets if this scene operation is cancelled.</summary>
        public bool wasCancelled { get; private set; }

        /// <summary>Gets if this scene operation reports progress.</summary>
        /// <remarks>This will disable scene loaders from reporting progress to <see cref="LoadingScreenUtility.ReportProgress(ILoadProgressData)"/>. Loading screens and other listeners will not receive reports.</remarks>
        public bool reportsProgress { get; private set; } = true;

        /// <summary>Gets the <see cref="LoadPriority"/> this operation will use.</summary>
        /// <remarks>Overrides <see cref="SceneCollection.loadPriority"/> and <see cref="Scene.loadPriority"/> if set.</remarks>
        public LoadPriority loadPriority { get; private set; } = LoadPriority.Auto;

        /// <summary>Gets the operation flags this operation will use.</summary>
        /// <remarks>Use this to disable steps of operation for better performance.</remarks>
        public SceneOperationFlags flags { get; private set; } = SceneOperationFlags.All;

        /// <summary>Gets whatever scene callbacks should run outside of play mode.</summary>
        public bool runSceneCallbacksOutsideOfPlayMode { get; private set; }

    }

}
