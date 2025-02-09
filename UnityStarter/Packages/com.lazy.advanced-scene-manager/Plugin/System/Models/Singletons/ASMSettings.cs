using UnityEngine;
using AdvancedSceneManager.Utility;
using System.ComponentModel;
using AdvancedSceneManager.Models.Enums;
using AdvancedSceneManager.Models.Utility;
using System.Collections.Generic;
using System.Collections;
using System;
using AdvancedSceneManager.Models.Helpers;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.Models.Internal;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
using AdvancedSceneManager.Editor.Utility;
#endif

namespace AdvancedSceneManager.Models
{

    /// <summary>Contains the core of ASM assets. Contains <see cref="projectSettings"/> and <see cref="assets"/></summary>
    /// <remarks>Only available in editor.</remarks>
    [ASMFilePath("ProjectSettings/AdvancedSceneManager.asset")]
    public class ASMSettings : ASMScriptableSingleton<ASMSettings>, INotifyPropertyChanged, IProjectSettings
    {

#if UNITY_EDITOR

        public override void Save()
        {
            Update();
            Assets.Cleanup();
            base.Save();
        }

        void Update()
        {
            //If any update steps are needed, add them here
        }

#endif

        #region Properties

        [Serializable]
        public class CustomDataDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
        {

            [SerializeField] private SerializableDictionary<TKey, TValue> dict = new();

            public TValue this[TKey key]
            {
                get => dict[key];
                set => dict[key] = value;
            }

            /// <summary>Gets custom data.</summary>
            public bool Get(TKey key, out TValue value) =>
               dict.TryGetValue(key, out value);

            /// <summary>Gets custom data.</summary>
            public TValue Get(TKey key) =>
                dict.ContainsKey(key)
                ? dict[key]
                : default;

            /// <summary>Sets custom data.</summary>
            public void Set(TKey key, TValue value)
            {
                _ = dict.Set(key, value);
                SceneManager.settings.project.Save();
            }

            /// <summary>Clears custom data.</summary>
            public void Clear(TKey key)
            {
                if (dict.Remove(key))
                    SceneManager.settings.project.Save();
            }

            /// <summary>Gets if the key exists.</summary>
            public bool ContainsKey(TKey key) =>
                dict.ContainsKey(key);

            public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => dict.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => dict.GetEnumerator();

        }

        [Serializable]
        public class CustomData : CustomDataDictionary<string, string>
        { }

        [Serializable]
        public class SceneData : CustomDataDictionary<string, CustomData>
        { }

        [SerializeField] internal SceneData sceneData = new();

        #region AssetRef

        [Header("Assets")]
        [SerializeField] internal List<Profile> m_profiles = new();
        [SerializeField] internal List<Scene> m_scenes = new();
        [SerializeField] internal List<SceneCollection> m_collections = new();
        [SerializeField] internal List<SceneCollectionTemplate> m_collectionTemplates = new();

        [SerializeField] internal ASMSceneHelper m_sceneHelper;
        [SerializeField] internal DefaultScenes m_defaultScenes;

        #endregion
        #region Profiles

        [Header("Profiles")]
        [SerializeField] internal Profile m_defaultProfile;
        [SerializeField] internal Profile m_forceProfile;

        [SerializeField] internal Profile m_buildProfile;

        /// <summary>The profile to use when none is set.</summary>
        public Profile defaultProfile
        {
            get => m_defaultProfile;
            set { m_defaultProfile = value; OnPropertyChanged(); }
        }

        /// <summary>The profile to force everyone in this project to use.</summary>
        public Profile forceProfile
        {
            get => m_forceProfile;
            set { m_forceProfile = value; OnPropertyChanged(); }
        }

        /// <summary>The profile to use during build.</summary>
        public Profile buildProfile => m_buildProfile;

#if UNITY_EDITOR

        /// <summary>Sets the build profile.</summary>
        public void SetBuildProfile(Profile profile)
        {
            if (m_buildProfile != profile)
            {
                m_buildProfile = profile;
                SaveNow();
            }
        }

#endif

