#if VCONTAINER_PRESENT
using System;
using System.Threading;

using Cysharp.Threading.Tasks;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.InputSystem.Runtime.Integrations.VContainer
{
    /// <summary>
    /// Creates the explicit AssetManagement package loader consumed by the base VContainer integration.
    /// </summary>
    public static class InputSystemAssetManagementVContainerAdapter
    {
        private static readonly InputSystemPackageConfigurationLoader Loader = LoadAsync;

        public static InputSystemPackageConfigurationLoader CreatePackageConfigurationLoader()
        {
            return Loader;
        }

        private static async UniTask<string> LoadAsync(
            object package,
            string configLocation,
            CancellationToken cancellationToken)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!(package is IAssetPackage assetPackage))
            {
                throw new ArgumentException(
                    $"Package must implement {nameof(IAssetPackage)}.",
                    nameof(package));
            }

            InputSystemDefaultConfigurationLoader loader =
                InputSystemAssetManagementHelper.CreateConfigLoader(assetPackage, configLocation);
            return loader == null ? null : await loader(cancellationToken);
        }
    }
}
#endif
