namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a granted instance of a GameplayAbility on an AbilitySystemComponent.
    /// It holds the runtime state for an ability, such as its level and whether it's currently active.
    /// </summary>
    public class GameplayAbilitySpec
    {
        private static readonly System.Collections.Generic.Stack<GameplayAbilitySpec> pool = new System.Collections.Generic.Stack<GameplayAbilitySpec>(16);

        /// <summary>
        /// The stateless definition of the ability. This is the template from which instances are created.
        /// </summary>
        public GameplayAbility Ability { get; private set; }

        /// <summary>
        /// A convenience accessor for the ability's Class Default Object (CDO).
        /// This is the primary object for NonInstanced abilities.
        /// </summary>
        public GameplayAbility AbilityCDO => Ability;

        /// <summary>
        /// The live, stateful instance of the ability, if its instancing policy requires one.
        /// This will be null for NonInstanced abilities.
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
            var spec = pool.Count > 0 ? pool.Pop() : new GameplayAbilitySpec();
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
        /// Gets the primary object to execute logic on. Returns the live instance if it exists,
        /// otherwise falls back to the Class Default Object (for NonInstanced abilities).
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
        /// Clears the stateful instance of the ability, returning it to the pool if necessary.
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
        /// Called when the ability is being removed from the ASC. Ensures proper cleanup.
        /// </summary>
        internal void OnRemoveSpec()
        {
            if (AbilityInstance != null)
            {
                if (IsActive) AbilityInstance.CancelAbility();
                if (Ability.InstancingPolicy != EGameplayAbilityInstancingPolicy.NonInstanced)
                {
                    // For PerActor instances, it's returned to pool on removal.
                    PoolManager.ReturnAbility(AbilityInstance);
                }
                AbilityInstance = null;
            }
            AbilityCDO?.OnRemoveAbility();

            // Return self to pool
            Ability = null;
            Owner = null;
            Level = 0;
            IsActive = false;
            pool.Push(this);
        }
    }
}