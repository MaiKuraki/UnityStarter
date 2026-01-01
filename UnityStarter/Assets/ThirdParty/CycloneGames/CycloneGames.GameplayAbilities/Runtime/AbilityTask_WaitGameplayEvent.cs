using System;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// AbilityTask that waits for a GameplayEvent with a specific tag to be received.
    /// Essential for creating responsive abilities that react to events from other abilities or systems.
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
        private OnTagCountChangedDelegate tagCallback;

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

            // Register for tag change events - use method reference to avoid closure allocation
            tagCallback = HandleTagCountChanged;
            Ability.AbilitySystemComponent.RegisterTagEventCallback(
                eventTag, 
                GameplayTagEventType.NewOrRemoved, 
                tagCallback
            );
        }

        private void HandleTagCountChanged(GameplayTag tag, int newCount)
        {
            if (!IsActive || IsCancelled) return;
            
            // Only trigger when tag is added (count increases from 0 to 1+)
            if (newCount > 0)
            {
                var eventData = new GameplayEventData
                {
                    EventTag = tag,
                    Instigator = Ability.AbilitySystemComponent,
                    Target = Ability.AbilitySystemComponent
                };
                
                OnEventReceived?.Invoke(eventData);
                
                if (onlyTriggerOnce)
                {
                    EndTask();
                }
            }
        }

        public override void CancelTask()
        {
            OnCancelled?.Invoke();
            base.CancelTask();
        }

        protected override void OnDestroy()
        {
            // Unregister callback
            if (Ability?.AbilitySystemComponent != null && tagCallback != null)
            {
                Ability.AbilitySystemComponent.RemoveTagEventCallback(eventTag, GameplayTagEventType.NewOrRemoved, tagCallback);
            }
            
            OnEventReceived = null;
            OnCancelled = null;
            tagCallback = null;
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
