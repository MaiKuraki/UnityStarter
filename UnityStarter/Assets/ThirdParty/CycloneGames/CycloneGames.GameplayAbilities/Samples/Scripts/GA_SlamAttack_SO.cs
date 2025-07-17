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
            if (!CommitAbility(actorInfo, spec))
            {
                EndAbility();
                return;
            }

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
                // Here we identify enemies by checking for an AbilitySystemComponent
                // and ensuring it's not our own. This avoids using Unity Tags.
                if (col.TryGetComponent<AbilitySystemComponentHolder>(out var holder) && holder.AbilitySystemComponent != this.AbilitySystemComponent)
                {
                    hitTargets.Add(holder.AbilitySystemComponent);
                }
            }

            // Apply the damage effect to all valid targets found.
            foreach (var targetASC in hitTargets)
            {
                var damageSpec = GameplayEffectSpec.Create(slamDamageEffect, AbilitySystemComponent, Spec.Level);
                targetASC.ApplyGameplayEffectSpecToSelf(damageSpec);
            }

            EndAbility();
        }

        public override GameplayAbility CreatePoolableInstance() => new GA_SlamAttack(slamDamageEffect, slamRadius);
    }
    
    // We need a new AbilityTask for this
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
            // In a real game, you would subscribe to an event on your CharacterMovementComponent.
            // For this sample, we'll simulate it with a simple delay.
            var delayTask = AbilityTask_WaitDelay.WaitDelay(this.Ability, 0.5f);
            delayTask.OnFinishDelay += () => OnLanded?.Invoke();
            delayTask.Activate();
        }
    }

    [CreateAssetMenu(fileName = "GA_SlamAttack", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Slam Attack")]
    public class GA_SlamAttack_SO : GameplayAbilitySO
    {
        public GameplayEffectSO DamageEffect;
        public float Radius = 5.0f;
        public override GameplayAbility CreateAbility() => new GA_SlamAttack(DamageEffect.CreateGameplayEffect(), Radius);
    }
}