        #endregion
        #region Spam check

        [Header("Spam Check")]
        [SerializeField] private bool m_checkForDuplicateSceneOperations = true;
        [SerializeField] private bool m_preventSpammingEventMethods = true;
        [SerializeField] private float m_spamCheckCooldown = 0.5f;

        /// <summary>By default, ASM checks for duplicate scene operations, since this is usually caused by mistake, but this will disable that.</summary>
        public bool checkForDuplicateSceneOperations
        {
            get => m_checkForDuplicateSceneOperations;
            set { m_checkForDuplicateSceneOperations = value; OnPropertyChanged(); }
        }

        /// <summary>By default, ASM will prevent spam calling event methods (i.e. calling Scene.Open() from a button press), but this will disable that.</summary>
        public bool preventSpammingEventMethods
        {
            get => m_preventSpammingEventMethods;
            set { m_preventSpammingEventMethods = value; OnPropertyChanged(); }
        }

        /// <summary>Sets the default cooldown for <see cref="SpamCheck"/>.</summary>
        public float spamCheckCooldown
        {
            get => m_spamCheckCooldown;
            set { m_spamCheckCooldown = value; OnPropertyChanged(); }
        }

        #endregion
        #region Netcode

#if NETCODE

        [Header("Netcode")]
        [SerializeField] private bool m_isNetcodeValidationEnabled = true;

        /// <summary>Specifies whatever ASM should validate scenes in netcode.</summary>
        public bool isNetcodeValidationEnabled
        {
            get => m_isNetcodeValidationEnabled;
            set { m_isNetcodeValidationEnabled = value; OnPropertyChanged(); }
        }

#endif

        #endregion
        #region Scenes

        [Header("Scenes")]
        [SerializeField] internal Blocklist m_blacklist = new();
        [SerializeField] internal Blocklist m_whitelist = new();

        [SerializeField] private bool m_enableCrossSceneReferences;
        [SerializeField] private bool m_enableGUIDReferences = true;
        [SerializeField] public SceneImportOption m_sceneImportOption = SceneImportOption.Manual;
        [SerializeField] private bool m_allowExcludingCollectionsFromBuild = false;
        [SerializeField] private bool m_reverseUnloadOrderOnCollectionClose = true;
        [SerializeField] private bool m_autoUpdateBuildScenes = true;

        /// <summary>Gets or sets whatever cross-scene references should be enabled.</summary>
        /// <remarks>Experimental.</remarks>
        public bool enableCrossSceneReferences
        {
            get => m_enableCrossSceneReferences;
            set { m_enableCrossSceneReferences = value; OnPropertyChanged(); }
        }

        /// <summary>Gets or sets whatever GUID references should be enabled.</summary>
        public bool enableGUIDReferences
        {
            get => m_enableGUIDReferences;
            set { m_enableGUIDReferences = value; OnPropertyChanged(); }
        }

