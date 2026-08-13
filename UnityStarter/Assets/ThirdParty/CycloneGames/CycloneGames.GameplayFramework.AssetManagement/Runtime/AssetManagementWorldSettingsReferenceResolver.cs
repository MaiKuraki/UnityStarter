using System;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement
{
    /// <summary>
    /// Resolves WorldSettings locations through one explicitly owned AssetManagement package.
    /// Component references are loaded as prefab GameObjects and require exactly one matching
    /// component on the prefab root.
    /// </summary>
    public sealed class AssetManagementWorldSettingsReferenceResolver : IWorldSettingsReferenceResolver
    {
        private const string Owner = nameof(WorldSettings);

        private readonly IAssetPackage package;

        public AssetManagementWorldSettingsReferenceResolver(IAssetPackage package)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public bool Supports(WorldSettingsReferenceSource source)
        {
            return source == WorldSettingsReferenceSource.AssetReference;
        }

        public UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
            string location,
            CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                return UniTask.FromResult(
                    new WorldSettingsAssetLoadResult<T>(false, null, "Asset reference location is empty."));
            }

            return typeof(Component).IsAssignableFrom(typeof(T))
                ? ResolvePrefabComponentAsync<T>(location, cancellationToken)
                : ResolveAssetAsync<T>(location, cancellationToken);
        }

        private async UniTask<WorldSettingsAssetLoadResult<T>> ResolvePrefabComponentAsync<T>(
            string location,
            CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            IAssetHandle<GameObject> handle = null;
            try
            {
                handle = package.LoadAssetAsync<GameObject>(
                    location,
                    owner: Owner,
                    cancellationToken: cancellationToken);
                if (handle == null)
                {
                    return new WorldSettingsAssetLoadResult<T>(
                        false,
                        null,
                        "Prefab asset handle creation returned null.");
                }

                await handle.Task.AttachExternalCancellation(cancellationToken);
                await UniTask.SwitchToMainThread();
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(handle.Error))
                {
                    return FailAndDispose<T, GameObject>(handle.Error, ref handle);
                }

                GameObject prefab = handle.Asset;
                if (prefab == null)
                {
                    return FailAndDispose<T, GameObject>(
                        "Prefab asset handle completed but returned null.",
                        ref handle);
                }

                Component[] components = prefab.GetComponents(typeof(T));
                if (components.Length != 1)
                {
                    return FailAndDispose<T, GameObject>(
                        $"Prefab '{prefab.name}' must contain exactly one {typeof(T).Name} component on its root, but found {components.Length}.",
                        ref handle);
                }

                T component = components[0] as T;
                IAssetHandle<GameObject> lease = handle;
                handle = null;
                return new WorldSettingsAssetLoadResult<T>(true, component, null, lease);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await UniTask.SwitchToMainThread();
                handle?.Dispose();
                throw;
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                return FailAndDispose<T, GameObject>(exception.Message, ref handle);
            }
        }

        private async UniTask<WorldSettingsAssetLoadResult<T>> ResolveAssetAsync<T>(
            string location,
            CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            IAssetHandle<T> handle = null;
            try
            {
                handle = package.LoadAssetAsync<T>(
                    location,
                    owner: Owner,
                    cancellationToken: cancellationToken);
                if (handle == null)
                {
                    return new WorldSettingsAssetLoadResult<T>(
                        false,
                        null,
                        "Asset handle creation returned null.");
                }

                await handle.Task.AttachExternalCancellation(cancellationToken);
                await UniTask.SwitchToMainThread();
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(handle.Error))
                {
                    return FailAndDispose<T, T>(handle.Error, ref handle);
                }

                T asset = handle.Asset;
                if (asset == null)
                {
                    return FailAndDispose<T, T>(
                        "Asset handle completed but returned null.",
                        ref handle);
                }

                IAssetHandle<T> lease = handle;
                handle = null;
                return new WorldSettingsAssetLoadResult<T>(true, asset, null, lease);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await UniTask.SwitchToMainThread();
                handle?.Dispose();
                throw;
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                return FailAndDispose<T, T>(exception.Message, ref handle);
            }
        }

        private static WorldSettingsAssetLoadResult<TResult> FailAndDispose<TResult, THandle>(
            string error,
            ref IAssetHandle<THandle> handle)
            where TResult : UnityEngine.Object
            where THandle : UnityEngine.Object
        {
            handle?.Dispose();
            handle = null;
            return new WorldSettingsAssetLoadResult<TResult>(false, null, error);
        }
    }
}
