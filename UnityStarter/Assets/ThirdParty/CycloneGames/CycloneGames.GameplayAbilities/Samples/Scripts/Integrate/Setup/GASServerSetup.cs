using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Integrate.Setup
{
    /// <summary>
    /// Server-side initialization utilities.
    /// Uses NullGameplayCueManager for zero-overhead cue handling.
    /// </summary>
    public static class GASServerSetup
    {
        /// <summary>
        /// Creates an explicitly owned server/headless context. All cue operations are no-ops.
        /// </summary>
        public static GASRuntimeContext CreateContext(
            GASRuntimeThreadPolicy threadPolicy = GASRuntimeThreadPolicy.Throw,
            GASRuntimeCacheProfile cacheProfile = null)
        {
            return new GASRuntimeContext(
                cueManager: NullGameplayCueManager.Instance,
                threadPolicy: threadPolicy,
                cacheProfile: cacheProfile);
        }
    }
}
