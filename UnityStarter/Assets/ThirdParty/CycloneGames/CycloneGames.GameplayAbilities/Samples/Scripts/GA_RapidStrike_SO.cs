using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Samples
{
    /// <summary>
    /// [INTERMEDIATE] Demonstrates per-spec duration override: the cooldown of this ability is
    /// computed per activation from an attack-speed multiplier instead of the static duration
    /// authored on the cooldown effect asset.
    ///
    /// Key Concepts:
    /// - Overriding CreateCooldownEffectSpec and calling GameplayEffectSpec.SetDuration
    /// - The shared cooldown effect asset is never mutated; each activation gets its own duration
    /// - GetCooldownInfo reports the computed per-spec duration for UI display
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/GameplayAbilities/Samples/Ability/Rapid Strike")]
    public class GA_RapidStrike_SO : GameplayAbilitySO
    {
        [Header("Computed Cooldown")]
        [Tooltip("The cooldown in seconds at an attack-speed multiplier of 1.")]
        public float BaseCooldownSeconds = 5f;

        [Tooltip("Attack-speed multiplier applied per activation. Cooldown = Base / Multiplier.")]
        public float AttackSpeedMultiplier = 2f;

        protected override GameplayAbility CreateGameplayAbility()
        {
            var ability = new GA_RapidStrike(BaseCooldownSeconds, AttackSpeedMultiplier);
            InitializeAbility(ability);
            return ability;
        }
    }

    public class GA_RapidStrike : GameplayAbility
    {
        private static readonly LogChannel Log = GameplayAbilitiesSampleLog.Channel;

        private readonly float baseCooldownSeconds;
        private readonly float attackSpeedMultiplier;

        public GA_RapidStrike(float baseCooldownSeconds, float attackSpeedMultiplier)
        {
            this.baseCooldownSeconds = baseCooldownSeconds;
            this.attackSpeedMultiplier = attackSpeedMultiplier;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            if (!CommitAbility(actorInfo, spec).Succeeded)
            {
                EndAbility();
                return;
            }

            var owner = spec.Owner;
            if (owner.GetCooldownInfo(this, out float remaining, out float total))
            {
                Log.Info($"{Name} committed. Cooldown: {remaining:F2}s / {total:F2}s " +
                    $"(base {baseCooldownSeconds:F2}s, attack speed x{attackSpeedMultiplier:F2})");
            }

            EndAbility();
        }

        protected override GameplayEffectSpec CreateCooldownEffectSpec(AbilitySystemComponent asc, GameplayAbilitySpec spec)
        {
            GameplayEffectSpec cooldownSpec = base.CreateCooldownEffectSpec(asc, spec);
            if (cooldownSpec != null && attackSpeedMultiplier > 0f)
            {
                cooldownSpec.SetDuration(baseCooldownSeconds / attackSpeedMultiplier);
            }

            return cooldownSpec;
        }

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_RapidStrike(baseCooldownSeconds, attackSpeedMultiplier);
        }

        protected override void ResetRuntimeState()
        {
        }
    }
}
