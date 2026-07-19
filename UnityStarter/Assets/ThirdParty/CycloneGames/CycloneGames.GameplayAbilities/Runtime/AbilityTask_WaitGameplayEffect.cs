using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// AbilityTask that waits for a GameplayEffect to be applied to the owner's ASC.
    /// UE5: UAbilityTask_WaitGameplayEffectApplied.
    /// </summary>
    public class AbilityTask_WaitGameplayEffectApplied : AbilityTask
    {
        /// <summary>
        /// Fired when a matching effect is applied. Provides the applied ActiveGameplayEffect.
        /// </summary>
        public Action<ActiveGameplayEffect> OnEffectApplied;
        public Action OnCancelled;

        private bool onlyTriggerOnce;
        private ActiveEffectDelegate effectCallback;
        private AbilitySystemComponent subscriptionOwner;
        private bool terminalCallbackStarted;

        public override void InitTask(GameplayAbility ability)
        {
            base.InitTask(ability);
            terminalCallbackStarted = false;
        }

        /// <summary>
        /// Creates a WaitGameplayEffectApplied task that fires when any effect is applied to the owner.
        /// </summary>
        public static AbilityTask_WaitGameplayEffectApplied WaitGameplayEffectApplied(GameplayAbility ability, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitGameplayEffectApplied>();
            task.onlyTriggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            subscriptionOwner = Ability?.AbilitySystemComponent;
            if (subscriptionOwner == null)
            {
                GASLog.Warning("WaitGameplayEffectApplied: Invalid ability or ASC.");
                EndTask();
                return;
            }

            effectCallback = HandleEffectApplied;
            subscriptionOwner.OnGameplayEffectAppliedToSelf += effectCallback;
        }

        private void HandleEffectApplied(ActiveGameplayEffect effect)
        {
            if (!IsActive || IsCancelled) return;

            if (onlyTriggerOnce)
            {
                if (!AbilityTaskTerminalCallbackGuard.TryBegin(
                        this,
                        ref terminalCallbackStarted,
                        out ulong leaseGeneration)) return;
                try
                {
                    OnEffectApplied?.Invoke(effect);
                }
                finally
                {
                    EndTaskIfCurrentLease(leaseGeneration);
                }
                return;
            }

            OnEffectApplied?.Invoke(effect);
        }

        public override void CancelTask()
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
            if (subscriptionOwner != null && effectCallback != null)
            {
                subscriptionOwner.OnGameplayEffectAppliedToSelf -= effectCallback;
            }

            OnEffectApplied = null;
            OnCancelled = null;
            effectCallback = null;
            subscriptionOwner = null;
            base.OnDestroy();
        }
    }

    /// <summary>
    /// AbilityTask that waits for a GameplayEffect to be removed from the owner's ASC.
    /// UE5: UAbilityTask_WaitGameplayEffectRemoved.
    /// </summary>
    public class AbilityTask_WaitGameplayEffectRemoved : AbilityTask
    {
        /// <summary>
        /// Fired when a matching effect is removed. Provides the removed ActiveGameplayEffect.
        /// </summary>
        public Action<ActiveGameplayEffect> OnEffectRemoved;
        public Action OnCancelled;

        private bool onlyTriggerOnce;
        private ActiveEffectDelegate effectCallback;
        private AbilitySystemComponent subscriptionOwner;
        private bool terminalCallbackStarted;

        public override void InitTask(GameplayAbility ability)
        {
            base.InitTask(ability);
            terminalCallbackStarted = false;
        }

        /// <summary>
        /// Creates a WaitGameplayEffectRemoved task that fires when any effect is removed from the owner.
        /// </summary>
        public static AbilityTask_WaitGameplayEffectRemoved WaitGameplayEffectRemoved(GameplayAbility ability, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitGameplayEffectRemoved>();
            task.onlyTriggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            subscriptionOwner = Ability?.AbilitySystemComponent;
            if (subscriptionOwner == null)
            {
                GASLog.Warning("WaitGameplayEffectRemoved: Invalid ability or ASC.");
                EndTask();
                return;
            }

            effectCallback = HandleEffectRemoved;
            subscriptionOwner.OnGameplayEffectRemovedFromSelf += effectCallback;
        }

        private void HandleEffectRemoved(ActiveGameplayEffect effect)
        {
            if (!IsActive || IsCancelled) return;

            if (onlyTriggerOnce)
            {
                if (!AbilityTaskTerminalCallbackGuard.TryBegin(
                        this,
                        ref terminalCallbackStarted,
                        out ulong leaseGeneration)) return;
                try
                {
                    OnEffectRemoved?.Invoke(effect);
                }
                finally
                {
                    EndTaskIfCurrentLease(leaseGeneration);
                }
                return;
            }

            OnEffectRemoved?.Invoke(effect);
        }

        public override void CancelTask()
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
            if (subscriptionOwner != null && effectCallback != null)
            {
                subscriptionOwner.OnGameplayEffectRemovedFromSelf -= effectCallback;
            }

            OnEffectRemoved = null;
            OnCancelled = null;
            effectCallback = null;
            subscriptionOwner = null;
            base.OnDestroy();
        }
    }
}
