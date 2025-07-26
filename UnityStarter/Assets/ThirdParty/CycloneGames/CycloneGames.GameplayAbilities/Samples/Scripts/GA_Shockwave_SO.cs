using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_Shockwave : GameplayAbility
    {
        private readonly float radius;
        private readonly GameplayEffect damageEffect;
        private readonly GameplayTagContainer targetRequiredFactions;
        private readonly GameplayTagContainer targetForbiddenFactions;

        public GA_Shockwave(float radius, GameplayEffect damageEffect, GameplayTagContainer required, GameplayTagContainer forbidden)
        {
            this.radius = radius;
            this.damageEffect = damageEffect;
            this.targetRequiredFactions = required;
            this.targetForbiddenFactions = forbidden;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            // Create a targeting query to find enemies.
            var query = new TargetingQuery
            {
                OwningAbility = this,
                IgnoreCaster = true, // A shockwave should not hit the caster.
                RequiredTags = this.targetRequiredFactions,
                ForbiddenTags = this.targetForbiddenFactions
            };

            // Create the task with our sphere overlap actor.
            var targetTask = AbilityTask_WaitTargetData.WaitTargetData(this,
                new GameplayAbilityTargetActor_SphereOverlap(-1, query, radius));

            targetTask.OnValidData += OnTargetDataReceived;
            targetTask.OnCancelled += () =>
            {
                CLogger.LogInfo("Shockwave hit no targets.");
                EndAbility();
            };

            targetTask.Activate();
        }

        private void OnTargetDataReceived(TargetData data)
        {
            var multiTargetData = data as GameplayAbilityTargetData_MultiTarget;
            if (multiTargetData == null || multiTargetData.Actors.Count == 0)
            {
                EndAbility();
                return;
            }

            // The TargetActor has already filtered for valid enemies. We can now commit the ability.
            CommitAbility(ActorInfo, Spec);

            CLogger.LogInfo($"Shockwave hit {multiTargetData.Actors.Count} targets.");
            foreach (var targetObject in multiTargetData.Actors)
            {
                if (damageEffect != null && targetObject.TryGetComponent<AbilitySystemComponentHolder>(out var holder))
                {
                    var damageSpec = GameplayEffectSpec.Create(damageEffect, AbilitySystemComponent, Spec.Level);
                    holder.AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(damageSpec);
                }
            }

            EndAbility();
        }

        public override GameplayAbility CreatePoolableInstance()
        {
            var ability = new GA_Shockwave(this.radius, this.damageEffect, this.targetRequiredFactions, this.targetForbiddenFactions);

            ability.Initialize(
                this.Name,
                this.InstancingPolicy,
                this.NetExecutionPolicy,
                this.CostEffectDefinition,
                this.CooldownEffectDefinition,
                this.AbilityTags,
                this.ActivationBlockedTags,
                this.ActivationRequiredTags,
                this.CancelAbilitiesWithTag,
                this.BlockAbilitiesWithTag
            );

            return ability;
        }
    }

    [CreateAssetMenu(fileName = "GA_Shockwave", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Shockwave")]
    public class GA_Shockwave_SO : GameplayAbilitySO
    {
        // NEW: Configurable properties for the shockwave.
        [Tooltip("The radius of the shockwave effect in meters.")]
        public float Radius = 8.0f;

        [Tooltip("The damage to apply to all targets hit by the shockwave.")]
        public GameplayEffectSO DamageEffect;

        [Header("Targeting")]
        [Tooltip("Targets found must have ALL of these faction tags to be affected (e.g., Faction.Enemy).")]
        public GameplayTagContainer TargetRequiredFactions;

        [Tooltip("Targets found that have ANY of these faction tags will be ignored.")]
        public GameplayTagContainer TargetForbiddenFactions;

        public override GameplayAbility CreateAbility()
        {
            var effect = DamageEffect ? DamageEffect.CreateGameplayEffect() : null;
            var ability = new GA_Shockwave(Radius, effect, TargetRequiredFactions, TargetForbiddenFactions);

            ability.Initialize(
                AbilityName,
                InstancingPolicy,
                NetExecutionPolicy,
                CostEffect?.CreateGameplayEffect(),
                CooldownEffect?.CreateGameplayEffect(),
                AbilityTags,
                ActivationBlockedTags,
                ActivationRequiredTags,
                CancelAbilitiesWithTag,
                BlockAbilitiesWithTag
            );
            return ability;
        }
    }
}