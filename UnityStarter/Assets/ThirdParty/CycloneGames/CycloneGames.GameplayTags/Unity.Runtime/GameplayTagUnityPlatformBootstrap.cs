#if UNITY_5_3_OR_NEWER
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using CycloneGames.GameplayTags.Core;

[assembly: InternalsVisibleTo("CycloneGames.GameplayTags.Unity.Editor")]

namespace CycloneGames.GameplayTags.Unity.Runtime
{
    /// <summary>
    /// The Unity host platform: supplies build data from a <c>Resources</c> asset and tells the registry
    /// whether the game is playing.
    /// </summary>
    /// <remarks>
    /// Installed once per domain through <see cref="RuntimeInitializeOnLoadMethod"/>. Installing the
    /// platform does not build the registry; the first tag resolution does, which is what keeps tag lookup
    /// cheap when nothing ever asks for one.
    /// </remarks>
    internal sealed class GameplayTagUnityPlatform : GameplayTagHostPlatformBase
    {
        private const string BuildCatalogResourcePath =
            "CycloneGames.GameplayTags/GameplayTags";

        private readonly Func<byte[]> m_CustomLoader;

        /// <summary>Creates the default platform, which reads the baked manifest from Resources.</summary>
        public GameplayTagUnityPlatform() { }

        /// <summary>
        /// Creates a platform whose manifest comes from the host's asset pipeline instead of Resources.
        /// Pass the loader your game already uses - a YooAsset or Addressables handle's
        /// <c>ReadBytes</c>, resolved once during bootstrap - and the tag registry will never touch
        /// Resources.
        /// </summary>
        public GameplayTagUnityPlatform(Func<byte[]> customBuildDataLoader)
        {
            m_CustomLoader = customBuildDataLoader;
        }

        public override string Name => "Unity";

        public override bool IsRuntimePlaying => Application.isPlaying;

        public override bool TryLoadBuildTagData(out byte[] data)
        {
            if (m_CustomLoader != null)
            {
                data = m_CustomLoader();
                return data != null && data.Length > 0;
            }

            return TryLoadFromResources(out data);
        }

        /// <summary>
        /// Fallback for editor and local builds. The baked manifest is a few kilobytes and is needed
        /// synchronously on the first tag resolution, and Resources is the one Unity built-in that does
        /// that on every platform including Android and WebGL. A shipping game that routes assets through
        /// YooAsset or Addressables should supply its own loader through the constructor instead; the
        /// registry never depends on this path.
        /// </summary>
#pragma warning disable CG0014
        private static bool TryLoadFromResources(out byte[] data)
        {
            TextAsset asset = Resources.Load<TextAsset>(BuildCatalogResourcePath);
            if (asset == null)
            {
                data = null;
                return false;
            }

            try
            {
                data = asset.bytes;
                return data != null && data.Length > 0;
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }
        }
#pragma warning restore CG0014
    }

    internal static class GameplayTagUnityPlatformBootstrap
    {
        private static bool s_ownsAmbientDiagnostics;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            if (s_ownsAmbientDiagnostics)
            {
                GameplayTagsDiagnostics.TryReset(GameplayTagsLogWriterAdapter.Ambient);
                s_ownsAmbientDiagnostics = false;
            }

            GameplayTagManager.Reset();
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

            GameplayTagHost.Use(new GameplayTagUnityPlatform());
        }
    }
}
#endif
