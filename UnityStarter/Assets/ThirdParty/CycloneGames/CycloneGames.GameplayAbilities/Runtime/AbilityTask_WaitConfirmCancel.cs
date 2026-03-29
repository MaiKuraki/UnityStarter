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
            if (!IsActive || IsCancelled || resolved) return;
            resolved = true;
            OnConfirm?.Invoke();
            EndTask();
        }

        /// <summary>
        /// Call this to cancel the action.
        /// </summary>
        public void Cancel()
        {
            if (!IsActive || resolved) return;
            resolved = true;
            OnCancel?.Invoke();
            CancelTask();
        }

        public override void CancelTask()
        {
            if (!resolved)
            {
                resolved = true;
                OnCancel?.Invoke();
            }
            base.CancelTask();
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
