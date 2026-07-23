using UnityEngine;
using UnityEngine.Audio;
using CycloneGames.Audio.Runtime;

namespace CycloneGames.Audio.Editor
{
    /// <summary>
    /// Immutable, non-owning data captured for one profiler sample. It intentionally stores
    /// names and instance IDs instead of Unity objects or pooled ActiveEvent instances.
    /// </summary>
    internal readonly struct ProfilerEventSnapshot
    {
        public readonly string EventName;
        public readonly string EmitterName;
        public readonly int EmitterInstanceId;
        public readonly string BusName;
        public readonly int BusInstanceId;
        public readonly EventStatus Status;
        public readonly float TimeStarted;

        public ProfilerEventSnapshot(
            string eventName,
            string emitterName,
            int emitterInstanceId,
            string busName,
            int busInstanceId,
            EventStatus status,
            float timeStarted)
        {
            EventName = eventName ?? string.Empty;
            EmitterName = emitterName ?? string.Empty;
            EmitterInstanceId = emitterInstanceId;
            BusName = busName ?? string.Empty;
            BusInstanceId = busInstanceId;
            Status = status;
            TimeStarted = timeStarted;
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
