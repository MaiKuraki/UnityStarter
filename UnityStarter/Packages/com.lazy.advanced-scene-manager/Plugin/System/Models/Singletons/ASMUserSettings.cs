#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.ComponentModel;
using AdvancedSceneManager.DependencyInjection.Editor;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.Serialization;

namespace AdvancedSceneManager.Models
{

    public enum UpdateInterval
    {
        Auto, Never, EveryHour, Every3Hours, Every6Hours, Every12Hours, Every24Hours, Every48Hours, EveryWeek
    }

    [Serializable]
    class CollectionScenePair
    {
        public SceneCollection collection;
        public int sceneIndex;
    }

    /// <summary>Contains settings that are stored locally, that aren't synced to source control.</summary>
    /// <remarks>Only available in editor.</remarks>
    [ASMFilePath("UserSettings/AdvancedSceneManager.asset")]
    public class ASMUserSettings : ASMScriptableSingleton<ASMUserSettings>, INotifyPropertyChanged, IUserSettings
    {

        public override bool editorOnly => true;

        #region Callback utility

        [Header("Callback Utility")]
        [SerializeField] private bool m_isCallbackUtilityEnabled;
        [SerializeField] private string m_callbackUtilityWindow;

        internal bool isCallbackUtilityEnabled
        {
            get => m_isCallbackUtilityEnabled;
            set { m_isCallbackUtilityEnabled = value; OnPropertyChanged(); }
        }

        internal string callbackUtilityWindow
        {
            get => m_callbackUtilityWindow;
            set { m_callbackUtilityWindow = value; OnPropertyChanged(); }
        }

        #endregion
        #region Menu popup

        [Header("Menu Popup")]
        [SerializeField] private string m_quickBuildPath;
        [SerializeField] private bool m_quickBuildUseProfiler;

        internal string quickBuildPath
        {
            get => m_quickBuildPath;
            set { m_quickBuildPath = value; OnPropertyChanged(); }
        }

        internal bool quickBuildUseProfiler
        {
            get => m_quickBuildUseProfiler;
            set { m_quickBuildUseProfiler = value; OnPropertyChanged(); }
        }

        #endregion
        #region Startup

        [Header("Startup")]
        [SerializeField] private Profile m_activeProfile;
        [SerializeField] private bool m_startupProcessOnCollectionPlay = true;

