using Cysharp.Threading.Tasks;
using System.Threading;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Provides a centralized, asynchronous way to create and initialize asset packages.
    /// This is essential for integrating with DI containers that do not natively support async factory methods.
    /// </summary>
    public static class AssetPackageFactory
    {
        private const string DEBUG_FLAG = "[AssetPackageFactory]";
        /// <summary>
        /// Creates a new asset package, initializes it asynchronously, and returns the fully ready-to-use package.
        /// </summary>
        /// <param name="module">The asset module to create the package from.</param>
        /// <param name="packageName">The name of the package to create.</param>
        /// <param name="options">The initialization options for the package.</param>
        /// <param name="cancellationToken">A token to cancel the async operation.</param>
        /// <returns>A UniTask that resolves to the initialized IAssetPackage, or null if initialization fails.</returns>
        public static async UniTask<IAssetPackage> CreateAndInitializePackageAsync(
            IAssetModule module,
            string packageName,
            AssetPackageInitOptions options,
            CancellationToken cancellationToken = default)
        {
            if (module == null)
            {
                UnityEngine.Debug.LogError($"{DEBUG_FLAG} Invalid AssetModule");
                return null;
            }

            var package = module.CreatePackage(packageName);
            if (package == null)
            {
                UnityEngine.Debug.LogError($"{DEBUG_FLAG} Invalid Package");
                return null;
            }

            bool success = await package.InitializeAsync(options, cancellationToken);
            if (!success)
            {
                UnityEngine.Debug.LogError($"{DEBUG_FLAG} Initialize asset package failed, package name: {package.Name}");
                module.RemovePackage(packageName);
                return null;
            }

            return package;
        }
    }
}