namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Immutable authored gameplay marker on a section timeline. Events are dispatched when the playhead
    /// crosses <see cref="Time"/>. They carry no behavior; consumers (e.g. a GameplayAbilities bridge) decide
    /// how to react. Timing is section-relative seconds.
    /// </summary>
    public readonly struct ChoreographyEvent
    {
        /// <summary>Stable event identifier (e.g. "Hit", "Cast", "FootstepLeft").</summary>
        public readonly string EventId;

        /// <summary>Trigger time relative to the owning section start, in seconds. Must be &gt;= 0.</summary>
        public readonly double Time;

        /// <summary>Optional scalar payload (e.g. damage magnitude, intensity).</summary>
        public readonly float Magnitude;

        /// <summary>Optional integer payload (e.g. a hit index or channel).</summary>
        public readonly int IntPayload;

        /// <summary>Optional string payload (e.g. a socket or bone name). May be null.</summary>
        public readonly string StringPayload;

        public ChoreographyEvent(string eventId, double time, float magnitude = 0f, int intPayload = 0, string stringPayload = null)
        {
            EventId = eventId;
            Time = time < 0d ? 0d : time;
            Magnitude = magnitude;
            IntPayload = intPayload;
            StringPayload = stringPayload;
        }
    }
}
