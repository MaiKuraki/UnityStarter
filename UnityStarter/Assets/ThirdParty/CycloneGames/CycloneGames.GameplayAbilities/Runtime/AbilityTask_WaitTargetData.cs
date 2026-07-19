using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public class AbilityTask_WaitTargetData : AbilityTask
    {
        public Action<TargetData> OnValidData;
        public Action OnCancelled;
        private ITargetActor targetActorInstance;
        private bool terminalCallbackStarted;

        public override void InitTask(GameplayAbility ability)
        {
            base.InitTask(ability);
            terminalCallbackStarted = false;
        }

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
                GASLog.Error("WaitTargetData task failed: ITargetActor instance is null.");
                EndTask();
                return;
            }

            // Re-wire configure call to pass delegates
            targetActorInstance.Configure(this.Ability, HandleTargetDataReady, HandleCancelled);
            targetActorInstance.StartTargeting();
        }

        private void HandleTargetDataReady(TargetData data)
        {
            if (!IsActive || IsCancelled ||
                !AbilityTaskTerminalCallbackGuard.TryBegin(
                    this,
                    ref terminalCallbackStarted,
                    out ulong leaseGeneration))
            {
                data?.Release();
                return;
            }

            try
            {
                data?.StampPrediction(Ability, PredictionKey);
                OnValidData?.Invoke(data);
            }
            finally
            {
                // TargetData is a callback-scoped lease. Consumers must copy durable data during the callback.
                try
                {
                    data?.Release();
                }
                finally
                {
                    EndTaskIfCurrentLease(leaseGeneration);
                }
            }
        }

        private void HandleCancelled()
        {
            CancelInternal();
        }

        public override void CancelTask()
        {
            CancelInternal();
        }

        private void CancelInternal()
        {
            if (!AbilityTaskTerminalCallbackGuard.TryBegin(
                    this,
                    ref terminalCallbackStarted,
                    out ulong leaseGeneration)) return;
            try
            {
                OnCancelled?.Invoke();
            }
            finally
            {
                if (IsCurrentLease(leaseGeneration))
                {
                    base.CancelTask();
                }
            }
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
