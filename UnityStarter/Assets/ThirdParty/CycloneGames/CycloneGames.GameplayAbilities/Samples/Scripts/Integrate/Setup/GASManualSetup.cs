using CycloneGames.AssetManagement.Runtime;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayAbilities.Runtime.Integrations.AssetManagement;

namespace CycloneGames.GameplayAbilities.Integrate.Setup
{
    /// <summary>
    /// Example manual startup utility for projects that use CycloneGames.AssetManagement without a DI container.
    /// </summary>
    public static class GASManualSetup
    {
        /// <summary>
        /// Creates an explicitly owned runtime context and cue manager without a DI container.
        /// Dispose every ASC first, then dispose the returned context and cue manager.
        /// </summary>
        public static GASRuntimeContext CreateContext(
            IAssetPackage assetPackage,
            GameObjectPoolManager.PoolConfig cuePoolConfig,
            out GameplayCueManager cueManager,
            GASRuntimeThreadPolicy threadPolicy = GASRuntimeThreadPolicy.Throw,
            GASRuntimeCacheProfile cacheProfile = null)
        {
            if (assetPackage == null)
            {
                throw new System.ArgumentNullException(nameof(assetPackage));
            }

            cueManager = new GameplayCueManager(cuePoolConfig);
            try
            {
                cueManager.Initialize(new AssetManagementResourceLocator(assetPackage));
                return new GASRuntimeContext(
                    cueManager: cueManager,
                    threadPolicy: threadPolicy,
                    cacheProfile: cacheProfile);
            }
            catch
            {
                cueManager.Dispose();
                cueManager = null;
                throw;
            }
        }
    }
}
