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

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_Meteor(groundSelectorPrefab);
        }
    }

    [CreateAssetMenu(fileName = "GA_Meteor", menuName = "CycloneGames/GameplayAbilities/Samples/Ability/Meteor")]
    public class GA_Meteor_SO : GameplayAbilitySO
    {
        public GameObject GroundSelectorPrefab;

        protected override GameplayAbility CreateGameplayAbility()
        {
            var ability = new GA_Meteor(this.GroundSelectorPrefab);
            InitializeAbility(ability);
            return ability;
        }
    }
}
