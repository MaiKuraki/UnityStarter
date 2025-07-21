using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum EGameplayAbilityInstancingPolicy { NonInstanced, InstancedPerActor, InstancedPerExecution }
    public enum ENetExecutionPolicy { LocalOnly, LocalPredicted, ServerOnly }

    public struct GameplayAbilityActorInfo
    {
        public readonly object OwnerActor;
        public readonly object AvatarActor;

        public GameplayAbilityActorInfo(object owner, object avatar)
        {
            OwnerActor = owner;
            AvatarActor = avatar;
        }
    }

    public struct GameplayAbilityActivationInfo
    {
        public PredictionKey PredictionKey { get; set; }
    }

    public abstract class GameplayAbility
    {
        public string Name { get; protected set; }
        public EGameplayAbilityInstancingPolicy InstancingPolicy { get; protected set; }
        public ENetExecutionPolicy NetExecutionPolicy { get; protected set; }
        public GameplayEffect CostEffectDefinition { get; protected set; }
        public GameplayEffect CooldownEffectDefinition { get; protected set; }
        public GameplayTagContainer AbilityTags { get; protected set; }
        public GameplayTagContainer ActivationBlockedTags { get; protected set; }
        public GameplayTagContainer ActivationRequiredTags { get; protected set; }
        public GameplayTagContainer CancelAbilitiesWithTag { get; protected set; }
        public GameplayTagContainer BlockAbilitiesWithTag { get; protected set; }

        public AbilitySystemComponent AbilitySystemComponent { get; private set; }
        public GameplayAbilitySpec Spec { get; private set; }
        public GameplayAbilityActorInfo ActorInfo { get; private set; }

        private readonly List<AbilityTask> activeTasks = new List<AbilityTask>();
        private bool isEnding = false;

        protected GameplayAbility() { }

        // It is recommended to use an object initializer for better readability.
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

        public virtual void OnGiveAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            this.ActorInfo = actorInfo;
            this.Spec = spec;
            this.AbilitySystemComponent = spec.Owner;
            this.isEnding = false;
            this.activeTasks.Clear();
        }

        public virtual void OnRemoveAbility()
        {
            CancelAbility();
        }

        public virtual void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            if (CommitAbility(actorInfo, spec))
            {
                CLogger.LogInfo($"Ability '{Name}' activated successfully.");
                EndAbility();
            }
            else
            {
                CLogger.LogWarning($"Ability '{Name}' failed to commit and was cancelled.");
                CancelAbility();
            }
        }

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

        public virtual void CancelAbility()
        {
            CLogger.LogInfo($"Ability '{Name}' was cancelled.");
            EndAbility();
        }

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

        public virtual bool CanActivate(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            if (isEnding) return false;
            if (spec.Owner.CombinedTags.HasAny(ActivationBlockedTags)) return false;
            if (!spec.Owner.CombinedTags.HasAll(ActivationRequiredTags)) return false;
            if (!CheckCooldown(spec.Owner)) return false;
            if (!CheckCost(spec.Owner)) return false;

            return true;
        }

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

        protected bool CheckCost(AbilitySystemComponent asc)
        {
            if (CostEffectDefinition != null)
            {
                foreach (var mod in CostEffectDefinition.Modifiers)
                {
                    var attr = asc.GetAttribute(mod.AttributeName);
                    float costMagnitude = mod.Magnitude.GetValueAtLevel(this.Spec.Level);
                    if (attr == null || attr.CurrentValue < -costMagnitude) // Cost is negative
                    {
                        CLogger.LogInfo($"Ability '{Name}' failed: insufficient {attr?.Name ?? "resource"}.");
                        return false;
                    }
                }
            }
            return true;
        }

        public bool CommitAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            if (!CheckCost(spec.Owner) || !CheckCooldown(spec.Owner)) return false;

            ApplyCooldown(spec.Owner, spec);
            ApplyCost(spec.Owner, spec);
            return true;
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