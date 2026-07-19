using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
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
            this.targetRequiredFactions = required != null
                ? new GameplayTagContainer(required)
                : new GameplayTagContainer();
            this.targetForbiddenFactions = forbidden != null
                ? new GameplayTagContainer(forbidden)
                : new GameplayTagContainer();
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
            if (multiTargetData == null || multiTargetData.ActorCount == 0)
            {
                EndAbility();
                return;
            }

            // The TargetActor has already filtered for valid enemies. We can now commit the ability.
            if (!CommitAbility(ActorInfo, Spec).Succeeded)
            {
                EndAbility();
                return;
            }

            CLogger.LogInfo($"Shockwave hit {multiTargetData.ActorCount} targets.");
            for (int i = 0; i < multiTargetData.ActorCount; i++)
            {
                var targetObject = multiTargetData.GetActor(i);
                if (damageEffect != null && targetObject.TryGetComponent<AbilitySystemComponentHolder>(out var holder))
                {
                    var damageSpec = GameplayEffectSpec.Create(damageEffect, AbilitySystemComponent, Spec.Level);
                    holder.AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(damageSpec);
                }
            }

            EndAbility();
        }

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_Shockwave(radius, damageEffect, targetRequiredFactions, targetForbiddenFactions);
        }
    }

    [CreateAssetMenu(fileName = "GA_Shockwave", menuName = "CycloneGames/GameplayAbilities/Samples/Ability/Shockwave")]
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

        protected override GameplayAbility CreateGameplayAbility()
        {
            var effect = DamageEffect ? DamageEffect.GetGameplayEffect() : null;
            var ability = new GA_Shockwave(Radius, effect, TargetRequiredFactions, TargetForbiddenFactions);
            InitializeAbility(ability);
            return ability;
        }
    }
}
