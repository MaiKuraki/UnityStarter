using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    // A centralized pool manager for various types in the Ability System.
    public static class PoolManager
    {
        // Pool for AbilityTask instances.
        private static readonly Dictionary<Type, Stack<AbilityTask>> taskPools = new Dictionary<Type, Stack<AbilityTask>>();
        // Pool for GameplayAbility instances.
        private static readonly Dictionary<Type, Stack<GameplayAbility>> abilityPools = new Dictionary<Type, Stack<GameplayAbility>>();

        /// <summary>
        /// Retrieves a Task from the pool or creates a new one.
        /// </summary>
        public static T GetTask<T>() where T : AbilityTask, new()
        {
            var taskType = typeof(T);
            if (taskPools.TryGetValue(taskType, out var pool) && pool.Count > 0)
            {
                return (T)pool.Pop();
            }
            return new T();
        }

        /// <summary>
        /// Returns a Task to the pool.
        /// </summary>
        public static void ReturnTask(AbilityTask task)
        {
            var taskType = task.GetType();
            if (!taskPools.TryGetValue(taskType, out var pool))
            {
                pool = new Stack<AbilityTask>();
                taskPools[taskType] = pool;
            }
            pool.Push(task);
        }

        /// <summary>
        /// Retrieves a GameplayAbility instance from the pool or creates a new one.
        /// This is crucial for InstancedPerExecution abilities to avoid GC.
        /// </summary>
        public static T GetAbility<T>() where T : GameplayAbility, new()
        {
            var abilityType = typeof(T);
            if (abilityPools.TryGetValue(abilityType, out var pool) && pool.Count > 0)
            {
                return (T)pool.Pop();
            }
            return new T();
        }
        
        /// <summary>
        /// Returns a GameplayAbility instance to the pool.
        /// </summary>
        public static void ReturnAbility(GameplayAbility ability)
        {
            var abilityType = ability.GetType();
            if (!abilityPools.TryGetValue(abilityType, out var pool))
            {
                pool = new Stack<GameplayAbility>();
                abilityPools[abilityType] = pool;
            }
            
            // Ensure the ability is in a clean state before being pooled.
            ability.OnReturnedToPool();
            pool.Push(ability);
        }
    }
}