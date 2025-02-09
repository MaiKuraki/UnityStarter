using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using AdvancedSceneManager.Utility;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Utility.CrossSceneReferences;

using unityScene = UnityEngine.SceneManagement.Scene;
using System.IO;
using Object = UnityEngine.Object;
using UnityEngine.Events;
using System.Text;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AdvancedSceneManager.Models
{

    /// <summary>Represents a scene. This scene can be dragged dropped, and is used by ASM to perform operations on the wrapped unity scene.</summary>
    /// <remarks>A scene can be imported in the ASM window (via notification / popup), or by using <see cref="AdvancedSceneManager.Editor.Utility.SceneImportUtility"/>.</remarks>
    public partial class Scene : ASMModel,
        Scene.IEquality,
        Scene.IMethods, Scene.IMethods.IEvent,
        ILockable
    {

        #region Properties

        /// <summary>Gets whatever we are tracked by AssetRef.</summary>
        /// <remarks>Only available in editor.</remarks>
        public bool isImported => Assets.scenes.Contains(this);

        /// <summary>Gets whatever this scene is included in build.</summary>
        public bool isIncludedInBuilds => SceneUtility.IsIncluded(this);

        [SerializeField] internal bool m_isDefaultASMScene;

        /// <summary>Gets if this is a default ASM scene.</summary>
        public bool isDefaultASMScene => m_isDefaultASMScene;

        #region SceneAsset

        [Header("Scene")]
        [SerializeField] private string m_path;
        [SerializeField] internal string m_sceneAssetGUID;
        [SerializeField] private Object m_sceneAsset;

#if UNITY_EDITOR

        /// <summary>Gets the path of this <see cref="Scene"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        public string asmPath => AssetDatabase.GetAssetPath(this);

        /// <summary>Gets the associated <see cref="SceneAsset"/>.</summary>
        /// <remarks>Only available in the editor.</remarks>
        public SceneAsset sceneAsset
        {
            get => m_sceneAsset as SceneAsset;
            internal set => m_sceneAsset = value;
        }

#endif

        public string sceneAssetGUID
        {
            get
            {

#if UNITY_EDITOR
                var guid = AssetDatabase.GUIDFromAssetPath(m_path).ToString();

                if (guid != m_sceneAssetGUID)
                {
                    m_sceneAssetGUID = guid;
                    Save();
                }
#endif

                return m_sceneAssetGUID;

            }
        }

        /// <summary>Gets if <see cref="m_sceneAsset"/> has a value.</summary>
        /// <remarks>Only available in the editor.</remarks>
        public bool hasSceneAsset => m_sceneAsset;

        /// <summary>Gets the path of the associated <see cref="SceneAsset"/>.</summary>
        public string path
        {
            get => m_path ?? "";
            internal set { m_path = value; OnPropertyChanged(); }
        }

        #endregion
        #region Loading screen / splash screen

        [Header("Special scene flags")]
        [SerializeField] private bool m_isLoadingScreen;
        [SerializeField] private bool m_isSplashScreen;

        /// <summary>Gets if this scene is a loading screen.</summary>
        /// <remarks>
        /// <para>Automatically updated.</para>
        /// <para>If this is <see langword="false"/> for an actual loading screen, please make sure scene contains a <see cref="Callbacks.LoadingScreen"/> script.</para>
        /// <para>Scene might sometimes have to be re-saved for this flag to appear.</para>
        /// </remarks>
        public bool isLoadingScreen
        {
            get => m_isLoadingScreen;
            internal set => m_isLoadingScreen = value;
        }

        /// <summary>Gets if this scene is a splash screen.</summary>
        /// <remarks>
        /// <para>Automatically updated.</para>
        /// <para>If this is <see langword="false"/> for an actual splash screen screen, please make sure scene contains a <see cref="Callbacks.SplashScreen"/> script.</para>
        /// <para>Scene might sometimes have to be re-saved for this flag to appear.</para>
        /// </remarks>
        public bool isSplashScreen
        {
            get => m_isSplashScreen;
            internal set => m_isSplashScreen = value;
        }

        /// <summary>Gets if this is a 'special' scene.</summary>
        /// <remarks>A scene is special if any of the following is <see langword="true"/>: <see cref="isSplashScreen"/>, <see cref="isLoadingScreen"/> or <see cref="isDontDestroyOnLoad"/>.</remarks>
        public bool isSpecial =>
            isSplashScreen || isLoadingScreen || isDontDestroyOnLoad;

        #endregion
        #region Persistent properties

        [Header("Persistent")]
        [SerializeField] private bool m_keepOpenWhenCollectionsClose;
        [SerializeField] private bool m_keepOpenWhenNewCollectionWouldReopen = true;

        /// <summary>Specifies whatever this scene will remain open when collections close.</summary>
        /// <remarks>You'll have to close it manually, if needed.</remarks>
        public bool keepOpenWhenCollectionsClose
        {
            get => m_keepOpenWhenCollectionsClose;
            set { m_keepOpenWhenCollectionsClose = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever this will remain open when a newly opened collection would have reopened it.</summary>
        /// <remarks>You'll have to close it manually, if needed.</remarks>
        public bool keepOpenWhenNewCollectionWouldReopen
        {
            get => m_keepOpenWhenNewCollectionWouldReopen;
            set { m_keepOpenWhenNewCollectionWouldReopen = value; OnPropertyChanged(); }
        }

        /// <summary>Gets whatever this scene will close normally after a collection closes.</summary>
        public bool isNonPersistant => !keepOpenWhenCollectionsClose && !keepOpenWhenNewCollectionWouldReopen;

        #endregion
        #region Startup / Auto open

        [Header("Auto open")]
        [SerializeField] private bool m_openOnStartup;
        [SerializeField] private bool m_openOnPlayMode;
        [SerializeField] private EditorPersistentOption m_autoOpenInEditor;
        [SerializeField] internal List<Scene> m_autoOpenInEditorScenes = new();

        /// <summary>Specifies whatever this scene should be opened on startup.</summary>
        /// <remarks>Only effective when scene added to <see cref="Profile.standaloneScenes"/>.</remarks>
        public bool openOnStartup
        {
            get => m_openOnStartup;
            set { m_openOnStartup = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever this scene should be opened when entering playmode.</summary>
        /// <remarks>Only effective when scene added to <see cref="Profile.standaloneScenes"/>.</remarks>
        public bool openOnPlayMode
        {
            get => m_openOnPlayMode;
            set { m_openOnPlayMode = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever this scene should be opened automatically outside of play-mode.</summary>
        public EditorPersistentOption autoOpenInEditor
        {
            get => m_autoOpenInEditor;
            set { m_autoOpenInEditor = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies the scenes that should trigger this scene to open when <see cref="autoOpenInEditor"/> is set to <see cref="EditorPersistentOption.WhenAnyOfTheFollowingScenesAreOpened"/>.</summary>
        public List<Scene> autoOpenInEditorScenes => m_autoOpenInEditorScenes;

        #endregion
        #region Locking

        [Header("Locking")]
        [SerializeField] private bool m_isLocked;
        [SerializeField] private string m_lockMessage;

        /// <summary>Gets if this scene is locked.</summary>
        public bool isLocked
        {
            get => m_isLocked;
            set { m_isLocked = value; OnPropertyChanged(); }
        }

        /// <summary>Gets the lock message for this scene.</summary>
        public string lockMessage
        {
            get => m_lockMessage;
            set { m_lockMessage = value; OnPropertyChanged(); }
        }

        #endregion
        #region Cross-scene references

        [Header("Cross-scene references")]
        [SerializeField] private List<CrossSceneReference> m_crossSceneReferences = new();

        /// <summary>Enumerates the cross-scene references defined on this scene.</summary>
        public IEnumerable<CrossSceneReference> crossSceneReferences
        {
            get => m_crossSceneReferences;
            internal set
            {
                m_crossSceneReferences = value?.ToList() ?? new();
                Save();
            }
        }

#if UNITY_EDITOR

        /// <summary>Adds a cross-scene reference for this scene.</summary>
        /// <remarks>Only available in editor.</remarks>
        public void AddCrossSceneReference(CrossSceneReference reference)
        {

            if (RemoveCrossSceneReference(reference, out var i))
                m_crossSceneReferences.Insert(i, reference);
            else
                m_crossSceneReferences.Add(reference);

            Save();

        }

        /// <summary>Removes a cross-scene reference for this scene.</summary>
        /// <remarks>Only available in editor.</remarks>
        public void RemoveCrossSceneReference(CrossSceneReference reference)
        {
            RemoveCrossSceneReference(reference, out _);
            if (internalScene.HasValue)
            {
                CrossSceneReferenceUtility.ResetScene(internalScene.Value);
                CrossSceneReferenceUtility.ResolveScene(internalScene.Value);
            }
            Save();
        }

        bool RemoveCrossSceneReference(CrossSceneReference reference, out int index)
        {

            index = m_crossSceneReferences.FindIndex(r => r.id == reference.id);
            if (index == -1)
                return false;
            else
            {
                m_crossSceneReferences.RemoveAt(index);
                return true;
            }

        }

#endif

        #endregion
        #region Input bindings

        [SerializeField] internal InputBinding[] m_inputBindings = Array.Empty<InputBinding>();
        [SerializeField] private Scene m_inputBindingsLoadingScene;
        [SerializeField] private Scene[] m_ignoreInputBindingsForScenes;

        /// <summary>Gets or sets the input bindings for this scene.</summary>
        /// <remarks>No effect for non-standalone scenes.</remarks>
        public InputBinding[] inputBindings
        {
            get => m_inputBindings;
            set { m_inputBindings = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies loading screen to use when opening or closing this scene using a input binding.</summary>
        public Scene inputBindingsLoadingScene
        {
            get => m_inputBindingsLoadingScene;
            set { m_inputBindingsLoadingScene = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies scenes where input bindings should be ignored while open (for this scene).</summary>
        public Scene[] ignoreInputBindingsForScenes
        {
            get => m_ignoreInputBindingsForScenes;
            set { m_ignoreInputBindingsForScenes = value; OnPropertyChanged(); }
        }

        #endregion
        #region LoadPriority

        [SerializeField] private LoadPriority m_loadPriority = LoadPriority.Auto;

        /// <summary>Specifies the <see cref="LoadPriority"/> to use when opening this collection.</summary>
        public LoadPriority loadPriority
        {
            get => m_loadPriority;
            set { m_loadPriority = value; OnPropertyChanged(); }
        }

        #endregion

        #endregion
        #region Methods

        #region Shared

        #region Interfaces

        /// <summary>Defines a set of methods that is meant to be shared between: <see cref="Scene"/>, <see cref="ASMSceneHelper"/>, and <see cref="SceneManager.runtime"/>.</summary>
        interface IXmlDocsHelper
        { }

        /// <inheritdoc cref="IXmlDocsHelper"/>
        /// <remarks>Specified methods to be used programmatically, on the scene itself.</remarks>
        public interface IMethods
        {

            /// <summary>Opens the scene.</summary>
            /// <remarks>No effect if scene is already open.</remarks>
            public SceneOperation Open();

            /// <summary>Toggles this scene open or closed.</summary>
            public SceneOperation ToggleOpen();

            /// <summary>Closes the scene.</summary>
            /// <remarks>No effect if scene is already closed.</remarks>
            public SceneOperation Close();

            /// <summary>Preloads the scene, to be displayed at a later time. See also: <see cref="FinishPreload"/>, <see cref="DiscardPreload"/>.</summary>
            /// <remarks>Scene must be closed beforehand.</remarks>
            public SceneOperation Preload(Action onPreloaded = null);

            /// <summary>Opens the scene while a loading screen is open.</summary>
            public SceneOperation OpenWithLoadingScreen(Scene loadingScene);

            /// <summary>Closes the scene while a loading screen is open.</summary>
            public SceneOperation CloseWithLoadingScreen(Scene loadingScene);

            /// <summary>Opens the scene and sets it as active.</summary>
            public SceneOperation OpenAndActivate();

            /// <summary>Sets the scene as active in heirarchy.</summary>
            public void SetActive();

            /// <inheritdoc cref="IXmlDocsHelper"/>
            /// <remarks>Specifies methods to be used in UnityEvent, using the scene itself.</remarks>
            public interface IEvent
            {

                /// <summary>Event method. Its meant for <see cref="UnityEngine.Events.UnityEvent"/>.</summary>
                public void _Open();

                /// <inheritdoc cref="_Open"/>
                public void _ToggleOpen();

                /// <inheritdoc cref="_Open"/>
                public void _Close();

                /// <inheritdoc cref="_Open"/>
                public void _Preload();

                /// <inheritdoc cref="_Open"/>
                public void _FinishPreload();

                /// <inheritdoc cref="_Open"/>
                public void _DiscardPreload();

                /// <inheritdoc cref="_Open"/>
                public void _CancelPreload();

                /// <inheritdoc cref="_Open"/>
                public void _OpenWithLoadingScreen(Scene loadingScene);

                /// <inheritdoc cref="_Open"/>
                public void _CloseWithLoadingScreen(Scene loadingScene);

                /// <inheritdoc cref="_Open"/>
                public void _SetActive();

                /// <inheritdoc cref="_Open"/>
                public void _OpenAndActivate();

            }

        }

        /// <inheritdoc cref="IXmlDocsHelper"/>
        /// <remarks>Specifies methods to be used programmatically, using scene as first parameter.</remarks>
        public interface IMethods_Target
        {

            /// <summary>Opens the specified scene.</summary>
            /// <remarks>Already open scenes not affected.</remarks>
            public SceneOperation Open(Scene scene);

            /// <summary>Toggles the open state of the specified scene.</summary>
            public SceneOperation ToggleOpen(Scene scene);

            /// <summary>Closes the specified scene.</summary>
            /// <remarks>Already closed scenes not affected.</remarks>
            public SceneOperation Close(Scene scene);

            /// <summary>Preloads the specified scene, to be displayed at a later time. See also: <see cref="FinishPreload(Scene)"/>, <see cref="DiscardPreload(Scene)"/>.</summary>
            /// <remarks>Scene must be closed beforehand.</remarks>
            public SceneOperation Preload(Scene scene, Action onPreloaded = null);

            /// <summary>Opens the specified scene while a loading screen is open.</summary>
            public SceneOperation OpenWithLoadingScreen(Scene scene, Scene loadingScene);

            /// <summary>Closes the specified scene while a loading screen is open.</summary>
            public SceneOperation CloseWithLoadingScreen(Scene scene, Scene loadingScene);

            /// <summary>Opens the scene and activates it.</summary>
            public SceneOperation OpenAndActivate(Scene scene);

            /// <summary>Sets the specified scene as active in heirarchy.</summary>
            public void SetActive(Scene scene);

            /// <inheritdoc cref="IXmlDocsHelper"/>
            /// <remarks>Specifies methods to be used in UnityEvent, when not using scene itself.</remarks>
            public interface IEvent
            {

                /// <inheritdoc cref="IMethods.IEvent._Open"/>
                public void _Open(Scene scene);

                /// <inheritdoc cref="_Open"/>
                public void _ToggleOpen(Scene scene);

                /// <inheritdoc cref="_Open"/>
                public void _Close(Scene scene);

                /// <inheritdoc cref="_Open"/>
                public void _Preload(Scene scene);

                /// <inheritdoc cref="_Open"/>
                public void _FinishPreload();

                /// <inheritdoc cref="_Open"/>
                public void _DiscardPreload();

                /// <inheritdoc cref="_Open"/>
                public void _CancelPreload();

                /// <inheritdoc cref="_Open"/>
                public void _SetActive(Scene scene);

                /// <inheritdoc cref="_Open"/>
                public void _OpenAndActivate(Scene scene);

            }

        }

        #endregion
        #region IMethods

        public SceneOperation Open() => !isOpen ? SceneManager.runtime.Open(this) : Error($"The scene '{name}' cannot be opened, as it is already open.");
        public SceneOperation OpenAndActivate() => !isOpen ? SceneManager.runtime.OpenAndActivate(this) : Error($"The scene '{name}' cannot be opened, as it is already open.");
        public SceneOperation ToggleOpen() => SceneManager.runtime.ToggleOpen(this);
        public SceneOperation Close() => isOpen ? SceneManager.runtime.Close(this) : Error($"The scene '{name}' cannot be closed, as it is not open.");
        public SceneOperation Preload(Action onPreloaded = null) => !isOpenInHierarchy ? SceneManager.runtime.Preload(this, onPreloaded) : Error($"The scene '{name}' cannot be preloaded, as it is already open.");
        public SceneOperation FinishPreload() => SceneManager.runtime.FinishPreload();
        public SceneOperation CancelPreload() => SceneManager.runtime.CancelPreload();
        public SceneOperation OpenWithLoadingScreen(Scene loadingScreen) => !isOpen ? SceneManager.runtime.OpenWithLoadingScreen(this, loadingScreen) : Error($"The scene '{name}' cannot be opened, as it is already open.");
        public SceneOperation CloseWithLoadingScreen(Scene loadingScreen) => isOpen ? SceneManager.runtime.CloseWithLoadingScreen(this, loadingScreen) : Error($"The scene '{name}' cannot be closed, as it is not open.");

        [Obsolete("DiscardPreload is obsolete, please use CancelPreload instead.")]
        public SceneOperation DiscardPreload() => SceneManager.runtime.DiscardPreload();

        /// <inheritdoc cref="SetActive"/>
        public void Activate() => SceneManager.runtime.Activate(this);

        [Obsolete]
        public void SetActive() => SceneManager.runtime.SetActive(this);

        SceneOperation Error(string message)
        {
            Debug.LogError(message);
            return SceneOperation.done;
        }

        #endregion
        #region IEvent

        public void _Open() => SpamCheck.EventMethods.Execute(() => Open());
        public void _OpenAndActivate() => SpamCheck.EventMethods.Execute(() => OpenAndActivate());
        public void _ToggleOpen() => SpamCheck.EventMethods.Execute(() => ToggleOpen());
        public void _Close() => SpamCheck.EventMethods.Execute(() => Close());
        public void _Preload() => SpamCheck.EventMethods.Execute(() => Preload());
        public void _FinishPreload() => SpamCheck.EventMethods.Execute(() => FinishPreload());
        public void _CancelPreload() => SpamCheck.EventMethods.Execute(() => CancelPreload());
        public void _OpenWithLoadingScreen(Scene loadingScene) => SpamCheck.EventMethods.Execute(() => OpenWithLoadingScreen(loadingScene));
        public void _CloseWithLoadingScreen(Scene loadingScene) => SpamCheck.EventMethods.Execute(() => CloseWithLoadingScreen(loadingScene));

        [Obsolete]
        public void _SetActive() => Activate();
        public void _Activate() => Activate();

        [Obsolete("DiscardPreload is obsolete, please use CancelPreload instead.")]
        public void _DiscardPreload() => CancelPreload();

        #endregion

        #endregion
        #region Find

        /// <summary>Gets 't:AdvancedSceneManager.Models.Scene', the string to use in <see cref="AssetDatabase.FindAssets(string)"/>.</summary>
        public readonly static string AssetSearchString = "t:" + typeof(Scene).FullName;

        /// <summary>Gets if <paramref name="q"/> matches <see cref="ASMModel.name"/>, <see cref="id"/>, <see cref="path"/>.</summary>
        public override bool IsMatch(string q) =>
            base.IsMatch(q) || IsPathMatch(q) || IsGUIDMatch(q);

        bool IsPathMatch(string q) =>
            q == path;

        bool IsGUIDMatch(string q) =>
            q == sceneAssetGUID;

        /// <inheritdoc cref="SceneUtility.Find(Func{Scene, bool}) Find"/>
        public static IEnumerable<Scene> Find(Func<Scene, bool> predicate) =>
            SceneUtility.Find(predicate);

        /// <inheritdoc cref="SceneUtility.Find(string)"/>
        public static Scene Find(string q) =>
            SceneUtility.Find(q);

        /// <inheritdoc cref="SceneUtility.Find(string)"/>
        public static bool TryFind(string q, out Scene scene) =>
            scene = Find(q);

        /// <inheritdoc cref="SceneUtility.FindOpen(string)"/>
        public static IEnumerable<Scene> FindOpen(string q) =>
            SceneUtility.FindOpen(q);

        /// <inheritdoc cref="SceneUtility.FindOpen(Func{Scene, bool})"/>
        public static IEnumerable<Scene> FindOpen(Func<Scene, bool> predicate) =>
            SceneUtility.FindOpen(predicate);

        #endregion
        #region Helpers

        /// <summary>Gets whatever this scene will be opened as persistent.</summary>
        /// <param name="parentCollection">Specifies the parent collection that was opened before <paramref name="finalCollection"/>.</param>
        /// <param name="collectionToOpen">Specifies the collection that will be opened, if you are not evaluating state after it would have opened, pass <see langword="null"/>. If multiple collections are opened in sequence, then pass the final one.</param>
        public bool EvalOpenAsPersistent(SceneCollection parentCollection, SceneCollection collectionToOpen = null) =>
            keepOpenWhenCollectionsClose ||
            (parentCollection && parentCollection.openAsPersistent) ||
            (keepOpenWhenNewCollectionWouldReopen && collectionToOpen && collectionToOpen.Contains(this));

#if UNITY_EDITOR

        internal bool CheckIfSpecialScene()
        {

            if (File.Exists(path))
            {

                var str = File.ReadAllText(path);
                var isSplashScreen = str.Contains("isSplashScreen: 1");
                var isLoadingScreen = str.Contains("isLoadingScreen: 1");

                if (this.isSplashScreen != isSplashScreen || this.isLoadingScreen != isLoadingScreen)
                {
                    this.isSplashScreen = isSplashScreen;
                    this.isLoadingScreen = isLoadingScreen;
                    return true;
                }

            }

            return false;

        }

#endif

        #endregion

        #endregion
        #region Events

        [SerializeField] Events m_events;

        /// <summary>Gets the unity events for this scene.</summary>
        public Events events => m_events;

        [Serializable]
        public struct Events
        {

            /// <summary>Occurs when this scene is opened.</summary>
            public UnityEvent<Scene> OnOpen;
            /// <summary>Occurs when this scene is closed.</summary>
            public UnityEvent<Scene> OnClose;
            /// <summary>Occurs when this scene is preloaded.</summary>
            public UnityEvent<Scene> OnPreload;
            /// <summary>Occurs when preload is finished for this scene.</summary>
            public UnityEvent<Scene> OnPreloadFinished;

            /// <summary>Occurs when a collection opened this scene.</summary>
            public UnityEvent<Scene, SceneCollection> OnCollectionOpened;
            /// <summary>Occurs when a collection closed this scene.</summary>
            public UnityEvent<Scene, SceneCollection> OnCollectionClosed;

        }

        #endregion
        #region Scene loader

        [SerializeField] private string m_sceneLoader;

        /// <summary>Specifies what <see cref="SceneManagement.SceneLoader"/> to use.</summary>
        public string sceneLoader
        {
            get => m_sceneLoader;
            set { m_sceneLoader = value; OnPropertyChanged(); NotifyAddressables(); }
        }

        /// <summary>Specifies the scene loader to use for this scene.</summary>
        /// <remarks>If the specified scene loader is not registered when scene is opened, then ASM will fallback to other scene loaders, if any (normal ASM functionality is used if not).</remarks>
        public void SetSceneLoader<T>() where T : SceneLoader
        {
            sceneLoader = SceneLoader.GetKey<T>();
            NotifyAddressables();
        }

        void NotifyAddressables()
        {
#if ADDRESSABLES
            PackageSupport.Addressables.Extensions.OnAddressablesChanged(this);
#endif
        }

        /// <summary>Gets the scene loader specified for this scene. <see langword="null"/> if none set.</summary>
        public SceneLoader GetSceneLoader() =>
            SceneManager.runtime.GetSceneLoader(sceneLoader);

        /// <summary>Gets the effective, contextual, scene loader for this scene. <see langword="null"/> if none found (this means normal ASM loader will be used).</summary>
        public SceneLoader GetEffectiveSceneLoader() =>
            SceneManager.runtime.GetLoaderForScene(this);

        public bool UsesSceneLoader<T>() where T : SceneLoader =>
            SceneManager.runtime.GetSceneLoaderType(sceneLoader) == typeof(T);

        /// <summary>Clears custom scene loader for this scene. This means normal ASM functionality will be used.</summary>
        public void ClearSceneLoader() =>
            sceneLoader = null;

        #endregion
        #region Netcode

#if NETCODE

        /// <summary>Gets or sets if this scene is enabled for netcode.</summary>
        public bool isNetcode
        {
            get => UsesSceneLoader<PackageSupport.Netcode.SceneLoader>();
            set { if (value) SetSceneLoader<PackageSupport.Netcode.SceneLoader>(); OnPropertyChanged(); }
        }

        /// <summary>Gets if this scene was synced using netcode.</summary>
        public bool isSynced { get; internal set; }

#endif

        #endregion
        #region Addressables

#if ADDRESSABLES

        /// <summary>Gets or sets if this scene is enabled for addressables.</summary>
        public bool isAddressable
        {
            get => UsesSceneLoader<PackageSupport.Addressables.SceneLoader>();
            set { if (value) SetSceneLoader<PackageSupport.Addressables.SceneLoader>(); OnPropertyChanged(); }
        }

        /// <summary>Gets the addressable address for this scene.</summary>
        public string address => isAddressable ? $"{name} ({id})" : null;

#endif

        #endregion
        #region Equality

        public interface IEquality : IEquatable<Scene>, IEquatable<unityScene?>
#if UNITY_EDITOR
    , IEquatable<SceneAsset>
#endif
        { }

        public override int GetHashCode() => id?.GetHashCode() ?? 0;

        public override bool Equals(object obj) => IsEqual(obj, this);
        public bool Equals(Scene scene) => IsEqual(scene, this);
        public bool Equals(unityScene? scene) => IsEqual(scene, this);

        public static bool operator ==(Scene scene, Scene scene1) => IsEqual(scene, scene1);
        public static bool operator !=(Scene scene, Scene scene1) => !IsEqual(scene, scene1);

#if UNITY_EDITOR
        public bool Equals(SceneAsset scene) => IsEqual(scene, this);
        public static bool operator ==(Scene scene, SceneAsset sceneAsset) => IsEqual(scene, sceneAsset);
        public static bool operator !=(Scene scene, SceneAsset sceneAsset) => !IsEqual(scene, sceneAsset);
        public static bool operator ==(SceneAsset sceneAsset, Scene scene) => IsEqual(scene, sceneAsset);
        public static bool operator !=(SceneAsset sceneAsset, Scene scene) => !IsEqual(scene, sceneAsset);
#endif

        public static bool IsEqual(object left, object right)
        {
            if (left is null || right is null)
                return left is null && right is null; // True if both are null, false otherwise.

            // Check paths for non-null objects.
            return GetPath(left, out string l) && GetPath(right, out string r) && l == r;
        }

        static bool GetPath(object obj, out string path)
        {
            path = obj switch
            {
                Scene scene when scene => scene.path,
                unityScene unityScene => unityScene.path,
#if UNITY_EDITOR
                SceneAsset sceneAsset when sceneAsset => AssetDatabase.GetAssetPath(sceneAsset),
#endif
                _ => null
            };

            return !string.IsNullOrEmpty(path);
        }

        #endregion
        #region Implicit

        #region Path

        public static implicit operator string(Scene scene) => scene ? scene.path : string.Empty;

        #endregion
        #region Unity scene

        public static implicit operator unityScene?(Scene scene) => scene ? scene.internalScene : default;
        public static implicit operator unityScene(Scene scene) => scene ? scene.internalScene ?? default : default;

        public static implicit operator Scene(unityScene scene) => scene.ASMScene();
        public static implicit operator Scene(unityScene? scene) => scene?.ASMScene();

        #endregion
        #region SceneAsset
#if UNITY_EDITOR

        public static implicit operator SceneAsset(Scene scene) => scene ? (SceneAsset)scene.sceneAsset : default;
        public static implicit operator Scene(SceneAsset scene) => scene ? scene.ASMScene() : default;

#endif
        #endregion

        #endregion
        #region ToString

        public const string InGameToolbarDescription = "The in-game toolbar can help with diagnosing scene state during builds.";
        public const string PauseScreenDescription = "The pause scene provides a default pause screen, for when your game does not yet have one.";

        public override string ToString()
        {

            var persistentIndicator = "";
            if (keepOpenWhenCollectionsClose)
                persistentIndicator = " (keepOpenWhenCollectionsClose)";
            else if (keepOpenWhenNewCollectionWouldReopen)
                persistentIndicator = " (keepOpenWhenNewCollectionWouldReopen)";

            return $"{name}{persistentIndicator}";

        }

        public string GetTooltip()
        {

            var sb = new StringBuilder();

            GetPathInfo(sb);
            GetStartupInfo(sb);
            GetPersistentInfo(sb);
            GetDescriptionInfo(sb);
            GetHotkeyInfo(sb);
            GetSceneLoaderInfo(sb);

            return sb.ToString().Trim('\n');

        }

        void GetPathInfo(StringBuilder sb)
        {
            sb.AppendLine("\n<i><b>" + name + "</b></i>");
            sb.AppendLine("<i>" + path + "</i>");
        }

        void GetStartupInfo(StringBuilder sb)
        {

            sb.AppendLine("\n<b>Startup</b>");

            if (openOnStartup ||
                (Profile.current && Profile.current.startupScenes.Contains(this)) ||
                (Profile.current && Profile.current.startupCollections.SelectMany(s => s.scenesToAutomaticallyOpen).Contains(this)))
                sb.AppendLine("Will <b>open</b> during startup.");
            else
                sb.AppendLine("Will <b>not open</b> during startup.");

        }

        void GetPersistentInfo(StringBuilder sb)
        {

            sb.AppendLine("\n<b>Persistent</b>");

            if (keepOpenWhenCollectionsClose)
                sb.AppendLine("This scene will <b>remain open</b> when the collection is closed, or when another collection tries to close it.");
            else
                sb.AppendLine("This scene will <b>close</b> when the collection is closed, or when another collection tries to close it.");

            sb.AppendLine();

            if (keepOpenWhenNewCollectionWouldReopen)
                sb.AppendLine("This scene will <b>stay open</b> when a new collection tries to reopen it.");
            else
                sb.AppendLine("This scene will <b>be closed</b> when a new collection tries to reopen it.");

        }

        void GetDescriptionInfo(StringBuilder sb)
        {

            if (this == SceneManager.assets.defaults.inGameToolbarScene)
            {
                sb.AppendLine("\n<b>Description:</b>");
                sb.AppendLine(InGameToolbarDescription);
            }
            else if (this == SceneManager.assets.defaults.pauseScene)
            {
                sb.AppendLine("\n<b>Description:</b>");
                sb.AppendLine(PauseScreenDescription);
            }

        }

        void GetHotkeyInfo(StringBuilder sb)
        {

            sb.AppendLine("\n<b>Hotkeys:</b>");

#if INPUTSYSTEM && ENABLE_INPUT_SYSTEM

            if (!Profile.current)
            {
                sb.AppendLine("--");
                return;
            }

            var scene = Profile.current.standaloneScenes.FirstOrDefault(s => s == this);
            if (!scene || scene.inputBindings?.Length == 0)
            {
                sb.AppendLine("--");
                return;
            }

            foreach (var binding in scene.inputBindings)
            {

                if (binding.buttons.Any())
                    sb.AppendJoin(" + ", binding.buttons.Select(b => b.name));
                else
                    sb.Append("None");

                sb.Append($" <i>(interaction: {binding.interactionType})</i>");
                sb.Append("\n");

            }

#else
            sb.AppendLine("--");
#endif

        }

        void GetSceneLoaderInfo(StringBuilder sb)
        {

            sb.AppendLine("\n<b>Scene loader:</b>");

            var sceneLoader = SceneManager.runtime.GetSceneLoader(this.sceneLoader);
            sb.AppendLine(sceneLoader?.Key ?? "ASM runtime scene loader");

        }

        #endregion
        #region Runtime

        /// <summary>Gets if this scene is currently active.</summary>
        public bool isActive =>
            SceneManager.runtime.activeScene == this;

        /// <summary>Gets whatever the scene is open in the hierarchy, this is <see langword="true"/> if scene is currently loading, if scene is preloaded, if scene is fully open.</summary>
        public bool isOpenInHierarchy =>
            SceneUtility.GetAllOpenUnityScenes().Any(s => s.path == path);

        /// <inheritdoc cref="Runtime.GetState(Scene)"/>
        public SceneState state =>
            SceneManager.runtime.GetState(this);

        /// <summary>Gets whatever the scene is open.</summary>
        public bool isOpen =>
            isDontDestroyOnLoad || SceneManager.runtime.IsTracked(this);

        /// <summary>Gets whatever the scene is preloaded.</summary>
        public bool isPreloaded =>
            state == SceneState.Preloaded;

        /// <summary>Gets the <see cref="unityScene"/> that this scene is associated with.</summary>
        /// <remarks><see langword="null"/> if scene is not open.</remarks>
        public unityScene? internalScene { get; internal set; }

        /// <summary>Gets if this scene is opened as persistent.</summary>
        public bool isPersistent =>
            isOpen &&
            isDontDestroyOnLoad ||
            keepOpenWhenCollectionsClose ||
            (openedBy && openedBy.openAsPersistent);

        internal SceneCollection openedBy { get; set; }

        /// <summary>Gets if this scene is the dontDestroyOnLoad scene.</summary>
        public bool isDontDestroyOnLoad =>
            internalScene?.handle == SceneManager.runtime.dontDestroyOnLoadScene.handle;

        /// <summary>Gets if this scene is dynamic, it is not persisted to disk.</summary>
        public bool isDynamic =>
            internalScene?.IsValid() ?? false && string.IsNullOrWhiteSpace(internalScene.Value.path);

        /// <summary>Gets the root game objects in this <see cref="Scene"/>.</summary>
        /// <remarks>Only usable if scene is open.</remarks>
        public IEnumerable<GameObject> GetRootGameObjects() =>
            (internalScene?.IsValid() ?? false)
            ? internalScene.Value.GetRootGameObjects()
            : Array.Empty<GameObject>();

        /// <summary>Finds the object in the hierarchy of this <see cref="Scene"/>.</summary>
        /// <remarks>Only works if scene is loaded.</remarks>
        public T FindObject<T>() =>
            FindObjects<T>().FirstOrDefault();

        /// <inheritdoc cref="FindObject{T}()"/>
        public bool FindObject<T>(out T component) =>
            (component = FindObject<T>()) != null;

        [SuppressMessage("TypeSafety", "UNT0014:Invalid type for call to GetComponent", Justification = "Interfaces can be used")]
        /// <summary>Finds the objects in the hierarchy of this <see cref="Scene"/>.</summary>
        /// <remarks>Only works if scene is loaded.</remarks>
        public IEnumerable<T> FindObjects<T>()
        {
            foreach (var obj in GetRootGameObjects())
            {
                var components = obj.GetComponentsInChildren<T>();
                foreach (var component in components)
                    yield return component;
            }
        }

        #endregion

    }

}
