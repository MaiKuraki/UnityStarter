using System;
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Provides a centralized, asynchronous way to create and initialize asset packages.
    /// This is essential for integrating with DI containers that do not natively support async factory methods.
    /// </summary>
    public static class AssetPackageFactory
    {
        /// <summary>
        /// Creates a new asset package, initializes it asynchronously, and returns the fully ready-to-use package.
        /// </summary>
        /// <param name="module">The asset module to create the package from.</param>
        /// <param name="packageName">The name of the package to create.</param>
        /// <param name="options">The initialization options for the package.</param>
        /// <param name="cancellationToken">A token to cancel the async operation.</param>
        /// <returns>A UniTask that resolves to the initialized package.</returns>
        public static async UniTask<IAssetPackage> CreateAndInitializePackageAsync(
            IAssetModule module,
            string packageName,
            AssetPackageInitOptions options,
            CancellationToken cancellationToken = default)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            IAssetPackage package = module.CreatePackage(packageName);
            if (package == null)
            {
                throw new InvalidOperationException($"Asset module returned a null package for '{packageName}'.");
            }

            try
            {
                bool success = await package.InitializeAsync(options, cancellationToken);
                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
                if (!success)
                {
                    throw new InvalidOperationException($"Asset package initialization failed: {package.Name}.");
                }

                return package;
            }
            catch (Exception initializationFailure)
            {
                await UniTask.SwitchToMainThread();
                try
                {
                    bool removed = await module.RemovePackageAsync(packageName);
                    await UniTask.SwitchToMainThread();
                    if (!removed)
                    {
                        throw new InvalidOperationException(
                            $"Asset package '{packageName}' was not removed after initialization failed.");
                    }
                }
                catch (Exception cleanupFailure) when (AssetRuntimeGuard.IsRecoverableException(cleanupFailure))
                {
                    throw new AggregateException(
                        $"Asset package '{packageName}' failed to initialize and cleanup also failed.",
                        initializationFailure,
                        cleanupFailure);
                }

                throw;
            }
        }
    }
}
