using System.Text;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Central utility class for managing all GAS-related object pools.
    /// Provides convenient methods for configuration, statistics, and scene transition cleanup.
    /// </summary>
    public static class GASPoolUtility
    {
        #region Pool Configuration Presets
        
        /// <summary>
        /// Configures all pools for high-performance scenarios (e.g., bullet hell, large battles).
        /// Higher memory usage, better hit rates.
        /// </summary>
        public static void ConfigureForHighPerformance()
        {
            GameplayEffectSpec.SetMaxPoolCapacity(1024);
            GameplayEffectSpec.SetMinPoolCapacity(128);
            
            ActiveGameplayEffect.SetMaxPoolCapacity(1024);
            ActiveGameplayEffect.SetMinPoolCapacity(128);
            
            GameplayEffectContext.SetMaxPoolCapacity(512);
            GameplayEffectContext.SetMinPoolCapacity(64);
            
            PoolManager.SetMaxPoolCapacity(256);
            PoolManager.SetMinPoolCapacity(32);
        }
        
        /// <summary>
        /// Configures all pools for balanced scenarios (default game types).
        /// </summary>
        public static void ConfigureForBalanced()
        {
            GameplayEffectSpec.SetMaxPoolCapacity(256);
            GameplayEffectSpec.SetMinPoolCapacity(16);
            
            ActiveGameplayEffect.SetMaxPoolCapacity(512);
            ActiveGameplayEffect.SetMinPoolCapacity(32);
            
            GameplayEffectContext.SetMaxPoolCapacity(256);
            GameplayEffectContext.SetMinPoolCapacity(32);
            
            PoolManager.SetMaxPoolCapacity(128);
            PoolManager.SetMinPoolCapacity(8);
        }
        
        /// <summary>
        /// Configures all pools for low-end devices (mobile, WebGL).
        /// Lower memory usage, may have more cache misses during peaks.
        /// </summary>
        public static void ConfigureForLowEnd()
        {
            GameplayEffectSpec.SetMaxPoolCapacity(64);
            GameplayEffectSpec.SetMinPoolCapacity(4);
            
            ActiveGameplayEffect.SetMaxPoolCapacity(128);
            ActiveGameplayEffect.SetMinPoolCapacity(8);
            
            GameplayEffectContext.SetMaxPoolCapacity(64);
            GameplayEffectContext.SetMinPoolCapacity(8);
            
            PoolManager.SetMaxPoolCapacity(32);
            PoolManager.SetMinPoolCapacity(4);
        }
        
        #endregion
        
        #region Pre-warming
        
        /// <summary>
        /// Pre-warms all pools with default counts.
        /// Call during level loading to minimize runtime allocations.
        /// </summary>
        public static void WarmAllPools()
        {
            WarmAllPools(32, 64, 32);
        }
        
        /// <summary>
        /// Pre-warms all pools with specified counts.
        /// </summary>
        public static void WarmAllPools(int effectSpecCount, int activeEffectCount, int contextCount)
        {
            GameplayEffectSpec.WarmPool(effectSpecCount);
            ActiveGameplayEffect.WarmPool(activeEffectCount);
            GameplayEffectContext.WarmPool(contextCount);
        }
        
        #endregion
        
        #region Scene Transition / Cleanup
        
        /// <summary>
        /// Aggressively shrinks all pools to their minimum capacity.
        /// Call during scene transitions to free memory for loading.
        /// </summary>
        public static void AggressiveShrinkAll()
        {
            GameplayEffectSpec.AggressiveShrink();
            ActiveGameplayEffect.AggressiveShrink();
            GameplayEffectContext.AggressiveShrink();
        }
        
        /// <summary>
        /// Clears all pools completely.
        /// Use with caution - only when completely resetting the game state.
        /// </summary>
        public static void ClearAllPools()
        {
            GameplayEffectSpec.ClearPool();
            ActiveGameplayEffect.ClearPool();
            GameplayEffectContext.ClearPool();
            PoolManager.ClearAllPools();
        }
        
        #endregion
        
        #region Statistics
        
        /// <summary>
        /// Logs statistics for all pools to the console.
        /// Only works in Editor and Development builds.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void LogAllStatistics()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== GAS Pool Statistics ===");
            
            var specStats = GameplayEffectSpec.GetStatistics();
            sb.AppendLine($"[GameplayEffectSpec] Pool:{specStats.PoolSize} Active:{specStats.ActiveCount} Peak:{specStats.PeakActive} Gets:{specStats.TotalGets} Misses:{specStats.TotalMisses} HitRate:{specStats.HitRate:P1}");
            
            var activeStats = ActiveGameplayEffect.GetStatistics();
            sb.AppendLine($"[ActiveGameplayEffect] Pool:{activeStats.PoolSize} Active:{activeStats.ActiveCount} Peak:{activeStats.PeakActive} Gets:{activeStats.TotalGets} Misses:{activeStats.TotalMisses} HitRate:{activeStats.HitRate:P1}");
            
            var contextStats = GameplayEffectContext.GetStatistics();
            sb.AppendLine($"[GameplayEffectContext] Pool:{contextStats.PoolSize} Active:{contextStats.ActiveCount} Peak:{contextStats.PeakActive} Gets:{contextStats.TotalGets} Misses:{contextStats.TotalMisses} HitRate:{contextStats.HitRate:P1}");
            
            GASLog.Info(sb.ToString());
        }
        
        /// <summary>
        /// Resets statistics for all pools.
        /// </summary>
        public static void ResetAllStatistics()
        {
            GameplayEffectSpec.ResetStatistics();
            ActiveGameplayEffect.ResetStatistics();
            GameplayEffectContext.ResetStatistics();
        }
        
        /// <summary>
        /// Gets a summary of pool health. Returns true if all pools have acceptable hit rates (>80%).
        /// </summary>
        public static bool CheckPoolHealth(out string report)
        {
            var sb = new StringBuilder();
            bool healthy = true;
            
            var specStats = GameplayEffectSpec.GetStatistics();
            if (specStats.TotalGets > 100 && specStats.HitRate < 0.8f)
            {
                sb.AppendLine($"[WARNING] GameplayEffectSpec hit rate {specStats.HitRate:P1} < 80%. Consider increasing MinCapacity or WarmPool count.");
                healthy = false;
            }
            
            var activeStats = ActiveGameplayEffect.GetStatistics();
            if (activeStats.TotalGets > 100 && activeStats.HitRate < 0.8f)
            {
                sb.AppendLine($"[WARNING] ActiveGameplayEffect hit rate {activeStats.HitRate:P1} < 80%. Consider increasing MinCapacity or WarmPool count.");
                healthy = false;
            }
            
            var contextStats = GameplayEffectContext.GetStatistics();
            if (contextStats.TotalGets > 100 && contextStats.HitRate < 0.8f)
            {
                sb.AppendLine($"[WARNING] GameplayEffectContext hit rate {contextStats.HitRate:P1} < 80%. Consider increasing MinCapacity or WarmPool count.");
                healthy = false;
            }
            
            if (healthy)
            {
                sb.AppendLine("[OK] All pools are healthy (hit rate >= 80%)");
            }
            
            report = sb.ToString();
            return healthy;
        }
        
        #endregion
    }
}
