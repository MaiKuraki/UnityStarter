#if UNITY_5_3_OR_NEWER
using System.Runtime.CompilerServices;
using UnityEngine;
using CycloneGames.GameplayTags.Core;

[assembly: InternalsVisibleTo("CycloneGames.GameplayTags.Unity.Editor")]

namespace CycloneGames.GameplayTags.Unity.Runtime
{
    internal static class GameplayTagUnityPlatformBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            GameplayTagManager.ResetRuntimeState();
            GameplayTagRedirector.ClearAll();
            Configure();
        }

        internal static void Configure()
        {
            GameplayTagRuntimePlatform.LogWarning = message => Debug.LogWarning(message);
            GameplayTagRuntimePlatform.LogError = message => Debug.LogError(message);
            GameplayTagRuntimePlatform.IsRuntimePlaying = () => Application.isPlaying;
            GameplayTagRuntimePlatform.LoadBuildTagData = LoadBuildTagData;
        }

        private static byte[] LoadBuildTagData()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameplayTags");
            if (asset == null)
            {
                return null;
            }

            try
            {
                return asset.bytes;
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }
        }

    }
}
#endif
