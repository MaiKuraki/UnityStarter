using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a granted instance of a GameplayAbility on an AbilitySystemComponent.
    /// It holds the runtime state for an ability, such as its level and whether it's currently active.
    /// </summary>
    public class GameplayAbilitySpec
    {
        #region Pool Management
        
        private static readonly Stack<GameplayAbilitySpec> pool = new Stack<GameplayAbilitySpec>(16);
        
        // Platform-adaptive pool configuration
#if UNITY_IOS || UNITY_ANDROID || UNITY_SWITCH
        private const int kDefaultMaxCapacity = 64;
        private const int kDefaultMinCapacity = 4;
        private const int kShrinkCheckInterval = 16;
        private const int kMaxShrinkPerCheck = 4;
#elif UNITY_STANDALONE || UNITY_PS4 || UNITY_PS5 || UNITY_XBOXONE || UNITY_GAMECORE
        private const int kDefaultMaxCapacity = 256;
        private const int kDefaultMinCapacity = 16;
        private const int kShrinkCheckInterval = 64;
        private const int kMaxShrinkPerCheck = 8;
#else
        private const int kDefaultMaxCapacity = 128;
        private const int kDefaultMinCapacity = 8;
        private const int kShrinkCheckInterval = 32;
        private const int kMaxShrinkPerCheck = 4;
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
        
        public static void SetMaxPoolCapacity(int maxCapacity) => s_MaxPoolCapacity = maxCapacity;
        public static void SetMinPoolCapacity(int minCapacity) => s_MinPoolCapacity = minCapacity;
        
        public static void WarmPool(int count)
        {
            for (int i = 0; i < count && (s_MaxPoolCapacity < 0 || pool.Count < s_MaxPoolCapacity); i++)
            {
                pool.Push(new GameplayAbilitySpec());
            }
        }
        
        public static void ClearPool()
        {
            pool.Clear();
            s_ActiveCount = 0;
            s_PeakActiveSinceLastCheck = 0;
            s_ReturnCounter = 0;
        }
        
        public static void AggressiveShrink()
        {
            while (pool.Count > s_MinPoolCapacity)
            {
                pool.Pop();
            }
            s_PeakActiveSinceLastCheck = s_ActiveCount;
        }
        
        public static (int PoolSize, int ActiveCount, int PeakActive, long TotalGets, long TotalMisses, float HitRate) GetStatistics()
        {
            float hitRate = s_TotalGets > 0 ? (float)(s_TotalGets - s_TotalMisses) / s_TotalGets : 0f;
            return (pool.Count, s_ActiveCount, s_PeakActive, s_TotalGets, s_TotalMisses, hitRate);
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

        /// <summary>
        /// The stateless definition of the ability. This is the template from which instances are created.
        /// </summary>
        public GameplayAbility Ability { get; private set; }

        /// <summary>
        /// A convenience accessor for the ability's Class Default Object (CDO).
        /// </summary>
        public GameplayAbility AbilityCDO => Ability;

        /// <summary>
        /// The live, stateful instance of the ability, if its instancing policy requires one.
        /// </summary>
        public GameplayAbility AbilityInstance { get; private set; }

        /// <summary>
        /// The current level of this specific granted ability.
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// A flag indicating if this ability is currently executing.
        /// </summary>
        public bool IsActive { get; internal set; }

        /// <summary>
        /// A reference to the AbilitySystemComponent that owns this ability spec.
        /// </summary>
        public AbilitySystemComponent Owner { get; private set; }

        private GameplayAbilitySpec() { }

        public static GameplayAbilitySpec Create(GameplayAbility ability, int level = 1)
        {
            s_TotalGets++;
            GameplayAbilitySpec spec;
            
            if (pool.Count > 0)
            {
                spec = pool.Pop();
            }
            else
            {
                spec = new GameplayAbilitySpec();
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
            
            spec.Ability = ability;
            spec.Level = level;
            spec.IsActive = false;
            spec.AbilityInstance = null;
            spec.Owner = null;
            return spec;
        }

        internal void Init(AbilitySystemComponent owner)
        {
            this.Owner = owner;
        }

        /// <summary>
        /// Gets the primary object to execute logic on.
        /// </summary>
        public GameplayAbility GetPrimaryInstance() => AbilityInstance ?? AbilityCDO;

        /// <summary>
        /// Creates a stateful instance of the ability if required by its instancing policy.
        /// </summary>
        internal void CreateInstance()
        {
            if (Ability.InstancingPolicy != EGameplayAbilityInstancingPolicy.NonInstanced && AbilityInstance == null)
            {
                AbilityInstance = Ability.CreatePoolableInstance();
                AbilityInstance.OnGiveAbility(new GameplayAbilityActorInfo(Owner.OwnerActor, Owner.AvatarActor), this);
            }
        }

        /// <summary>
        /// Clears the stateful instance of the ability.
        /// </summary>
        internal void ClearInstance()
        {
            if (AbilityInstance != null)
            {
                if (IsActive) AbilityInstance.CancelAbility();

                if (Ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.InstancedPerExecution)
                {
                    PoolManager.ReturnAbility(AbilityInstance);
                }
                AbilityInstance = null;
            }
        }

        /// <summary>
        /// Called when the ability is being removed from the ASC.
        /// </summary>
        internal void OnRemoveSpec()
        {
            if (AbilityInstance != null)
            {
                if (IsActive) AbilityInstance.CancelAbility();
                if (Ability.InstancingPolicy != EGameplayAbilityInstancingPolicy.NonInstanced)
                {
                    PoolManager.ReturnAbility(AbilityInstance);
                }
                AbilityInstance = null;
            }
            AbilityCDO?.OnRemoveAbility();

            // Return self to pool with capacity management
            if (s_ActiveCount > 0) s_ActiveCount--;
            
            Ability = null;
            Owner = null;
            Level = 0;
            IsActive = false;
            
            // Enforce max capacity
            if (s_MaxPoolCapacity > 0 && pool.Count >= s_MaxPoolCapacity)
            {
                return; // Discard
            }
            
            pool.Push(this);
            
            // Periodic smart shrink
            s_ReturnCounter++;
            if (s_ReturnCounter >= kShrinkCheckInterval)
            {
                s_ReturnCounter = 0;
                PerformSmartShrink();
            }
        }
    }
}