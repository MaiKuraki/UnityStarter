using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Models.Enums;
using UnityEngine;
using AdvancedSceneManager.Utility;
using UnityEngine.Events;

#if INPUTSYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.Utilities;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AdvancedSceneManager.Models
{

    /// <summary>Represents a collection of scenes.</summary>
    /// <remarks>Only one collection can be open at a time.</remarks>
    public class SceneCollection : ASMModel,
        IEquatable<SceneCollection>,
        ISceneCollection,
        ISceneCollection.IEditable, ISceneCollection.IOpenable,
        SceneCollection.IMethods, SceneCollection.IMethods.IEvent,
        ILockable
    {

        #region Startup

        void UpdateStartup()
        {

            if (FindProfile(out var profile))
                foreach (var collection in profile.collections.Cast<ISceneCollection>())
                    collection.OnPropertyChanged(nameof(isStartupCollection));

        }

        #endregion
        #region ISceneCollection

        public int count =>
            m_scenes.Count;

        public Scene this[int index] =>
            m_scenes.ElementAtOrDefault(index);

        public string title =>
            m_title;

        [HideInInspector]
        public string description
        {
            get => m_description;
            set => m_description = value;
        }

        public IEnumerable<string> scenePaths =>
            m_scenes?.Select(s => s.path) ?? Enumerable.Empty<string>();

        public IEnumerable<Scene> scenes =>
            m_scenes ?? Enumerable.Empty<Scene>();

        public IEnumerator<Scene> GetEnumerator() =>
            m_scenes?.GetEnumerator() ?? Enumerable.Empty<Scene>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();

        #endregion
        #region ISceneCollection.IEditable

        List<Scene> ISceneCollection.IEditable.sceneList => m_scenes;

        #endregion
        #region Name / title

#if UNITY_EDITOR

        /// <summary>Gets if name should be prefixed with <see cref="m_prefix"/>.</summary>
        protected virtual bool UsePrefix { get; } = true;

        internal void SetTitleAfterCreation(string prefix, string title)
        {
            m_title = title;
            ((ScriptableObject)this).name = $"{(UsePrefix ? prefix : "")}{title}";
        }

        internal void Rename(string newName, string prefix)
        {
            m_prefix = prefix;
            Rename(newName);
        }

        internal override void Rename(string newName)
        {

            if (string.IsNullOrEmpty(newName))
                return;

            if (m_prefix == null && FindProfile(out var p))
                m_prefix = p.prefix;

            if (!string.IsNullOrEmpty(m_prefix) && newName.StartsWith(m_prefix))
                newName = newName.Substring(m_prefix.Length);

            m_title = newName;

            var prefix = UsePrefix ? m_prefix : "";
            newName = $"{prefix}{newName}";

            while (newName.EndsWith(".asset"))
                newName = newName.Remove(name.Length - ".asset".Length);

            base.Rename(newName);

        }

#endif

        #endregion
        #region Fields

#if UNITY_EDITOR

        public override void OnValidate()
        {
            EditorApplication.delayCall += Editor.Utility.BuildUtility.UpdateSceneList;
            base.OnValidate();
        }

#endif

        //Core variables
        [SerializeField] internal string m_title = "New Collection";
        [SerializeField] protected string m_description;
        [SerializeField] internal List<Scene> m_scenes = new();
        [SerializeField] internal string m_prefix;

        //Extra scenes
        [SerializeField] private LoadingScreenUsage m_loadingScreenUsage = LoadingScreenUsage.UseDefault;
        [SerializeField] private Scene m_loadingScreen;
        [SerializeField] private Scene m_activeScene;
        [SerializeField] private bool m_setActiveSceneWhenOpenedAsAdditive;

        //Collection open options
        [SerializeField] private bool m_unloadUnusedAssets = true;
        [SerializeField] private bool m_openAsPersistent = false;

        //Other
        [SerializeField] private ScriptableObject m_extraData;
        [SerializeField] private CollectionStartupOption m_startupOption = CollectionStartupOption.Auto;
        [SerializeField] private LoadPriority m_loadPriority = LoadPriority.Auto;
        [SerializeField] private bool m_isIncluded = true;
        [SerializeField] private bool m_isLocked;
        [SerializeField] private string m_lockMessage;

        [SerializeField] private List<Scene> m_scenesThatShouldNotAutomaticallyOpen = new();

        [SerializeField] internal InputBinding[] m_inputBindings = Array.Empty<InputBinding>();
        [SerializeField] private Scene[] m_ignoreInputBindingsForScenes;

        #endregion
        #region Properties

        public override void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {

            base.OnPropertyChanged(propertyName);

            if (propertyName == nameof(startupOption))
                UpdateStartup();

        }

        /// <summary>Gets both <see cref="scenes"/> and <see cref="loadingScreen"/>.</summary>
        /// <remarks><see langword="null"/> is filtered out.</remarks>
        public IEnumerable<Scene> allScenes =>
            m_scenes.
            Concat(new[] { loadingScreen }).
            Where(s => s);

        /// <summary>Gets if this collection has any scenes.</summary>
        public bool hasScenes => m_scenes.Where(s => s).Any();

        /// <summary>Gets if this is a startup collection.</summary>
        /// <remarks>Only available in editor.</remarks>
        public bool isStartupCollection => isIncluded && FindProfile(out var profile) && profile.IsStartupCollection(this);

        /// <summary>The extra data that is associated with this collection.</summary>
        /// <remarks>Use <see cref="UserData{T}"/> to cast it to the desired type.</remarks>
        public ScriptableObject userData
        {
            get => m_extraData;
            set { m_extraData = value; OnPropertyChanged(); }
        }

        /// <summary>Gets whatever this collection should be included in build.</summary>
        public bool isIncluded
        {
            get => SceneManager.settings.project.allowExcludingCollectionsFromBuild ? m_isIncluded : true;
            set { m_isIncluded = value; OnPropertyChanged(); }
        }

        /// <summary>The loading screen that is associated with this collection.</summary>
        public Scene loadingScreen
        {
            get => m_loadingScreen;
            set { m_loadingScreen = value; OnPropertyChanged(); }
        }

        /// <summary>Gets effective loading screen depending on <see cref="loadingScreenUsage"/>.</summary>
        public Scene effectiveLoadingScreen
        {
            get
            {
                if (loadingScreenUsage == LoadingScreenUsage.Override)
                    return loadingScreen;
                else if (loadingScreenUsage == LoadingScreenUsage.UseDefault)
                    return Profile.current.loadingScene;
                else
                    return null;
            }
        }

        /// <summary>Specifies what loading screen to use.</summary>
        public LoadingScreenUsage loadingScreenUsage
        {
            get => m_loadingScreenUsage;
            set { m_loadingScreenUsage = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies the scene that should be activated after collection is opened.</summary>
        public Scene activeScene
        {
            get => m_activeScene;
            set { m_activeScene = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies startup option.</summary>
        public CollectionStartupOption startupOption
        {
            get => m_startupOption;
            set { m_startupOption = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies the <see cref="LoadPriority"/> to use when opening this collection.</summary>
        public LoadPriority loadPriority
        {
            get => m_loadPriority;
            set { m_loadPriority = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever this collection should be opened as persistent.</summary>
        public bool openAsPersistent
        {
            get => m_openAsPersistent;
            set { m_openAsPersistent = value; OnPropertyChanged(); }
        }

        /// <summary>Calls <see cref="Resources.UnloadUnusedAssets"/> after collection is opened or closed.</summary>
        public bool unloadUnusedAssets
        {
            get => m_unloadUnusedAssets;
            set { m_unloadUnusedAssets = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies scenes that should not open automatically.</summary>
        public List<Scene> scenesThatShouldNotAutomaticallyOpen =>
            m_scenesThatShouldNotAutomaticallyOpen;

        public IEnumerable<Scene> scenesToAutomaticallyOpen =>
            scenes.NonNull().Except(scenesThatShouldNotAutomaticallyOpen);

        /// <summary>Gets if this collection is locked.</summary>
        public bool isLocked
        {
            get => m_isLocked;
            set { m_isLocked = value; OnPropertyChanged(); }
        }

        /// <summary>Gets the lock message for this collection.</summary>
        public string lockMessage
        {
            get => m_lockMessage;
            set { m_lockMessage = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever <see cref="activeScene"/> should be set, when collection is opened as additive.</summary>
        public bool setActiveSceneWhenOpenedAsActive
        {
            get => m_setActiveSceneWhenOpenedAsAdditive;
            set { m_setActiveSceneWhenOpenedAsAdditive = value; OnPropertyChanged(); }
        }

        /// <summary>Gets or sets the input bindings for this collection.</summary>
        public InputBinding[] inputBindings
        {
            get => m_inputBindings;
            set { m_inputBindings = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies scenes where input bindings should be ignored while open (for this collection).</summary>
        public Scene[] ignoreInputBindingsForScenes
        {
            get => m_ignoreInputBindingsForScenes;
            set { m_ignoreInputBindingsForScenes = value; OnPropertyChanged(); }
        }

        #endregion
        #region Runtime

        /// <summary>Gets if this collection is open.</summary>
        public bool isOpen => isOpenNonAdditive || isOpenAdditive;

        /// <summary>Gets if this collection is opened additively.</summary>
        public bool isOpenAdditive => SceneManager.runtime.openAdditiveCollections.Contains(this);

        /// <summary>Gets if this collection is opened additively.</summary>
        public bool isOpenNonAdditive => SceneManager.runtime.openCollection == this;

        #endregion
        #region Find

        /// <summary>Gets 't:AdvancedSceneManager.Models.SceneCollection', the string to use in <see cref="AssetDatabase.FindAssets(string)"/>.</summary>
        public readonly static string AssetSearchString = "t:" + typeof(SceneCollection).FullName;

        /// <summary>Gets if <paramref name="q"/> matches <see cref="ASMModel.name"/>.</summary>
        public override bool IsMatch(string q) =>
            base.IsMatch(q) || title == q;

        /// <summary>Finds a collection based on its title or id.</summary>
        public static SceneCollection Find(string q, bool activeProfile = true) =>
            activeProfile
            ? Profile.current.collections.FirstOrDefault(c => c && c.IsMatch(q))
            : SceneManager.assets.collections.Find(q);

        /// <summary>Finds a collection based on its title or id.</summary>
        public static bool TryFind(string q, out SceneCollection collection, bool activeProfile = true) =>
          collection =
            (activeProfile
            ? Profile.current.collections.FirstOrDefault(c => c && c.IsMatch(q))
            : SceneManager.assets.collections.Find(q));

        #endregion
        #region Methods

        #region Interfaces

        /// <summary>Defines a set of methods that is meant to be shared between: <see cref="SceneCollection"/>, <see cref="ASMSceneHelper"/>, and <see cref="SceneManager.runtime"/>.</summary>
        interface IXmlDocsHelper
        { }

        /// <inheritdoc cref="IXmlDocsHelper"/>
        /// <remarks>Specified methods to be used programmatically, on the collection itself.</remarks>
        public interface IMethods
        {

            /// <summary>Reopens this collection.</summary>
            /// <param name="openAll">Specifies whatever scenes flagged to not open with collection, should.</param>
            public SceneOperation Reopen(bool openAll = false);

            /// <summary>Opens this collection.</summary>
            /// <param name="openAll">Specifies whatever scenes flagged to not open with collection, should.</param>
            public SceneOperation Open(bool openAll = false);

            /// <summary>Opens this collection as additive.</summary>
            /// <param name="openAll">Specifies whatever scenes flagged to not open with collection, should.</param>
            /// <remarks>Additive collections are not "opened", all scenes within are merely opened like normal scenes. Mostly intended for convenience.</remarks>
            public SceneOperation OpenAdditive(bool openAll = false);

            /// <summary>Preloads the collection.</summary>
            /// <remarks>Loading screen not supported. Some operations that would normally run in collection open are delayed until <see cref="Runtime.FinishPreload()"/> (scene close and scene activate).</remarks>
            public SceneOperation Preload(bool openAll = false);

            /// <summary>Preloads the collection as additive.</summary>
            /// <remarks>Loading screen not supported. Some operations that would normally run in collection open are delayed until <see cref="Runtime.FinishPreload()"/> (scene close and scene activate).</remarks>
            public SceneOperation PreloadAdditive(bool openAll = false);

            /// <summary>Toggles this collection open or closed.</summary>
            /// <param name="openAll">Specifies whatever scenes flagged to not open with collection, should.</param>
            public SceneOperation ToggleOpen(bool openAll = false);

            /// <summary>Closes this collection.</summary>
            /// <remarks>No effect if collection is already closed. Note that "additive collections" are not actually opened, only its scenes are.</remarks>
            public SceneOperation Close();

            /// <inheritdoc cref="IXmlDocsHelper"/>
            /// <remarks>Specifies methods to be used in UnityEvent, using the collection itself.</remarks>
            public interface IEvent
            {

                /// <inheritdoc cref="Open"/>
                public void _Open();

                /// <inheritdoc cref="OpenAdditive"/>
                public void _OpenAdditive();

                /// <inheritdoc cref="Preload"/>
                public void _Preload();

                /// <inheritdoc cref="PreloadAdditive"/>
                public void _PreloadAdditive();

                /// <inheritdoc cref="ToggleOpen"/>
                public void _ToggleOpen();

                /// <inheritdoc cref="Close"/>
                public void _Close();

            }

        }

        /// <inheritdoc cref="IXmlDocsHelper"/>
        /// <remarks>Specifies methods to be used programmatically, using collection as first parameter.</remarks>
        public interface IMethods_Target
        {

            /// <inheritdoc cref="IMethods.Open"/>
            public SceneOperation Open(SceneCollection collection, bool openAll = false);

            /// <inheritdoc cref="IMethods.OpenAdditive"/>
            public SceneOperation OpenAdditive(SceneCollection collection, bool openAll = false);

            /// <inheritdoc cref="IMethods.Preload"/>
            public SceneOperation Preload(SceneCollection collection, bool openAll = false);

            /// <inheritdoc cref="IMethods.PreloadAdditive"/>
            public SceneOperation PreloadAdditive(SceneCollection collection, bool openAll = false);

            /// <inheritdoc cref="IMethods.ToggleOpen"/>
            public SceneOperation ToggleOpen(SceneCollection collection, bool openAll = false);

            /// <inheritdoc cref="IMethods.Close"/>
            public SceneOperation Close(SceneCollection collection);

            /// <inheritdoc cref="IXmlDocsHelper"/>
            /// <remarks>Specifies methods to be used in UnityEvent, when not using collection itself.</remarks>
            public interface IEvent
            {

                /// <inheritdoc cref="IMethods.Open(bool)"/>
                public void _Open(SceneCollection collection);

                /// <inheritdoc cref="IMethods.OpenAdditive(bool)"/>
                public void _OpenAdditive(SceneCollection collection);

                /// <inheritdoc cref="IMethods.Preload(SceneCollection, bool)"/>
                public void _Preload(SceneCollection collection);

                /// <inheritdoc cref="IMethods.PreloadAdditive(SceneCollection, bool)"/>
                public void _PreloadAdditive(SceneCollection collection);

                /// <inheritdoc cref="IMethods.ToggleOpen( bool)"/>
                public void _ToggleOpen(SceneCollection collection);

                /// <inheritdoc cref="IMethods.Close"/>
                public void _Close(SceneCollection collection);

            }

        }

        #endregion
        #region IMethods

        public SceneOperation Reopen(bool openAll = false) => SceneManager.runtime.Reopen(this, openAll);
        public SceneOperation Open(bool openAll = false) => SceneManager.runtime.Open(this, openAll);
        public SceneOperation OpenAdditive(bool openAll = false) => SceneManager.runtime.OpenAdditive(this, openAll);

        public SceneOperation Preload(bool openAll = false) => SceneManager.runtime.Preload(this, openAll);
        public SceneOperation PreloadAdditive(bool openAll = false) => SceneManager.runtime.PreloadAdditive(this, openAll);

        public SceneOperation ToggleOpen(bool openAll = false) => SceneManager.runtime.ToggleOpen(this, openAll);

        public SceneOperation Close() => SceneManager.runtime.Close(this);

        #endregion
        #region IMethods.IEvent

        public void _Open() => SpamCheck.EventMethods.Execute(() => Open());
        public void _Reopen() => SceneManager.runtime.Reopen(this);
        public void _OpenAdditive() => SpamCheck.EventMethods.Execute(() => OpenAdditive());

        public void _Preload() => SceneManager.runtime.Preload(this);
        public void _PreloadAdditive() => SceneManager.runtime.PreloadAdditive(this);

        public void _ToggleOpen() => SpamCheck.EventMethods.Execute(() => ToggleOpen());

        public void _Close() => SpamCheck.EventMethods.Execute(() => Close());

        #endregion

        /// <summary>Find the <see cref="Profile"/> that this collection is associated with.</summary>
        public bool FindProfile(out Profile profile) =>
            profile = FindProfile();

        /// <summary>Find the <see cref="Profile"/> that this collection is associated with.</summary>
        public Profile FindProfile() =>
            SceneManager.assets.profiles.FirstOrDefault(p => p && p.Contains(this, true));

        /// <summary>Casts and returns <see cref="userData"/> as the specified type. Returns null if invalid type.</summary>
        public T UserData<T>() where T : ScriptableObject =>
            (T)userData;

        internal bool IsOpen(Scene scene) =>
            SceneManager.runtime.openScenes.Contains(scene);

        /// <summary>Gets if this collection contains <paramref name="scene"/>.</summary>
        public bool Contains(Scene scene) =>
            scenes.Contains(scene);

        /// <summary>Gets or sets whatever the scene should automatically open, when this collection is open. Default is <see langword="true"/>.</summary>
        public bool AutomaticallyOpenScene(Scene scene, bool? value = null)
        {

            if (value.HasValue)
            {

                scenesThatShouldNotAutomaticallyOpen.Remove(scene);

                if (!value.Value)
                    scenesThatShouldNotAutomaticallyOpen.Add(scene);

                Save();

                OnPropertyChanged(nameof(scenesThatShouldNotAutomaticallyOpen));

            }

            return !scenesThatShouldNotAutomaticallyOpen.Contains(scene);

        }

        #endregion
        #region Equality

        public override bool Equals(object obj) => Equals(obj as SceneCollection);
        public override int GetHashCode() => id?.GetHashCode() ?? 0;

        public bool Equals(SceneCollection other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return id == other.id;
        }

        public static bool operator ==(SceneCollection left, SceneCollection right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left is null)
                return false;

            return left.Equals(right);
        }

        public static bool operator !=(SceneCollection left, SceneCollection right)
        {
            return !(left == right);
        }

        #endregion
        #region Events

        [SerializeField] Events m_events;

        /// <summary>Gets the unity events for this scene.</summary>
        public Events events => m_events;

        [Serializable]
        public struct Events
        {

            /// <summary>Occurs when this collection is opened.</summary>
            public UnityEvent<SceneCollection> OnOpen;

            /// <summary>Occurs when this collection is closed.</summary>
            public UnityEvent<SceneCollection> OnClose;

        }

        #endregion

        public override string ToString() =>
            title;

    }

}
