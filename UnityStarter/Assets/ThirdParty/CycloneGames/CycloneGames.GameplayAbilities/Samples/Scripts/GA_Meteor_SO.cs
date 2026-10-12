using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_Meteor : GameplayAbility
    {
        private static readonly LogChannel Log = GameplayAbilitiesSampleLog.Channel;

        private readonly GameObject groundSelectorPrefab;

        public GA_Meteor(GameObject groundSelectorPrefab)
        {
            this.groundSelectorPrefab = groundSelectorPrefab;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            if (groundSelectorPrefab == null)
            {
                Log.Error("GA_Meteor is missing its GroundSelectorPrefab. Ensure it's assigned in the SO asset.");
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
                    Log.Info($"Meteor impacting at: {impactPoint}");
                    // Here you would spawn the meteor VFX and apply damage in an area around impactPoint...
                }
                EndAbility();
            };

            targetTask.OnCancelled += () =>
            {
                Log.Info("Meteor cast was cancelled.");
                EndAbility();
            };

            targetTask.Activate();
        }

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_Meteor(groundSelectorPrefab);
        }
    }

    /// <summary>
    /// [ADVANCED] Ground-targeted meteor: the player picks a location with a spawned targeting
    /// actor, then the strike resolves after confirmation. The most involved activation flow
    /// in the sample set.
    ///
    /// Key Concepts:
    /// - AbilityTask_WaitTargetData with a spawned target actor
    /// - Confirm/cancel input flow during activation
    /// - Delayed area effect resolution at a chosen location
    /// </summary>
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
