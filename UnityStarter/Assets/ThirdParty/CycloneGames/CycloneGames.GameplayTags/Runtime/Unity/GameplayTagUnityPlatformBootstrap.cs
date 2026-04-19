#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CycloneGames.GameplayTags.Runtime
{
    internal static class GameplayTagUnityPlatformBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            GameplayTagRuntimePlatform.LogWarning = message => Debug.LogWarning(message);
            GameplayTagRuntimePlatform.LogError = message => Debug.LogError(message);
            GameplayTagRuntimePlatform.IsRuntimePlaying = () => Application.isPlaying;
            GameplayTagRuntimePlatform.LoadBuildTagData = LoadBuildTagData;
            GameplayTagRuntimePlatform.GetProjectTagSettingsDirectory =
                () => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProjectSettings", "GameplayTags"));
#if UNITY_EDITOR
            GameplayTagRuntimePlatform.EnumerateProjectTagSources = EnumerateProjectTagSources;
#else
            GameplayTagRuntimePlatform.EnumerateProjectTagSources = static () => Array.Empty<IGameplayTagSource>();
#endif
        }

        private static byte[] LoadBuildTagData()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameplayTags");
            return asset != null ? asset.bytes : null;
        }

#if UNITY_EDITOR
        private static IEnumerable<IGameplayTagSource> EnumerateProjectTagSources()
        {
            foreach (FileGameplayTagSource source in FileGameplayTagSource.GetAllFileSources())
            {
                yield return source;
            }
        }
#endif
    }
}
#endif
