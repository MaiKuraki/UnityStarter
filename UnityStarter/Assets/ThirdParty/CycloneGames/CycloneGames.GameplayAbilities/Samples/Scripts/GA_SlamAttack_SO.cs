using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_SlamAttack : GameplayAbility
    {
        private readonly GameplayEffect slamDamageEffect;
        private readonly float slamRadius;

        public GA_SlamAttack(GameplayEffect damageEffect, float radius)
        {
            this.slamDamageEffect = damageEffect;
            this.slamRadius = radius;
        }

        public override bool CanActivate(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            // For this ability, we could add a check to see if the character is airborne.
            // if (!character.IsAirborne) return false;
            return base.CanActivate(actorInfo, spec);
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            CommitAbility(actorInfo, spec);

            // Create a task that waits for the character to land.
            var landingTask = AbilityTask_WaitForLanding.WaitForLanding(this);
            landingTask.OnLanded += HandleLanded;
            landingTask.Activate();
        }

        private void HandleLanded()
        {
            var caster = ActorInfo.AvatarActor as GameObject;
            if (caster == null)
            {
                EndAbility();
                return;
            }

            CLogger.LogInfo($"{Name} impacts the ground!");

            // Perform a sphere overlap to find all enemies in the slam radius.
            var colliders = Physics.OverlapSphere(caster.transform.position, slamRadius);
            var hitTargets = new HashSet<AbilitySystemComponent>();

            foreach (var col in colliders)
            {
                // Identify enemies by checking for an AbilitySystemComponent and ensuring it's not our own.
                if (col.TryGetComponent<AbilitySystemComponentHolder>(out var holder) && holder.AbilitySystemComponent != this.AbilitySystemComponent)
                {
                    hitTargets.Add(holder.AbilitySystemComponent);
                }
            }
            CLogger.LogInfo($"{Name} hit {hitTargets.Count} targets.");

            // Apply the damage effect to all valid targets found.
            foreach (var targetASC in hitTargets)
            {
                var damageSpec = GameplayEffectSpec.Create(slamDamageEffect, AbilitySystemComponent, Spec.Level);
                targetASC.ApplyGameplayEffectSpecToSelf(damageSpec);
            }

            EndAbility();
        }

        public override GameplayAbility CreatePoolableInstance()
        {
            var ability = new GA_SlamAttack(this.slamDamageEffect, this.slamRadius);
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

    public class AbilityTask_WaitForLanding : AbilityTask
    {
        public System.Action OnLanded;

        public static AbilityTask_WaitForLanding WaitForLanding(GameplayAbility ability)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitForLanding>();
            return task;
        }

        protected override void OnActivate()
        {
            // TODO: In a real game, you would subscribe to an event on your CharacterMovementComponent.
            // For this sample, we'll simulate it with a simple delay to represent falling time.
            var delayTask = AbilityTask_WaitDelay.WaitDelay(this.Ability, 0.5f);
            delayTask.OnFinishDelay += () =>
            {
                if (!IsCancelled)
                {
                    OnLanded?.Invoke();
                }
                EndTask();
            };
            delayTask.Activate();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            OnLanded = null;
        }
    }

    [CreateAssetMenu(fileName = "GA_SlamAttack", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Slam Attack")]
    public class GA_SlamAttack_SO : GameplayAbilitySO
    {
        public GameplayEffectSO DamageEffect;
        public float Radius = 5.0f;

        public override GameplayAbility CreateAbility()
        {
            var effect = DamageEffect ? DamageEffect.CreateGameplayEffect() : null;
            var ability = new GA_SlamAttack(effect, Radius);
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