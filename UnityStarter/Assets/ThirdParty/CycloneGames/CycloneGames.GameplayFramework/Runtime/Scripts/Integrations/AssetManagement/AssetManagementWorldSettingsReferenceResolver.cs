using System;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement
{
    public sealed class AssetManagementWorldSettingsReferenceResolver : IWorldSettingsReferenceResolver
    {
        private const string OWNER = nameof(WorldSettings);

        public static readonly AssetManagementWorldSettingsReferenceResolver Default =
            new AssetManagementWorldSettingsReferenceResolver(() => AssetManagementLocator.DefaultPackage);

        private readonly Func<IAssetPackage> _packageProvider;

        public AssetManagementWorldSettingsReferenceResolver(Func<IAssetPackage> packageProvider)
        {
            _packageProvider = packageProvider ?? throw new ArgumentNullException(nameof(packageProvider));
        }

        public bool Supports(WorldSettingsReferenceSource source)
        {
            return source == WorldSettingsReferenceSource.AssetReference;
        }

        public async UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(string location, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                return new WorldSettingsAssetLoadResult<T>(false, null, "Asset reference location is empty.");
            }

            IAssetPackage package = _packageProvider();
            if (package == null)
            {
                return new WorldSettingsAssetLoadResult<T>(false, null, "AssetManagementLocator.DefaultPackage is null.");
            }

            IAssetHandle<T> handle = null;
            try
            {
                handle = package.LoadAssetAsync<T>(location, owner: OWNER, cancellationToken: cancellationToken);
                if (handle == null)
                {
                    return new WorldSettingsAssetLoadResult<T>(false, null, "Asset handle creation returned null.");
                }

                await handle.Task.AttachExternalCancellation(cancellationToken);

                if (!string.IsNullOrEmpty(handle.Error))
                {
                    return FailAndDispose<T>(handle.Error, ref handle);
                }

                T asset = handle.Asset;
                if (asset == null)
                {
                    return FailAndDispose<T>("Asset handle completed but returned null.", ref handle);
                }

                IAssetHandle<T> lease = handle;
                handle = null;
                return new WorldSettingsAssetLoadResult<T>(true, asset, null, lease);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return FailAndDispose<T>("Asset reference resolution was canceled.", ref handle);
            }
            catch (Exception ex)
            {
                return FailAndDispose<T>(ex.Message, ref handle);
            }
        }

        private static WorldSettingsAssetLoadResult<T> FailAndDispose<T>(string error, ref IAssetHandle<T> handle) where T : UnityEngine.Object
        {
            handle?.Dispose();
            handle = null;
            return new WorldSettingsAssetLoadResult<T>(false, null, error);
        }
    }

    public static class AssetManagementWorldSettingsReferenceResolverBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void RegisterDefaultResolver()
        {
            WorldSettingsReferenceResolverRegistry.Register(AssetManagementWorldSettingsReferenceResolver.Default);
        }
    }
}
