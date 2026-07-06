using UnityEngine.Timeline;

namespace CycloneGames.Choreography.Timeline
{
    /// <summary>
    /// A marker-only Timeline track for authoring <see cref="ChoreographyMarker"/> events alongside animation and
    /// audio tracks. It carries no clips; it exists to group choreography event markers and give them a distinct
    /// color in the Timeline window. Emitted markers are delivered to bound
    /// <see cref="ChoreographyNotificationReceiver"/> components through the PlayableDirector's notification system.
    /// </summary>
    [TrackColor(0.36f, 0.61f, 0.84f)]
    public sealed class ChoreographyEventTrack : TrackAsset
    {
    }
}
