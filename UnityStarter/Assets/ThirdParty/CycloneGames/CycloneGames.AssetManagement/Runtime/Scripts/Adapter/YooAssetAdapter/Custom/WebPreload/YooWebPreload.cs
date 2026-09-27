#if CYCLONEGAMES_HAS_YOOASSET
using System;

using UnityEngine.Networking;
using YooAsset;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Entry point for YooAsset 3.0.6 Web preload support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// YooAsset declares <c>IWebPreloadStrategy</c> as <c>internal</c> and grants access only to an assembly named
    /// <c>YooAsset.Custom</c>. This assembly carries that name so the strategy can be implemented without forking
    /// YooAsset. The strategy instance is handed back as <see cref="object"/> because the interface itself is not
    /// publicly visible; <see cref="AttachPreloadStrategy"/> performs the typed placement.
    /// </para>
    /// <para>
    /// Preload is only read by YooAsset's Web network file system. Attaching it to a sandbox or builtin file
    /// system parameter set has no effect.
    /// </para>
    /// </remarks>
    public static class YooWebPreload
    {
        /// <summary>
        /// Creates the opaque strategy value for <paramref name="platform"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="platform"/> is null.</exception>
        public static object CreatePreloadStrategy(IWebPreloadPlatform platform)
        {
            if (platform == null)
            {
                throw new ArgumentNullException(nameof(platform));
            }

            return new BridgedWebPreloadStrategy(platform);
        }

        /// <summary>
        /// Attaches a preload strategy to <paramref name="parameters"/> under
        /// <see cref="EFileSystemParameter.WebPreloadStrategy"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="parameters"/> already carries a WebPreloadStrategy parameter.
        /// </exception>
        public static void AttachPreloadStrategy(
            FileSystemParameters parameters,
            IWebPreloadPlatform platform)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            parameters.AddParameter(
                EFileSystemParameter.WebPreloadStrategy,
                CreatePreloadStrategy(platform));
        }
    }

    /// <summary>
    /// Adapts the public <see cref="IWebPreloadPlatform"/> contract onto YooAsset's internal
    /// <c>IWebPreloadStrategy</c>. Declared internal on purpose: a public type cannot surface an
    /// interface that is internal to another assembly.
    /// </summary>
    internal sealed class BridgedWebPreloadStrategy : IWebPreloadStrategy
    {
        private readonly IWebPreloadPlatform _platform;

        /// <summary>
        /// Reused across queries so a preload sweep stays allocation-free after the first call.
        /// </summary>
        private readonly WebPreloadBundleQuery _query = new WebPreloadBundleQuery();

        internal BridgedWebPreloadStrategy(IWebPreloadPlatform platform)
        {
            _platform = platform;
        }

        public bool IsBundleCached(WebPreloadQueryArgs args)
        {
            PackageBundle bundle = args.Bundle;
            _query.Reset(
                bundle?.BundleName,
                bundle?.FileHash,
                bundle == null ? 0L : bundle.FileSize,
                bundle != null && bundle.IsEncrypted,
                args.CandidateUrls);
            return _platform.IsBundleCached(_query);
        }

        public UnityWebRequest CreatePreloadRequest(WebPreloadRequestArgs args)
        {
            return _platform.CreatePreloadRequest(args.Url);
        }
    }
}
#endif
