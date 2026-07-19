using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// AbilityTask that waits for any ability to be activated on the owning ASC.
    /// UE5: UAbilityTask_WaitAbilityActivate.
    /// </summary>
    public class AbilityTask_WaitAbilityActivate : AbilityTask
    {
        public Action<GameplayAbility> OnAbilityActivated;
        public Action OnCancelled;

        private Action<GameplayAbility> cachedCallback;
        private AbilitySystemComponent subscriptionOwner;
        private bool triggerOnce;
        private bool terminalCallbackStarted;

        public override void InitTask(GameplayAbility ability)
        {
            base.InitTask(ability);
            terminalCallbackStarted = false;
        }

        public static AbilityTask_WaitAbilityActivate WaitAbilityActivate(GameplayAbility ability, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitAbilityActivate>();
            task.triggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            subscriptionOwner = Ability?.AbilitySystemComponent;
            if (subscriptionOwner == null)
            {
                EndTask();
                return;
            }

            cachedCallback = OnAbilityActivatedCallback;
            subscriptionOwner.OnAbilityActivated += cachedCallback;
        }

        private void OnAbilityActivatedCallback(GameplayAbility activatedAbility)
        {
            // Don't trigger for the ability that owns this task
            if (activatedAbility == Ability) return;

            if (triggerOnce)
            {
                if (!AbilityTaskTerminalCallbackGuard.TryBegin(
                        this,
                        ref terminalCallbackStarted,
                        out ulong leaseGeneration)) return;
                try
                {
                    OnAbilityActivated?.Invoke(activatedAbility);
                }
                finally
                {
                    EndTaskIfCurrentLease(leaseGeneration);
                }
                return;
            }

            OnAbilityActivated?.Invoke(activatedAbility);
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
            if (subscriptionOwner != null && cachedCallback != null)
            {
                subscriptionOwner.OnAbilityActivated -= cachedCallback;
            }
            OnAbilityActivated = null;
            OnCancelled = null;
            cachedCallback = null;
            subscriptionOwner = null;
            base.OnDestroy();
        }
    }

    /// <summary>
    /// AbilityTask that waits for any ability to end on the owning ASC.
    /// UE5: UAbilityTask_WaitAbilityEnd (subset of WaitGameplayAbilityEnd).
    /// </summary>
    public class AbilityTask_WaitAbilityEnd : AbilityTask
    {
        public Action<GameplayAbility> OnAbilityEnded;
        public Action OnCancelled;

        private Action<GameplayAbility> cachedCallback;
        private AbilitySystemComponent subscriptionOwner;
        private bool triggerOnce;
        private bool terminalCallbackStarted;

        public override void InitTask(GameplayAbility ability)
        {
            base.InitTask(ability);
            terminalCallbackStarted = false;
        }

        public static AbilityTask_WaitAbilityEnd WaitAbilityEnd(GameplayAbility ability, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitAbilityEnd>();
            task.triggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            subscriptionOwner = Ability?.AbilitySystemComponent;
            if (subscriptionOwner == null)
            {
                EndTask();
                return;
            }

            cachedCallback = OnAbilityEndedCallback;
            subscriptionOwner.OnAbilityEndedEvent += cachedCallback;
        }

        private void OnAbilityEndedCallback(GameplayAbility endedAbility)
        {
            if (endedAbility == Ability) return;

            if (triggerOnce)
            {
                if (!AbilityTaskTerminalCallbackGuard.TryBegin(
                        this,
                        ref terminalCallbackStarted,
                        out ulong leaseGeneration)) return;
                try
                {
                    OnAbilityEnded?.Invoke(endedAbility);
                }
                finally
                {
                    EndTaskIfCurrentLease(leaseGeneration);
                }
                return;
            }

            OnAbilityEnded?.Invoke(endedAbility);
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
            if (subscriptionOwner != null && cachedCallback != null)
            {
                subscriptionOwner.OnAbilityEndedEvent -= cachedCallback;
            }
            OnAbilityEnded = null;
            OnCancelled = null;
            cachedCallback = null;
            subscriptionOwner = null;
            base.OnDestroy();
        }
    }
}
