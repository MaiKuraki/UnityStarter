using UnityEngine;

namespace CycloneGames.EventBus.Runtime
{
    /// <summary>
    /// Unity host for an <see cref="EventBusPump"/>. Attach one to a bootstrap object, register the
    /// queues and streams that need draining, and every buffered event enters its bus at a known
    /// point in the frame with a known ceiling.
    ///
    /// Execution order is deliberately early (before the default time). Cross-thread events that
    /// arrived during the last frame are delivered before most gameplay <c>Update</c> calls run, so
    /// gameplay reads current state in the same frame rather than lagging one frame behind. Change
    /// the order on the component if your game wants the opposite.
    ///
    /// Main-thread only, like everything it drives.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("CycloneGames/EventBus Pump")]
    public sealed class EventBusPumpMonoBehaviour : MonoBehaviour
    {
        [SerializeField]
        [Tooltip(
            "Maximum events published per registered source, per frame. Bounds the worst case; "
            + "events left over are drained on the next frame. Size it to the worst frame you are "
            + "willing to pay, not the average one.")]
        private int maxEventsPerTargetPerFrame = 1024;

        [SerializeField]
        [Tooltip(
            "When false the pump still ticks but publishes nothing. Useful for pausing ingress "
            + "without unregistering sources.")]
        private bool pumpingEnabled = true;

        private readonly EventBusPump _pump = new EventBusPump();

        /// <summary>The pump this component drives. Register sources here during setup.</summary>
        public EventBusPump Pump => _pump;

        /// <summary>
        /// Per-source, per-frame publish ceiling. Tunable at runtime so a build can lower it on
        /// mobile hardware without a code change.
        /// </summary>
        public int MaxEventsPerTargetPerFrame
        {
            get => maxEventsPerTargetPerFrame;
            set => maxEventsPerTargetPerFrame = value < 0 ? 0 : value;
        }

        /// <summary>Whether the pump publishes. Disabling leaves registrations intact.</summary>
        public bool PumpingEnabled
        {
            get => pumpingEnabled;
            set => pumpingEnabled = value;
        }

        private void Update()
        {
            if (pumpingEnabled)
            {
                _pump.Drain(maxEventsPerTargetPerFrame);
            }
        }

        private void OnDestroy()
        {
            _pump.Clear();
        }
    }
}
