using Cysharp.Threading.Tasks;
using System.Threading;

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
        public static async UniTask<IAssetPackage> InitializeDefaultPackageAsync(
            IAssetModule module,
            string packageName,
            AssetManagementOptions moduleOptions,
            AssetPackageInitOptions packageOptions,
            CancellationToken cancellationToken = default)
        {
            // 1. Initialize Module
            if (!module.Initialized)
            {
                await module.InitializeAsync(moduleOptions);
            }

            // 2. Get or Create Package
            IAssetPackage package = module.GetPackage(packageName);
            if (package == null)
            {
                package = module.CreatePackage(packageName);
            }

            // 3. Initialize Package
            await package.InitializeAsync(packageOptions, cancellationToken);

            // 4. Set as Default (Optional convenience)
            AssetManagementLocator.DefaultPackage = package;

            return package;
        }
    }
}