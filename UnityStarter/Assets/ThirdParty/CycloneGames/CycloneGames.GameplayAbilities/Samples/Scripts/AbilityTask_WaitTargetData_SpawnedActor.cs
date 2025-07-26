using System;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// An AbilityTask that spawns a specified TargetActor prefab, waits for it to produce data,
    /// and then destroys it. Ideal for targeting that requires a visual presence in the world,
    /// such as ground-targeted AOEs.
    /// </summary>
    public class AbilityTask_WaitTargetData_SpawnedActor : AbilityTask
    {
        public Action<TargetData> OnValidData;
        public Action OnCancelled;
        private GameObject targetActorPrefab;
        private ITargetActor spawnedTargetActor;

        public static AbilityTask_WaitTargetData_SpawnedActor WaitTargetData(GameplayAbility ability, GameObject prefab)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitTargetData_SpawnedActor>();
            task.targetActorPrefab = prefab;
            return task;
        }

        protected override void OnActivate()
        {
            if (targetActorPrefab == null)
            {
                CLogger.LogError("WaitTargetData_SpawnedActor failed: TargetActor prefab is null.");
                EndTask();
                return;
            }

            // Instantiate the prefab and get the ITargetActor interface.
            var actorInstance = UnityEngine.Object.Instantiate(targetActorPrefab);
            spawnedTargetActor = actorInstance.GetComponent<ITargetActor>();
            
            if (spawnedTargetActor == null)
            {
                CLogger.LogError($"WaitTargetData_SpawnedActor failed: Prefab '{targetActorPrefab.name}' does not have a component implementing ITargetActor.");
                UnityEngine.Object.Destroy(actorInstance);
                EndTask();
                return;
            }

            spawnedTargetActor.Configure(this.Ability, HandleTargetDataReady, HandleCancelled);
            spawnedTargetActor.StartTargeting();
        }

        private void HandleTargetDataReady(TargetData data)
        {
            if (IsActive && !IsCancelled)
            {
                OnValidData?.Invoke(data);
            }
            EndTask();
        }

        private void HandleCancelled()
        {
            if (IsActive && !IsCancelled)
            {
                OnCancelled?.Invoke();
            }
            EndTask();
        }

        protected override void OnDestroy()
        {
            if (spawnedTargetActor != null)
            {
                spawnedTargetActor.Destroy(); // This will handle destroying the GameObject.
                spawnedTargetActor = null;
            }

            OnValidData = null;
            OnCancelled = null;
            base.OnDestroy();
        }
    }
}