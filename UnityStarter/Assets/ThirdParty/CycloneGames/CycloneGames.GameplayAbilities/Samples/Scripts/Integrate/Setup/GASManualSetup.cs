using CycloneGames.AssetManagement.Runtime;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Integrate.Setup
{
    /// <summary>
    /// Example manual startup utility for projects that use CycloneGames.AssetManagement without a DI container.
    /// </summary>
    public static class GASManualSetup
    {
        /// <summary>
        /// Initializes GAS cue loading without DI. Call once from the project's composition root.
        /// </summary>
        public static void Initialize(IAssetPackage assetPackage)
        {
            if (assetPackage == null)
            {
                return;
            }

            var cueManager = GameplayCueManager.Instance;
            if (cueManager is GameplayCueManager unityManager)
            {
                unityManager.Initialize(assetPackage);
            }
        }
        
        /// <summary>
        /// Shuts down sample GAS services. Call from the same owner that initialized the sample.
        /// </summary>
        public static void Shutdown()
        {
            if (GameplayCueManager.Instance is GameplayCueManager unityManager)
            {
                unityManager.Shutdown();
            }
            GameplayCueManager.ResetInstance();
        }
    }
}
