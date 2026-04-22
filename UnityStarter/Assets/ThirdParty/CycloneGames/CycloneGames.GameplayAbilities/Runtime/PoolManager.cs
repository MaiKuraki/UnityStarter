using System;
using System.Collections.Concurrent;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A centralized pool manager for GameplayAbility and AbilityTask types.
    /// Uses type-based dictionary lookup for per-type pooling.
    /// For other GAS types, use GASPool<T> directly.
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

        //  ConcurrentDictionary<Type, ConcurrentStack<>> replaces Dictionary + lock.
        // ConcurrentStack.TryPop/Push are lock-free on the hot path, eliminating contention
        // for high-frequency tasks (WaitDelay, WaitAttributeChange, etc.).
        private static readonly ConcurrentDictionary<Type, ConcurrentStack<AbilityTask>> taskPools
            = new ConcurrentDictionary<Type, ConcurrentStack<AbilityTask>>();

        public static T GetTask<T>() where T : AbilityTask, new()
        {
            var pool = taskPools.GetOrAdd(typeof(T), _ => new ConcurrentStack<AbilityTask>());
            if (pool.TryPop(out var item))
                return (T)item;
            return new T();
        }

        public static void ReturnTask(AbilityTask task)
        {
            var pool = taskPools.GetOrAdd(task.GetType(), _ => new ConcurrentStack<AbilityTask>());
            if (s_MaxPoolCapacity > 0 && pool.Count >= s_MaxPoolCapacity)
                return; // Discard
            pool.Push(task);
        }

        #endregion

        #region Ability Pool

        private static readonly ConcurrentDictionary<Type, ConcurrentStack<GameplayAbility>> abilityPools
            = new ConcurrentDictionary<Type, ConcurrentStack<GameplayAbility>>();

        public static T GetAbility<T>() where T : GameplayAbility, new()
        {
            var pool = abilityPools.GetOrAdd(typeof(T), _ => new ConcurrentStack<GameplayAbility>());
            if (pool.TryPop(out var item))
                return (T)item;
            return new T();
        }

        public static void ReturnAbility(GameplayAbility ability)
        {
            ability.OnReturnedToPool();
            var pool = abilityPools.GetOrAdd(ability.GetType(), _ => new ConcurrentStack<GameplayAbility>());
            if (s_MaxPoolCapacity > 0 && pool.Count >= s_MaxPoolCapacity)
                return; // Discard
            pool.Push(ability);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Clears all pools. Call during scene transitions.
        /// </summary>
        public static void ClearAllPools()
        {
            taskPools.Clear();
            abilityPools.Clear();

            // Also trigger GASPool registry clear
            GASPoolRegistry.ClearAll();
        }

        /// <summary>
        /// Pre-warms task pool during loading screens.
        /// </summary>
        public static void WarmTaskPool<T>(int count) where T : AbilityTask, new()
        {
            var pool = taskPools.GetOrAdd(typeof(T), _ => new ConcurrentStack<AbilityTask>());
            for (int i = 0; i < count && (s_MaxPoolCapacity < 0 || pool.Count < s_MaxPoolCapacity); i++)
                pool.Push(new T());
        }

        /// <summary>
        /// Pre-warms ability pool during loading screens.
        /// </summary>
        public static void WarmAbilityPool<T>(int count) where T : GameplayAbility, new()
        {
            var pool = abilityPools.GetOrAdd(typeof(T), _ => new ConcurrentStack<GameplayAbility>());
            for (int i = 0; i < count && (s_MaxPoolCapacity < 0 || pool.Count < s_MaxPoolCapacity); i++)
                pool.Push(new T());
        }

        #endregion
    }
}