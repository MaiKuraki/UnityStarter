using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a stateful instance of a GameplayEffect that is currently active on an AbilitySystemComponent.
    /// It tracks state such as remaining time and stack count.
    /// Instances of this class should be acquired from an object pool.
    /// </summary>
    public class ActiveGameplayEffect
    {
        #region Pool Management
        
        private static readonly Stack<ActiveGameplayEffect> pool = new Stack<ActiveGameplayEffect>(64);
        
        // Platform-adaptive pool configuration for memory safety
#if UNITY_IOS || UNITY_ANDROID || UNITY_SWITCH
        private const int kDefaultMaxCapacity = 256;   // Mobile/Switch: conservative memory
        private const int kDefaultMinCapacity = 16;
        private const int kShrinkCheckInterval = 32;
        private const int kMaxShrinkPerCheck = 8;
#elif UNITY_STANDALONE || UNITY_PS4 || UNITY_PS5 || UNITY_XBOXONE || UNITY_GAMECORE
        private const int kDefaultMaxCapacity = 1024;  // PC/Console: larger pools for complex combat
        private const int kDefaultMinCapacity = 64;
        private const int kShrinkCheckInterval = 128;
        private const int kMaxShrinkPerCheck = 16;
#else
        private const int kDefaultMaxCapacity = 512;   // Default fallback
        private const int kDefaultMinCapacity = 32;
        private const int kShrinkCheckInterval = 64;
        private const int kMaxShrinkPerCheck = 8;
#endif
        private const float kBufferRatio = 1.25f;
        
        private static int s_MaxPoolCapacity = kDefaultMaxCapacity;
        private static int s_MinPoolCapacity = kDefaultMinCapacity;
        private static int s_PeakActiveSinceLastCheck = 0;
        private static int s_ActiveCount = 0;
        private static int s_ReturnCounter = 0;
        
        // Statistics
        private static long s_TotalGets = 0;
        private static long s_TotalMisses = 0;
        private static int s_PeakActive = 0;

        /// <summary>
        /// Sets the maximum pool capacity. Set to -1 for unlimited (not recommended for low-end devices).
        /// </summary>
        public static void SetMaxPoolCapacity(int maxCapacity) => s_MaxPoolCapacity = maxCapacity;
        
        /// <summary>
        /// Sets the minimum pool capacity to prevent over-shrinking.
        /// </summary>
        public static void SetMinPoolCapacity(int minCapacity) => s_MinPoolCapacity = minCapacity;
        
        /// <summary>
        /// Pre-warms the pool with a specified number of instances.
        /// </summary>
        public static void WarmPool(int count)
        {
            for (int i = 0; i < count && (s_MaxPoolCapacity < 0 || pool.Count < s_MaxPoolCapacity); i++)
            {
                pool.Push(new ActiveGameplayEffect());
            }
        }
        
        /// <summary>
        /// Clears the entire pool.
        /// </summary>
        public static void ClearPool()
        {
            pool.Clear();
            s_ActiveCount = 0;
            s_PeakActiveSinceLastCheck = 0;
            s_ReturnCounter = 0;
        }
        
        /// <summary>
        /// Aggressively shrinks the pool to minimum capacity.
        /// Call during scene transitions to free memory.
        /// </summary>
        public static void AggressiveShrink()
        {
            while (pool.Count > s_MinPoolCapacity)
            {
                pool.Pop();
            }
            s_PeakActiveSinceLastCheck = s_ActiveCount;
        }
        
        /// <summary>
        /// Gets pool statistics for profiling and tuning.
        /// </summary>
        public static (int PoolSize, int ActiveCount, int PeakActive, long TotalGets, long TotalMisses, float HitRate) GetStatistics()
        {
            float hitRate = s_TotalGets > 0 ? (float)(s_TotalGets - s_TotalMisses) / s_TotalGets : 0f;
            return (pool.Count, s_ActiveCount, s_PeakActive, s_TotalGets, s_TotalMisses, hitRate);
        }
        
        /// <summary>
        /// Resets pool statistics.
        /// </summary>
        public static void ResetStatistics()
        {
            s_TotalGets = 0;
            s_TotalMisses = 0;
            s_PeakActive = s_ActiveCount;
        }
        
        private static void PerformSmartShrink()
        {
            int targetCapacity = (int)(s_PeakActiveSinceLastCheck * kBufferRatio);
            targetCapacity = Math.Max(targetCapacity, s_MinPoolCapacity);
            
            int currentTotal = s_ActiveCount + pool.Count;
            if (currentTotal > targetCapacity && pool.Count > s_MinPoolCapacity)
            {
                int excess = currentTotal - targetCapacity;
                int toRemove = Math.Min(excess, kMaxShrinkPerCheck);
                toRemove = Math.Min(toRemove, pool.Count - s_MinPoolCapacity);
                
                for (int i = 0; i < toRemove && pool.Count > s_MinPoolCapacity; i++)
                {
                    pool.Pop();
                }
            }
            
            s_PeakActiveSinceLastCheck = Math.Max(s_ActiveCount, (int)(s_PeakActiveSinceLastCheck * 0.5f));
        }
        
        #endregion

        public GameplayEffectSpec Spec { get; private set; }
        public float TimeRemaining { get; private set; }
        public int StackCount { get; private set; }
        public bool IsExpired { get; private set; }

        private float periodTimer;
        private float cachedPeriod;
        private EDurationPolicy cachedDurationPolicy;

        private ActiveGameplayEffect() { }

        public static ActiveGameplayEffect Create(GameplayEffectSpec spec)
        {
            s_TotalGets++;
            ActiveGameplayEffect activeEffect;
            
            if (pool.Count > 0)
            {
                activeEffect = pool.Pop();
            }
            else
            {
                activeEffect = new ActiveGameplayEffect();
                s_TotalMisses++;
            }
            
            // Track active count for auto-scaling
            s_ActiveCount++;
            if (s_ActiveCount > s_PeakActiveSinceLastCheck)
            {
                s_PeakActiveSinceLastCheck = s_ActiveCount;
            }
            if (s_ActiveCount > s_PeakActive)
            {
                s_PeakActive = s_ActiveCount;
            }
            
            activeEffect.Spec = spec;
            activeEffect.TimeRemaining = spec.Duration;
            activeEffect.StackCount = 1;
            activeEffect.IsExpired = false;

            // Cache high-frequency data
            activeEffect.cachedPeriod = spec.Def.Period;
            activeEffect.cachedDurationPolicy = spec.Def.DurationPolicy;

            // If the effect is periodic, set the timer to 0 to ensure the first tick executes on the very next AbilitySystemComponent update.
            // This implements the common behavior where a periodic effect's first tick is applied immediately upon application.
            activeEffect.periodTimer = activeEffect.cachedPeriod > 0 ? 0f : -1f;

            return activeEffect;
        }

        public void ReturnToPool()
        {
            // Track active count for auto-scaling
            if (s_ActiveCount > 0) s_ActiveCount--;
            
            Spec?.ReturnToPool();
            Spec = null;
            TimeRemaining = 0;
            StackCount = 0;
            IsExpired = false;
            periodTimer = -1f;
            
            // Enforce max capacity to prevent memory overflow
            if (s_MaxPoolCapacity > 0 && pool.Count >= s_MaxPoolCapacity)
            {
                return; // Discard this instance
            }
            
            pool.Push(this);
            
            // Periodic smart shrink check
            s_ReturnCounter++;
            if (s_ReturnCounter >= kShrinkCheckInterval)
            {
                s_ReturnCounter = 0;
                PerformSmartShrink();
            }
        }

        /// <summary>
        /// Called when a new stack is successfully applied to this existing effect.
        /// </summary>
        public void OnStackApplied()
        {
            StackCount = Math.Min(StackCount + 1, Spec.Def.Stacking.Limit);

            if (Spec.Def.Stacking.DurationPolicy == EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication)
            {
                TimeRemaining = Spec.Duration;
            }

            if (periodTimer > 0)
            {
                periodTimer = cachedPeriod;
            }
        }

        /// <summary>
        /// Refreshes this effect's remaining time and period timer without modifying the stack count.
        /// Useful when a new application occurs while already at max stacks and the policy requires a refresh.
        /// </summary>
        public void RefreshDurationAndPeriod()
        {
            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                TimeRemaining = Spec.Duration;
            }

            if (periodTimer >= 0)
            {
                periodTimer = cachedPeriod;
            }
        }

        /// <summary>
        /// Ticks the effect's duration and period timer.
        /// </summary>
        /// <param name="deltaTime">The time since the last frame.</param>
        /// <param name="asc">The owning AbilitySystemComponent to execute periodic effects on.</param>
        /// <returns>True if the effect expired this tick, false otherwise.</returns>
        public bool Tick(float deltaTime, AbilitySystemComponent asc)
        {
            // --- Duration Handling ---
            if (!IsExpired && cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                TimeRemaining -= deltaTime;
                if (TimeRemaining <= 0)
                {
                    IsExpired = true;
                }
            }

            // --- Periodic Effect Handling ---
            if (!IsExpired && periodTimer >= 0)
            {
                periodTimer -= deltaTime;
                if (periodTimer <= 0)
                {
                    // Period has elapsed, execute the effect's instant logic.
                    // Note: Periodic effect executions are not predicted in this model.
                    asc.ExecuteInstantEffect(this.Spec);

                    // Reset the timer for the next period, carrying over any leftover time.
                    // This prevents timer drift due to frame rate fluctuations.
                    periodTimer += cachedPeriod;
                }
            }

            return IsExpired;
        }
    }
}
