using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Samples
{
    /// <summary>
    /// Sample-only <see cref="IUIWindowAssetProvider"/> composed from host-supplied
    /// load delegates.
    /// </summary>
    /// <remarks>
    /// The host owns loading and asset caching; this provider owns only the session lease.
    /// The lease clears its reference on dispose and never unloads or destroys the asset.
    /// <see cref="UIAssetLoadContext"/> is forwarded unchanged as optional hints.
    /// Both delegates must be
    /// invoked on the Unity main thread and must observe the supplied cancellation token.
    /// </remarks>
    public sealed class DelegatedUIWindowAssetProvider : IUIWindowAssetProvider
    {
        /// <summary>
        /// Host-owned load operation. Return a non-null Unity object; the provider wraps it
        /// in one session-owned lease. Throwing or returning null is reported as a failed open.
        /// </summary>
        public delegate UniTask<TAsset> LoadAssetAsync<TAsset>(
            string location,
            UIAssetLoadContext context,
            CancellationToken cancellationToken)
            where TAsset : UnityEngine.Object;

        private const int MaxLocationLength = 1024;

        private readonly LoadAssetAsync<UIWindowConfiguration> _loadConfiguration;
        private readonly LoadAssetAsync<GameObject> _loadPrefab;

        public DelegatedUIWindowAssetProvider(
            LoadAssetAsync<UIWindowConfiguration> loadConfiguration,
            LoadAssetAsync<GameObject> loadPrefab)
        {
            _loadConfiguration = loadConfiguration ??
                throw new ArgumentNullException(nameof(loadConfiguration));
            _loadPrefab = loadPrefab ??
                throw new ArgumentNullException(nameof(loadPrefab));
        }

        public UniTask<IUIAssetLease<UIWindowConfiguration>> AcquireConfigurationAsync(
            string windowId,
            UIAssetLoadContext context,
            CancellationToken cancellationToken)
        {
            EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(windowId))
            {
                throw new ArgumentException("Window id cannot be empty.", nameof(windowId));
            }

            ValidateLocation(windowId, nameof(windowId));
            return AwaitLeaseAsync(
                _loadConfiguration(windowId, context, cancellationToken),
                windowId, cancellationToken);
        }

        public UniTask<IUIAssetLease<GameObject>> AcquirePrefabAsync(
            UIAssetReference reference,
            UIAssetLoadContext context,
            CancellationToken cancellationToken)
        {
            EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (!reference.IsValid)
            {
                throw new ArgumentException(
                    "UI asset reference requires a runtime location.", nameof(reference));
            }

            string location = reference.Location;
            ValidateLocation(location, nameof(reference));
            return AwaitLeaseAsync(
                _loadPrefab(location, context, cancellationToken),
                location, cancellationToken);
        }

        private static async UniTask<IUIAssetLease<TAsset>> AwaitLeaseAsync<TAsset>(
            UniTask<TAsset> load,
            string location,
            CancellationToken cancellationToken)
            where TAsset : UnityEngine.Object
        {
            TAsset asset = await load;
            await UniTask.SwitchToMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"The host load delegate returned no asset for '{location}'.");
            }

            return new DelegatedAssetLease<TAsset>(asset);
        }

        private static void ValidateLocation(string location, string parameterName)
        {
            if (location.Length > MaxLocationLength || location.IndexOf('\0') >= 0)
            {
                throw new ArgumentException(
                    "UI asset location exceeds the allowed length or contains an invalid character.",
                    parameterName);
            }
        }

        private static void EnsureMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    "Delegated UI asset operations require the Unity main thread.");
            }
        }

        private sealed class DelegatedAssetLease<TAsset> : IUIAssetLease<TAsset>
            where TAsset : UnityEngine.Object
        {
            private TAsset _asset;

            public DelegatedAssetLease(TAsset asset)
            {
                _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            }

            public TAsset Asset
            {
                get
                {
                    EnsureMainThread();
                    if (_asset == null)
                    {
                        throw new ObjectDisposedException(
                            nameof(DelegatedAssetLease<TAsset>),
                            "The UI asset lease has been disposed.");
                    }

                    return _asset;
                }
            }

            public void Dispose()
            {
                EnsureMainThread();
                // Clear only: the host retains ownership of the underlying asset and cache.
                _asset = null;
            }
        }
    }
}
