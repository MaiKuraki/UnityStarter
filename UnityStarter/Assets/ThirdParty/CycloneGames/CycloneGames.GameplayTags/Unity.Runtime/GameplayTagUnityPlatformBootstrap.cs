#if UNITY_5_3_OR_NEWER
using System.Runtime.CompilerServices;
using UnityEngine;
using CycloneGames.GameplayTags.Core;

[assembly: InternalsVisibleTo("CycloneGames.GameplayTags.Unity.Editor")]

namespace CycloneGames.GameplayTags.Unity.Runtime
{
    internal static class GameplayTagUnityPlatformBootstrap
    {
        private const string BuildCatalogResourcePath =
            "CycloneGames.GameplayTags/GameplayTags";

        private static bool s_ownsAmbientDiagnostics;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            if (s_ownsAmbientDiagnostics)
            {
                GameplayTagsDiagnostics.TryReset(GameplayTagsLogWriterAdapter.Ambient);
                s_ownsAmbientDiagnostics = false;
            }

            GameplayTagManager.ResetRuntimeState();
            GameplayTagRedirector.ClearAll();
            Configure();
        }

        internal static void Configure()
        {
            if (s_ownsAmbientDiagnostics &&
                !object.ReferenceEquals(
                    GameplayTagsDiagnostics.Current,
                    GameplayTagsLogWriterAdapter.Ambient))
            {
                s_ownsAmbientDiagnostics = false;
            }

            if (!s_ownsAmbientDiagnostics)
            {
                s_ownsAmbientDiagnostics =
                    GameplayTagsDiagnostics.TryInstall(GameplayTagsLogWriterAdapter.Ambient);
            }

            GameplayTagRuntimePlatform.IsRuntimePlaying = () => Application.isPlaying;
            GameplayTagRuntimePlatform.LoadBuildTagData = LoadBuildTagData;
        }

        private static byte[] LoadBuildTagData()
        {
            TextAsset asset = Resources.Load<TextAsset>(BuildCatalogResourcePath);
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
