using UnityEngine;

namespace CycloneGames.EventBus.Runtime
{
    /// <summary>
    /// Unity host for an <see cref="EventBusPump"/>. Runs early ([DefaultExecutionOrder(-500)]) so
    /// cross-thread events from the last frame are delivered before most Update calls. Both budgets
    /// are runtime-tunable so a build can lower them on constrained hardware without a code change.
    /// Main-thread only.
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

        // Clamps the per-source budget domain: 0 pauses, positive is the budget, negatives
        // collapse to 0. Shared by setter, OnValidate and Update (OnValidate is Editor-only).
        internal static int ClampBudget(int value)
        {
            return value < 0 ? 0 : value;
        }

        // Maps the serialized per-frame budget: a zero or negative value means "no additional cap",
        // which keeps older serialized data working (the field reads as zero). Pausing is
        // PumpingEnabled's job.
        internal static int ResolveFrameBudget(int value)
        {
            return value > 0 ? value : int.MaxValue;
        }

        private void OnValidate()
        {
            // Deserialization bypasses the property setter.
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
