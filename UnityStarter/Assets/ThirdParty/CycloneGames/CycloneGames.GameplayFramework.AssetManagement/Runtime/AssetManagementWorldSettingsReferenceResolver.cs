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
            IWorldSettingsLeaseRegistrar leaseRegistrar,
            CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            if (leaseRegistrar == null)
            {
                throw new ArgumentNullException(nameof(leaseRegistrar));
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                return UniTask.FromResult(
                    new WorldSettingsAssetLoadResult<T>(false, null, "Asset reference location is empty."));
            }

            return typeof(Component).IsAssignableFrom(typeof(T))
                ? ResolvePrefabComponentAsync<T>(location, leaseRegistrar, cancellationToken)
                : ResolveAssetAsync<T>(location, leaseRegistrar, cancellationToken);
        }

        private async UniTask<WorldSettingsAssetLoadResult<T>> ResolvePrefabComponentAsync<T>(
            string location,
            IWorldSettingsLeaseRegistrar leaseRegistrar,
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

                leaseRegistrar.Register(handle);
                await handle.Task.AttachExternalCancellation(cancellationToken);
                await UniTask.SwitchToMainThread();
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(handle.Error))
                {
                    return Failure<T>(handle.Error);
                }

                GameObject prefab = handle.Asset;
                if (prefab == null)
                {
                    return Failure<T>(
                        "Prefab asset handle completed but returned null.");
                }

                Component[] components = prefab.GetComponents(typeof(T));
                if (components.Length != 1)
                {
                    return Failure<T>(
                        $"Prefab '{prefab.name}' must contain exactly one {typeof(T).Name} component on its root, but found {components.Length}.");
                }

                T component = components[0] as T;
                return new WorldSettingsAssetLoadResult<T>(true, component, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await UniTask.SwitchToMainThread();
                throw;
            }
            catch (Exception exception) when (FindOutOfMemory(exception) == null)
            {
                await UniTask.SwitchToMainThread();
                return Failure<T>(exception.Message);
            }
        }

        private async UniTask<WorldSettingsAssetLoadResult<T>> ResolveAssetAsync<T>(
            string location,
            IWorldSettingsLeaseRegistrar leaseRegistrar,
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

                leaseRegistrar.Register(handle);
                await handle.Task.AttachExternalCancellation(cancellationToken);
                await UniTask.SwitchToMainThread();
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(handle.Error))
                {
                    return Failure<T>(handle.Error);
                }

                T asset = handle.Asset;
                if (asset == null)
                {
                    return Failure<T>(
                        "Asset handle completed but returned null.");
                }

                return new WorldSettingsAssetLoadResult<T>(true, asset, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await UniTask.SwitchToMainThread();
                throw;
            }
            catch (Exception exception) when (FindOutOfMemory(exception) == null)
            {
                await UniTask.SwitchToMainThread();
                return Failure<T>(exception.Message);
            }
        }

        private static WorldSettingsAssetLoadResult<T> Failure<T>(string error)
            where T : UnityEngine.Object
        {
            return new WorldSettingsAssetLoadResult<T>(false, null, error);
        }

        private static OutOfMemoryException FindOutOfMemory(Exception exception)
        {
            if (exception is OutOfMemoryException outOfMemoryException)
            {
                return outOfMemoryException;
            }

            if (exception is AggregateException aggregateException)
            {
                for (int index = 0; index < aggregateException.InnerExceptions.Count; index++)
                {
                    OutOfMemoryException nested = FindOutOfMemory(
                        aggregateException.InnerExceptions[index]);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }
    }
}
