using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Integrate.Setup
{
    /// <summary>
    /// Example manual, non-DI startup utility. The host supplies an IResourceLocator,
    /// so neither this helper nor the sample assembly depends on a concrete asset backend.
    /// </summary>
    public static class GASManualSetup
    {
        /// <summary>
        /// Creates an explicitly owned runtime context and cue manager without a DI container.
        /// Dispose every ASC first, then dispose the returned context and cue manager.
        /// </summary>
        public static GASRuntimeContext CreateContext(
            IResourceLocator resourceLocator,
            GameObjectPoolManager.PoolConfig cuePoolConfig,
            out GameplayCueManager cueManager,
            GASRuntimeThreadPolicy threadPolicy = GASRuntimeThreadPolicy.Throw,
            GASRuntimeCacheProfile cacheProfile = null)
        {
            if (resourceLocator == null)
            {
                throw new System.ArgumentNullException(nameof(resourceLocator));
            }

            cueManager = new GameplayCueManager(cuePoolConfig);
            try
            {
                cueManager.Initialize(resourceLocator);
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
