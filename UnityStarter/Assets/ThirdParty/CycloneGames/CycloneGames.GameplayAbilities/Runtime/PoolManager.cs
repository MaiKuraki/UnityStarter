using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A centralized pool manager for various types in the Ability System.
    /// Features auto-scaling with max capacity limits to prevent memory overflow on low-end devices.
    /// </summary>
    public static class PoolManager
    {
        #region Configuration
        
        // Pool configuration for memory safety on low-end devices
        private const int kDefaultMaxCapacity = 128;
        private const int kDefaultMinCapacity = 8;
        private const int kShrinkCheckInterval = 32;
        private const int kMaxShrinkPerCheck = 4;
        private const float kBufferRatio = 1.25f;
        
        private static int s_MaxPoolCapacity = kDefaultMaxCapacity;
        private static int s_MinPoolCapacity = kDefaultMinCapacity;
        
        /// <summary>
        /// Sets the maximum pool capacity for all managed pools. Set to -1 for unlimited.
        /// </summary>
        public static void SetMaxPoolCapacity(int maxCapacity) => s_MaxPoolCapacity = maxCapacity;
        
        /// <summary>
        /// Sets the minimum pool capacity to prevent over-shrinking.
        /// </summary>
        public static void SetMinPoolCapacity(int minCapacity) => s_MinPoolCapacity = minCapacity;
        
        #endregion

        #region Task Pool
        
        private static readonly Dictionary<Type, Stack<AbilityTask>> taskPools = new Dictionary<Type, Stack<AbilityTask>>();
        private static readonly Dictionary<Type, int> taskActiveCount = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, int> taskPeakCount = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, int> taskReturnCounter = new Dictionary<Type, int>();

        /// <summary>
        /// Retrieves a Task from the pool or creates a new one.
        /// </summary>
        public static T GetTask<T>() where T : AbilityTask, new()
        {
            var taskType = typeof(T);
            lock (taskPools)
            {
                if (taskPools.TryGetValue(taskType, out var pool) && pool.Count > 0)
                {
                    TrackTaskActive(taskType, 1);
                    return (T)pool.Pop();
                }
                
                TrackTaskActive(taskType, 1);
            }
            return new T();
        }

        /// <summary>
        /// Returns a Task to the pool.
        /// </summary>
        public static void ReturnTask(AbilityTask task)
        {
            var taskType = task.GetType();
            lock (taskPools)
            {
                TrackTaskActive(taskType, -1);
                
                if (!taskPools.TryGetValue(taskType, out var pool))
                {
                    pool = new Stack<AbilityTask>();
                    taskPools[taskType] = pool;
                }
                
                // Enforce max capacity
                if (s_MaxPoolCapacity > 0 && pool.Count >= s_MaxPoolCapacity)
                {
                    return; // Discard
                }
                
                pool.Push(task);
                
                // Periodic shrink check
                if (!taskReturnCounter.TryGetValue(taskType, out var counter))
                {
                    counter = 0;
                }
                taskReturnCounter[taskType] = counter + 1;
                
                if (counter + 1 >= kShrinkCheckInterval)
                {
                    taskReturnCounter[taskType] = 0;
                    PerformTaskShrink(taskType, pool);
                }
            }
        }
        
        private static void TrackTaskActive(Type taskType, int delta)
        {
            if (!taskActiveCount.TryGetValue(taskType, out var count))
            {
                count = 0;
            }
            count += delta;
            taskActiveCount[taskType] = count;
            
            if (delta > 0)
            {
                if (!taskPeakCount.TryGetValue(taskType, out var peak) || count > peak)
                {
                    taskPeakCount[taskType] = count;
                }
            }
        }
        
        private static void PerformTaskShrink(Type taskType, Stack<AbilityTask> pool)
        {
            taskPeakCount.TryGetValue(taskType, out var peak);
            taskActiveCount.TryGetValue(taskType, out var active);
            
            int targetCapacity = (int)(peak * kBufferRatio);
            targetCapacity = Math.Max(targetCapacity, s_MinPoolCapacity);
            
            int currentTotal = active + pool.Count;
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
            
            // Decay peak
            taskPeakCount[taskType] = Math.Max(active, (int)(peak * 0.5f));
        }
        
        #endregion

        #region Ability Pool
        
        private static readonly Dictionary<Type, Stack<GameplayAbility>> abilityPools = new Dictionary<Type, Stack<GameplayAbility>>();
        private static readonly Dictionary<Type, int> abilityActiveCount = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, int> abilityPeakCount = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, int> abilityReturnCounter = new Dictionary<Type, int>();

        /// <summary>
        /// Retrieves a GameplayAbility instance from the pool or creates a new one.
        /// This is crucial for InstancedPerExecution abilities to avoid GC.
        /// </summary>
        public static T GetAbility<T>() where T : GameplayAbility, new()
        {
            var abilityType = typeof(T);
            lock (abilityPools)
            {
                if (abilityPools.TryGetValue(abilityType, out var pool) && pool.Count > 0)
                {
                    TrackAbilityActive(abilityType, 1);
                    return (T)pool.Pop();
                }
                
                TrackAbilityActive(abilityType, 1);
            }
            return new T();
        }
        
        /// <summary>
        /// Returns a GameplayAbility instance to the pool.
        /// </summary>
        public static void ReturnAbility(GameplayAbility ability)
        {
            var abilityType = ability.GetType();
            // Ensure the ability is in a clean state before being pooled.
            ability.OnReturnedToPool();

            lock (abilityPools)
            {
                TrackAbilityActive(abilityType, -1);
                
                if (!abilityPools.TryGetValue(abilityType, out var pool))
                {
                    pool = new Stack<GameplayAbility>();
                    abilityPools[abilityType] = pool;
                }
                
                // Enforce max capacity
                if (s_MaxPoolCapacity > 0 && pool.Count >= s_MaxPoolCapacity)
                {
                    return; // Discard
                }
                
                pool.Push(ability);
                
                // Periodic shrink check
                if (!abilityReturnCounter.TryGetValue(abilityType, out var counter))
                {
                    counter = 0;
                }
                abilityReturnCounter[abilityType] = counter + 1;
                
                if (counter + 1 >= kShrinkCheckInterval)
                {
                    abilityReturnCounter[abilityType] = 0;
                    PerformAbilityShrink(abilityType, pool);
                }
            }
        }
        
        private static void TrackAbilityActive(Type abilityType, int delta)
        {
            if (!abilityActiveCount.TryGetValue(abilityType, out var count))
            {
                count = 0;
            }
            count += delta;
            abilityActiveCount[abilityType] = count;
            
            if (delta > 0)
            {
                if (!abilityPeakCount.TryGetValue(abilityType, out var peak) || count > peak)
                {
                    abilityPeakCount[abilityType] = count;
                }
            }
        }
        
        private static void PerformAbilityShrink(Type abilityType, Stack<GameplayAbility> pool)
        {
            abilityPeakCount.TryGetValue(abilityType, out var peak);
            abilityActiveCount.TryGetValue(abilityType, out var active);
            
            int targetCapacity = (int)(peak * kBufferRatio);
            targetCapacity = Math.Max(targetCapacity, s_MinPoolCapacity);
            
            int currentTotal = active + pool.Count;
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
            
            // Decay peak
            abilityPeakCount[abilityType] = Math.Max(active, (int)(peak * 0.5f));
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Clears all pools. Call during scene transitions or when memory is critical.
        /// </summary>
        public static void ClearAllPools()
        {
            lock (taskPools)
            {
                taskPools.Clear();
                taskActiveCount.Clear();
                taskPeakCount.Clear();
                taskReturnCounter.Clear();
            }
            
            lock (abilityPools)
            {
                abilityPools.Clear();
                abilityActiveCount.Clear();
                abilityPeakCount.Clear();
                abilityReturnCounter.Clear();
            }
        }
        
        /// <summary>
        /// Pre-warms pools during loading screens to avoid runtime allocations.
        /// </summary>
        public static void WarmTaskPool<T>(int count) where T : AbilityTask, new()
        {
            var taskType = typeof(T);
            lock (taskPools)
            {
                if (!taskPools.TryGetValue(taskType, out var pool))
                {
                    pool = new Stack<AbilityTask>();
                    taskPools[taskType] = pool;
                }
                
                for (int i = 0; i < count && (s_MaxPoolCapacity < 0 || pool.Count < s_MaxPoolCapacity); i++)
                {
                    pool.Push(new T());
                }
            }
        }
        
        /// <summary>
        /// Pre-warms ability pools during loading screens.
        /// </summary>
        public static void WarmAbilityPool<T>(int count) where T : GameplayAbility, new()
        {
            var abilityType = typeof(T);
            lock (abilityPools)
            {
                if (!abilityPools.TryGetValue(abilityType, out var pool))
                {
                    pool = new Stack<GameplayAbility>();
                    abilityPools[abilityType] = pool;
                }
                
                for (int i = 0; i < count && (s_MaxPoolCapacity < 0 || pool.Count < s_MaxPoolCapacity); i++)
                {
                    pool.Push(new T());
                }
            }
        }
        
        #endregion
    }
}