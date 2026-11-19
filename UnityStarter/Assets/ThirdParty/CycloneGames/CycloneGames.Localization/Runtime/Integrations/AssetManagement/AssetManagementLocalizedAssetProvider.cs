using System;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.Localization.Runtime.Integrations.AssetManagement
{
    /// <summary>
    /// Adapter from CycloneGames.AssetManagement handles to localization leases.
    /// The package and its bucket policy remain owned by the composition root.
    /// </summary>

    public sealed class AssetManagementLocalizedAssetProvider : ILocalizedAssetProvider
    {
        private sealed class AssetLease<TAsset> : ILocalizedAssetLease<TAsset>
            where TAsset : UnityEngine.Object
        {
            private IAssetHandle<TAsset> _handle;

            public AssetLease(IAssetHandle<TAsset> handle)
            {
                _handle = handle ?? throw new ArgumentNullException(nameof(handle));
            }

            public TAsset Asset
            {
                get
                {
                    EnsureMainThread();
                    if (_handle == null)
                    {
                        throw new ObjectDisposedException(
                            nameof(AssetLease<TAsset>),
                            "The localized asset lease has been disposed.");
                    }

                    return _handle.Asset;
                }
            }

            public void Dispose()
            {
                EnsureMainThread();
                IAssetHandle<TAsset> handle = _handle;
                if (handle == null)
                {
                    return;
                }

                // Clear before disposing: a backend that throws on an externally disposed
                // handle must not turn a retry into a double release.
                _handle = null;
                handle.Dispose();
            }
        }

        private readonly IAssetPackage _package;
        private readonly string _bucket;
        private readonly string _tag;
        private readonly string _owner;

        public AssetManagementLocalizedAssetProvider(
            IAssetPackage package,
            string bucket = null,
            string tag = null,
            string owner = null)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _bucket = bucket;
            _tag = tag;
            _owner = owner;
        }

        public async UniTask<ILocalizedAssetLease<TAsset>> AcquireAsync<TAsset>(
            string location,
            CancellationToken cancellationToken)
            where TAsset : UnityEngine.Object
        {
            EnsureMainThread();
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Asset location is required.", nameof(location));
            }

            IAssetHandle<TAsset> handle = _package.LoadAssetAsync<TAsset>(
                location, _bucket, _tag, _owner, cancellationToken);
            if (handle == null)
            {
                throw new InvalidOperationException(
                    $"Asset provider returned a null handle for '{location}'.");
            }

            try
            {
                await handle.Task.AttachExternalCancellation(cancellationToken);
                await UniTask.SwitchToMainThread();
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Asset == null)
                {
                    throw new InvalidOperationException(
                        $"Localized asset '{location}' completed without an asset. " +
                        $"Provider diagnostic: {handle.Error ?? "none"}");
                }

                return new AssetLease<TAsset>(handle);
            }
            catch
            {
                await UniTask.SwitchToMainThread();
                handle.Dispose();
                throw;
            }
        }

        private static void EnsureMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    "AssetManagement localized asset operations require the Unity main thread.");
            }
        }
    }
}
