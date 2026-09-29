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
    /// Two budgets are exposed: the per-source one bounds how much of a single backlog a frame may
    /// spend, the per-frame one bounds the whole drain. Only the second bounds the frame, because the
    /// first multiplies by the number of registered sources.
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
            "Maximum events published in total per frame, across every registered source. This is the "
            + "ceiling that bounds frame cost; without it, N sources can each publish the per-source "
            + "budget in one frame. Zero or negative means no additional cap; use PumpingEnabled to "
            + "pause publishing.")]
        private int maxEventsPerFrame = 8192;

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
        /// Whole-drain, per-frame publish ceiling across every registered source. Zero or negative
        /// means no additional cap beyond the per-source budget. Tunable at runtime so a build can
        /// lower it on mobile hardware without a code change.
        /// </summary>
        public int MaxEventsPerFrame
        {
            get => maxEventsPerFrame;
            set => maxEventsPerFrame = value;
        }

        /// <summary>
        /// Clamps the per-source budget to its documented domain: 0 pauses publishing, any positive
        /// value is the budget, negatives collapse to 0. Shared by the setter, <c>OnValidate</c> and
        /// <c>Update</c> so the runtime never depends on an Editor-only pass.
        /// </summary>
        internal static int ClampBudget(int value)
        {
            return value < 0 ? 0 : value;
        }

        /// <summary>
        /// Maps the serialized per-frame budget onto the pump parameter. Zero and negative mean "no
        /// additional cap" rather than "spend nothing", which is why this is not <see cref="ClampBudget"/>:
        /// a component serialized before this field existed deserializes it as zero, and mapping that
        /// to a zero budget would silently stop every buffered event from being delivered on upgrade.
        /// Pausing is <see cref="PumpingEnabled"/>, so this field does not need a pause value.
        /// </summary>
        internal static int ResolveFrameBudget(int value)
        {
            return value > 0 ? value : int.MaxValue;
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
                _pump.Drain(
                    ClampBudget(maxEventsPerTargetPerFrame),
                    ResolveFrameBudget(maxEventsPerFrame));
            }
        }

        private void OnDestroy()
        {
            _pump.Clear();
        }
    }
}
