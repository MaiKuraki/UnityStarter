using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// AbilityTask that waits for external confirmation or cancellation.
    /// Used for abilities requiring a confirm/cancel step (e.g., targeting, channeling).
    /// UE5: UAbilityTask_WaitConfirmCancel.
    /// Call Confirm() or Cancel() from external code (input handlers, UI, etc.).
    /// </summary>
    public class AbilityTask_WaitConfirmCancel : AbilityTask
    {
        public Action OnConfirm;
        public Action OnCancel;

        private bool resolved;
        private bool terminalCallbackStarted;

        public override void InitTask(GameplayAbility ability)
        {
            base.InitTask(ability);
            terminalCallbackStarted = false;
        }

        /// <summary>
        /// Creates a WaitConfirmCancel task.
        /// </summary>
        public static AbilityTask_WaitConfirmCancel WaitConfirmCancel(GameplayAbility ability)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitConfirmCancel>();
            task.resolved = false;
            return task;
        }

        protected override void OnActivate()
        {
            // Wait for external Confirm() or Cancel() call.
        }

        /// <summary>
        /// Call this to confirm the action.
        /// </summary>
        public void Confirm()
        {
            if (!IsActive || IsCancelled || resolved ||
                !AbilityTaskTerminalCallbackGuard.TryBegin(
                    this,
                    ref terminalCallbackStarted,
                    out ulong leaseGeneration)) return;
            resolved = true;
            try
            {
                OnConfirm?.Invoke();
            }
            finally
            {
                EndTaskIfCurrentLease(leaseGeneration);
            }
        }

        /// <summary>
        /// Call this to cancel the action.
        /// </summary>
        public void Cancel()
        {
            if (!IsActive || resolved) return;
            CancelInternal();
        }

        public override void CancelTask()
        {
            CancelInternal();
        }

        private void CancelInternal()
        {
            if (resolved ||
                !AbilityTaskTerminalCallbackGuard.TryBegin(
                    this,
                    ref terminalCallbackStarted,
                    out ulong leaseGeneration)) return;
            resolved = true;
            try
            {
                OnCancel?.Invoke();
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
            OnConfirm = null;
            OnCancel = null;
            resolved = false;
            base.OnDestroy();
        }
    }
}
