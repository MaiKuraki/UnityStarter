using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_Meteor : GameplayAbility
    {
        private readonly GameObject groundSelectorPrefab;

        public GA_Meteor(GameObject groundSelectorPrefab)
        {
            this.groundSelectorPrefab = groundSelectorPrefab;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            if (groundSelectorPrefab == null)
            {
                CLogger.LogError("GA_Meteor is missing its GroundSelectorPrefab. Ensure it's assigned in the SO asset.");
                EndAbility();
                return;
            }

            // Use the task to spawn the ground selector prefab.
            var targetTask = AbilityTask_WaitTargetData_SpawnedActor.WaitTargetData(this, groundSelectorPrefab);

            targetTask.OnValidData += (data) =>
            {
                var hitData = data as GameplayAbilityTargetData_SingleTargetHit;
                if (hitData != null)
                {
                    Vector3 impactPoint = hitData.HitResult.point;
                    CLogger.LogInfo($"Meteor impacting at: {impactPoint}");
                    // Here you would spawn the meteor VFX and apply damage in an area around impactPoint...
                }
                EndAbility();
            };

            targetTask.OnCancelled += () =>
            {
                CLogger.LogInfo("Meteor cast was cancelled.");
                EndAbility();
            };

            targetTask.Activate();
        }

        public override GameplayAbility CreatePoolableInstance()
        {
            var ability = new GA_Meteor(this.groundSelectorPrefab);

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

    [CreateAssetMenu(fileName = "GA_Meteor", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Meteor")]
    public class GA_Meteor_SO : GameplayAbilitySO
    {
        public GameObject GroundSelectorPrefab;

        public override GameplayAbility CreateAbility()
        {
            var ability = new GA_Meteor(this.GroundSelectorPrefab);

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