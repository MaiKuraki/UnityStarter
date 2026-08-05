// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Owns the compatibility-only automatic discovery policy for Audio configuration assets.
    /// Product configuration should prefer serialized overrides or the corresponding SetConfig method.
    /// </summary>
    internal static class AudioConfigDiscovery
    {
        private static readonly LogChannel Log = AudioRuntimeLog.Channel;

        internal static AudioPoolConfig DiscoverAudioPoolConfig()
        {
            return Discover<AudioPoolConfig>(nameof(AudioPoolConfig));
        }

        internal static AudioPlatformProfile DiscoverAudioPlatformProfile()
        {
            return Discover<AudioPlatformProfile>(nameof(AudioPlatformProfile));
        }

        internal static AudioVoicePolicyProfile DiscoverAudioVoicePolicyProfile()
        {
            return Discover<AudioVoicePolicyProfile>(nameof(AudioVoicePolicyProfile));
        }

        internal static AudioDuckingProfile DiscoverAudioDuckingProfile()
        {
            return Discover<AudioDuckingProfile>(nameof(AudioDuckingProfile));
        }

        private static T Discover<T>(string canonicalResourcePath)
            where T : UnityEngine.Object
        {
            T config = Resources.Load<T>(canonicalResourcePath);
            if (config != null)
                return config;

            T[] allConfigs = Resources.LoadAll<T>(string.Empty);
            if (allConfigs != null && allConfigs.Length > 0)
            {
                if (allConfigs.Length > 1)
                {
                    Log.Warning(
                        $"{typeof(T).Name}: Found {allConfigs.Length} configs in Resources. Using first.");
                }

                return allConfigs[0];
            }

#if UNITY_EDITOR
            string configTypeName = typeof(T).Name;
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{configTypeName}");
            if (guids.Length > 0)
            {
                if (guids.Length > 1)
                {
                    Log.Warning(
                        $"{configTypeName}: Found {guids.Length} configs in project. Only one should exist. Using first found.");
                }

                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            }
#endif

            return null;
        }
    }
}
