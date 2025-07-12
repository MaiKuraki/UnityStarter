using System;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public class AbilityTask_WaitTargetData : AbilityTask
    {
        public Action<TargetData> OnValidData;
        public Action OnCancelled;
        private ITargetActor targetActorInstance;

        public static AbilityTask_WaitTargetData WaitTargetData(GameplayAbility ability, ITargetActor actorInstance)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitTargetData>();
            task.targetActorInstance = actorInstance;
            return task;
        }

        protected override void OnActivate()
        {
            if (targetActorInstance == null)
            {
                CLogger.LogError("WaitTargetData task failed: ITargetActor instance is null.");
                EndTask();
                return;
            }

            // Re-wire configure call to pass delegates
            targetActorInstance.Configure(this.Ability, HandleTargetDataReady, HandleCancelled);
            targetActorInstance.StartTargeting();
        }

        private void HandleTargetDataReady(TargetData data)
        {
            if (IsActive && !IsCancelled)
            {
                OnValidData?.Invoke(data);
            }
            EndTask(); // Task ends, but actor destruction is handled in OnDestroy
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
            if (targetActorInstance != null)
            {
                targetActorInstance.Destroy();
                targetActorInstance = null;
            }

            OnValidData = null;
            OnCancelled = null;
            base.OnDestroy();
        }
    }
}