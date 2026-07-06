using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CycloneGames.Choreography.Timeline
{
    /// <summary>
    /// A Timeline marker that emits a choreography event notification when the playhead crosses it. Add markers of
    /// this type to a <see cref="ChoreographyEventTrack"/> (or any marker-capable track) and bind a
    /// <see cref="ChoreographyNotificationReceiver"/> to receive them. This assembly compiles only when
    /// <c>com.unity.timeline</c> is installed (guarded by the <c>CYCLONEGAMES_HAS_TIMELINE</c> define); the base
    /// Choreography module has no Timeline dependency, so Timeline stays fully optional.
    /// </summary>
    public sealed class ChoreographyMarker : Marker, INotification
    {
        [Tooltip("Choreography event id raised when the playhead crosses this marker.")]
        [SerializeField] private string EventId;

        [Tooltip("Optional scalar payload forwarded with the event.")]
        [SerializeField] private float Magnitude;

        [Tooltip("Optional integer payload forwarded with the event.")]
        [SerializeField] private int IntPayload;

        public string Event => EventId;

        public float EventMagnitude => Magnitude;

        public int EventIntPayload => IntPayload;

        public PropertyName id => new PropertyName("CycloneGames.Choreography.Event");
    }
}
