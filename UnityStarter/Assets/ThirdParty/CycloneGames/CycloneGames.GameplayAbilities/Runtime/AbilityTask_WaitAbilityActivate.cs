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
        private bool triggerOnce;

        public static AbilityTask_WaitAbilityActivate WaitAbilityActivate(GameplayAbility ability, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitAbilityActivate>();
            task.triggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            cachedCallback = OnAbilityActivatedCallback;
            Ability.AbilitySystemComponent.OnAbilityActivated += cachedCallback;
        }

        private void OnAbilityActivatedCallback(GameplayAbility activatedAbility)
        {
            // Don't trigger for the ability that owns this task
            if (activatedAbility == Ability) return;

            OnAbilityActivated?.Invoke(activatedAbility);

            if (triggerOnce)
            {
                EndTask();
            }
        }

        public override void CancelTask()
        {
            OnCancelled?.Invoke();
            base.CancelTask();
        }

        protected override void OnDestroy()
        {
            if (Ability?.AbilitySystemComponent != null && cachedCallback != null)
            {
                Ability.AbilitySystemComponent.OnAbilityActivated -= cachedCallback;
            }
            OnAbilityActivated = null;
            OnCancelled = null;
            cachedCallback = null;
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
        private bool triggerOnce;

        public static AbilityTask_WaitAbilityEnd WaitAbilityEnd(GameplayAbility ability, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitAbilityEnd>();
            task.triggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            cachedCallback = OnAbilityEndedCallback;
            Ability.AbilitySystemComponent.OnAbilityEndedEvent += cachedCallback;
        }

        private void OnAbilityEndedCallback(GameplayAbility endedAbility)
        {
            if (endedAbility == Ability) return;

            OnAbilityEnded?.Invoke(endedAbility);

            if (triggerOnce)
            {
                EndTask();
            }
        }

        public override void CancelTask()
        {
            OnCancelled?.Invoke();
            base.CancelTask();
        }

        protected override void OnDestroy()
        {
            if (Ability?.AbilitySystemComponent != null && cachedCallback != null)
            {
                Ability.AbilitySystemComponent.OnAbilityEndedEvent -= cachedCallback;
            }
            OnAbilityEnded = null;
            OnCancelled = null;
            cachedCallback = null;
            base.OnDestroy();
        }
    }
}
