using System;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// AbilityTask that waits for a specific gameplay tag to be added to the owner.
    /// UE5: UAbilityTask_WaitGameplayTagAdded.
    /// </summary>
    public class AbilityTask_WaitGameplayTagAdded : AbilityTask
    {
        public Action OnTagAdded;
        public Action OnCancelled;

        private GameplayTag watchedTag;
        private bool onlyTriggerOnce;
        private OnTagCountChangedDelegate tagCallback;

        /// <summary>
        /// Creates a WaitGameplayTagAdded task.
        /// </summary>
        public static AbilityTask_WaitGameplayTagAdded WaitGameplayTagAdded(GameplayAbility ability, GameplayTag tag, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitGameplayTagAdded>();
            task.watchedTag = tag;
            task.onlyTriggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            if (Ability?.AbilitySystemComponent == null || watchedTag.IsNone)
            {
                GASLog.Warning("WaitGameplayTagAdded: Invalid ability or tag.");
                EndTask();
                return;
            }

            // If the tag is already present, fire immediately
            if (Ability.AbilitySystemComponent.HasMatchingGameplayTag(watchedTag))
            {
                OnTagAdded?.Invoke();
                if (onlyTriggerOnce)
                {
                    EndTask();
                    return;
                }
            }

            tagCallback = HandleTagChanged;
            Ability.AbilitySystemComponent.RegisterTagEventCallback(watchedTag, GameplayTagEventType.NewOrRemoved, tagCallback);
        }

        private void HandleTagChanged(GameplayTag tag, int newCount)
        {
            if (!IsActive || IsCancelled) return;
            if (newCount <= 0) return; // Only fires when tag is added (count goes above 0)

            OnTagAdded?.Invoke();
            if (onlyTriggerOnce)
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
            if (Ability?.AbilitySystemComponent != null && tagCallback != null)
            {
                Ability.AbilitySystemComponent.RemoveTagEventCallback(watchedTag, GameplayTagEventType.NewOrRemoved, tagCallback);
            }

            OnTagAdded = null;
            OnCancelled = null;
            tagCallback = null;
            base.OnDestroy();
        }
    }

    /// <summary>
    /// AbilityTask that waits for a specific gameplay tag to be removed from the owner.
    /// UE5: UAbilityTask_WaitGameplayTagRemoved.
    /// </summary>
    public class AbilityTask_WaitGameplayTagRemoved : AbilityTask
    {
        public Action OnTagRemoved;
        public Action OnCancelled;

        private GameplayTag watchedTag;
        private bool onlyTriggerOnce;
        private OnTagCountChangedDelegate tagCallback;

        /// <summary>
        /// Creates a WaitGameplayTagRemoved task.
        /// </summary>
        public static AbilityTask_WaitGameplayTagRemoved WaitGameplayTagRemoved(GameplayAbility ability, GameplayTag tag, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitGameplayTagRemoved>();
            task.watchedTag = tag;
            task.onlyTriggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            if (Ability?.AbilitySystemComponent == null || watchedTag.IsNone)
            {
                GASLog.Warning("WaitGameplayTagRemoved: Invalid ability or tag.");
                EndTask();
                return;
            }

            // If the tag is already absent, fire immediately
            if (!Ability.AbilitySystemComponent.HasMatchingGameplayTag(watchedTag))
            {
                OnTagRemoved?.Invoke();
                if (onlyTriggerOnce)
                {
                    EndTask();
                    return;
                }
            }

            tagCallback = HandleTagChanged;
            Ability.AbilitySystemComponent.RegisterTagEventCallback(watchedTag, GameplayTagEventType.NewOrRemoved, tagCallback);
        }

        private void HandleTagChanged(GameplayTag tag, int newCount)
        {
            if (!IsActive || IsCancelled) return;
            if (newCount > 0) return; // Only fires when tag is removed (count goes to 0)

            OnTagRemoved?.Invoke();
            if (onlyTriggerOnce)
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
            if (Ability?.AbilitySystemComponent != null && tagCallback != null)
            {
                Ability.AbilitySystemComponent.RemoveTagEventCallback(watchedTag, GameplayTagEventType.NewOrRemoved, tagCallback);
            }

            OnTagRemoved = null;
            OnCancelled = null;
            tagCallback = null;
            base.OnDestroy();
        }
    }
}
