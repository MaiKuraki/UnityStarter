using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public interface IGameplayEffectContext
    {
        AbilitySystemComponent Instigator { get; }
        GameplayAbility AbilityInstance { get; }
        TargetData TargetData { get; }
        PredictionKey PredictionKey { get; set; }

        void AddInstigator(AbilitySystemComponent instigator, GameplayAbility abilityInstance);
        void AddTargetData(TargetData targetData);
        void Reset();
    }

    public class GameplayEffectContext : IGameplayEffectContext
    {
        #region Pool Management
        
        private static readonly Stack<GameplayEffectContext> pool = new Stack<GameplayEffectContext>(64);
        
        // Pool configuration for memory safety on low-end devices
        private const int kDefaultMaxCapacity = 256;
        private const int kDefaultMinCapacity = 32;
        private const int kShrinkCheckInterval = 64;
        private const int kMaxShrinkPerCheck = 8;
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
        /// Sets the maximum pool capacity. Set to -1 for unlimited.
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
                pool.Push(new GameplayEffectContext());
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

        public AbilitySystemComponent Instigator { get; private set; }
        public GameplayAbility AbilityInstance { get; private set; }
        public TargetData TargetData { get; private set; }
        public PredictionKey PredictionKey { get; set; }

        public GameplayEffectContext() { }

        internal static GameplayEffectContext Get()
        {
            s_TotalGets++;
            GameplayEffectContext context;
            
            if (pool.Count > 0)
            {
                context = pool.Pop();
            }
            else
            {
                context = new GameplayEffectContext();
                s_TotalMisses++;
            }
            
            s_ActiveCount++;
            if (s_ActiveCount > s_PeakActiveSinceLastCheck)
            {
                s_PeakActiveSinceLastCheck = s_ActiveCount;
            }
            if (s_ActiveCount > s_PeakActive)
            {
                s_PeakActive = s_ActiveCount;
            }
            
            return context;
        }

        public void AddInstigator(AbilitySystemComponent instigator, GameplayAbility abilityInstance)
        {
            Instigator = instigator;
            AbilityInstance = abilityInstance;
        }

        public void AddTargetData(TargetData data)
        {
            TargetData = data;
        }

        public void Reset()
        {
            Instigator = null;
            AbilityInstance = null;
            TargetData = null;
            PredictionKey = default;
        }

        public void ReturnToPool()
        {
            if (s_ActiveCount > 0) s_ActiveCount--;
            
            Reset();
            
            // Enforce max capacity
            if (s_MaxPoolCapacity > 0 && pool.Count >= s_MaxPoolCapacity)
            {
                return;
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
    }
}