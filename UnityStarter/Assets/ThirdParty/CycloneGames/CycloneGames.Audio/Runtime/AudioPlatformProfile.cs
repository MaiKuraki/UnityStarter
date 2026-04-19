// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Platform-specific runtime playback policy tuning.
    /// Complements AudioPoolConfig and AudioVoicePolicyProfile with pre-play decisions such as
    /// repeat-trigger throttling, audibility culling, category budget scaling, and focus behavior overrides.
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/Audio/Audio Platform Profile")]
    public sealed class AudioPlatformProfile : ScriptableObject
    {
        [Serializable]
        public struct CategoryRuntimeSettings
        {
            [Range(0.5f, 2f)]
            public float voiceBudgetMultiplier;

            [Range(0f, 0.25f)]
            public float repeatTriggerWindowSeconds;
        }

        [Serializable]
        public struct PlatformRuntimeSettings
        {
            public bool overrideFocusMode;
            public AudioFocusMode focusMode;

            public bool enableRepeatTriggerThrottling;
            public bool throttlePerEmitter;
            public bool throttleScheduledPlayback;

            public bool enableAudibilityCulling;
            public bool cullLoopingEvents;
            public bool cull2DEvents;
            public bool cullScheduledPlayback;

            [Range(0f, 20f)]
            public float distanceCullPadding;

            [Range(0f, 0.25f)]
            public float minEstimatedAudibility;

            public CategoryRuntimeSettings criticalUI;
            public CategoryRuntimeSettings gameplaySFX;
            public CategoryRuntimeSettings voice;
            public CategoryRuntimeSettings ambient;
            public CategoryRuntimeSettings music;

            public float GetVoiceBudgetMultiplier(AudioEventCategory category)
            {
                return Mathf.Max(0.5f, GetCategorySettings(category).voiceBudgetMultiplier);
            }

            public float GetRepeatTriggerWindow(AudioEventCategory category)
            {
                if (!enableRepeatTriggerThrottling)
                    return 0f;

                return Mathf.Max(0f, GetCategorySettings(category).repeatTriggerWindowSeconds);
            }

            public float GetMaxRepeatTriggerWindow()
            {
                float maxWindow = 0f;
                maxWindow = Mathf.Max(maxWindow, criticalUI.repeatTriggerWindowSeconds);
                maxWindow = Mathf.Max(maxWindow, gameplaySFX.repeatTriggerWindowSeconds);
                maxWindow = Mathf.Max(maxWindow, voice.repeatTriggerWindowSeconds);
                maxWindow = Mathf.Max(maxWindow, ambient.repeatTriggerWindowSeconds);
                maxWindow = Mathf.Max(maxWindow, music.repeatTriggerWindowSeconds);
                return maxWindow;
            }

            private CategoryRuntimeSettings GetCategorySettings(AudioEventCategory category)
            {
                switch (category)
                {
                    case AudioEventCategory.CriticalUI:
                        return criticalUI;
                    case AudioEventCategory.Voice:
                        return voice;
                    case AudioEventCategory.Ambient:
                        return ambient;
                    case AudioEventCategory.Music:
                        return music;
                    default:
                        return gameplaySFX;
                }
            }
        }

        [Header("Desktop (Windows / Linux / macOS)")]
        [SerializeField] private PlatformRuntimeSettings desktop = CreateDesktopDefaults();

        [Header("Mobile (Android / iOS)")]
        [SerializeField] private PlatformRuntimeSettings mobile = CreateMobileDefaults();

        [Header("WebGL")]
        [SerializeField] private PlatformRuntimeSettings webGL = CreateWebGLDefaults();

        [Header("Console / Other")]
        [SerializeField] private PlatformRuntimeSettings console = CreateConsoleDefaults();

        public PlatformRuntimeSettings GetSettingsForCurrentPlatform()
        {
#if UNITY_WEBGL
            return webGL;
#elif UNITY_ANDROID || UNITY_IOS
            return mobile;
#elif UNITY_PS5 || UNITY_XBOXONE || UNITY_GAMECORE || UNITY_SWITCH
            return console;
#else
            RuntimePlatform platform = Application.platform;
            if (platform == RuntimePlatform.WebGLPlayer)
                return webGL;
            if (Application.isMobilePlatform)
                return mobile;
            return desktop;
#endif
        }

        public string GetCurrentPlatformLabel()
        {
#if UNITY_WEBGL
            return "WebGL";
#elif UNITY_ANDROID || UNITY_IOS
            return "Mobile";
#elif UNITY_PS5 || UNITY_XBOXONE || UNITY_GAMECORE || UNITY_SWITCH
            return "Console";
#else
            RuntimePlatform platform = Application.platform;
            if (platform == RuntimePlatform.WebGLPlayer)
                return "WebGL";
            if (Application.isMobilePlatform)
                return "Mobile";
            return "Desktop";
#endif
        }

        public static PlatformRuntimeSettings GetFallbackSettings()
        {
            return CreateDesktopDefaults();
        }

        private static PlatformRuntimeSettings CreateDesktopDefaults()
        {
            return new PlatformRuntimeSettings
            {
                overrideFocusMode = false,
                focusMode = AudioFocusMode.All,
                enableRepeatTriggerThrottling = true,
                throttlePerEmitter = true,
                throttleScheduledPlayback = false,
                enableAudibilityCulling = true,
                cullLoopingEvents = false,
                cull2DEvents = false,
                cullScheduledPlayback = false,
                distanceCullPadding = 1.5f,
                minEstimatedAudibility = 0.015f,
                criticalUI = CreateCategorySettings(1.15f, 0f),
                gameplaySFX = CreateCategorySettings(1f, 0.01f),
                voice = CreateCategorySettings(1f, 0.02f),
                ambient = CreateCategorySettings(0.85f, 0.04f),
                music = CreateCategorySettings(0.75f, 0f)
            };
        }

        private static PlatformRuntimeSettings CreateMobileDefaults()
        {
            return new PlatformRuntimeSettings
            {
                overrideFocusMode = true,
                focusMode = AudioFocusMode.All,
                enableRepeatTriggerThrottling = true,
                throttlePerEmitter = true,
                throttleScheduledPlayback = false,
                enableAudibilityCulling = true,
                cullLoopingEvents = true,
                cull2DEvents = false,
                cullScheduledPlayback = false,
                distanceCullPadding = 0.75f,
                minEstimatedAudibility = 0.025f,
                criticalUI = CreateCategorySettings(1.2f, 0f),
                gameplaySFX = CreateCategorySettings(0.95f, 0.016f),
                voice = CreateCategorySettings(1f, 0.03f),
                ambient = CreateCategorySettings(0.7f, 0.06f),
                music = CreateCategorySettings(0.75f, 0f)
            };
        }

        private static PlatformRuntimeSettings CreateWebGLDefaults()
        {
            return new PlatformRuntimeSettings
            {
                overrideFocusMode = true,
                focusMode = AudioFocusMode.AutoPauseOnly,
                enableRepeatTriggerThrottling = true,
                throttlePerEmitter = true,
                throttleScheduledPlayback = false,
                enableAudibilityCulling = true,
                cullLoopingEvents = true,
                cull2DEvents = false,
                cullScheduledPlayback = false,
                distanceCullPadding = 0.5f,
                minEstimatedAudibility = 0.03f,
                criticalUI = CreateCategorySettings(1.2f, 0f),
                gameplaySFX = CreateCategorySettings(0.85f, 0.02f),
                voice = CreateCategorySettings(0.9f, 0.03f),
                ambient = CreateCategorySettings(0.65f, 0.075f),
                music = CreateCategorySettings(0.7f, 0f)
            };
        }

        private static PlatformRuntimeSettings CreateConsoleDefaults()
        {
            return new PlatformRuntimeSettings
            {
                overrideFocusMode = true,
                focusMode = AudioFocusMode.All,
                enableRepeatTriggerThrottling = true,
                throttlePerEmitter = true,
                throttleScheduledPlayback = false,
                enableAudibilityCulling = true,
                cullLoopingEvents = false,
                cull2DEvents = false,
                cullScheduledPlayback = false,
                distanceCullPadding = 1.25f,
                minEstimatedAudibility = 0.015f,
                criticalUI = CreateCategorySettings(1.15f, 0f),
                gameplaySFX = CreateCategorySettings(1.05f, 0.01f),
                voice = CreateCategorySettings(1.05f, 0.02f),
                ambient = CreateCategorySettings(0.8f, 0.05f),
                music = CreateCategorySettings(0.8f, 0f)
            };
        }

        private static CategoryRuntimeSettings CreateCategorySettings(float voiceBudgetMultiplier, float repeatTriggerWindowSeconds)
        {
            return new CategoryRuntimeSettings
            {
                voiceBudgetMultiplier = voiceBudgetMultiplier,
                repeatTriggerWindowSeconds = repeatTriggerWindowSeconds
            };
        }

        #region Auto-Discovery

        private static AudioPlatformProfile cachedConfig;
        private static bool hasSearchedForConfig;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void ResetCacheOnDomainReload()
        {
            ClearCache();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCacheOnPlayModeEnter()
        {
            ClearCache();
        }

        public static AudioPlatformProfile FindConfig()
        {
            if (hasSearchedForConfig && cachedConfig != null) return cachedConfig;

            if (hasSearchedForConfig && cachedConfig == null)
                hasSearchedForConfig = false;

            hasSearchedForConfig = true;

            cachedConfig = Resources.Load<AudioPlatformProfile>("AudioPlatformProfile");
            if (cachedConfig != null) return cachedConfig;

            cachedConfig = Resources.Load<AudioPlatformProfile>("Audio Platform Profile");
            if (cachedConfig != null) return cachedConfig;

            AudioPlatformProfile[] allConfigs = Resources.LoadAll<AudioPlatformProfile>(string.Empty);
            if (allConfigs != null && allConfigs.Length > 0)
            {
                cachedConfig = allConfigs[0];
                if (allConfigs.Length > 1)
                    Debug.LogWarning($"AudioPlatformProfile: Found {allConfigs.Length} configs in Resources. Using first.");
                return cachedConfig;
            }

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:AudioPlatformProfile");
            if (guids.Length > 0)
            {
                if (guids.Length > 1)
                    Debug.LogWarning($"AudioPlatformProfile: Found {guids.Length} configs in project. Only one should exist. Using first found.");
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                cachedConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioPlatformProfile>(path);
            }
#endif
            return cachedConfig;
        }

        public static void SetConfig(AudioPlatformProfile config)
        {
            cachedConfig = config;
            hasSearchedForConfig = true;
        }

        public static void ClearCache()
        {
            cachedConfig = null;
            hasSearchedForConfig = false;
        }

        #endregion
    }
}
