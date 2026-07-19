using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public class AbilityTask_WaitDelay : AbilityTask, IAbilityTaskTick
    {
        public Action OnFinishDelay;
        private double timeRemaining;
        private bool terminalCallbackStarted;

        public override void InitTask(GameplayAbility ability)
        {
            base.InitTask(ability);
            terminalCallbackStarted = false;
        }

        public static AbilityTask_WaitDelay WaitDelay(GameplayAbility ability, float duration)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitDelay>();
            task.timeRemaining = duration;
            return task;
        }

        protected override void OnActivate()
        {
            if (timeRemaining <= 0d)
            {
                CompleteDelay();
            }
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive) return;

            timeRemaining -= deltaTime;
            if (timeRemaining <= 0d)
            {
                CompleteDelay();
            }
        }

        private void CompleteDelay()
        {
            if (IsCancelled ||
                !AbilityTaskTerminalCallbackGuard.TryBegin(
                    this,
                    ref terminalCallbackStarted,
                    out ulong leaseGeneration)) return;
            try
            {
                OnFinishDelay?.Invoke();
            }
            finally
            {
                EndTaskIfCurrentLease(leaseGeneration);
            }
        }

        public override void CancelTask()
        {
            if (!AbilityTaskTerminalCallbackGuard.TryBegin(
                    this,
                    ref terminalCallbackStarted,
                    out ulong leaseGeneration)) return;
            if (IsCurrentLease(leaseGeneration))
            {
                base.CancelTask();
            }
        }

        protected override void OnDestroy()
        {
            OnFinishDelay = null;
            base.OnDestroy();
        }
    }
}
