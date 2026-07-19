using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Simplified facade for initializing the Asset Management system.
    /// Reduces the boilerplate of Register -> InitModule -> CreatePackage -> InitPackage.
    /// </summary>
    public static class AssetManager
    {
        /// <summary>
        /// Initializes the module and a default package in one step.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the package reports an initialization failure.</exception>
        public static async UniTask<IAssetPackage> InitializeDefaultPackageAsync(
            IAssetModule module,
            string packageName,
            AssetManagementOptions moduleOptions,
            AssetPackageInitOptions packageOptions,
            CancellationToken cancellationToken = default)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (string.IsNullOrWhiteSpace(packageName))
            {
                throw new ArgumentException("Package name cannot be null or empty.", nameof(packageName));
            }

            AssetRuntimeGuard.EnsureMainThread();
            if (!module.Initialized)
            {
                await module.InitializeAsync(moduleOptions);
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            }

            IAssetPackage package = module.GetPackage(packageName);
            if (package == null)
            {
                return await AssetPackageFactory.CreateAndInitializePackageAsync(
                    module,
                    packageName,
                    packageOptions,
                    cancellationToken);
            }

            bool initialized = await package.InitializeAsync(packageOptions, cancellationToken);
            await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            if (!initialized)
            {
                throw new InvalidOperationException($"Asset package '{packageName}' failed to initialize.");
            }

            return package;
        }
    }
}
