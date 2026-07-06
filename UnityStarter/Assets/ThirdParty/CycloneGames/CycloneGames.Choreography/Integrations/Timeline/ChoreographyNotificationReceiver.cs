using System;
using CycloneGames.Choreography.Core;
using UnityEngine;
using UnityEngine.Playables;

namespace CycloneGames.Choreography.Timeline
{
    /// <summary>
    /// Receives <see cref="ChoreographyMarker"/> notifications from a bound PlayableDirector and republishes them as
    /// engine-free <see cref="ChoreographyEventInvocation"/> values. Wire <see cref="EventRaised"/> to the same
    /// gameplay bridge used by <see cref="ChoreographyScheduler.EventRaised"/> so Timeline-authored and
    /// scheduler-driven choreographies share one event path. Attach this component next to the PlayableDirector.
    /// </summary>
    public sealed class ChoreographyNotificationReceiver : MonoBehaviour, INotificationReceiver
    {
        [Tooltip("Instance id stamped on republished events (lets consumers distinguish Timeline sources).")]
        [SerializeField] private int SourceInstanceId;

        /// <summary>Raised for every choreography marker crossed by the bound director.</summary>
        public event Action<ChoreographyEventInvocation> EventRaised;

        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (!(notification is ChoreographyMarker marker))
            {
                return;
            }

            ChoreographyEvent choreographyEvent = new ChoreographyEvent(
                marker.Event,
                (float)origin.GetTime(),
                marker.EventMagnitude,
                marker.EventIntPayload);

            EventRaised?.Invoke(new ChoreographyEventInvocation(SourceInstanceId, in choreographyEvent));
        }
    }
}