        /// <summary>Gets or sets when to automatically import scenes.</summary>
        public SceneImportOption sceneImportOption
        {
            get => m_sceneImportOption;
            set { m_sceneImportOption = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever collections can be excluded from build.</summary>
        /// <remarks>When <see langword="true"/>, a toggle will be shown in scene manager window.</remarks>
        public bool allowExcludingCollectionsFromBuild
        {
            get => m_allowExcludingCollectionsFromBuild;
            set { m_allowExcludingCollectionsFromBuild = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever collections should unload scenes in the reverse order.</summary>
        public bool reverseUnloadOrderOnCollectionClose
        {
            get => m_reverseUnloadOrderOnCollectionClose;
            set { m_reverseUnloadOrderOnCollectionClose = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever build scene list should be automatically updated.</summary>
        public bool autoUpdateBuildScenes
        {
            get => m_autoUpdateBuildScenes;
            set { m_autoUpdateBuildScenes = value; OnPropertyChanged(); }
        }

        #endregion
        #region ASM info

        [Header("ASM")]
        [SerializeField] private string m_assetPath = "Assets/Settings/AdvancedSceneManager";
        [SerializeField] private CustomData m_customData = new();

        /// <summary>Specifies the path where profiles and imported scenes should be generated to.</summary>
        public string assetPath
        {
            get => m_assetPath;
            set { m_assetPath = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies custom data.</summary>
        public CustomData customData => m_customData;

        #endregion
        #region Splash screen color

        [Header("Splash screen")]
        [SerializeField] private Color m_unitySplashScreenColor = Color.black;

        /// <summary>This is the color of the unity splash screen, used to make the transition from unity splash screen to ASM smooth, this is set before building. <see cref="Color.black"/> is used when the unity splash screen is disabled.</summary>
        public Color buildUnitySplashScreenColor => m_unitySplashScreenColor;

#if UNITY_EDITOR

        [InitializeInEditorMethod]
        static void OnLoad() =>
            SceneManager.OnInitialized(() =>
                BuildUtility.preBuild += (e) => SceneManager.settings.project.UpdateSplashScreenColor());

        void UpdateSplashScreenColor()
        {

            var color =
                PlayerSettings.SplashScreen.show
                ? PlayerSettings.SplashScreen.backgroundColor
                : Color.black;

            if (color != m_unitySplashScreenColor)
            {
                m_unitySplashScreenColor = color;
                SaveNow();
            }

        }

#endif

        #endregion
        #region Locking

        [Header("Locking")]
        [SerializeField] private bool m_allowSceneLocking = true;
        [SerializeField] private bool m_allowCollectionLocking = true;

        /// <summary>Specifies whatever asm will allow locking scenes.</summary>
        public bool allowSceneLocking
        {
            get => m_allowSceneLocking;
            set { m_allowSceneLocking = value; OnPropertyChanged(); }
        }

        /// <summary>Specifies whatever asm will allow locking collections.</summary>
        public bool allowCollectionLocking
        {
            get => m_allowCollectionLocking;
            set { m_allowCollectionLocking = value; OnPropertyChanged(); }
        }

        #endregion
        #region Runtime

        [Header("Runtime")]
        [SerializeField] private SceneCollection m_openCollection;
        [SerializeField] private List<SceneCollection> m_additiveCollections = new();

        internal SceneCollection openCollection
        {
            get
            {
#if UNITY_EDITOR
                return SceneManager.settings.user.m_openCollection;
#else
                return m_openCollection;
#endif
            }
            set
            {
#if UNITY_EDITOR             

                if (!m_openCollection)
                {
                    m_openCollection = null;
                    Save();
                }

                SceneManager.settings.user.m_openCollection = value;
                SceneManager.settings.user.Save();

#else
                m_openCollection = value; 
                Save(); 
#endif
                OnPropertyChanged();
            }
        }

        internal IEnumerable<SceneCollection> openAdditiveCollections
        {
            get
            {
#if UNITY_EDITOR
                return SceneManager.settings.user.m_additiveCollections;
#else
                return m_additiveCollections;
#endif
            }
        }

        internal void AddAdditiveCollection(SceneCollection collection)
        {
#if UNITY_EDITOR
            SceneManager.settings.user.m_additiveCollections.Add(collection);
#else
            m_additiveCollections.Add(collection);
#endif
        }

        internal void RemoveAdditiveCollection(SceneCollection collection)
        {
#if UNITY_EDITOR
            SceneManager.settings.user.m_additiveCollections.Remove(collection);
#else
            m_additiveCollections.Remove(collection);
#endif
        }

        internal void ClearAdditiveCollections()
        {
#if UNITY_EDITOR
            SceneManager.settings.user.m_additiveCollections.Clear();
#else
            m_additiveCollections.Clear();
#endif
        }

        #endregion
        #region Fade scene

        [SerializeField] private Scene m_fadeScene;

        /// <summary>Specifies the scene to use for certain methods, i.e. <see cref="LoadingScreenUtility.FadeOut(float, Color?, Action{float})"/>.</summary>
        public Scene fadeScene
        {
            get => m_fadeScene;
            set { m_fadeScene = value; OnPropertyChanged(); }
        }

        #endregion
        #region Allow loading scenes in parallel

        [SerializeField] private bool m_allowLoadingScenesInParallel;

        /// <summary>Specifies if scenes should be loaded in parallel, rather than sequentially.</summary>
        public bool allowLoadingScenesInParallel
        {
            get => m_allowLoadingScenesInParallel;
            set { m_allowLoadingScenesInParallel = value; OnPropertyChanged(); }
        }

        #endregion
        #region Updates

        [SerializeField] private bool m_allowUpdateCheck = true;

        public bool allowUpdateCheck
        {
            get => m_allowUpdateCheck;
            set { m_allowUpdateCheck = value; OnPropertyChanged(); }
        }

        #endregion

        #endregion
        #region Initialize

        static readonly List<Action> callbacks = new();
        /// <summary>Runs the callback when ASMSettings has initialized.</summary>
        public static void OnInitialized(Action action)
        {

            if (isInitialized)
                action.Invoke();
            else
                callbacks.Add(action);

            Initialize();

        }

        void OnEnable()
        {

            isInitialized = true;

#if UNITY_EDITOR
            EditorApplication.delayCall += CheckDefaultAssets;
#endif
            foreach (var callback in callbacks)
                callback.Invoke();
            callbacks.Clear();


        }

        internal static bool isInitialized;
        static bool hasInitialized;
        internal static void Initialize()
        {

            if (hasInitialized)
                return;
            hasInitialized = true;

#if UNITY_EDITOR
            EditorApplication.delayCall += () => _ = instance;
#else
            _ = instance;
#endif

        }

        string DefaultAssetsFolder => "Packages/com.lazy.advanced-scene-manager/Plugin/Assets";

        void CheckDefaultAssets()
        {

#if UNITY_EDITOR

            if (UnityEditor.MPE.ProcessService.level == UnityEditor.MPE.ProcessLevel.Secondary)
                return;

            CheckFallbackScene();
            if (CheckDefaultAsset(ref m_sceneHelper) | CheckDefaultAsset(ref m_defaultScenes) || TrackASMScenes())
                Save();

#endif

        }

        bool CheckDefaultAsset<T>(ref T obj) where T : ScriptableObject
        {

            var needsSave = false;
            var path = $"{DefaultAssetsFolder}/{typeof(T).Name}.asset";

#if UNITY_EDITOR

            if (!obj)
            {
                obj = AssetDatabase.LoadAssetAtPath<T>(path);
                needsSave = true;
            }

#if ASM_DEV
            if (!obj)
            {
                AssetDatabase.CreateAsset(CreateInstance<T>(), path);
                obj = AssetDatabase.LoadAssetAtPath<T>(path);
                needsSave = true;
            }
#endif

#endif

            if (!obj)
                Debug.LogError($"Could not find {path}. You may have to re-install ASM.");

            return needsSave;

        }

        void CheckFallbackScene()
        {

#if UNITY_EDITOR

            var path = $"{DefaultAssetsFolder}/{FallbackSceneUtility.Name}.unity";
            var obj = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

#if ASM_DEV
            if (!obj)
                obj = SceneUtility.Create(path);
#endif

            if (!obj)
                Debug.LogError($"Could not find {path}. You may have to re-install ASM.");

#endif

        }

        bool TrackASMScenes()
        {

            bool hasChanges = false;

#if UNITY_EDITOR

            var scenesToImport = m_defaultScenes.Enumerate().Where(s => !s.isImported);
            if (scenesToImport.Any())
            {
                Assets.Add(scenesToImport, false);
                hasChanges = true;
            }

            if (SceneManager.assets.defaults.fadeScene && !SceneManager.settings.project.fadeScene)
            {
                SceneManager.settings.project.fadeScene = SceneManager.assets.defaults.fadeScene;
                hasChanges = true;

            }

#endif

            return hasChanges;

        }

        #endregion

    }

}
