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
        /// Initializes GAS for server/headless mode. All cue operations are no-ops.
        /// </summary>
        public static void Initialize()
        {
            // Use the null implementation - all cue operations are safe no-ops
            GASServices.CueManager = NullGameplayCueManager.Instance;
        }
    }
}
