// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Owns the compatibility-only automatic discovery policy for Audio configuration assets.
    /// In the Editor, discovery falls back to an AssetDatabase type search so newly created
    /// projects work without manual wiring. Player builds never scan or load automatically:
    /// assign the serialized overrides on <see cref="AudioManager"/> or call the corresponding
    /// SetConfig method (for example after loading the asset through CycloneGames.AssetManagement)
    /// before the first configuration request.
    /// </summary>
    internal static class AudioConfigDiscovery
    {
        private static readonly LogChannel Log = AudioRuntimeLog.Channel;

        internal static AudioPoolConfig DiscoverAudioPoolConfig()
        {
            return Discover<AudioPoolConfig>();
        }

        internal static AudioPlatformProfile DiscoverAudioPlatformProfile()
        {
            return Discover<AudioPlatformProfile>();
        }

        internal static AudioVoicePolicyProfile DiscoverAudioVoicePolicyProfile()
        {
            return Discover<AudioVoicePolicyProfile>();
        }

        internal static AudioDuckingProfile DiscoverAudioDuckingProfile()
        {
            return Discover<AudioDuckingProfile>();
        }

        private static T Discover<T>()
            where T : UnityEngine.Object
        {
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

            // Player builds rely on explicit wiring: serialized overrides or SetConfig.
            return null;
        }
    }
}
