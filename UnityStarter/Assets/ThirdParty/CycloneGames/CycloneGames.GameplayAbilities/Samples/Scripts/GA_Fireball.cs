using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logger;
using UnityEngine;

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

        if (CommitAbility(actorInfo, spec))
        {
            // --- Targeting ---
            // In a real game, you would spawn a projectile or use a targeting system.
            // Here, we simulate finding a target in front of the caster.
            var caster = actorInfo.AvatarActor as GameObject;
            var target = FindTarget(caster);

            if (target != null && target.TryGetComponent<AbilitySystemComponent>(out var targetASC))
            {
                CLogger.LogInfo($"{caster.name} casts {Name} on {target.name}");

                // 1. Apply Instant Damage
                var damageSpec = GameplayEffectSpec.Create(fireballDamageEffect, AbilitySystemComponent, spec.Level);
                // We can dynamically add tags or set values here if needed.
                targetASC.ApplyGameplayEffectSpecToSelf(damageSpec);

                // 2. Apply Burn Debuff
                var burnSpec = GameplayEffectSpec.Create(burnEffect, AbilitySystemComponent, spec.Level);
                targetASC.ApplyGameplayEffectSpecToSelf(burnSpec);
            }
            else
            {
                CLogger.LogWarning($"{Name} could not find a valid target.");
            }
        }

        EndAbility();
    }

    // A placeholder for a real targeting system
    private GameObject FindTarget(GameObject caster)
    {
        // For simplicity, find the closest object with the "Enemy" tag in front of the caster.
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        GameObject closest = null;
        float minDistance = float.MaxValue;

        foreach (var enemy in enemies)
        {
            if (enemy == caster) continue;

            Vector3 toEnemy = enemy.transform.position - caster.transform.position;
            if (Vector3.Dot(caster.transform.forward, toEnemy.normalized) > 0.5f) // Is in front?
            {
                float distance = toEnemy.sqrMagnitude;
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closest = enemy;
                }
            }
        }
        return closest;
    }

    public override GameplayAbility CreatePoolableInstance()
    {
        return new GA_Fireball(fireballDamageEffect, burnEffect);
    }
}

// Corresponding ScriptableObject
[CreateAssetMenu(fileName = "GA_Fireball", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Fireball")]
public class GA_Fireball_SO : GameplayAbilitySO
{
    public GameplayEffectSO FireballDamageEffect;
    public GameplayEffectSO BurnEffect;

    public override GameplayAbility CreateAbility()
    {
        return new GA_Fireball(FireballDamageEffect.CreateGameplayEffect(), BurnEffect.CreateGameplayEffect());
    }
}