using System;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// AbilityTask that waits for a specific attribute to change value.
    /// UE5: UAbilityTask_WaitAttributeChange.
    /// </summary>
    public class AbilityTask_WaitAttributeChange : AbilityTask
    {
        /// <summary>
        /// Fired when the watched attribute changes. Provides (oldValue, newValue).
        /// </summary>
        public Action<float, float> OnAttributeChanged;
        public Action OnCancelled;

        private GameplayAttribute watchedAttribute;
        private bool onlyTriggerOnce;
        private Action<float, float> attributeChangeCallback;

        /// <summary>
        /// Creates a WaitAttributeChange task.
        /// </summary>
        /// <param name="ability">The owning ability.</param>
        /// <param name="attributeName">The name of the attribute to watch.</param>
        /// <param name="triggerOnce">If true, the task ends after one change. If false, keeps listening.</param>
        public static AbilityTask_WaitAttributeChange WaitAttributeChange(GameplayAbility ability, string attributeName, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitAttributeChange>();
            task.watchedAttribute = ability.AbilitySystemComponent?.GetAttribute(attributeName);
            task.onlyTriggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            if (watchedAttribute == null)
            {
                GASLog.Warning("WaitAttributeChange: Attribute not found.");
                EndTask();
                return;
            }

            attributeChangeCallback = HandleAttributeChange;
            watchedAttribute.OnCurrentValueChanged += attributeChangeCallback;
        }

        private void HandleAttributeChange(float oldValue, float newValue)
        {
            if (!IsActive || IsCancelled) return;

            OnAttributeChanged?.Invoke(oldValue, newValue);

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
            if (watchedAttribute != null && attributeChangeCallback != null)
            {
                watchedAttribute.OnCurrentValueChanged -= attributeChangeCallback;
            }

            OnAttributeChanged = null;
            OnCancelled = null;
            attributeChangeCallback = null;
            watchedAttribute = null;
            base.OnDestroy();
        }
    }
}
