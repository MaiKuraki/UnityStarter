using System;
using System.Collections.Generic;
using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Defines how a clip resource location is authored before it is converted to the provider-agnostic Core key.
    /// </summary>
    public enum ChoreographyResourceSource
    {
        /// <summary>
        /// Raw provider location string for external banks, custom loaders, or non-Unity resources.
        /// </summary>
        Location = 0,

        /// <summary>
        /// Loader-agnostic asset key built from a runtime location and an editor GUID.
        /// </summary>
        AssetKey = 1,

        /// <summary>
        /// Backend-owned cue such as a Wwise event, CycloneGames.Audio event, or other non-Unity-object handle.
        /// </summary>
        BackendCue = 2
    }

    /// <summary>
    /// Serializable authoring record for a resource reference. Converted to the immutable Core
    /// <see cref="ChoreographyResourceReference"/> when the owning asset builds its runtime model.
    /// </summary>
    [Serializable]
    public sealed class ChoreographyResourceReferenceAuthoring
    {
        [Tooltip("Active resource authoring source. Location uses provider keys; Asset Key stores a loader-agnostic Location/Guid pair; Backend Cue stores non-Unity-object event data.")]
        [SerializeField] private ChoreographyResourceSource Source = ChoreographyResourceSource.Location;

        [Tooltip("Loader-agnostic asset key. Resource integrations can use this location without coupling the base package to a concrete asset system.")]
        [SerializeField] private ChoreographyAssetKey Asset;

        [Tooltip("Provider location key for manual addresses, external banks, or non-Unity resources.")]
        [SerializeField] private string Address;

        [Tooltip("Backend id for event-style resources, e.g. CycloneGames.Audio, Wwise, UnityAudioClip, or a project-specific provider.")]
        [SerializeField] private string Backend;

        [Tooltip("Optional bank, collection, or package id used by event-style resources.")]
        [SerializeField] private string Bank;

        [Tooltip("Event or cue id resolved by the selected backend.")]
        [SerializeField] private string Cue;

        [Tooltip("Resource classification used for preload routing and provider selection.")]
        [SerializeField] private ChoreographyResourceKind Kind = ChoreographyResourceKind.Generic;

        [Tooltip("Optional grouping tag, e.g. a lifetime bucket. Leave empty for none.")]
        [SerializeField] private string Tag;

        public ChoreographyResourceSource SourceMode => Source;

        public ChoreographyAssetKey AssetKey => Source == ChoreographyResourceSource.AssetKey ? Asset : default;

        public string BackendId => Source == ChoreographyResourceSource.BackendCue ? Backend : null;

        public string BankId => Source == ChoreographyResourceSource.BackendCue ? Bank : null;

        public string CueId => Source == ChoreographyResourceSource.BackendCue ? Cue : null;

        public string EffectiveLocation
        {
            get
            {
                if (Source == ChoreographyResourceSource.AssetKey)
                {
                    return Asset.IsValid ? Asset.Location : (Address ?? string.Empty);
                }

                if (Source == ChoreographyResourceSource.BackendCue)
                {
                    return Cue ?? string.Empty;
                }

                return Address ?? string.Empty;
            }
        }

        public bool IsConfigured
        {
            get
            {
                if (Source == ChoreographyResourceSource.AssetKey)
                {
                    return Asset.IsValid;
                }

                if (Source == ChoreographyResourceSource.BackendCue)
                {
                    return !string.IsNullOrEmpty(Backend) && !string.IsNullOrEmpty(Cue);
                }

                return !string.IsNullOrEmpty(Address);
            }
        }

        public ChoreographyResourceReference ToRuntime()
        {
            return new ChoreographyResourceReference(
                EffectiveLocation,
                Kind,
                string.IsNullOrEmpty(Tag) ? null : Tag,
                Source == ChoreographyResourceSource.BackendCue && !string.IsNullOrEmpty(Backend) ? Backend : null,
                Source == ChoreographyResourceSource.BackendCue && !string.IsNullOrEmpty(Bank) ? Bank : null);
        }
    }

    /// <summary>Serializable authoring record for a single clip.</summary>
    [Serializable]
    public sealed class ChoreographyClipAuthoring
    {
        [SerializeField] private string Id;
        [SerializeField] private ChoreographyResourceReferenceAuthoring Resource = new ChoreographyResourceReferenceAuthoring();

        [Tooltip("Start offset from the owning section start, in seconds.")]
        [SerializeField] private double StartTime;

        [Tooltip("Duration in seconds. A value <= 0 marks a fire-and-forget one-shot.")]
        [SerializeField] private double Duration;

        [Range(0f, 1f)]
        [SerializeField] private float Weight = 1f;

        [Tooltip("Optional sub-channel within the track (e.g. an animation layer or audio bus index).")]
        [SerializeField] private int Channel;

        [SerializeField] private bool Loop;

        public ChoreographyClip ToRuntime()
        {
            ChoreographyResourceReference reference = Resource != null ? Resource.ToRuntime() : default;
            return new ChoreographyClip(Id, reference, StartTime, Duration, Weight, Channel, Loop);
        }
    }

    /// <summary>Serializable authoring record for a timeline event.</summary>
    [Serializable]
    public sealed class ChoreographyEventAuthoring
    {
        [SerializeField] private string EventId;
        [SerializeField] private double Time;
        [SerializeField] private float Magnitude;
        [SerializeField] private int IntPayload;
        [SerializeField] private string StringPayload;

        public ChoreographyEvent ToRuntime()
        {
            return new ChoreographyEvent(EventId, Time, Magnitude, IntPayload, string.IsNullOrEmpty(StringPayload) ? null : StringPayload);
        }
    }

    /// <summary>Serializable authoring record for a duration-spanning event state.</summary>
    [Serializable]
    public sealed class ChoreographyEventStateAuthoring
    {
        [SerializeField] private string Id;
        [SerializeField] private string EventId;

        [Tooltip("Start offset from the owning section start, in seconds.")]
        [SerializeField] private double StartTime;

        [Tooltip("End offset from the owning section start, in seconds.")]
        [SerializeField] private double EndTime;

        [SerializeField] private float Magnitude;
        [SerializeField] private int IntPayload;
        [SerializeField] private string StringPayload;

        public ChoreographyEventState ToRuntime()
        {
            return new ChoreographyEventState(
                Id,
                EventId,
                StartTime,
                EndTime,
                Magnitude,
                IntPayload,
                string.IsNullOrEmpty(StringPayload) ? null : StringPayload);
        }
    }

    /// <summary>Serializable authoring record for a track (a lane of clips of one kind).</summary>
    [Serializable]
    public sealed class ChoreographyTrackAuthoring
    {
        [SerializeField] private string Id;
        [SerializeField] private ChoreographyTrackKind Kind = ChoreographyTrackKind.Animation;
        [SerializeField] private List<ChoreographyClipAuthoring> Clips = new List<ChoreographyClipAuthoring>();

        public ChoreographyTrack ToRuntime()
        {
            int count = Clips != null ? Clips.Count : 0;
            ChoreographyClip[] runtimeClips = count == 0 ? Array.Empty<ChoreographyClip>() : new ChoreographyClip[count];
            for (int i = 0; i < count; i++)
            {
                runtimeClips[i] = Clips[i].ToRuntime();
            }
            return new ChoreographyTrack(Id, Kind, runtimeClips);
        }
    }

    /// <summary>Serializable authoring record for a section (a sequential timeline segment).</summary>
    [Serializable]
    public sealed class ChoreographySectionAuthoring
    {
        [SerializeField] private string Id;
        [SerializeField] private double Duration;
        [Tooltip("When false, the scheduler must not interrupt this section (e.g. a committed attack windup).")]
        [SerializeField] private bool Interruptible = true;
        [Tooltip("Default competition strategy while this section is dominant on its channel.")]
        [SerializeField] private ChoreographyPlaybackMode PreferredMode = ChoreographyPlaybackMode.Inherit;
        [Tooltip("Preferred time authority for this section. Inherit uses the active request driver. Internal Timeline uses choreography time. Fixed Frame quantizes samples.")]
        [SerializeField] private ChoreographySectionClockSource ClockSource = ChoreographySectionClockSource.Inherit;
        [Tooltip("Behavior when an external section source has no more samples before the section ends.")]
        [SerializeField] private ChoreographyExternalClockEndPolicy ExternalEndPolicy = ChoreographyExternalClockEndPolicy.ContinueInternal;
        [Tooltip("Frame rate used when this section is driven by Fixed Frame mode.")]
        [SerializeField] private double FrameRate = 60d;
        [SerializeField] private List<ChoreographyTrackAuthoring> Tracks = new List<ChoreographyTrackAuthoring>();
        [SerializeField] private List<ChoreographyEventAuthoring> Events = new List<ChoreographyEventAuthoring>();
        [SerializeField] private List<ChoreographyEventStateAuthoring> EventStates = new List<ChoreographyEventStateAuthoring>();

        public ChoreographySection ToRuntime()
        {
            int trackCount = Tracks != null ? Tracks.Count : 0;
            ChoreographyTrack[] runtimeTracks = trackCount == 0 ? Array.Empty<ChoreographyTrack>() : new ChoreographyTrack[trackCount];
            for (int i = 0; i < trackCount; i++)
            {
                runtimeTracks[i] = Tracks[i].ToRuntime();
            }

            int eventCount = Events != null ? Events.Count : 0;
            ChoreographyEvent[] runtimeEvents = eventCount == 0 ? Array.Empty<ChoreographyEvent>() : new ChoreographyEvent[eventCount];
            for (int i = 0; i < eventCount; i++)
            {
                runtimeEvents[i] = Events[i].ToRuntime();
            }

            int eventStateCount = EventStates != null ? EventStates.Count : 0;
            ChoreographyEventState[] runtimeEventStates = eventStateCount == 0
                ? Array.Empty<ChoreographyEventState>()
                : new ChoreographyEventState[eventStateCount];
            for (int i = 0; i < eventStateCount; i++)
            {
                runtimeEventStates[i] = EventStates[i].ToRuntime();
            }

            ChoreographySectionClock clock = new ChoreographySectionClock(ClockSource, ExternalEndPolicy, FrameRate);
            return new ChoreographySection(Id, Duration, runtimeTracks, runtimeEvents, Interruptible, PreferredMode, runtimeEventStates, clock);
        }
    }
}
