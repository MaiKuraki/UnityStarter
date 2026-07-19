using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
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

            if (!CommitAbility(actorInfo, spec).Succeeded)
            {
                EndAbility();
                return;
            }

            var caster = actorInfo.AvatarGameObject;
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

        private GameObject FindTarget(GameObject caster)
        {
            // Sample scene lookup. Production abilities should use a project targeting service.
            GameObject enemy = GameObject.Find("Enemy");
            return enemy;
        }

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_Fireball(fireballDamageEffect, burnEffect);
        }
    }

    [CreateAssetMenu(fileName = "GA_Fireball", menuName = "CycloneGames/GameplayAbilities/Samples/Ability/Fireball")]
    public class GA_Fireball_SO : GameplayAbilitySO
    {
        public GameplayEffectSO FireballDamageEffect;
        public GameplayEffectSO BurnEffect;

        protected override GameplayAbility CreateGameplayAbility()
        {
            var effect_fireball = FireballDamageEffect ? FireballDamageEffect.GetGameplayEffect() : null;
            var effect_burn = BurnEffect ? BurnEffect.GetGameplayEffect() : null;
            var ability = new GA_Fireball(effect_fireball, effect_burn);
            InitializeAbility(ability);
            return ability;
        }
    }
}
