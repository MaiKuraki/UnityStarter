using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Integrations
{
    /// <summary>
    /// Adapter from CycloneGames.AssetManagement handles to UIFramework leases.
    /// The package and path builder remain owned by the composition root.
    /// </summary>
    public sealed class AssetManagementUIWindowAssetProvider : IUIWindowAssetProvider
    {
        private sealed class AssetLease<TAsset> : IUIAssetLease<TAsset>
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
                    return _handle != null ? _handle.Asset : null;
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

                _handle = null;
                handle.Dispose();
            }
        }

        private readonly IAssetPackage _package;
        private readonly IAssetPathBuilder _configurationPathBuilder;

        public AssetManagementUIWindowAssetProvider(
            IAssetPackage package,
            IAssetPathBuilder configurationPathBuilder)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _configurationPathBuilder = configurationPathBuilder ??
                throw new ArgumentNullException(nameof(configurationPathBuilder));
        }

        public async UniTask<IUIAssetLease<UIWindowConfiguration>> AcquireConfigurationAsync(
            string windowId,
            UIAssetLoadContext context,
            CancellationToken cancellationToken)
        {
            EnsureMainThread();
            if (string.IsNullOrWhiteSpace(windowId))
            {
                throw new ArgumentException("Window id cannot be empty.", nameof(windowId));
            }

            string location = _configurationPathBuilder.GetAssetPath(windowId);
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new InvalidOperationException(
                    $"No configuration location was resolved for '{windowId}'.");
            }

            IAssetHandle<UIWindowConfiguration> handle =
                _package.LoadAssetAsync<UIWindowConfiguration>(
                    location,
                    context.ConfigBucket,
                    context.ConfigTag,
                    context.ConfigOwner,
                    cancellationToken);
            return await AwaitLeaseAsync(handle, location, cancellationToken);
        }

        public async UniTask<IUIAssetLease<GameObject>> AcquirePrefabAsync(
            UIAssetReference reference,
            UIAssetLoadContext context,
            CancellationToken cancellationToken)
        {
            EnsureMainThread();
            if (!reference.IsValid)
            {
                throw new ArgumentException("UI asset reference requires a runtime location.", nameof(reference));
            }

            string location = reference.Location;
            IAssetHandle<GameObject> handle = _package.LoadAssetAsync<GameObject>(
                location,
                context.PrefabBucket,
                context.PrefabTag,
                context.PrefabOwner,
                cancellationToken);
            return await AwaitLeaseAsync(handle, location, cancellationToken);
        }

        private static async UniTask<IUIAssetLease<TAsset>> AwaitLeaseAsync<TAsset>(
            IAssetHandle<TAsset> handle,
            string location,
            CancellationToken cancellationToken)
            where TAsset : UnityEngine.Object
        {
            if (handle == null)
            {
                throw new InvalidOperationException(
                    $"Asset provider returned a null handle for '{location}'.");
            }

            try
            {
                await handle.Task;
                await UniTask.SwitchToMainThread();
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Asset == null)
                {
                    throw new InvalidOperationException(
                        $"UI asset '{location}' completed without an asset. " +
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
                    "AssetManagement UI asset operations require the Unity main thread.");
            }
        }
    }

    public static class UIAssetLoadContextAssetManagementExtensions
    {
        public static UIAssetLoadContext ToUIAssetLoadContext(this AssetBucketScope scope)
        {
            return new UIAssetLoadContext(scope.Bucket, scope.Tag, scope.Owner);
        }

        public static UIAssetLoadContext ToUIAssetLoadContext(
            this AssetBucketScope configurationScope,
            AssetBucketScope prefabScope)
        {
            return new UIAssetLoadContext(
                configurationScope.Bucket,
                configurationScope.Tag,
                configurationScope.Owner,
                prefabScope.Bucket,
                prefabScope.Tag,
                prefabScope.Owner);
        }
    }
}
