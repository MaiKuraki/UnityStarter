namespace CycloneGames.GameplayAbilities.Runtime
{
    public class GameplayAbilitySpec
    {
        public GameplayAbility Ability { get; }
        public GameplayAbility AbilityCDO => Ability;
        public GameplayAbility AbilityInstance { get; private set; }
        public int Level { get; set; }
        public bool IsActive { get; internal set; }
        public AbilitySystemComponent Owner { get; private set; }

        public GameplayAbilitySpec(GameplayAbility ability, int level = 1)
        {
            Ability = ability;
            Level = level;
        }

        /// <summary>
        /// Initializes the Spec with its owning AbilitySystemComponent.
        /// This MUST be called immediately after the spec is created.
        /// </summary>
        internal void Init(AbilitySystemComponent owner)
        {
            this.Owner = owner;
        }

        public GameplayAbility GetPrimaryInstance() => AbilityInstance ?? AbilityCDO;

        internal void CreateInstance()
        {
            if (Ability.InstancingPolicy != EGameplayAbilityInstancingPolicy.NonInstanced && AbilityInstance == null)
            {
                AbilityInstance = Ability.CreatePoolableInstance();
                AbilityInstance.OnGiveAbility(new GameplayAbilityActorInfo(Owner.OwnerActor, Owner.AvatarActor), this);
            }
        }

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
        }
    }
}