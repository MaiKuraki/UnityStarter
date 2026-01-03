namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a granted instance of a GameplayAbility on an AbilitySystemComponent.
    /// Holds runtime state such as level and active status.
    /// Pooled via GASPool for zero-allocation runtime.
    /// </summary>
    public class GameplayAbilitySpec : IGASPoolable
    {
        /// <summary>
        /// The stateless definition of the ability (template).
        /// </summary>
        public GameplayAbility Ability { get; private set; }

        /// <summary>
        /// Convenience accessor for the ability's CDO.
        /// </summary>
        public GameplayAbility AbilityCDO => Ability;

        /// <summary>
        /// The live, stateful instance if instancing policy requires one.
        /// </summary>
        public GameplayAbility AbilityInstance { get; private set; }

        /// <summary>
        /// The current level of this granted ability.
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Flag indicating if this ability is currently executing.
        /// </summary>
        public bool IsActive { get; internal set; }

        /// <summary>
        /// Reference to the owning ASC.
        /// </summary>
        public AbilitySystemComponent Owner { get; private set; }

        public GameplayAbilitySpec() { }

        #region IGASPoolable Implementation

        void IGASPoolable.OnGetFromPool()
        {
            // Initialization happens in Initialize()
        }

        void IGASPoolable.OnReturnToPool()
        {
            Ability = null;
            AbilityInstance = null;
            Owner = null;
            Level = 0;
            IsActive = false;
        }

        #endregion

        #region Factory

        public static GameplayAbilitySpec Create(GameplayAbility ability, int level = 1)
        {
            var spec = GASPool<GameplayAbilitySpec>.Shared.Get();
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

        #endregion

        /// <summary>
        /// Gets the primary object to execute logic on.
        /// </summary>
        public GameplayAbility GetPrimaryInstance() => AbilityInstance ?? AbilityCDO;

        /// <summary>
        /// Creates a stateful instance if required by instancing policy.
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
        /// Clears the stateful instance.
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

            // Return self to pool
            GASPool<GameplayAbilitySpec>.Shared.Return(this);
        }
    }
}