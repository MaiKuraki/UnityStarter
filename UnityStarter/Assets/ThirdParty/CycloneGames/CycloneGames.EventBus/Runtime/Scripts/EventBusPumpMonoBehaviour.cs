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
            set => maxEventsPerTargetPerFrame = ClampBudget(value);
        }

        /// <summary>
        /// Clamps a per-frame budget to its documented domain: 0 pauses publishing, any positive
        /// value is the budget, negatives collapse to 0. Shared by the setter, <c>OnValidate</c> and
        /// <c>Update</c> so the runtime never depends on an Editor-only pass.
        /// </summary>
        internal static int ClampBudget(int value)
        {
            return value < 0 ? 0 : value;
        }

        private void OnValidate()
        {
            // Deserialization bypasses the property setter, so a prefab or scene authored (or
            // hand-edited) with a negative value would otherwise reach Drain every frame and throw.
            // Clamping here keeps the stored field honest for every later serialize.
            maxEventsPerTargetPerFrame = ClampBudget(maxEventsPerTargetPerFrame);
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
                // Same clamp as OnValidate: OnValidate does not run in a Player build, and a
                // serialized negative value must not become a per-frame ArgumentOutOfRangeException.
                _pump.Drain(ClampBudget(maxEventsPerTargetPerFrame));
            }
        }

        private void OnDestroy()
        {
            _pump.Clear();
        }
    }
}
