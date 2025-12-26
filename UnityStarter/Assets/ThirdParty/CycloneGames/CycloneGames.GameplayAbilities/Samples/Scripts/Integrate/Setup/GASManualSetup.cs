#if CYCLONE_GAMES_ASSET_MANAGEMENT_PRESENT

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Integrate.Setup
{
    /// <summary>
    /// Manual (non-DI) initialization utilities.
    /// Use this when not using a DI container.
    /// 
    /// 
    /// NOTE: This class just sample for AssetManagement initialize, you must implement your own GAS initialize
    /// </summary>
    public static class GASManualSetup
    {
        /// <summary>
        /// Initializes GAS without DI. Call once at game startup.
        /// </summary>
        public static void Initialize(object assetPackage)
        {
            // Access Instance to create default GameplayCueManager
            var cueManager = GameplayCueManager.Instance;
            
            // Initialize with asset package
            if (cueManager is GameplayCueManager unityManager)
            {
                unityManager.Initialize((CycloneGames.AssetManagement.Runtime.IAssetPackage)assetPackage);
            }
        }
        
        /// <summary>
        /// Shuts down GAS. Call on application quit.
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
#endif