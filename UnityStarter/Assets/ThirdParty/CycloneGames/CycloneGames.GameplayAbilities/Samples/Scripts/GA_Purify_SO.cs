using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using UnityEngine;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_Purify : GameplayAbility
    {
        private readonly float areaOfEffectRadius;
        private readonly GameplayTagContainer targetRequiredFactions;
        private readonly GameplayTagContainer targetForbiddenFactions;

        public GA_Purify(float radius, GameplayTagContainer required, GameplayTagContainer forbidden)
        {
            this.areaOfEffectRadius = radius;
            this.targetRequiredFactions = required;
            this.targetForbiddenFactions = forbidden;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            // Create a targeting query using the tags defined in our SO.
            var query = new TargetingQuery
            {
                OwningAbility = this,
                IgnoreCaster = false,
                RequiredTags = this.targetRequiredFactions,
                ForbiddenTags = this.targetForbiddenFactions
            };

            var targetTask = AbilityTask_WaitTargetData.WaitTargetData(this,
                new GameplayAbilityTargetActor_SphereOverlap(-1, query, areaOfEffectRadius));

            targetTask.OnValidData += OnTargetDataReceived;
            targetTask.OnCancelled += () => OnTargetDataReceived(null);

            targetTask.Activate();
        }

        private void OnTargetDataReceived(TargetData data)
        {
            CommitAbility(ActorInfo, Spec);

            var charactersToProcess = new HashSet<Character>();

            var casterCharacter = (ActorInfo.AvatarActor as GameObject)?.GetComponent<Character>();
            if (casterCharacter != null)
            {
                charactersToProcess.Add(casterCharacter);
            }

            var multiTargetData = data as GameplayAbilityTargetData_MultiTarget;
            if (multiTargetData != null)
            {
                foreach (var targetObject in multiTargetData.Actors)
                {
                    var character = targetObject.GetComponent<Character>();
                    if (character != null)
                    {
                        charactersToProcess.Add(character);
                    }
                }
            }

            int targetsPurified = 0;
            foreach (var targetCharacter in charactersToProcess)
            {
                if (targetCharacter == null) continue;

                if (targetCharacter.AbilitySystemComponent != null && CanDispelPoison(targetCharacter.AbilitySystemComponent, Spec.Level))
                {
                    var poisonTagContainer = new GameplayTagContainer { GameplayTagManager.RequestTag(GASSampleTags.Debuff_Poison) };
                    targetCharacter.AbilitySystemComponent.RemoveActiveEffectsWithGrantedTags(poisonTagContainer);
                    targetsPurified++;
                    CLogger.LogInfo($"Purified {targetCharacter.name}.");
                }
            }

            if (targetsPurified == 0)
            {
                CLogger.LogInfo("Purify was cast, but no one had cleansable poison.");
            }

            EndAbility();
        }

        private bool CanDispelPoison(AbilitySystemComponent targetASC, int dispelLevel)
        {
            var poisonDebuffTag = GameplayTagManager.RequestTag(GASSampleTags.Debuff_Poison);
            if (poisonDebuffTag.IsNone) return false;

            foreach (var activeEffect in targetASC.ActiveEffects)
            {
                if (activeEffect.Spec.Def.GrantedTags.HasTag(poisonDebuffTag))
                {
                    if (dispelLevel >= activeEffect.Spec.Level)
                    {
                        return true;
                    }
                }
            }
            return false;
        }


        public override GameplayAbility CreatePoolableInstance()
        {
            var ability = new GA_Purify(this.areaOfEffectRadius, this.targetRequiredFactions, this.targetForbiddenFactions);

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

    [CreateAssetMenu(fileName = "GA_Purify", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Purify")]
    public class GA_Purify_SO : GameplayAbilitySO
    {
        [Tooltip("The radius of the purify effect in meters.")]
        public float AreaOfEffectRadius = 5.0f;

        [Tooltip("Targets found must have ALL of these faction tags to be affected.")]
        public GameplayTagContainer TargetRequiredFactions;

        [Tooltip("Targets found that have ANY of these faction tags will be ignored.")]
        public GameplayTagContainer TargetForbiddenFactions;

        public override GameplayAbility CreateAbility()
        {
            // Pass the tag containers to the ability's constructor.
            var ability = new GA_Purify(AreaOfEffectRadius, TargetRequiredFactions, TargetForbiddenFactions);

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
