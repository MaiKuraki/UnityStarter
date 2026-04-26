using System;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// AbilityTask that waits for a GameplayEvent with a specific tag to be received.
    /// Listens to the ASC's GameplayEvent delegate system (SendGameplayEventToActor pattern),
    /// NOT tag count changes. This matches UE5's UAbilityTask_WaitGameplayEvent behavior.
    /// </summary>
    public class AbilityTask_WaitGameplayEvent : AbilityTask
    {
        /// <summary>
        /// Fired when a matching GameplayEvent is received.
        /// </summary>
        public Action<GameplayEventData> OnEventReceived;

        /// <summary>
        /// Fired if the wait is cancelled or the ability ends before receiving an event.
        /// </summary>
        public Action OnCancelled;

        private GameplayTag eventTag;
        private bool onlyTriggerOnce;
        private GameplayEventDelegate eventCallback;

        /// <summary>
        /// Creates a WaitGameplayEvent task.
        /// </summary>
        /// <param name="ability">The owning ability.</param>
        /// <param name="tag">The GameplayTag to listen for.</param>
        /// <param name="triggerOnce">If true, the task ends after receiving one event. If false, it continues listening.</param>
        public static AbilityTask_WaitGameplayEvent WaitGameplayEvent(GameplayAbility ability, GameplayTag tag, bool triggerOnce = true)
        {
            var task = ability.NewAbilityTask<AbilityTask_WaitGameplayEvent>();
            task.eventTag = tag;
            task.onlyTriggerOnce = triggerOnce;
            return task;
        }

        protected override void OnActivate()
        {
            if (Ability?.AbilitySystemComponent == null || eventTag.IsNone)
            {
                GASLog.Warning("WaitGameplayEvent: Invalid ability or event tag.");
                EndTask();
                return;
            }

            // Register for gameplay events via the ASC's event delegate system
            eventCallback = HandleGameplayEvent;
            Ability.AbilitySystemComponent.RegisterGameplayEventCallback(eventTag, eventCallback);
        }

        private void HandleGameplayEvent(GameplayEventData eventData)
        {
            if (!IsActive || IsCancelled) return;

            OnEventReceived?.Invoke(eventData);

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
            // Unregister callback from the event delegate system
            if (Ability?.AbilitySystemComponent != null && eventCallback != null)
            {
                Ability.AbilitySystemComponent.RemoveGameplayEventCallback(eventTag, eventCallback);
            }

            OnEventReceived = null;
            OnCancelled = null;
            eventCallback = null;
            base.OnDestroy();
        }
    }

    /// <summary>
    /// Data payload for gameplay events.
    /// </summary>
    public struct GameplayEventData
    {
        /// <summary>
        /// The tag of the event that was triggered.
        /// </summary>
        public GameplayTag EventTag;

        /// <summary>
        /// The AbilitySystemComponent that caused this event.
        /// </summary>
        public AbilitySystemComponent Instigator;

        /// <summary>
        /// The AbilitySystemComponent that should receive this event.
        /// </summary>
        public AbilitySystemComponent Target;

        /// <summary>
        /// Optional magnitude value associated with the event.
        /// </summary>
        public float EventMagnitude;

        /// <summary>
        /// Optional context data (e.g., hit location, source ability).
        /// </summary>
        public object OptionalObject;
    }
}
