using UnityEngine;
using UnityEngine.Audio;
using CycloneGames.Audio.Runtime;

namespace CycloneGames.Audio.Editor
{
    /// <summary>
    /// Immutable, non-owning data captured for one profiler sample. It intentionally stores
    /// names and session ids instead of Unity objects or pooled ActiveEvent instances.
    /// </summary>
    internal readonly struct ProfilerEventSnapshot
    {
        public readonly string EventName;
        public readonly string EmitterName;
        public readonly int EmitterId;
        public readonly string BusName;
        public readonly int BusId;
        public readonly EventStatus Status;
        public readonly float TimeStarted;
        /// <summary>Preformatted list row, so repainting the list does not rebuild the string.</summary>
        public readonly string DisplayLine;

        public ProfilerEventSnapshot(
            string eventName,
            string emitterName,
            int emitterId,
            string busName,
            int busId,
            EventStatus status,
            float timeStarted)
        {
            EventName = eventName ?? string.Empty;
            EmitterName = emitterName ?? string.Empty;
            EmitterId = emitterId;
            BusName = busName ?? string.Empty;
            BusId = busId;
            Status = status;
            TimeStarted = timeStarted;
            DisplayLine = $"{timeStarted:F3} : {EventName} - {Status}";
        }
    }

    /// <summary>
    /// Legacy mutable profiler event carrier retained for source compatibility.
    /// AudioProfiler history uses <see cref="ProfilerEventSnapshot"/> instead so it never
    /// retains pooled ActiveEvent instances.
    /// </summary>
    public sealed class ProfilerEvent
    {
        /// <summary>
        /// The name of the ActiveEvent being profiled
        /// </summary>
        public string eventName = "";
        /// <summary>
        /// The name of the audio file being played in the event
        /// </summary>
        public AudioClip clip;
        /// <summary>
        /// The GameObject containing the AudioSource component playing the event
        /// </summary>
        public GameObject emitterObject;
        /// <summary>
        /// The audio bus the event is routed to
        /// </summary>
        public AudioMixerGroup bus;
        /// <summary>
        /// The Active Event reference for the playing sound
        /// </summary>
        public ActiveEvent activeEvent;

        public void Reset()
        {
            eventName = string.Empty;
            clip = null;
            emitterObject = null;
            bus = null;
            activeEvent = null;
        }
    }
}
