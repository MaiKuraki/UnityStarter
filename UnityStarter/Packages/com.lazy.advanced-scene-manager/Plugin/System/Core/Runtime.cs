using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Callbacks.Events;
using AdvancedSceneManager.Core.Callbacks;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AdvancedSceneManager.Core
{

    static class SceneOperationExtensions
    {

        public static SceneOperation TrackCollectionCallback(this SceneOperation operation, SceneCollection collection, bool isAdditive = false)
        {

            bool isClosingCollection = SceneManager.openCollection;

            operation.RegisterCallback<SceneClosePhaseEvent>(e =>
            {

                if (!SceneManager.runtime.openCollection)
                    return;

                SceneManager.runtime.Untrack(SceneManager.runtime.openCollection, isAdditive);

                //Make sure additive collection is removed when it is opened non-additively
                if (!isAdditive)
                    SceneManager.runtime.Untrack(SceneManager.runtime.openCollection, true);

                e.WaitFor(e.operation.InvokeCallback<CollectionCloseEvent>(new() { collection = collection }));

            }, When.After);

            operation.RegisterCallback<SceneOpenPhaseEvent>(e =>
            {
                SceneManager.runtime.Track(collection, isAdditive);
                e.WaitFor(e.operation.InvokeCallback<CollectionOpenEvent>(new() { collection = collection }));
            }, When.After);

            return operation;

        }

        public static SceneOperation UntrackCollectionCallback(this SceneOperation operation, bool isAdditive = false)
        {
            operation.RegisterCallback<SceneClosePhaseEvent>(e => SceneManager.runtime.Untrack(SceneManager.runtime.openCollection, isAdditive), When.After);
            return operation;
        }

    }

    /// <summary>Manages runtime functionality for Advanced Scene Manager such as open scenes and collection.</summary>
    public sealed class Runtime : DependencyInjection.ISceneManager,
        Scene.IMethods_Target,
        SceneCollection.IMethods_Target
    {

        public Runtime()
        {

            AddSceneLoader<RuntimeSceneLoader>();

            sceneClosed += Runtime_sceneClosed;

            QueueUtility<SceneOperation>.queueFilled += () => startedWorking?.Invoke();
            QueueUtility<SceneOperation>.queueEmpty += () =>
            {
                stoppedWorking?.Invoke();
                if (SceneUtility.unitySceneCount == 1 && FallbackSceneUtility.isOpen)
                    OnAllScenesClosed();
            };

        }

        void Runtime_sceneClosed(Scene scene)
        {
            var collections = openAdditiveCollections.Where(c => !c.scenes.Any(s => s && s.isOpen));
            foreach (var collection in collections.ToArray())
                Untrack(collection, isAdditive: true);
        }

        #region Properties

        internal void Reset()
        {
            UntrackScenes();
            UntrackPreload();
            UntrackCollections();
        }

        private readonly List<Scene> m_openScenes = new();
        private readonly Dictionary<Scene, Func<IEnumerator>> m_preloadedScenes = new();
        private SceneCollection m_openCollection
        {
            get => SceneManager.settings.project.openCollection;
            set => SceneManager.settings.project.openCollection = value;
        }

        /// <summary>Gets the scenes that are open.</summary>
        public IEnumerable<Scene> openScenes => m_openScenes.NonNull();

        /// <summary>Gets the scenes that are preloaded.</summary>
        public IEnumerable<Scene> preloadedScenes => m_preloadedScenes.Keys.NonNull();

        /// <summary>Gets the collections that are opened as additive.</summary>
        public IEnumerable<SceneCollection> openAdditiveCollections => SceneManager.settings.project.openAdditiveCollections.NonNull().Distinct();

        /// <summary>Gets the collection that is currently open.</summary>
        public SceneCollection openCollection => m_openCollection;

        /// <summary>Gets the currently preloaded collection.</summary>
        public SceneCollection preloadedCollection { get; private set; }

        /// <summary>Gets if <see cref="preloadedCollection"/> is additive.</summary>
        public bool isPreloadedCollectionAdditive { get; private set; }

        #endregion
        #region Scene loaders

        internal List<SceneLoader> sceneLoaders = new();

        /// <summary>Gets a list of all added scene loaders that can be toggled scene by scene.</summary>
        public IEnumerable<SceneLoader> GetToggleableSceneLoaders() =>
            sceneLoaders.Where(l => !l.isGlobal && !string.IsNullOrWhiteSpace(l.sceneToggleText));

        /// <summary>Gets the loader for <paramref name="scene"/>.</summary>
        public SceneLoader GetLoaderForScene(Scene scene)
        {
            SceneLoader globalLoader = null;

            foreach (var loader in sceneLoaders)
            {
                // skip if cant be activated
                if (!loader.canBeActivated)
                    continue;

                // return first found
                if (Match(loader, scene))
                    return loader;

                // Track global to use if we don't find a match
                if (globalLoader == null && loader.isGlobal && loader.CanHandleScene(scene))
                    globalLoader = loader;
            }

            return globalLoader;
        }

        /// <summary>Returns the scene loader with the specified key.</summary>
        public SceneLoader GetSceneLoader(string sceneLoader) =>
            sceneLoaders.FirstOrDefault(l => l.Key == sceneLoader);

        /// <summary>Returns the scene loader type with the specified key.</summary>
        public Type GetSceneLoaderType(string sceneLoader) =>
            GetSceneLoader(sceneLoader)?.GetType();

        bool Match(SceneLoader loader, Scene scene) =>
            loader.GetType().FullName == scene.sceneLoader && loader.CanHandleScene(scene);

        /// <summary>Adds a scene loader.</summary>
        public void AddSceneLoader<T>() where T : SceneLoader, new()
        {
            var key = SceneLoader.GetKey<T>();
            sceneLoaders.RemoveAll(l => l.Key == key);
            sceneLoaders.Add(new T());
        }

        /// <summary>Removes a scene loader.</summary>
        public void RemoveSceneLoader<T>() =>
            sceneLoaders.RemoveAll(l => l is T);

        #endregion
        #region Scene

        bool IsValid(Scene scene) => scene;
        bool IsClosed(Scene scene) => !openScenes.Contains(scene);
        bool IsOpen(Scene scene) => openScenes.Contains(scene);
        bool CanOpen(Scene scene, SceneCollection collection, bool openAllScenes) => openAllScenes || !collection.scenesThatShouldNotAutomaticallyOpen.Contains(scene);

        bool LoadingScreen(Scene scene) => LoadingScreenUtility.IsLoadingScreenOpen(scene);

        bool IsPersistent(Scene scene, SceneCollection closeCollection = null, SceneCollection nextCollection = null) =>
            scene.isPersistent
            || (scene.keepOpenWhenNewCollectionWouldReopen && nextCollection && nextCollection.Contains(scene));

        bool NotPersistent(Scene scene, SceneCollection closeCollection = null, SceneCollection nextCollection = null) =>
            !IsPersistent(scene, closeCollection, nextCollection);

        bool NotPersistent(Scene scene, SceneCollection closeCollection = null) =>
            !IsPersistent(scene, closeCollection);

        bool NotLoadingScreen(Scene scene) =>
            !LoadingScreen(scene);

        #region Open

        public SceneOperation Open(Scene scene) =>
            Open(scenes: scene);

        public SceneOperation OpenAndActivate(Scene scene) =>
            SceneOperation.Queue().OpenAndActivate(scene);

        /// <inheritdoc cref="Open(IEnumerable{Scene})"/>
        public SceneOperation Open(params Scene[] scenes) =>
            Open((IEnumerable<Scene>)scenes);

        /// <summary>Opens the scenes.</summary>
        /// <remarks>Open scenes will not be re-opened, please close it first.</remarks>
        public SceneOperation Open(IEnumerable<Scene> scenes)
        {

            scenes = scenes.
                    NonNull().
                    Where(IsValid).
                    Where(IsClosed);

            if (!scenes.Any())
                return SceneOperation.done;

            if (SceneManager.runtime.currentOperation?.acceptsSubOperations ?? false)
            {
                //User is attempting to open a scene in a open callback, lets make current operation wait for this one
                var operation = SceneOperation.Start().Open(scenes);
                SceneManager.runtime.currentOperation.WaitFor(operation);
                return operation;
            }
            else
                return SceneOperation.Queue().Open(scenes);

        }

        public SceneOperation OpenWithLoadingScreen(Scene scene, Scene loadingScreen) =>
            Open(scene).With(loadingScreen);

        /// <summary>Opens a scene with a loading screen.</summary>
        public SceneOperation OpenWithLoadingScreen(IEnumerable<Scene> scene, Scene loadingScreen) =>
            Open(scene).With(loadingScreen);

        #endregion
        #region Close

        public SceneOperation Close(Scene scene) =>
            Close(scenes: scene);

        /// <inheritdoc cref="Close(IEnumerable{Scene})"/>
        public SceneOperation Close(params Scene[] scenes) =>
            Close((IEnumerable<Scene>)scenes);

        /// <summary>Closes the scenes.</summary>
        /// <remarks>Closes persistent scenes.</remarks>
        public SceneOperation Close(IEnumerable<Scene> scenes) =>
            Close(scenes, skipEmptySceneCheck: false);

        public SceneOperation Close(IEnumerable<Scene> scenes, bool skipEmptySceneCheck = false)
        {

            scenes = scenes.
                NonNull().
                Where(IsValid).
                Where(IsOpen);

            if (!skipEmptySceneCheck && !scenes.Any())
                return SceneOperation.done;

            return SceneOperation.Queue().Close(scenes);

        }

        public SceneOperation CloseWithLoadingScreen(Scene scene, Scene loadingScreen) =>
            Close(scene).With(loadingScreen);

        /// <summary>Opens a scene with a loading screen.</summary>
        public SceneOperation CloseWithLoadingScreen(IEnumerable<Scene> scene, Scene loadingScreen) =>
            Close(scene).With(loadingScreen);

        #endregion
        #region Preload

        /// <summary>Preloads the specified scene, to be displayed at a later time. See also: <see cref="FinishPreload(Scene)"/>, <see cref="DiscardPreload(Scene)"/>.</summary>
        /// <remarks>Scene must be closed beforehand.</remarks>
        public SceneOperation Preload(Scene scene, Action onPreloaded = null) =>
            Preload(onPreloaded: (s) => onPreloaded?.Invoke(), new[] { scene });

        /// <summary>Preloads the specified scenes, to be displayed at a later time. See also: <see cref="FinishPreload(Scene)"/>, <see cref="DiscardPreload(Scene)"/>.</summary>
        /// <remarks>Scene must be closed beforehand.</remarks>
        public SceneOperation Preload(Action<Scene> onPreloaded = null, params Scene[] scenes) =>
            Preload(scenes: scenes, onPreloaded);

        /// <summary>Preloads the specified scenes, to be displayed at a later time. See also: <see cref="FinishPreload(Scene)"/>, <see cref="DiscardPreload(Scene)"/>.</summary>
        /// <remarks>Scene must be closed beforehand.</remarks>
        public SceneOperation Preload(params Scene[] scenes) =>
            Preload(scenes, onPreloaded: null);

        #endregion
        #region Toggle

        /// <summary>Toggles the open state of this scene.</summary>
        public SceneOperation ToggleOpen(Scene scene) =>
            IsOpen(scene)
            ? Close(scene)
            : Open(scene);

        #endregion
        #region Active

        /// <summary>Gets the active scene.</summary>
        /// <remarks>Returns <see langword="null"/> if the active scene is not imported.</remarks>
        public Scene activeScene =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().ASMScene();

        [Obsolete]
        public void SetActive(Scene scene) =>
            SetActive(scene);

        /// <summary>Sets the scene as active.</summary>
        /// <remarks>No effect if not open.</remarks>
        public void Activate(Scene scene)
        {

            if (!scene || !scene.isOpen)
                return;

            if (scene.internalScene.HasValue && scene.internalScene.Value.isLoaded)
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene.internalScene.Value);
            else
                Debug.LogError("Could not set active scene since internalScene not valid.");

        }

        #endregion

        #endregion
        #region Collection

        bool IsOpen(SceneCollection collection) =>
            collection && (openCollection == collection || openAdditiveCollections.Contains(collection));

        /// <summary>Evaluate the scenes that would close.</summary>
        public IEnumerable<Scene> EvalScenesToClose(SceneCollection closeCollection = null, SceneCollection nextCollection = null, SceneCollection additiveCloseCollection = null)
        {

            var list = additiveCloseCollection
                ? additiveCloseCollection.scenes.Distinct().Where(s => !openCollection || !openCollection.Contains(s))
                : openScenes.Where(s => !openAdditiveCollections.Any(c => c.Contains(s)));

            list = list.Where(s => IsValid(s) && IsOpen(s) && NotLoadingScreen(s) && NotPersistent(s, closeCollection, nextCollection));

            if (SceneManager.settings.project.reverseUnloadOrderOnCollectionClose)
                list = list.Reverse();

            return list;

        }

        /// <summary>Evaluate the scenes that would open.</summary>
        public IEnumerable<Scene> EvalScenesToOpen(SceneCollection collection, bool openAll = false) =>
            collection.scenes.Distinct().Where(s => IsValid(s) && IsClosed(s) && CanOpen(s, collection, openAll));

        #region Open

        public SceneOperation Open(SceneCollection collection, bool openAll = false) =>
            Open(SceneOperation.Queue(), collection, openAll);

        /// <inheritdoc cref="Open(SceneCollection, bool)"/>
        internal SceneOperation Open(SceneOperation operation, SceneCollection collection, bool openAll = false)
        {

            if (IsOpen(collection))
                return SceneOperation.done;

            var scenesToOpen = EvalScenesToOpen(collection, openAll);
            var scenesToClose = EvalScenesToClose(nextCollection: collection);

            if (!scenesToOpen.Any() && !scenesToClose.Any())
            {
                Track(collection);
                SceneOperation.InvokeGlobalCallback<CollectionOpenEvent>(new() { collection = collection }).StartCoroutine();
                return SceneOperation.done;
            }

            return operation.
                With(collection, true).
                TrackCollectionCallback(collection).
                Close(scenesToClose).
                Open(scenesToOpen);

        }

        /// <summary>Opens the collection without closing existing scenes.</summary>
        /// <param name="collection">The collection to open.</param>
        /// <param name="openAll">Specifies whatever all scenes should open, regardless of open flag.</param>
        public SceneOperation OpenAdditive(SceneCollection collection, bool openAll = false)
        {

            if (!collection)
                return SceneOperation.done;

            if (m_openCollection == collection)
            {
                Debug.LogError("Cannot open collection as additive if it is already open normally.");
                return SceneOperation.done;
            }

            if (IsOpen(collection))
                return SceneOperation.done;

            var scenesToOpen = EvalScenesToOpen(collection, openAll);

            if (!scenesToOpen.Any())
            {
                Track(collection, isAdditive: true);
                SceneOperation.InvokeGlobalCallback<CollectionOpenEvent>(new() { collection = collection }).StartCoroutine();
                return SceneOperation.done;
            }

            return SceneOperation.Queue().
                With(collection, collection.setActiveSceneWhenOpenedAsActive).
                TrackCollectionCallback(collection, true).
                WithoutLoadingScreen().
                Open(scenesToOpen);

        }

        /// <summary>Opens the collection without closing existing scenes.</summary>
        /// <remarks>No effect if no additive collections could be opened. Note that <paramref name="activeCollection"/> will be removed from <paramref name="collections"/> if it is contained within.</remarks>
        public SceneOperation OpenAdditive(IEnumerable<SceneCollection> collections, SceneCollection activeCollection = null, Scene loadingScene = null)
        {

            collections = collections.Where(c => !c.isOpen).Except(activeCollection).NonNull();

            if (!collections.Any())
                return SceneOperation.done;

            var operation = SceneOperation.Queue().
                With(activeCollection, activeCollection.setActiveSceneWhenOpenedAsActive).
                With(loadingScene: loadingScene, useLoadingScene: loadingScene).
                Open(collections.SelectMany(c => c.scenes.
                    Distinct().
                    Where(IsValid).
                    Where(IsClosed).
                    Where(s => CanOpen(s, c, false))));

            if (activeCollection)
                operation.TrackCollectionCallback(activeCollection, true);

            return operation;

        }

        /// <summary>Reopens the collection.</summary>
        public SceneOperation Reopen(SceneCollection collection, bool openAll = false)
        {

            if (collection.isOpenAdditive)
            {
                Debug.LogError("Additive collections cannot currently be reopned. Please close and then open manually.");
                return SceneOperation.done;
            }

            var scenesToClose = collection.scenes.Distinct().Where(s => s.isOpen && !s.keepOpenWhenCollectionsClose);
            var scenesToOpen = (openAll ? collection.scenes.Distinct() : collection.scenesToAutomaticallyOpen).Where(s => !s.isOpen);

            return SceneOperation.Queue().
                With(collection, true).
                Close(scenesToClose).
                Open(scenesToOpen).
                UntrackCollectionCallback().
                TrackCollectionCallback(collection);

        }

        #endregion
        #region Close

        /// <summary>Closes <paramref name="collection"/>.</summary>
        public SceneOperation Close(SceneCollection collection) =>
            Close(SceneOperation.Queue(), collection);

        /// <inheritdoc cref="Close(SceneCollection)"/>
        public SceneOperation Close(SceneOperation operation, SceneCollection collection)
        {

            if (!collection)
                return SceneOperation.done;

            var scenes = EvalScenesToClose(collection, additiveCloseCollection: collection.isOpenAdditive ? collection : null);

            if (!scenes.Any())
            {
                if (collection.isOpen)
                {
                    SceneOperation.InvokeGlobalCallback<CollectionCloseEvent>(new() { collection = collection }).StartCoroutine();
                    Untrack(collection, isAdditive: collection.isOpenAdditive);
                }
                return SceneOperation.done;
            }

            return operation.
                With(collection).
                UntrackCollectionCallback(collection.isOpenAdditive).
                Close(scenes);

        }

        #endregion
        #region Toggle

        public SceneOperation ToggleOpen(SceneCollection collection, bool openAll = false) =>
            IsOpen(collection)
            ? Close(collection)
            : Open(collection, openAll);

        #endregion
        #region Preload

        public SceneOperation Preload(SceneCollection collection, bool openAll = false) =>
            PreloadInternal(collection, openAll, isAdditive: false);

        public SceneOperation PreloadAdditive(SceneCollection collection, bool openAll = false) =>
            PreloadInternal(collection, openAll, isAdditive: true);

        #endregion

        #endregion
        #region SceneState

        /// <summary>Gets the current state of the scene.</summary>
        public SceneState GetState(Scene scene)
        {

            if (!scene)
                return SceneState.Unknown;

            if (!scene.internalScene.HasValue)
                return SceneState.NotOpen;

            if (FallbackSceneUtility.IsFallbackScene(scene.internalScene.Value))
                throw new InvalidOperationException("Fallback scene is tracked by a Scene, this should not happen, something went wrong somewhere.");

            var isPreloaded = scene.internalScene.HasValue && !scene.internalScene.Value.isLoaded;
            var isOpen = openScenes.Contains(scene);
            var isQueued =
                QueueUtility<SceneOperation>.queue.Any(o => o.open?.Contains(scene) ?? false) ||
                QueueUtility<SceneOperation>.running.Any(o => o.open?.Contains(scene) ?? false);

            var isOpening = SceneOperation.currentLoadingScene == scene;
            var isPreloading = preloadedScenes.Contains(scene) || (SceneOperation.currentLoadingScene == scene && SceneOperation.isCurrentLoadingScenePreload);

            if (isPreloaded) return SceneState.Preloaded;
            else if (isPreloading) return SceneState.Preloading;
            else if (isOpen) return SceneState.Open;
            else if (isOpening) return SceneState.Opening;
            else if (isQueued) return SceneState.Queued;
            else return SceneState.NotOpen;

        }

        #endregion
        #region DontDestroyOnLoad

        [AddComponentMenu("")]
        /// <summary>Helper script hosted in DontDestroyOnLoad.</summary>
        internal class ASM : MonoBehaviour
        { }

        internal UnityEngine.SceneManagement.Scene dontDestroyOnLoadScene => helper ? helper.scene : default;
        bool hasDontDestroyOnLoadScene;

#if UNITY_EDITOR
        [InitializeOnEnterPlayMode]
        static void UnsetHasDontDestroyOnLoadScene() =>
            SceneManager.runtime.hasDontDestroyOnLoadScene = false;
#endif

        GameObject m_helper;
        GameObject helper
        {
            get
            {

                if (!Application.isPlaying)
                    return null;

                if (!m_helper && !hasDontDestroyOnLoadScene)
                {

                    var script = UnityCompatibiltyHelper.FindFirstObjectByType<ASM>();
                    if (script)
                        m_helper = script.gameObject;
                    else
                    {
                        m_helper = new GameObject("ASM helper");
                        _ = m_helper.AddComponent<ASM>();
                        hasDontDestroyOnLoadScene = true;
                    }

                    Object.DontDestroyOnLoad(m_helper);

                }

                return m_helper;

            }
        }

        Scene m_dontDestroyOnLoadScene;

        /// <summary>Gets the dontDestroyOnLoad scene.</summary>
        /// <remarks>Returns <see langword="null"/> outside of play mode.</remarks>
        public Scene dontDestroyOnLoad
        {
            get
            {

                if (!Application.isPlaying)
                    return null;

                if (!m_dontDestroyOnLoadScene)
                {
                    m_dontDestroyOnLoadScene = ScriptableObject.CreateInstance<Scene>();
                    ((Object)m_dontDestroyOnLoadScene).name = "DontDestroyOnLoad";
                }

                if (m_dontDestroyOnLoadScene.internalScene?.handle != dontDestroyOnLoadScene.handle)
                    m_dontDestroyOnLoadScene.internalScene = dontDestroyOnLoadScene;

                return m_dontDestroyOnLoadScene;

            }
        }

        /// <inheritdoc cref="AddToDontDestroyOnLoad{T}(out T)"/>
        internal bool AddToDontDestroyOnLoad<T>() where T : Component =>
            AddToDontDestroyOnLoad<T>(out _);

        /// <summary>Adds the component to the 'Advanced Scene Manager' gameobject in DontDestroyOnLoad.</summary>
        /// <remarks>Returns <see langword="false"/> outside of play-mode.</remarks>
        internal bool AddToDontDestroyOnLoad<T>(out T component) where T : Component
        {

            component = null;

            if (helper && helper.gameObject)
            {
                component = helper.gameObject.AddComponent<T>();
                return true;
            }
            else
                Debug.LogError("Cannot access DontDestroyOnLoad outside of play mode.");

            return false;

        }

        /// <summary>Adds the component to a new gameobject in DontDestroyOnLoad.</summary>
        /// <remarks>Returns <see langword="false"/> outside of play-mode.</remarks>
        internal bool AddToDontDestroyOnLoad<T>(out T component, out GameObject obj) where T : Component
        {

            obj = null;
            component = null;
            if (Application.isPlaying)
            {
                obj = new GameObject(typeof(T).Name);
                Object.DontDestroyOnLoad(obj);
                component = obj.AddComponent<T>();
                return true;
            }
            else
                Debug.LogError("Cannot access DontDestroyOnLoad outside of play mode.");

            return false;

        }

        #endregion
        #region Events

        /// <summary>Occurs when a scene is opened.</summary>
        public event Action<Scene> sceneOpened;

        /// <summary>Occurs when a scene is closed.</summary>
        public event Action<Scene> sceneClosed;

        /// <summary>Occurs when a collection is opened.</summary>
        public event Action<SceneCollection> collectionOpened;

        /// <summary>Occurs when a collection is closed.</summary>
        public event Action<SceneCollection> collectionClosed;

        /// <summary>Occurs when a scene is preloaded.</summary>
        public event Action<Scene> scenePreloaded;

        /// <summary>Occurs when a previously preloaded scene is opened.</summary>
        public event Action<Scene> scenePreloadFinished;

        /// <summary>Occurs when the last user scene closes.</summary>
        /// <remarks> 
        /// <para>This usually happens by mistake, and likely means that no user code would run, this is your chance to restore to a known state (return to main menu, for example), or crash to desktop.</para>
        /// <para>Returning to main menu can be done like this:<code>SceneManager.app.Restart()</code></para>
        /// </remarks>
        public Action onAllScenesClosed;

        internal void OnAllScenesClosed() =>
            onAllScenesClosed?.Invoke();

        /// <inheritdoc cref="SceneOperation.RegisterCallback{TEventType}(EventCallback{TEventType}, When?)"/>
        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback, When? when = null) where TEventType : SceneOperationEventBase, new() =>
            SceneOperation.RegisterGlobalCallback(callback, when);

        /// <inheritdoc cref="SceneOperation.UnregisterCallback{TEventType}(EventCallback{TEventType}, When?)"/>
        public void UnregisterCallback<TEventType>(EventCallback<TEventType> callback, When? when = null) where TEventType : SceneOperationEventBase, new() =>
            SceneOperation.UnregisterGlobalCallback(callback, when);

        internal void InvokeGlobalCallback<T>(T e) where T : SceneOperationEventBase, new() =>
            SceneOperation.InvokeGlobalCallback(e);

        #endregion
        #region Tracking

        #region Scenes

        /// <summary>Tracks the specified scene as open.</summary>
        /// <remarks>Does not open scene.</remarks>
        public void Track(Scene scene, UnityEngine.SceneManagement.Scene unityScene)
        {

            if (!scene)
                return;

            if (!FallbackSceneUtility.IsFallbackScene(unityScene))
                scene.internalScene = unityScene;

            Track(scene);

        }

        /// <inheritdoc cref="Track(Scene, UnityEngine.SceneManagement.Scene)"/>
        public void Track(Scene scene)
        {

            if (!scene)
                return;

            if (!scene.internalScene.HasValue)
                FindAssociatedScene(scene);

            if (!scene.internalScene.HasValue)
                return;

            if (FallbackSceneUtility.IsFallbackScene(scene.internalScene ?? default))
            {
                scene.internalScene = null;
                return;
            }

            if (!m_openScenes.Contains(scene))
            {

                m_openScenes.Add(scene);
                scene.OnPropertyChanged(nameof(Scene.isOpen));
                sceneOpened?.Invoke(scene);
                scene.events.OnOpen?.Invoke(scene);

                LogUtility.LogTracked(scene);

            }

        }

        /// <summary>Untracks the specified scene as open.</summary>
        /// <remarks>Does not close scene.</remarks>
        public bool Untrack(Scene scene)
        {

            if (scene && m_openScenes.Remove(scene))
            {

                UntrackPreload(scene);

                scene.internalScene = null;

                scene.OnPropertyChanged(nameof(Scene.isOpen));
                sceneClosed?.Invoke(scene);
                scene.events.OnClose?.Invoke(scene);
                LogUtility.LogUntracked(scene);

                return true;

            }

            return false;

        }

        /// <summary>Untracks all open scenes.</summary>
        /// <remarks>Does not close scenes.</remarks>
        public void UntrackScenes()
        {
            foreach (var scene in m_openScenes.ToArray())
                Untrack(scene);
            m_openScenes.Clear();
        }

        void FindAssociatedScene(Scene scene)
        {
            scene.internalScene = SceneUtility.GetAllOpenUnityScenes().FirstOrDefault(s => s.IsValid() && s.path == scene.path);
            if (!scene.internalScene.HasValue)
                throw new InvalidOperationException("Cannot track scene without a associated unity scene.");
        }

        /// <summary>Tracks a scene that doesn't have a associated unity scene.</summary>
        public void ForceTrack(Scene scene)
        {

            if (!scene)
                return;

            if (FallbackSceneUtility.IsFallbackScene(scene.internalScene ?? default))
            {
                scene.internalScene = null;
                return;
            }

            if (!m_openScenes.Contains(scene))
            {

                m_openScenes.Add(scene);
                scene.OnPropertyChanged(nameof(Scene.isOpen));
                sceneOpened?.Invoke(scene);
                scene.events.OnOpen?.Invoke(scene);

                LogUtility.LogTracked(scene);

            }

        }

        #endregion
        #region Collections

        /// <summary>Tracks the collection as open.</summary>
        /// <remarks>Does not open collection.</remarks>
        public void Track(SceneCollection collection, bool isAdditive = false)
        {

            if (!collection)
                return;

            if (!isAdditive && collection != m_openCollection)
            {
                m_openCollection = collection;
                collection.OnPropertyChanged(nameof(collection.isOpenNonAdditive));
                collection.OnPropertyChanged(nameof(collection.isOpen));

                collectionOpened?.Invoke(collection);
                foreach (var scene in collection.NonNull())
                    scene.events.OnCollectionOpened?.Invoke(scene, collection);
                collection.events.OnOpen?.Invoke(collection);

                LogUtility.LogTracked(collection);
            }
            else if (isAdditive && !openAdditiveCollections.Contains(collection))
            {
                SceneManager.settings.project.AddAdditiveCollection(collection);
                collection.OnPropertyChanged(nameof(collection.isOpenAdditive));
                collection.OnPropertyChanged(nameof(collection.isOpen));
                LogUtility.LogTracked(collection, true);
            }

        }

        /// <summary>Untracks the collection.</summary>
        /// <remarks>Does not close the collection.</remarks>
        public void Untrack(SceneCollection collection, bool isAdditive = false)
        {

            if (!collection)
                return;

            if (!isAdditive && collection == openCollection)
            {

                m_openCollection = null;

                collection.OnPropertyChanged(nameof(collection.isOpenNonAdditive));
                collection.OnPropertyChanged(nameof(collection.isOpen));

                collectionClosed?.Invoke(collection);
                foreach (var scene in collection.NonNull())
                    scene.events.OnCollectionClosed?.Invoke(scene, collection);
                collection.events.OnClose?.Invoke(collection);

                LogUtility.LogUntracked(collection);

                //Untrack all additive collections
                //openAdditiveCollections.ToArray().ForEach(c => Untrack(c, true));

            }
            else if (isAdditive && openAdditiveCollections.Contains(collection))
            {
                SceneManager.settings.project.RemoveAdditiveCollection(collection);
                collection.OnPropertyChanged(nameof(collection.isOpenAdditive));
                collection.OnPropertyChanged(nameof(collection.isOpen));
                LogUtility.LogUntracked(collection, true);
            }

        }

        /// <summary>Untracks all collections.</summary>
        /// <remarks>Does not close collections.</remarks>
        public void UntrackCollections()
        {
            Untrack(openCollection);
            openAdditiveCollections.ForEach(c => Untrack(c, true));
        }

        #endregion

        /// <summary>Gets whatever this scene is tracked as open.</summary>
        public bool IsTracked(Scene scene) =>
            scene && scene.internalScene.HasValue &&
            (scene.isDontDestroyOnLoad ||
            FallbackSceneUtility.IsFallbackScene(scene.internalScene ?? default) ||
            openScenes.Any(s => s.id == scene.id));

        /// <summary>Gets whatever this collection is tracked as open.</summary>
        public bool IsTracked(SceneCollection collection) =>
            openCollection == collection || openAdditiveCollections.Contains(collection);

        #endregion
        #region Queue

        /// <summary>Occurs when ASM has started working and is running scene operations.</summary>
        public event Action startedWorking;

        /// <summary>Occurs when ASM has finished working and no scene operations are running.</summary>
        public event Action stoppedWorking;

        /// <summary>Gets whatever ASM is busy with any scene operations.</summary>
        public bool isBusy => QueueUtility<SceneOperation>.isBusy;

        /// <summary>The currently running scene operations.</summary>
        public IEnumerable<SceneOperation> runningOperations =>
            QueueUtility<SceneOperation>.running;

        /// <summary>Gets the current scene operation queue.</summary>
        public IEnumerable<SceneOperation> queuedOperations =>
            QueueUtility<SceneOperation>.queue;

        /// <summary>Gets the current active operation in the queue.</summary>
        public SceneOperation currentOperation =>
            QueueUtility<SceneOperation>.queue.FirstOrDefault();

        #endregion
        #region Preload

        #region Scene

        public SceneOperation Preload(IEnumerable<Scene> scenes, Action<Scene> onPreloaded = null)
        {

            scenes = scenes.NonNull().Where(s => !preloadedScenes.Contains(s) && !s.isOpen).ToArray();

            if (!scenes.Any())
                return SceneOperation.done;

            return scenes.Any()
                ? SceneOperation.Queue().Preload(scenes).RegisterCallback<LoadingScreenClosePhaseEvent>(e => Callbacks(), When.Before)
                : SceneOperation.done;

            void Callbacks()
            {
                foreach (var scene in scenes)
                    onPreloaded?.Invoke(scene);
            }

        }

        internal void TrackPreload(Scene scene, Func<IEnumerator> preloadCallback)
        {

            if (m_preloadedScenes.ContainsKey(scene))
                return;

            m_preloadedScenes.Add(scene, () => Coroutine(preloadCallback));

            IEnumerator Coroutine(Func<IEnumerator> preloadCallback)
            {

                yield return preloadCallback();

                scenePreloadFinished?.Invoke(scene);
                scene.events.OnPreloadFinished?.Invoke(scene);

                m_preloadedScenes.Remove(scene);

            }

            if (scene)
            {
                scenePreloaded?.Invoke(scene);
                scene.events.OnPreload?.Invoke(scene);
            }

        }

        internal void UntrackPreload(Scene scene) =>
            m_preloadedScenes.Remove(scene);

        #endregion
        #region Collection

        private SceneOperation PreloadInternal(SceneCollection collection, bool openAll = false, bool isAdditive = false)
        {

            if (!collection)
                return SceneOperation.done;

            if (preloadedCollection)
            {
                Debug.LogError("Cannot preload multiple collections at once.");
                return SceneOperation.done;
            }

            preloadedCollection = collection;
            isPreloadedCollectionAdditive = isAdditive;

            return SceneOperation.Queue().
                With(collection, false).
                WithoutLoadingScreen().
                Preload(collection.scenes.
                    Where(IsValid).
                    Where(IsClosed).
                    Where(s => CanOpen(s, collection, openAll)));

        }

        SceneOperation FinishPreload(SceneCollection collection)
        {

            var operation = SceneOperation.Start().
                WithoutLoadingScreen().
                With(collection, collection.setActiveSceneWhenOpenedAsActive || !isPreloadedCollectionAdditive).
                TrackCollectionCallback(collection, isPreloadedCollectionAdditive);

            if (!isPreloadedCollectionAdditive)
                operation.Close(EvalScenesToClose(nextCollection: collection));

            return operation;

        }

        #endregion

        /// <summary>Finish loading preloaded scenes.</summary>
        /// <remarks>If a collection is preloaded, then scenes that would have normally closed when opening collection, will be closed when calling this. Scene will also be set as active.</remarks>
        public SceneOperation FinishPreload()
        {

            if (!preloadedScenes.Any())
                return SceneOperation.done;

            return SceneOperation.Start().RegisterCallback<LoadingScreenOpenPhaseEvent>(e => e.WaitFor(Coroutine), When.After);

            IEnumerator Coroutine()
            {

                foreach (var scene in preloadedScenes.ToArray())
                {
                    if (m_preloadedScenes.TryGetValue(scene, out var callback))
                        yield return callback.Invoke();
                    UntrackPreload(scene);
                }

                if (preloadedCollection)
                    yield return FinishPreload(preloadedCollection);

                UntrackPreload();

            }

        }

        /// <summary><see cref="DiscardPreload"/> is obsolete, please use <see cref="CancelPreload"/> instead.</summary>
        [Obsolete("DiscardPreload is obsolete, please use CancelPreload instead.")]
        public SceneOperation DiscardPreload() =>
            CancelPreload();

        /// <summary>Cancels the preload. All preloaded scenes will be fully loaded (limitation by Unity), then closed. No ASM scene callbacks will be called.</summary>
        public SceneOperation CancelPreload()
        {

            return SceneOperation.Start().RegisterCallback<LoadingScreenOpenPhaseEvent>(e => e.WaitFor(Coroutine), When.After);

            IEnumerator Coroutine()
            {

                var scenes = preloadedScenes.ToArray();
                foreach (var scene in scenes)
                {
                    if (m_preloadedScenes.TryGetValue(scene, out var callback))
                    {
                        //Debug.Log(scene);
                        yield return callback.Invoke();
                    }
                }

                yield return SceneOperation.Start().Close(scenes);

                UntrackPreload();

            }

        }

        internal void UntrackPreload()
        {
            m_preloadedScenes.Clear();
            preloadedCollection = null;
            isPreloadedCollectionAdditive = false;
        }

        #endregion
        #region Unimported scenes

        /// <summary>Retrieves the list of unimported scenes that are currently open.</summary>
        public IEnumerable<UnityEngine.SceneManagement.Scene> unimportedScenes =>
            SceneUtility.GetAllOpenUnityScenes().
            Where(s => !FallbackSceneUtility.IsFallbackScene(s)).
            Where(s => !s.ASMScene());

        /// <summary>Closes all open scenes that are unimported.</summary>
        public IEnumerator CloseUnimportedScenes()
        {

            foreach (var scene in unimportedScenes.ToArray())
                yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene.path);

        }

        #endregion

        /// <summary>Closes all scenes and collections.</summary>
        public SceneOperation CloseAll(bool exceptLoadingScreens = true, bool exceptUnimported = true, params Scene[] except)
        {

            var scenes = openScenes;
            if (exceptLoadingScreens)
                scenes = scenes.Where(s => !s.isLoadingScreen && !except.Contains(s));

            if (SceneManager.settings.project.reverseUnloadOrderOnCollectionClose)
                scenes = scenes.Reverse();

            var operation = Close(scenes, skipEmptySceneCheck: true).UntrackCollectionCallback().RegisterCallback<LoadingScreenClosePhaseEvent>(e => UntrackPreload(), When.Before);

            if (!exceptUnimported)
                operation.RegisterCallback<SceneClosePhaseEvent>(e => e.WaitFor(CloseUnimportedScenes), When.After);

            return operation;

            IEnumerator CloseUnimportedScenes()
            {

                var scenes = SceneUtility.GetAllOpenUnityScenes().
                    Where(s => !s.ASMScene() && !FallbackSceneUtility.IsFallbackScene(s)).
                    ToArray();

                foreach (var scene in scenes)
                    yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);

            }

        }

    }

}
