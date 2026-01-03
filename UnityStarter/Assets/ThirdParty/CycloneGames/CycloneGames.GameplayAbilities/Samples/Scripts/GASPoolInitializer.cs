using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Samples
{
    /// <summary>
    /// [BEGINNER] Demonstrates pool prewarming for optimal performance.
    /// Attach this to a GameObject in your scene to prewarm pools during loading.
    /// 
    /// Key Concepts:
    /// - GASPoolUtility.ConfigureXXX() for pool configuration
    /// - GASPoolUtility.WarmAllPools() for prewarming
    /// - Scene transition cleanup with AggressiveShrinkAll()
    /// 
    /// Best Practices:
    /// - Configure pools based on your game type
    /// - Prewarm during loading screens, not during gameplay
    /// - Use AggressiveShrink during scene transitions to free memory
    /// </summary>
    public class GASPoolInitializer : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Which tier to configure pools for")]
        public PoolTier Tier = PoolTier.Medium;
        
        [Tooltip("Whether to prewarm pools on Awake")]
        public bool PrewarmOnAwake = true;
        
        [Header("Custom Configuration (Optional)")]
        [Tooltip("Override with custom prewarm counts")]
        public bool UseCustomPrewarm = false;
        public int EffectSpecCount = 64;
        public int ActiveEffectCount = 128;
        public int ContextCount = 64;
        public int AbilitySpecCount = 32;

        public enum PoolTier
        {
            Minimal,
            Low,
            Medium,
            High,
            Ultra,
            Mobile
        }

        private void Awake()
        {
            ConfigurePoolsForTier();
            
            if (PrewarmOnAwake)
            {
                PrewarmPools();
            }
        }

        /// <summary>
        /// Configures all GAS pools based on the selected tier.
        /// Call this during game initialization.
        /// </summary>
        public void ConfigurePoolsForTier()
        {
            switch (Tier)
            {
                case PoolTier.Minimal:
                    GASPoolUtility.ConfigureMinimal();
                    break;
                case PoolTier.Low:
                    GASPoolUtility.ConfigureLow();
                    break;
                case PoolTier.Medium:
                    GASPoolUtility.ConfigureMedium();
                    break;
                case PoolTier.High:
                    GASPoolUtility.ConfigureHigh();
                    break;
                case PoolTier.Ultra:
                    GASPoolUtility.ConfigureUltra();
                    break;
                case PoolTier.Mobile:
                    GASPoolUtility.ConfigureMobile();
                    break;
            }
            
            GASLog.Info($"[GASPoolInitializer] Configured pools for {Tier} tier.");
        }

        /// <summary>
        /// Prewarms pools to reduce runtime allocations.
        /// Call this during loading screens.
        /// </summary>
        public void PrewarmPools()
        {
            if (UseCustomPrewarm)
            {
                GASPoolUtility.WarmAllPools(EffectSpecCount, ActiveEffectCount, ContextCount, AbilitySpecCount);
                GASLog.Info($"[GASPoolInitializer] Prewarmed with custom counts: Spec={EffectSpecCount}, Active={ActiveEffectCount}");
            }
            else
            {
                GASPoolUtility.WarmAllPools();
                GASLog.Info("[GASPoolInitializer] Prewarmed pools with default counts.");
            }
        }

        /// <summary>
        /// Call during scene transitions to free memory.
        /// </summary>
        public void OnSceneTransition()
        {
            GASPoolUtility.AggressiveShrinkAll();
            GASLog.Info("[GASPoolInitializer] Aggressively shrunk all pools for scene transition.");
        }

        /// <summary>
        /// Logs current pool statistics (Editor/Development only).
        /// </summary>
        [ContextMenu("Log Pool Statistics")]
        public void LogStatistics()
        {
            GASPoolUtility.LogAllStatistics();
        }

        /// <summary>
        /// Checks pool health and logs warnings.
        /// </summary>
        [ContextMenu("Check Pool Health")]
        public void CheckHealth()
        {
            if (GASPoolUtility.CheckPoolHealth(out string report))
            {
                GASLog.Info(report);
            }
            else
            {
                GASLog.Warning(report);
            }
        }
    }
}
