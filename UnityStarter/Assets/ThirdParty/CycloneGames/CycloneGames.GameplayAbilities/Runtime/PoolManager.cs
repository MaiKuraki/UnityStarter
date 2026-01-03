using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A centralized pool manager for GameplayAbility and AbilityTask types.
    /// Uses type-based dictionary lookup for per-type pooling.
    /// For other GAS types, use GASPool&lt;T&gt; directly.
    /// </summary>
    public static class PoolManager
    {
        #region Configuration
        
        // Platform-adaptive pool configuration
#if UNITY_IOS || UNITY_ANDROID || UNITY_SWITCH
        private const int kDefaultMaxCapacity = 64;
        private const int kDefaultMinCapacity = 4;
#elif UNITY_STANDALONE || UNITY_PS4 || UNITY_PS5 || UNITY_XBOXONE || UNITY_GAMECORE
        private const int kDefaultMaxCapacity = 256;
        private const int kDefaultMinCapacity = 16;
#else
        private const int kDefaultMaxCapacity = 128;
        private const int kDefaultMinCapacity = 8;
#endif
        
        private static int s_MaxPoolCapacity = kDefaultMaxCapacity;
        private static int s_MinPoolCapacity = kDefaultMinCapacity;
        
        public static void SetMaxPoolCapacity(int maxCapacity) => s_MaxPoolCapacity = maxCapacity;
        public static void SetMinPoolCapacity(int minCapacity) => s_MinPoolCapacity = minCapacity;
        
        #endregion

        #region Task Pool
        
        private static readonly Dictionary<Type, Stack<AbilityTask>> taskPools = new Dictionary<Type, Stack<AbilityTask>>();
        private static readonly object taskLock = new object();

        public static T GetTask<T>() where T : AbilityTask, new()
        {
            var taskType = typeof(T);
            lock (taskLock)
            {
                if (taskPools.TryGetValue(taskType, out var pool) && pool.Count > 0)
                {
                    return (T)pool.Pop();
                }
            }
            return new T();
        }

        public static void ReturnTask(AbilityTask task)
        {
            var taskType = task.GetType();
            lock (taskLock)
            {
                if (!taskPools.TryGetValue(taskType, out var pool))
                {
                    pool = new Stack<AbilityTask>();
                    taskPools[taskType] = pool;
                }
                
                if (s_MaxPoolCapacity > 0 && pool.Count >= s_MaxPoolCapacity)
                {
                    return; // Discard
                }
                
                pool.Push(task);
            }
        }
        
        #endregion

        #region Ability Pool
        
        private static readonly Dictionary<Type, Stack<GameplayAbility>> abilityPools = new Dictionary<Type, Stack<GameplayAbility>>();
        private static readonly object abilityLock = new object();

        public static T GetAbility<T>() where T : GameplayAbility, new()
        {
            var abilityType = typeof(T);
            lock (abilityLock)
            {
                if (abilityPools.TryGetValue(abilityType, out var pool) && pool.Count > 0)
                {
                    return (T)pool.Pop();
                }
            }
            return new T();
        }
        
        public static void ReturnAbility(GameplayAbility ability)
        {
            var abilityType = ability.GetType();
            ability.OnReturnedToPool();

            lock (abilityLock)
            {
                if (!abilityPools.TryGetValue(abilityType, out var pool))
                {
                    pool = new Stack<GameplayAbility>();
                    abilityPools[abilityType] = pool;
                }
                
                if (s_MaxPoolCapacity > 0 && pool.Count >= s_MaxPoolCapacity)
                {
                    return; // Discard
                }
                
                pool.Push(ability);
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Clears all pools. Call during scene transitions.
        /// </summary>
        public static void ClearAllPools()
        {
            lock (taskLock)
            {
                taskPools.Clear();
            }
            
            lock (abilityLock)
            {
                abilityPools.Clear();
            }
            
            // Also trigger GASPool registry clear
            GASPoolRegistry.ClearAll();
        }
        
        /// <summary>
        /// Pre-warms task pool during loading screens.
        /// </summary>
        public static void WarmTaskPool<T>(int count) where T : AbilityTask, new()
        {
            var taskType = typeof(T);
            lock (taskLock)
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
        /// Pre-warms ability pool during loading screens.
        /// </summary>
        public static void WarmAbilityPool<T>(int count) where T : GameplayAbility, new()
        {
            var abilityType = typeof(T);
            lock (abilityLock)
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