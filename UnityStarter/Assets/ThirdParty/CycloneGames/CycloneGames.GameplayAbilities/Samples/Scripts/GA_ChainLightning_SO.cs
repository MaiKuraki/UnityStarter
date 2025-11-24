using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_ChainLightning : GameplayAbility
    {
        private readonly GameplayEffect lightningDamageEffect;
        private readonly int maxBounces;
        private readonly float damageFalloffPerBounce;

        public GA_ChainLightning(GameplayEffect lightningDamage, int maxBounces, float damageFalloff)
        {
            this.lightningDamageEffect = lightningDamage;
            this.maxBounces = maxBounces;
            this.damageFalloffPerBounce = damageFalloff;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            CommitAbility(actorInfo, spec);
            
            var caster = actorInfo.AvatarActor as GameObject;

            // Use a HashSet to track who has been hit to prevent infinite chains.
            var hitTargets = new HashSet<GameObject>();
            hitTargets.Add(caster); // Caster can't be hit.

            GameObject currentTarget = FindInitialTarget(caster, hitTargets);
            if (currentTarget == null)
            {
                CLogger.LogWarning("Chain Lightning fizzles, no initial target found.");
                EndAbility();
                return;
            }

            // Chain loop
            for (int i = 0; i <= maxBounces; i++)
            {
                if (currentTarget == null)
                {
                    break; // Chain is broken
                }

                hitTargets.Add(currentTarget);

                if (!currentTarget.TryGetComponent<AbilitySystemComponentHolder>(out var holder))
                {
                    // Find next target even if current one has no ASC.
                    currentTarget = FindNextTarget(currentTarget, hitTargets);
                    continue;
                }

                var targetASC = holder.AbilitySystemComponent;

                // Calculate damage for this bounce
                float damageMultiplier = Mathf.Pow(1 - damageFalloffPerBounce, i);
                CLogger.LogInfo($"Chain Lightning hits {currentTarget.name} for {damageMultiplier:P0} damage.");

                // A better system would allow GameplayEffectSpec modification (e.g., SetByCaller).
                // For simplicity here, we create a temporary GE with the modified magnitude.
                var originalMod = lightningDamageEffect.Modifiers[0];
                var tempMod = new ModifierInfo(
                    originalMod.AttributeName,
                    originalMod.Operation,
                    new ScalableFloat(originalMod.Magnitude.GetValueAtLevel(spec.Level) * damageMultiplier)
                );

                var tempEffect = new GameplayEffect("TempLightning", EDurationPolicy.Instant, 0, 0, new List<ModifierInfo> { tempMod });
                var tempSpec = GameplayEffectSpec.Create(tempEffect, AbilitySystemComponent, spec.Level);

                targetASC.ApplyGameplayEffectSpecToSelf(tempSpec);

                // Find the next target
                currentTarget = FindNextTarget(currentTarget, hitTargets);
            }

            EndAbility();
        }

        private GameObject FindInitialTarget(GameObject caster, HashSet<GameObject> alreadyHit)
        {
            // Simple forward raycast
            if (Physics.Raycast(caster.transform.position + Vector3.up, caster.transform.forward, out RaycastHit hit, 100f))
            {
                if (!alreadyHit.Contains(hit.collider.gameObject) && hit.collider.CompareTag("Enemy"))
                {
                    return hit.collider.gameObject;
                }
            }
            return null;
        }

        private GameObject FindNextTarget(GameObject fromTarget, HashSet<GameObject> alreadyHit)
        {
            var colliders = Physics.OverlapSphere(fromTarget.transform.position, 15f); // 15m chain range
            GameObject closest = null;
            float minSqrDist = float.MaxValue;

            foreach (var col in colliders)
            {
                if (alreadyHit.Contains(col.gameObject) || !col.CompareTag("Enemy"))
                {
                    continue;
                }

                float sqrDist = (col.transform.position - fromTarget.transform.position).sqrMagnitude;
                if (sqrDist < minSqrDist)
                {
                    minSqrDist = sqrDist;
                    closest = col.gameObject;
                }
            }
            return closest;
        }

        public override GameplayAbility CreatePoolableInstance() => new GA_ChainLightning(lightningDamageEffect, maxBounces, damageFalloffPerBounce);
    }


    [CreateAssetMenu(fileName = "GA_ChainLightning", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/ChainLightning")]
    public class GA_ChainLightning_SO : GameplayAbilitySO
    {
        public GameplayEffectSO LightningDamageEffect;
        [Range(1, 10)]
        public int MaxBounces = 3;
        [Range(0f, 1f)]
        public float DamageFalloffPerBounce = 0.25f;

        public override GameplayAbility CreateAbility()
        {
            var effect = LightningDamageEffect ? LightningDamageEffect.GetGameplayEffect() : null;
            var ability = new GA_ChainLightning(effect, MaxBounces, DamageFalloffPerBounce);
            ability.Initialize(
                AbilityName,
                InstancingPolicy,
                NetExecutionPolicy,
                CostEffect?.GetGameplayEffect(),
                CooldownEffect?.GetGameplayEffect(),
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