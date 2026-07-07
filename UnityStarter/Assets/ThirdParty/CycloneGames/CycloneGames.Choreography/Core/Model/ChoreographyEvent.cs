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

    /// <summary>
    /// Duration-spanning authored gameplay state, equivalent to an AnimNotifyState.
    /// The state owns a section-relative time span and dispatches Begin/Update/End phases during playback.
    /// </summary>
    public sealed class ChoreographyEventState
    {
        /// <summary>Stable identifier, unique within its owning section.</summary>
        public string Id { get; }

        /// <summary>Semantic event identifier consumed by gameplay systems.</summary>
        public string EventId { get; }

        /// <summary>Start offset from the owning section start, in seconds.</summary>
        public double StartTime { get; }

        /// <summary>End offset from the owning section start, in seconds.</summary>
        public double EndTime { get; }

        /// <summary>Duration in seconds.</summary>
        public double Duration => EndTime - StartTime;

        /// <summary>Optional scalar payload.</summary>
        public float Magnitude { get; }

        /// <summary>Optional integer payload.</summary>
        public int IntPayload { get; }

        /// <summary>Optional string payload. May be null.</summary>
        public string StringPayload { get; }

        public ChoreographyEventState(
            string id,
            string eventId,
            double startTime,
            double endTime,
            float magnitude = 0f,
            int intPayload = 0,
            string stringPayload = null)
        {
            StartTime = startTime < 0d ? 0d : startTime;
            EndTime = endTime < StartTime ? StartTime : endTime;
            Id = id;
            EventId = eventId;
            Magnitude = magnitude;
            IntPayload = intPayload;
            StringPayload = stringPayload;
        }
    }
}
