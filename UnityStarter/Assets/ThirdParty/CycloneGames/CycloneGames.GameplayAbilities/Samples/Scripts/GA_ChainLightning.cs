using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using UnityEngine;

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
        if (CommitAbility(actorInfo, spec))
        {
            var caster = actorInfo.AvatarActor as GameObject;

            // Use a HashSet to track who has been hit to prevent infinite chains.
            var hitTargets = new HashSet<GameObject>();

            GameObject currentTarget = FindInitialTarget(caster);
            if (currentTarget == null)
            {
                CLogger.LogWarning("Chain Lightning fizzles, no initial target found.");
                EndAbility();
                return;
            }

            hitTargets.Add(currentTarget);

            // Chain loop
            for (int i = 0; i <= maxBounces; i++)
            {
                if (currentTarget == null || !currentTarget.TryGetComponent<AbilitySystemComponent>(out var targetASC))
                {
                    break; // Chain is broken
                }

                // Calculate damage for this bounce
                float damageMultiplier = Mathf.Pow(1 - damageFalloffPerBounce, i);

                CLogger.LogInfo($"Chain Lightning hits {currentTarget.name} for {damageMultiplier:P0} damage.");

                var damageSpec = GameplayEffectSpec.Create(lightningDamageEffect, AbilitySystemComponent, spec.Level);
                // Here we override the magnitude. This requires a SetByCaller-like mechanism.
                // For simplicity in this example, we'll create a temporary GE with modified magnitude.
                // A better system would allow GameplayEffectSpec modification.
                var tempMod = new ModifierInfo(lightningDamageEffect.Modifiers[0].AttributeName,
                    lightningDamageEffect.Modifiers[0].Operation,
                    new ScalableFloat(lightningDamageEffect.Modifiers[0].Magnitude.BaseValue * damageMultiplier));

                var tempEffect = new GameplayEffect("TempLightning", EDurationPolicy.Instant, 0, new List<ModifierInfo> { tempMod });
                var tempSpec = GameplayEffectSpec.Create(tempEffect, AbilitySystemComponent, spec.Level);

                targetASC.ApplyGameplayEffectSpecToSelf(tempSpec);

                // Find the next target
                currentTarget = FindNextTarget(currentTarget, hitTargets);
                if (currentTarget != null) hitTargets.Add(currentTarget);
            }
        }

        EndAbility();
    }

    private GameObject FindInitialTarget(GameObject caster)
    {
        // Simple forward raycast
        if (Physics.Raycast(caster.transform.position + Vector3.up, caster.transform.forward, out RaycastHit hit, 20f))
        {
            if (hit.collider.CompareTag("Enemy")) return hit.collider.gameObject;
        }
        return null;
    }

    private GameObject FindNextTarget(GameObject fromTarget, HashSet<GameObject> alreadyHit)
    {
        var colliders = Physics.OverlapSphere(fromTarget.transform.position, 10f); // 10m chain range
        GameObject closest = null;
        float minSqrDist = float.MaxValue;

        foreach (var col in colliders)
        {
            if (col.gameObject == fromTarget || alreadyHit.Contains(col.gameObject) || !col.CompareTag("Enemy"))
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

    public override GameplayAbility CreateAbility() => new GA_ChainLightning(LightningDamageEffect.CreateGameplayEffect(), MaxBounces, DamageFalloffPerBounce);
}