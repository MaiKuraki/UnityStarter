using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_Fireball : GameplayAbility
    {
        private readonly GameplayEffect fireballDamageEffect;
        private readonly GameplayEffect burnEffect;

        public GA_Fireball(GameplayEffect damageEffect, GameplayEffect burnEffectInstance)
        {
            this.fireballDamageEffect = damageEffect;
            this.burnEffect = burnEffectInstance;
        }

        public override bool CanActivate(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            // Add any specific checks here, e.g., if a weapon is equipped.
            return base.CanActivate(actorInfo, spec);
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            CLogger.LogInfo($"Activating {Name}");

            CommitAbility(actorInfo, spec);

            // --- Targeting ---
            // TODO: should spawn a projectile or use a targeting system.
            // simulate finding a target in front of the caster.
            var caster = actorInfo.AvatarActor as GameObject;
            var target = FindTarget(caster);

            if (target != null && target.TryGetComponent<AbilitySystemComponentHolder>(out var holder))
            {
                var targetASC = holder.AbilitySystemComponent;
                CLogger.LogInfo($"{caster.name} casts {Name} on {target.name}");

                // Apply Instant Damage
                var damageSpec = GameplayEffectSpec.Create(fireballDamageEffect, AbilitySystemComponent, spec.Level);

                //  Check Damage Multiplier (may player has some skills enhanced the damage)
                if (actorInfo.OwnerActor is Character casterCharacter)
                {
                    float bonusDamageMultiplier = casterCharacter.AttributeSet.GetCurrentValue(casterCharacter.AttributeSet.BonusDamageMultiplier);
                    damageSpec.SetSetByCallerMagnitude(GameplayTagManager.RequestTag(GASSampleTags.Data_DamageMultiplier), bonusDamageMultiplier);
                    CLogger.LogInfo($"Snapshotting DamageMultiplier: {bonusDamageMultiplier}");
                }

                targetASC.ApplyGameplayEffectSpecToSelf(damageSpec);

                // Apply Burn Debuff
                if (burnEffect != null)
                {
                    var burnSpec = GameplayEffectSpec.Create(burnEffect, AbilitySystemComponent, spec.Level);
                    targetASC.ApplyGameplayEffectSpecToSelf(burnSpec);
                }
            }
            else
            {
                CLogger.LogWarning($"{Name} could not find a valid target.");
            }

            EndAbility();
        }

        // A placeholder for a real targeting system
        private GameObject FindTarget(GameObject caster)
        {
            //  TODO: get enemies from other way.
            // GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
            // GameObject closest = null;
            // float minDistance = float.MaxValue;

            // foreach (var enemy in enemies)
            // {
            //     if (enemy == caster) continue;

            //     Vector3 toEnemy = enemy.transform.position - caster.transform.position;
            //     if (Vector3.Dot(caster.transform.forward, toEnemy.normalized) > 0.5f) // Is in front?
            //     {
            //         float distance = toEnemy.sqrMagnitude;
            //         if (distance < minDistance)
            //         {
            //             minDistance = distance;
            //             closest = enemy;
            //         }
            //     }
            // }
            GameObject enemy = GameObject.Find("Enemy");
            return enemy;
        }

        public override GameplayAbility CreatePoolableInstance()
        {
            var ability = new GA_Fireball(this.fireballDamageEffect, this.burnEffect);

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

    [CreateAssetMenu(fileName = "GA_Fireball", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Fireball")]
    public class GA_Fireball_SO : GameplayAbilitySO
    {
        public GameplayEffectSO FireballDamageEffect;
        public GameplayEffectSO BurnEffect;

        public override GameplayAbility CreateAbility()
        {
            var effect_fireball = FireballDamageEffect ? FireballDamageEffect.CreateGameplayEffect() : null;
            var effect_burn = BurnEffect ? BurnEffect.CreateGameplayEffect() : null;
            var ability = new GA_Fireball(effect_fireball, effect_burn);

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