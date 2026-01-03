using System.Text;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Central utility class for managing all GAS-related object pools.
    /// Provides configuration presets for different game scenarios.
    /// </summary>
    public static class GASPoolUtility
    {
        #region Pool Configuration Presets

        /// <summary>
        /// Minimal tier: RPG, adventure, turn-based games. ~10-50 concurrent entities.
        /// Memory: ~2-4 MB pool overhead.
        /// </summary>
        public static void ConfigureMinimal()
        {
            //                                Target  Peak   Max
            GASPool<GameplayEffectSpec>.SetSharedInstance(
                new GASPool<GameplayEffectSpec>(32, 128, 256));
            GASPool<ActiveGameplayEffect>.SetSharedInstance(
                new GASPool<ActiveGameplayEffect>(64, 256, 512));
            GASPool<GameplayEffectContext>.SetSharedInstance(
                new GASPool<GameplayEffectContext>(32, 128, 256));
            GASPool<GameplayAbilitySpec>.SetSharedInstance(
                new GASPool<GameplayAbilitySpec>(16, 64, 128));

            PoolManager.SetMaxPoolCapacity(64);
            PoolManager.SetMinPoolCapacity(8);
        }

        /// <summary>
        /// Low tier: ARPG, MOBA, standard action games. ~50-200 concurrent entities.
        /// Memory: ~8-16 MB pool overhead.
        /// </summary>
        public static void ConfigureLow()
        {
            GASPool<GameplayEffectSpec>.SetSharedInstance(
                new GASPool<GameplayEffectSpec>(64, 256, 512));
            GASPool<ActiveGameplayEffect>.SetSharedInstance(
                new GASPool<ActiveGameplayEffect>(128, 512, 1024));
            GASPool<GameplayEffectContext>.SetSharedInstance(
                new GASPool<GameplayEffectContext>(64, 256, 512));
            GASPool<GameplayAbilitySpec>.SetSharedInstance(
                new GASPool<GameplayAbilitySpec>(32, 128, 256));

            PoolManager.SetMaxPoolCapacity(128);
            PoolManager.SetMinPoolCapacity(16);
        }

        /// <summary>
        /// Medium tier: Fast-paced action, arena combat. ~200-500 concurrent entities.
        /// Memory: ~32-64 MB pool overhead.
        /// </summary>
        public static void ConfigureMedium()
        {
            GASPool<GameplayEffectSpec>.SetSharedInstance(
                new GASPool<GameplayEffectSpec>(256, 1024, 2048));
            GASPool<ActiveGameplayEffect>.SetSharedInstance(
                new GASPool<ActiveGameplayEffect>(512, 2048, 4096));
            GASPool<GameplayEffectContext>.SetSharedInstance(
                new GASPool<GameplayEffectContext>(256, 1024, 2048));
            GASPool<GameplayAbilitySpec>.SetSharedInstance(
                new GASPool<GameplayAbilitySpec>(128, 512, 1024));

            PoolManager.SetMaxPoolCapacity(256);
            PoolManager.SetMinPoolCapacity(32);
        }

        /// <summary>
        /// High tier: Vampire survivors, tower defense, RTS. ~500-2000 concurrent entities.
        /// Supports: ~1000 abilities, ~3000 effects, ~3000 contexts.
        /// Memory: ~128-256 MB pool overhead.
        /// </summary>
        public static void ConfigureHigh()
        {
            GASPool<GameplayEffectSpec>.SetSharedInstance(
                new GASPool<GameplayEffectSpec>(512, 2048, 8192));
            GASPool<ActiveGameplayEffect>.SetSharedInstance(
                new GASPool<ActiveGameplayEffect>(1024, 4096, 16384));
            GASPool<GameplayEffectContext>.SetSharedInstance(
                new GASPool<GameplayEffectContext>(512, 2048, 8192));
            GASPool<GameplayAbilitySpec>.SetSharedInstance(
                new GASPool<GameplayAbilitySpec>(256, 1024, 4096));

            PoolManager.SetMaxPoolCapacity(512);
            PoolManager.SetMinPoolCapacity(64);
        }

        /// <summary>
        /// Ultra tier: Bullet hell, extreme massive battles. 2000+ concurrent entities.
        /// Supports: ~2000 abilities, ~8000 effects, ~8000 contexts.
        /// Memory: ~512 MB+ pool overhead. Use only on high-end devices.
        /// </summary>
        public static void ConfigureUltra()
        {
            GASPool<GameplayEffectSpec>.SetSharedInstance(
                new GASPool<GameplayEffectSpec>(1024, 4096, 16384));
            GASPool<ActiveGameplayEffect>.SetSharedInstance(
                new GASPool<ActiveGameplayEffect>(2048, 8192, 32768));
            GASPool<GameplayEffectContext>.SetSharedInstance(
                new GASPool<GameplayEffectContext>(1024, 4096, 16384));
            GASPool<GameplayAbilitySpec>.SetSharedInstance(
                new GASPool<GameplayAbilitySpec>(512, 2048, 8192));

            PoolManager.SetMaxPoolCapacity(1024);
            PoolManager.SetMinPoolCapacity(128);
        }

        /// <summary>
        /// Mobile optimized: Lower memory ceiling for mobile/WebGL devices.
        /// Memory: ~4-8 MB pool overhead.
        /// </summary>
        public static void ConfigureMobile()
        {
            GASPool<GameplayEffectSpec>.SetSharedInstance(
                new GASPool<GameplayEffectSpec>(32, 128, 256));
            GASPool<ActiveGameplayEffect>.SetSharedInstance(
                new GASPool<ActiveGameplayEffect>(64, 256, 512));
            GASPool<GameplayEffectContext>.SetSharedInstance(
                new GASPool<GameplayEffectContext>(32, 128, 256));
            GASPool<GameplayAbilitySpec>.SetSharedInstance(
                new GASPool<GameplayAbilitySpec>(16, 64, 128));

            PoolManager.SetMaxPoolCapacity(64);
            PoolManager.SetMinPoolCapacity(4);
        }

        #endregion

        #region Pre-warming

        /// <summary>
        /// Pre-warms all pools to target capacity. Call during loading screens.
        /// </summary>
        public static void WarmAllPools()
        {
            GASPool<GameplayEffectSpec>.Shared.Warm();
            GASPool<ActiveGameplayEffect>.Shared.Warm();
            GASPool<GameplayEffectContext>.Shared.Warm();
            GASPool<GameplayAbilitySpec>.Shared.Warm();
        }

        /// <summary>
        /// Pre-warms all pools with custom counts.
        /// </summary>
        public static void WarmAllPools(int effectSpecCount, int activeEffectCount,
            int contextCount, int abilitySpecCount)
        {
            GASPool<GameplayEffectSpec>.Shared.Warm(effectSpecCount);
            GASPool<ActiveGameplayEffect>.Shared.Warm(activeEffectCount);
            GASPool<GameplayEffectContext>.Shared.Warm(contextCount);
            GASPool<GameplayAbilitySpec>.Shared.Warm(abilitySpecCount);
        }

        #endregion

        #region Scene Transition / Cleanup

        /// <summary>
        /// Aggressively shrinks all pools to target capacity.
        /// </summary>
        public static void AggressiveShrinkAll()
        {
            GASPool<GameplayEffectSpec>.Shared.AggressiveShrink();
            GASPool<ActiveGameplayEffect>.Shared.AggressiveShrink();
            GASPool<GameplayEffectContext>.Shared.AggressiveShrink();
            GASPool<GameplayAbilitySpec>.Shared.AggressiveShrink();
        }

        /// <summary>
        /// Clears all pools completely.
        /// </summary>
        public static void ClearAllPools()
        {
            GASPool<GameplayEffectSpec>.Shared.Clear();
            GASPool<ActiveGameplayEffect>.Shared.Clear();
            GASPool<GameplayEffectContext>.Shared.Clear();
            GASPool<GameplayAbilitySpec>.Shared.Clear();
            PoolManager.ClearAllPools();
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Logs statistics for all pools.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void LogAllStatistics()
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("=== GAS Pool Statistics ===");

            AppendPoolStats<GameplayEffectSpec>(sb, "EffectSpec");
            AppendPoolStats<ActiveGameplayEffect>(sb, "ActiveEffect");
            AppendPoolStats<GameplayEffectContext>(sb, "Context");
            AppendPoolStats<GameplayAbilitySpec>(sb, "AbilitySpec");

            GASLog.Info(sb.ToString());
        }

        private static void AppendPoolStats<T>(StringBuilder sb, string name)
            where T : class, IGASPoolable, new()
        {
            var stats = GASPool<T>.Shared.GetStatistics();
            sb.AppendLine($"[{name}] Pool={stats.PoolSize} Active={stats.ActiveCount} " +
                          $"Peak={stats.PeakActive} Hit={stats.HitRate:P0} " +
                          $"Caps=[{stats.TargetCapacity}/{stats.PeakCapacity}/{stats.MaxCapacity}]");
        }

        /// <summary>
        /// Resets statistics for all pools.
        /// </summary>
        public static void ResetAllStatistics()
        {
            GASPool<GameplayEffectSpec>.Shared.ResetStatistics();
            GASPool<ActiveGameplayEffect>.Shared.ResetStatistics();
            GASPool<GameplayEffectContext>.Shared.ResetStatistics();
            GASPool<GameplayAbilitySpec>.Shared.ResetStatistics();
        }

        /// <summary>
        /// Checks pool health. Returns true if all hit rates >= 80%.
        /// </summary>
        public static bool CheckPoolHealth(out string report)
        {
            var sb = new StringBuilder(256);
            bool healthy = true;

            CheckSinglePoolHealth<GameplayEffectSpec>(sb, "EffectSpec", ref healthy);
            CheckSinglePoolHealth<ActiveGameplayEffect>(sb, "ActiveEffect", ref healthy);
            CheckSinglePoolHealth<GameplayEffectContext>(sb, "Context", ref healthy);
            CheckSinglePoolHealth<GameplayAbilitySpec>(sb, "AbilitySpec", ref healthy);

            if (healthy)
            {
                sb.AppendLine("[OK] All pools healthy (hit rate >= 80%)");
            }

            report = sb.ToString();
            return healthy;
        }

        private static void CheckSinglePoolHealth<T>(StringBuilder sb, string name, ref bool healthy)
            where T : class, IGASPoolable, new()
        {
            var stats = GASPool<T>.Shared.GetStatistics();
            if (stats.TotalGets > 100 && stats.HitRate < 0.8f)
            {
                sb.AppendLine($"[WARN] {name} hit rate {stats.HitRate:P0} < 80%. Increase Target capacity.");
                healthy = false;
            }
        }

        #endregion
    }
}
