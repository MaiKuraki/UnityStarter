// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Project-level defaults for AudioEvent voice policy behavior.
    /// This keeps per-event authoring lightweight by letting Category drive a shared template.
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/Audio/Audio Voice Policy Profile")]
    public sealed class AudioVoicePolicyProfile : ScriptableObject
    {
        [Serializable]
        public struct CategoryPolicySettings
        {
            [Range(0.25f, 3f)]
            public float stealResistance;

            [Range(0.25f, 3f)]
            public float voiceBudgetWeight;

            public bool allowVoiceSteal;
            public bool allowDistanceBasedSteal;
            public bool protectScheduledPlayback;

            public AudioEventVoicePolicy ToPolicy()
            {
                return new AudioEventVoicePolicy(
                    stealResistance,
                    voiceBudgetWeight,
                    allowVoiceSteal,
                    allowDistanceBasedSteal,
                    protectScheduledPlayback);
            }
        }

        [Header("Category Defaults")]
        [SerializeField] private CategoryPolicySettings criticalUI = CreateDefaultCriticalUI();
        [SerializeField] private CategoryPolicySettings gameplaySFX = CreateDefaultGameplaySFX();
        [SerializeField] private CategoryPolicySettings voice = CreateDefaultVoice();
        [SerializeField] private CategoryPolicySettings ambient = CreateDefaultAmbient();
        [SerializeField] private CategoryPolicySettings music = CreateDefaultMusic();

        public AudioEventVoicePolicy GetPolicy(AudioEventCategory category)
        {
            switch (category)
            {
                case AudioEventCategory.CriticalUI:
                    return criticalUI.ToPolicy();
                case AudioEventCategory.Voice:
                    return voice.ToPolicy();
                case AudioEventCategory.Ambient:
                    return ambient.ToPolicy();
                case AudioEventCategory.Music:
                    return music.ToPolicy();
                default:
                    return gameplaySFX.ToPolicy();
            }
        }

        public static AudioEventVoicePolicy GetFallbackPolicy(AudioEventCategory category)
        {
            switch (category)
            {
                case AudioEventCategory.CriticalUI:
                    return CreateDefaultCriticalUI().ToPolicy();
                case AudioEventCategory.Voice:
                    return CreateDefaultVoice().ToPolicy();
                case AudioEventCategory.Ambient:
                    return CreateDefaultAmbient().ToPolicy();
                case AudioEventCategory.Music:
                    return CreateDefaultMusic().ToPolicy();
                default:
                    return CreateDefaultGameplaySFX().ToPolicy();
            }
        }

        private static CategoryPolicySettings CreateDefaultCriticalUI()
        {
            return new CategoryPolicySettings
            {
                stealResistance = 2.2f,
                voiceBudgetWeight = 1.5f,
                allowVoiceSteal = false,
                allowDistanceBasedSteal = false,
                protectScheduledPlayback = true
            };
        }

        private static CategoryPolicySettings CreateDefaultGameplaySFX()
        {
            return new CategoryPolicySettings
            {
                stealResistance = 1f,
                voiceBudgetWeight = 1f,
                allowVoiceSteal = true,
                allowDistanceBasedSteal = true,
                protectScheduledPlayback = true
            };
        }

        private static CategoryPolicySettings CreateDefaultVoice()
        {
            return new CategoryPolicySettings
            {
                stealResistance = 1.5f,
                voiceBudgetWeight = 1.35f,
                allowVoiceSteal = true,
                allowDistanceBasedSteal = false,
                protectScheduledPlayback = true
            };
        }

        private static CategoryPolicySettings CreateDefaultAmbient()
        {
            return new CategoryPolicySettings
            {
                stealResistance = 0.7f,
                voiceBudgetWeight = 0.7f,
                allowVoiceSteal = true,
                allowDistanceBasedSteal = true,
                protectScheduledPlayback = false
            };
        }

        private static CategoryPolicySettings CreateDefaultMusic()
        {
            return new CategoryPolicySettings
            {
                stealResistance = 2.6f,
                voiceBudgetWeight = 1.8f,
                allowVoiceSteal = false,
                allowDistanceBasedSteal = false,
                protectScheduledPlayback = true
            };
        }

        #region Auto-Discovery

        private static AudioVoicePolicyProfile cachedConfig;
        private static bool hasSearchedForConfig;
        private static bool isSearchingForConfig;
        private static readonly Func<AudioVoicePolicyProfile> DiscoverConfigCallback =
            AudioConfigDiscovery.DiscoverAudioVoicePolicyProfile;

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

        public static AudioVoicePolicyProfile FindConfig()
        {
            return AudioConfigCache.GetOrDiscover(
                ref hasSearchedForConfig,
                ref isSearchingForConfig,
                ref cachedConfig,
                DiscoverConfigCallback);
        }

        public static void SetConfig(AudioVoicePolicyProfile config)
        {
            cachedConfig = config;
            hasSearchedForConfig = true;
            isSearchingForConfig = false;
        }

        public static void ClearCache()
        {
            cachedConfig = null;
            hasSearchedForConfig = false;
            isSearchingForConfig = false;
        }

        #endregion
    }
}
