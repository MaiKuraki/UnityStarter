using System;
using CycloneGames.Choreography.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.Choreography.GameplayAbilities
{
    /// <summary>
    /// AbilityTask that plays a choreography on a <see cref="ChoreographyScheduler"/> for the duration of an
    /// ability and bridges choreography timeline events to GameplayEvents on the owning AbilitySystemComponent.
    ///
    /// Dependency direction is one-way: this integration references both Choreography.Core and GameplayAbilities,
    /// but neither of those references the other. The Choreography Core never depends on GAS, and the GAS core is
    /// not modified. The whole assembly is gated by the <c>CYCLONEGAMES_HAS_GAMEPLAY_ABILITIES</c> define, which is
    /// emitted by asmdef versionDefines when the GameplayAbilities package is installed through UPM.
    ///
    /// Bridging model: when the choreography raises an event, the task forwards it to
    /// <see cref="AbilitySystemComponent.HandleGameplayEvent"/> using a caller-supplied tag resolver
    /// (<c>eventId to GameplayTag</c>). Abilities elsewhere can react via <c>AbilityTask_WaitGameplayEvent</c>.
    /// The task completes when its scheduler instance ends and is interrupted if the ability cancels it.
    /// </summary>
    public sealed class AbilityTask_PlayChoreography : AbilityTask
    {
        /// <summary>Fired when the choreography instance completes naturally.</summary>
        public Action OnCompleted;

        /// <summary>Fired when the task is cancelled/interrupted before completing.</summary>
        public Action OnInterrupted;

        /// <summary>Fired for every choreography event, before the GameplayEvent bridge runs.</summary>
        public Action<ChoreographyEventInvocation> OnChoreographyEvent;

        private ChoreographyScheduler _scheduler;
        private IChoreographyAsset _asset;
        private ChoreographyPlayRequest _request;
        private Func<string, GameplayTag> _eventTagResolver;
        private int _instanceId = ChoreographyScheduler.InvalidInstanceId;

        /// <summary>
        /// Creates the task. <paramref name="eventTagResolver"/> maps a choreography event id to a GameplayTag; when
        /// it returns a valid (non-None) tag, the event is forwarded to the ASC as a GameplayEvent. Pass null to
        /// only receive <see cref="OnChoreographyEvent"/> callbacks without the GameplayEvent bridge.
        /// </summary>
        public static AbilityTask_PlayChoreography PlayChoreography(
            GameplayAbility ability,
            ChoreographyScheduler scheduler,
            IChoreographyAsset asset,
            ChoreographyPlayRequest request,
            Func<string, GameplayTag> eventTagResolver = null)
        {
            AbilityTask_PlayChoreography task = ability.NewAbilityTask<AbilityTask_PlayChoreography>();
            task._scheduler = scheduler;
            task._asset = asset;
            task._request = request;
            task._eventTagResolver = eventTagResolver;
            return task;
        }

        protected override void OnActivate()
        {
            if (_scheduler == null || _asset == null || Ability?.AbilitySystemComponent == null)
            {
                GASLog.Warning("AbilityTask_PlayChoreography: missing scheduler, asset, or ability system component.");
                EndTask();
                return;
            }

            _scheduler.EventRaised += HandleChoreographyEvent;
            _scheduler.InstanceEnded += HandleInstanceEnded;

            _instanceId = _scheduler.Play(_asset, _request);
            if (_instanceId == ChoreographyScheduler.InvalidInstanceId)
            {
                // The strategy rejected the request; nothing to wait for.
                EndTask();
            }
        }

        private void HandleChoreographyEvent(ChoreographyEventInvocation invocation)
        {
            if (invocation.InstanceId != _instanceId || !IsActive)
            {
                return;
            }

            OnChoreographyEvent?.Invoke(invocation);

            if (_eventTagResolver == null)
            {
                return;
            }

            GameplayTag tag = _eventTagResolver(invocation.Event.EventId);
            if (tag.IsNone)
            {
                return;
            }

            AbilitySystemComponent asc = Ability.AbilitySystemComponent;
            asc.HandleGameplayEvent(new GameplayEventData
            {
                EventTag = tag,
                Instigator = asc,
                Target = asc,
                EventMagnitude = invocation.Event.Magnitude,
                OptionalObject = invocation.Event.StringPayload
            });
        }

        private void HandleInstanceEnded(int instanceId)
        {
            if (instanceId != _instanceId || !IsActive)
            {
                return;
            }

            OnCompleted?.Invoke();
            EndTask();
        }

        public override void CancelTask()
        {
            if (_scheduler != null && _instanceId != ChoreographyScheduler.InvalidInstanceId)
            {
                _scheduler.Stop(_instanceId);
            }
            OnInterrupted?.Invoke();
            base.CancelTask();
        }

        protected override void OnDestroy()
        {
            if (_scheduler != null)
            {
                _scheduler.EventRaised -= HandleChoreographyEvent;
                _scheduler.InstanceEnded -= HandleInstanceEnded;
            }

            OnCompleted = null;
            OnInterrupted = null;
            OnChoreographyEvent = null;
            _scheduler = null;
            _asset = null;
            _eventTagResolver = null;
            _instanceId = ChoreographyScheduler.InvalidInstanceId;

            base.OnDestroy();
        }
    }
}