        /// <summary>Specifies the active profile in editor.</summary>
        public Profile activeProfile
        {
            get => m_activeProfile;
            set { m_activeProfile = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever startup process should run when pressing collection play button.</summary>
        public bool startupProcessOnCollectionPlay
        {
            get => m_startupProcessOnCollectionPlay;
            set { m_startupProcessOnCollectionPlay = value; OnPropertyChanged(); }
        }

        #endregion
        #region Logging

        [Header("Logging")]
        [SerializeField] private bool m_logImport;
        [SerializeField] private bool m_logTracking;
        [SerializeField] private bool m_logLoading;
        [SerializeField] private bool m_logStartup;
        [SerializeField] private bool m_logOperation;
        [SerializeField] private bool m_logBuildScenes;

        /// <summary>Specifies whatever ASM should log when a <see cref="ASMModel"/> is imported.</summary>
        public bool logImport
        {
            get => m_logImport;
            set { m_logImport = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever ASM should log when a scene is tracked after loaded.</summary>
        public bool logTracking
        {
            get => m_logTracking;
            set { m_logTracking = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever ASM should log when a scene is loaded.</summary>
        public bool logLoading
        {
            get => m_logLoading;
            set { m_logLoading = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever ASM should log during startup.</summary>
        public bool logStartup
        {
            get => m_logStartup;
            set { m_logStartup = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever ASM should log during scene operations.</summary>
        public bool logOperation
        {
            get => m_logOperation;
            set { m_logOperation = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever ASM should log when build scene list is updated.</summary>
        public bool logBuildScenes
        {
            get => m_logBuildScenes;
            set { m_logBuildScenes = value; OnPropertyChanged(); }
        }

        #endregion
        #region Netcode

#pragma warning disable CS0414
        [Header("Netcode")]
        [SerializeField] private bool m_displaySyncedIndicator = true;
#pragma warning restore CS0414

#if NETCODE

        /// <summary>Specifies that the 'synced' hierarchy indicator should be shown for synced scenes when using netcode.</summary>
        public bool displaySyncedIndicator
        {
            get => m_displaySyncedIndicator;
            set { m_displaySyncedIndicator = value; OnPropertyChanged(); }
        }

#endif

        #endregion
        #region Runtime

        [SerializeField] internal SceneCollection m_openCollection;
        [SerializeField] internal List<SceneCollection> m_additiveCollections = new();

        #endregion
        #region Collection overlay

        [SerializeField, FormerlySerializedAs("pinnedOverlayCollections")] private List<SceneCollection> m_pinnedOverlayCollections = new();

        /// <summary>Enumerates the pinned collections in the collection overlay.</summary>
        public IEnumerable<SceneCollection> pinnedOverlayCollections => m_pinnedOverlayCollections;

        /// <summary>Pins a collection to the collection overlay.</summary>
        public void PinCollectionToOverlay(SceneCollection collection, int? index = null)
        {
            m_pinnedOverlayCollections.Remove(collection);
            if (index.HasValue)
                m_pinnedOverlayCollections.Insert(Math.Clamp(index.Value, 0, m_pinnedOverlayCollections.Count - 1), collection);
            else
                m_pinnedOverlayCollections.Add(collection);
            Save();
            OnPropertyChanged(nameof(pinnedOverlayCollections));
        }

        /// <summary>Unpins a collection from the collection overlay.</summary>
        public void UnpinCollectionFromOverlay(SceneCollection collection)
        {
            m_pinnedOverlayCollections.Remove(collection);
            Save();
            OnPropertyChanged(nameof(pinnedOverlayCollections));
        }

        #endregion
        #region Always save scenes when entering play mode

        [SerializeField] private bool m_alwaysSaveScenesWhenEnteringPlayMode;

        /// <summary>Specifies whatever scenes should always auto save when entering play mode using ASM play button.</summary>
        public bool alwaysSaveScenesWhenEnteringPlayMode
        {
            get => m_alwaysSaveScenesWhenEnteringPlayMode;
            set { m_alwaysSaveScenesWhenEnteringPlayMode = value; OnPropertyChanged(); }
        }

        #endregion
        #region Indicators

        [SerializeField] private bool m_displayHierarchyIndicators = true;
        [SerializeField] private float m_hierarchyIndicatorsOffset = 0;

        [SerializeField] internal bool m_defaultSceneIndicator = true;
        [SerializeField] internal bool m_addressableIndicator = true;
        [SerializeField] internal bool m_netcodeIndicator = true;
        [SerializeField] internal bool m_collectionIndicator = true;
        [SerializeField] internal bool m_persistentIndicator = true;
        [SerializeField] internal bool m_bindingIndicator = true;
        [SerializeField] internal bool m_untrackedIndicator = true;
        [SerializeField] internal bool m_unimportedIndicator = true;
        [SerializeField] internal bool m_testIndicator = true;
        [SerializeField] internal bool m_lockIndicator = true;

        /// <summary>Specifies whatever the hierarchy indicators should be visible.</summary>
        public bool displayHierarchyIndicators
        {
            get => m_displayHierarchyIndicators;
            set { m_displayHierarchyIndicators = value; OnPropertyChanged(); }
        }

        /// <summary>Gets or sets the offset ASM will use for hierarchy indicators.</summary>
        public float hierarchyIndicatorsOffset
        {
            get => m_hierarchyIndicatorsOffset;
            set { m_hierarchyIndicatorsOffset = value; OnPropertyChanged(); }
        }

        #endregion
        #region Scene manager window

        #region Appearance

        [Header("Appearance")]
        [SerializeField] private bool m_displaySceneTooltips = true;
        [SerializeField] private bool m_displayProfileButton = true;
        [SerializeField] private bool m_displayCollectionPlayButton = true;
        [SerializeField] private bool m_displayCollectionOpenButton = true;
        [SerializeField] private bool m_displayCollectionAdditiveButton = true;
        [SerializeField] private bool m_displayIncludeInBuildToggle = false;
        [SerializeField] private int m_toolbarButtonCount = 1;
        [SerializeField] private float m_toolbarPlayButtonOffset = 0;
        [SerializeField] private SerializableDictionary<int, SceneCollection> m_toolbarButtonActions = new SerializableDictionary<int, SceneCollection>();
        [SerializeField] private SerializableDictionary<int, bool> m_toolbarButtonActions2 = new SerializableDictionary<int, bool>();

        [SerializeField] internal SerializableDictionary<string, bool> m_extendableButtons = new SerializableDictionary<string, bool>();

        [SerializeField] internal string[] m_extendableButtonOrder_Header = Array.Empty<string>();
        [SerializeField] internal string[] m_extendableButtonOrder_Collection = Array.Empty<string>();
        [SerializeField] internal string[] m_extendableButtonOrder_Scene = Array.Empty<string>();
        [SerializeField] internal string[] m_extendableButtonOrder_Footer = Array.Empty<string>();

        [SerializeField] internal int m_maxExtendableButtonsOnCollection = 2;
        [SerializeField] internal int m_maxExtendableButtonsOnScene = 2;

        /// <summary>Specifies whatever SceneField will display tooltips.</summary>
        public bool displaySceneTooltips
        {
            get => m_displaySceneTooltips;
            set { m_displaySceneTooltips = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever the include in build toggle should be displayed on collections.</summary>
        public bool displayIncludeInBuildToggle
        {
            get => m_displayIncludeInBuildToggle;
            set { m_displayIncludeInBuildToggle = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever the profile button should be visible.</summary>
        /// <remarks>Profile button will still become visible if no profile is active.</remarks>
        public bool displayProfileButton
        {
            get => m_displayProfileButton;
            set { m_displayProfileButton = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever collection play button should be visible.</summary>
        public bool displayCollectionPlayButton
        {
            get => m_displayCollectionPlayButton;
            set { m_displayCollectionPlayButton = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever collection open button should be visible.</summary>
        public bool displayCollectionOpenButton
        {
            get => m_displayCollectionOpenButton;
            set { m_displayCollectionOpenButton = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever collection open additive button should be visible.</summary>
        public bool displayCollectionAdditiveButton
        {
            get => m_displayCollectionAdditiveButton;
            set { m_displayCollectionAdditiveButton = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies how many buttons should be placed in toolbar.</summary>
        /// <remarks>Only has an effect if https://github.com/marijnz/unity-toolbar-extender is installed.</remarks>
        public int toolbarButtonCount
        {
            get => m_toolbarButtonCount;
            set { m_toolbarButtonCount = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies offset for toolbar play buttons.</summary>
        /// <remarks>Only has an effect if https://github.com/marijnz/unity-toolbar-extender is installed.</remarks>
        public float toolbarPlayButtonOffset
        {
            get => m_toolbarPlayButtonOffset;
            set { m_toolbarPlayButtonOffset = value; OnPropertyChanged(); }
        }

        /// <summary>Sets the scene collection to open for the specified toolbar button, if any.</summary>
        /// <remarks>Only has an effect if https://github.com/marijnz/unity-toolbar-extender is installed.</remarks>
        public void ToolbarAction(int i, out SceneCollection collection, out bool runStartupProcess)
        {
            collection = m_toolbarButtonActions?.GetValueOrDefault(i);
            runStartupProcess = m_toolbarButtonActions2?.GetValueOrDefault(i) ?? true;
        }

        /// <summary>Sets the scene collection to open for the specified toolbar button, if any.</summary>
        /// <remarks>Only has an effect if https://github.com/marijnz/unity-toolbar-extender is installed.</remarks>
        public void ToolbarAction(int i, SceneCollection collection, bool runStartupProcess)
        {

            if (m_toolbarButtonActions == null)
                m_toolbarButtonActions = new SerializableDictionary<int, SceneCollection>();

            if (m_toolbarButtonActions2 == null)
                m_toolbarButtonActions2 = new SerializableDictionary<int, bool>();

            m_toolbarButtonActions.Set(i, collection);
            m_toolbarButtonActions2.Set(i, runStartupProcess);

            this.Save();

        }

        #endregion

        [SerializeField] private bool m_alwaysDisplaySearch;

        [SerializeField] internal bool m_hideDocsNotification;
        [SerializeField] internal bool m_hideGitIgnoreNotification;
        [SerializeField] internal List<string> m_expandedCollections = new();
        [SerializeField] internal List<CollectionScenePair> m_selectedScenes = new();
        [SerializeField] internal List<SceneCollection> m_selectedCollections = new();

        /// <summary>The saved searches in scene manager window.</summary>
        [SerializeField] internal string[] savedSearches;

        /// <summary>Determines whatever search should always be displayed, and not just when actively searching.</summary>
        public bool alwaysDisplaySearch
        {
            get => m_alwaysDisplaySearch;
            set { m_alwaysDisplaySearch = value; OnPropertyChanged(); }
        }

        [SerializeField] internal SerializableDictionary<string, float> scrollPositions = new();

        #endregion
        #region Updates

        [SerializeField] private UpdateInterval m_updateInterval;
        [SerializeField] private string m_lastPatchWhenNotified;
        [SerializeField] private string m_cachedLatestVersion;
        [SerializeField] private string m_cachedPatchNotes;
        [SerializeField] private string m_lastUpdateCheck;

        public UpdateInterval updateInterval
        {
            get => m_updateInterval;
            set { m_updateInterval = value; OnPropertyChanged(); }
        }

        public string lastPatchWhenNotified
        {
            get => m_lastPatchWhenNotified;
            set { m_lastPatchWhenNotified = value; OnPropertyChanged(); }
        }

        public string cachedLatestVersion
        {
            get => m_cachedLatestVersion;
            set { m_cachedLatestVersion = value; OnPropertyChanged(); }
        }

        public string cachedPatchNotes
        {
            get => m_cachedPatchNotes;
            set { m_cachedPatchNotes = value; OnPropertyChanged(); }
        }

        public string lastUpdateCheck
        {
            get => m_lastUpdateCheck;
            set { m_lastUpdateCheck = value; OnPropertyChanged(); }
        }

        #endregion

        [Header("Misc")]
        [SerializeField] private bool m_openCollectionOnSceneAssetOpen;

        /// <summary>When <see langword="true"/>: opens the first found collection that a scene is contained in when opening an SceneAsset in editor.</summary>
        public bool openCollectionOnSceneAssetOpen
        {
            get => m_openCollectionOnSceneAssetOpen;
            set { m_openCollectionOnSceneAssetOpen = value; OnPropertyChanged(); }
        }

    }

}
#endif
