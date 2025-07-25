﻿using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Determines how an ability is instantiated when activated.
    /// </summary>
    public enum EGameplayAbilityInstancingPolicy
    {
        /// <summary>
        /// The ability runs on its Class Default Object (CDO). No new instance is created.
        /// Best for performance-critical, simple, stateless abilities (e.g., a basic jump).
        /// Cannot maintain state (member variables) across frames or activations. Cannot use latent AbilityTasks.
        /// </summary>
        NonInstanced,
        /// <summary>
        /// A single instance of the ability is created per actor (ASC) when the ability is granted.
        /// This instance is reused for every activation. Can maintain state across activations.
        /// This is the most common and versatile policy.
        /// </summary>
        InstancedPerActor,
        /// <summary>
        /// A new instance of the ability is created every time it is activated and destroyed when it ends.
        /// Guarantees a clean state on every execution but has higher performance overhead due to object creation/destruction.
        /// </summary>
        InstancedPerExecution
    }

    /// <summary>
    /// Defines where the primary logic of the ability executes in a networked environment.
    /// </summary>
    public enum ENetExecutionPolicy
    {
        /// <summary>
        /// The ability runs only on the client that activated it. It does not execute on the server.
        /// Useful for purely cosmetic abilities that have no impact on gameplay state.
        /// </summary>
        LocalOnly,
        /// <summary>
        /// The ability executes first on the owning client for immediate feedback, and then on the server.
        /// The server's execution is authoritative and will correct the client if there's a discrepancy (misprediction).
        /// Essential for responsive, networked gameplay.
        /// </summary>
        LocalPredicted,
        /// <summary>
        /// The ability executes only on the server. The client sends a request to the server to run the ability.
        /// This is the most secure option and is suitable for abilities where responsiveness is not critical or for AI-controlled characters.
        /// </summary>
        ServerOnly
    }

    /// <summary>
    /// A container for references to the core actors involved in an ability's execution.
    /// </summary>
    public struct GameplayAbilityActorInfo
    {
        /// <summary>
        /// The logical owner of the AbilitySystemComponent, often a PlayerState or the character itself.
        /// This actor is responsible for the lifetime of the ASC.
        /// </summary>
        public readonly object OwnerActor;
        /// <summary>
        /// The physical representation of the owner in the game world, typically a Character or Pawn.
        /// This is the actor that performs animations, has a transform, etc.
        /// </summary>
        public readonly object AvatarActor;

        public GameplayAbilityActorInfo(object owner, object avatar)
        {
            OwnerActor = owner;
            AvatarActor = avatar;
        }
    }

    /// <summary>
    /// Contains transient information specific to a single activation of an ability.
    /// </summary>
    public struct GameplayAbilityActivationInfo
    {
        /// <summary>
        /// A unique key generated by the client for a predicted ability activation.
        /// This key is used to associate client-side predicted effects with the server's authoritative confirmation,
        /// enabling rollback of mispredictions.
        /// </summary>
        public PredictionKey PredictionKey { get; set; }
    }

    /// <summary>
    /// The base class for all gameplay abilities. It defines the logic and properties of a single skill or action an actor can perform.
    /// This class is designed to be subclassed to implement specific abilities.
    /// </summary>
    public abstract class GameplayAbility
    {
        #region Configuration Properties

        /// <summary>
        /// The display name of the ability, primarily used for debugging and logging.
        /// </summary>
        public string Name { get; protected set; }

        /// <summary>
        /// Defines how this ability is instantiated upon activation. See <see cref="EGameplayAbilityInstancingPolicy"/>.
        /// </summary>
        public EGameplayAbilityInstancingPolicy InstancingPolicy { get; protected set; }

        /// <summary>
        /// Defines where the ability's logic executes in a networked game. See <see cref="ENetExecutionPolicy"/>.
        /// </summary>
        public ENetExecutionPolicy NetExecutionPolicy { get; protected set; }

        /// <summary>
        /// The GameplayEffect that defines the resource cost (e.g., mana, stamina) required to activate this ability.
        /// This effect is checked before activation and applied upon committing the ability.
        /// </summary>
        public GameplayEffect CostEffectDefinition { get; protected set; }

        /// <summary>
        /// The GameplayEffect that puts the ability on cooldown. This effect typically grants a specific cooldown tag to the owner.
        /// The presence of this tag is checked before activation.
        /// </summary>
        public GameplayEffect CooldownEffectDefinition { get; protected set; }

        /// <summary>
        /// Tags that describe the ability itself (e.g., "Ability.Damage.Fire", "Ability.Movement").
        /// These are used for identification and can be queried by other systems.
        /// </summary>
        public GameplayTagContainer AbilityTags { get; protected set; }

        /// <summary>
        /// This ability is blocked from activating if the owner has ANY of these tags.
        /// </summary>
        public GameplayTagContainer ActivationBlockedTags { get; protected set; }

        /// <summary>
        /// The owner must have ALL of these tags for the ability to be activatable.
        /// </summary>
        public GameplayTagContainer ActivationRequiredTags { get; protected set; }

        /// <summary>
        /// When this ability is activated, it will cancel any other active abilities that have ANY of these tags.
        /// </summary>
        public GameplayTagContainer CancelAbilitiesWithTag { get; protected set; }

        /// <summary>
        /// While this ability is active, other abilities that have ANY of these tags are blocked from activating.
        /// </summary>
        public GameplayTagContainer BlockAbilitiesWithTag { get; protected set; }

        #endregion

        #region Runtime Properties

        /// <summary>
        /// A direct reference to the owning AbilitySystemComponent.
        /// </summary>
        public AbilitySystemComponent AbilitySystemComponent { get; private set; }

        /// <summary>
        /// A reference to the GameplayAbilitySpec that represents this granted ability on the ASC.
        /// The Spec holds runtime state like level and active status.
        /// </summary>
        public GameplayAbilitySpec Spec { get; private set; }

        /// <summary>
        /// Cached actor information for this ability.
        /// </summary>
        public GameplayAbilityActorInfo ActorInfo { get; private set; }

        #endregion

        private readonly List<AbilityTask> activeTasks = new List<AbilityTask>();
        private bool isEnding = false;

        protected GameplayAbility() { }

        public void Initialize(string name, EGameplayAbilityInstancingPolicy instancingPolicy, ENetExecutionPolicy netExecutionPolicy,
            GameplayEffect cost, GameplayEffect cooldown, GameplayTagContainer abilityTags,
            GameplayTagContainer activationBlockedTags, GameplayTagContainer activationRequiredTags,
            GameplayTagContainer cancelAbilitiesWithTag, GameplayTagContainer blockAbilitiesWithTag)
        {
            Name = name;
            InstancingPolicy = instancingPolicy;
            NetExecutionPolicy = netExecutionPolicy;
            CostEffectDefinition = cost;
            CooldownEffectDefinition = cooldown;
            AbilityTags = abilityTags ?? new GameplayTagContainer();
            ActivationBlockedTags = activationBlockedTags ?? new GameplayTagContainer();
            ActivationRequiredTags = activationRequiredTags ?? new GameplayTagContainer();
            CancelAbilitiesWithTag = cancelAbilitiesWithTag ?? new GameplayTagContainer();
            BlockAbilitiesWithTag = blockAbilitiesWithTag ?? new GameplayTagContainer();
        }

        /// <summary>
        /// Called when the ability is granted to an AbilitySystemComponent.
        /// Use this for initial setup that requires access to the owner.
        /// </summary>
        public virtual void OnGiveAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            this.ActorInfo = actorInfo;
            this.Spec = spec;
            this.AbilitySystemComponent = spec.Owner;
            this.isEnding = false;
            this.activeTasks.Clear();
        }

        /// <summary>
        /// Called when the ability is removed from the AbilitySystemComponent.
        /// Ensures the ability is properly cleaned up if it was active.
        /// </summary>
        public virtual void OnRemoveAbility()
        {
            CancelAbility();
        }

        /// <summary>
        /// The main execution entry point for the ability's logic. This method is intended to be overridden by subclasses.
        /// </summary>
        public virtual void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            CLogger.LogWarning($"Base ActivateAbility called for '{Name}'. Did you forget to override it in your specific ability class?");
            CommitAbility(actorInfo, spec);
        }

        /// <summary>
        /// Triggers the end of the ability's execution. This cleans up all active tasks and notifies the ASC.
        /// </summary>
        public void EndAbility()
        {
            if (isEnding) return;
            isEnding = true;

            for (int i = activeTasks.Count - 1; i >= 0; i--)
            {
                activeTasks[i].CancelTask();
            }
            activeTasks.Clear();

            AbilitySystemComponent?.OnAbilityEnded(this);
            CLogger.LogInfo($"Ability '{Name}' ended.");
        }

        internal void InternalOnEndAbility()
        {
            isEnding = false;
        }

        /// <summary>
        /// A specific way to end the ability that implies it was interrupted rather than completed naturally.
        /// </summary>
        public virtual void CancelAbility()
        {
            CLogger.LogInfo($"Ability '{Name}' was cancelled.");
            EndAbility();
        }

        /// <summary>
        /// Creates a new AbilityTask instance from the pool, initializes it, and adds it to the active tasks list.
        /// </summary>
        public T NewAbilityTask<T>() where T : AbilityTask, new()
        {
            var task = PoolManager.GetTask<T>();
            task.InitTask(this);
            activeTasks.Add(task);
            return task;
        }

        internal void OnTaskEnded(AbilityTask task) => activeTasks.Remove(task);

        public void TickTasks(float deltaTime)
        {
            for (int i = activeTasks.Count - 1; i >= 0; i--)
            {
                if (activeTasks[i] is IAbilityTaskTick tickableTask)
                {
                    tickableTask.Tick(deltaTime);
                }
            }
        }

        /// <summary>
        /// Checks all conditions (tags, cost, cooldown) to determine if the ability can be activated.
        /// </summary>
        public virtual bool CanActivate(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            if (isEnding) return false;
            if (spec.Owner.CombinedTags.HasAny(ActivationBlockedTags)) return false;
            if (!spec.Owner.CombinedTags.HasAll(ActivationRequiredTags)) return false;
            if (!CheckCooldown(spec.Owner)) return false;
            if (!CheckCost(spec.Owner)) return false;

            return true;
        }

        /// <summary>
        /// Checks if the owner has the cooldown tag associated with this ability.
        /// </summary>
        protected bool CheckCooldown(AbilitySystemComponent asc)
        {
            if (CooldownEffectDefinition?.GrantedTags != null)
            {
                if (asc.CombinedTags.HasAny(CooldownEffectDefinition.GrantedTags))
                {
                    CLogger.LogInfo($"Ability '{Name}' failed: on cooldown.");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks if the owner has sufficient resources to pay the ability's cost.
        /// </summary>
        protected bool CheckCost(AbilitySystemComponent asc)
        {
            if (CostEffectDefinition != null)
            {
                foreach (var mod in CostEffectDefinition.Modifiers)
                {
                    var attr = asc.GetAttribute(mod.AttributeName);
                    // Cost magnitudes are typically negative, so we check against its negation.
                    float costMagnitude = mod.Magnitude.GetValueAtLevel(this.Spec.Level);
                    if (attr == null || attr.CurrentValue < -costMagnitude)
                    {
                        CLogger.LogInfo($"Ability '{Name}' failed: insufficient {attr?.Name ?? "resource"}.");
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Applies the cost and cooldown effects. This should be called once the ability's outcome is certain.
        /// </summary>
        public void CommitAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            ApplyCooldown(spec.Owner, spec);
            ApplyCost(spec.Owner, spec);
        }

        protected void ApplyCost(AbilitySystemComponent asc, GameplayAbilitySpec spec)
        {
            if (CostEffectDefinition != null)
            {
                var costSpec = GameplayEffectSpec.Create(CostEffectDefinition, asc, spec.Level);
                asc.ApplyGameplayEffectSpecToSelf(costSpec);
            }
        }

        protected void ApplyCooldown(AbilitySystemComponent asc, GameplayAbilitySpec spec)
        {
            if (CooldownEffectDefinition != null)
            {
                var cooldownSpec = GameplayEffectSpec.Create(CooldownEffectDefinition, asc, spec.Level);
                asc.ApplyGameplayEffectSpecToSelf(cooldownSpec);
            }
        }

        /// <summary>
        /// Creates a new, clean instance of this ability, typically for pooling.
        /// </summary>
        public abstract GameplayAbility CreatePoolableInstance();

        internal virtual void OnReturnedToPool()
        {
            this.Spec = null;
            this.AbilitySystemComponent = null;
            this.isEnding = false;
            this.activeTasks.Clear();
        }
    }
}
