using System;
using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
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
        /// Raw provider location string for external banks, custom loaders, or legacy assets.
        /// </summary>
        Location = 0,

        /// <summary>
        /// <see cref="AssetRef"/> value resolved through CycloneGames.AssetManagement.
        /// </summary>
        AssetReference = 1
    }

    /// <summary>
    /// Serializable authoring record for a resource reference. Converted to the immutable Core
    /// <see cref="ChoreographyResourceReference"/> when the owning asset builds its runtime model.
    /// </summary>
    [Serializable]
    public sealed class ChoreographyResourceReferenceAuthoring
    {
        [Tooltip("Active resource authoring source. Location keeps legacy string keys; Asset Reference uses CycloneGames.AssetManagement.")]
        [SerializeField] private ChoreographyResourceSource Source = ChoreographyResourceSource.Location;

        [Tooltip("AssetManagement-backed asset reference. When set, its location is used before the address fallback.")]
        [SerializeField] private AssetRef Asset;

        [Tooltip("Fallback provider location key for manual addresses, external banks, or non-Unity resources.")]
        [SerializeField] private string Address;

        [Tooltip("Resource classification used for preload routing and provider selection.")]
        [SerializeField] private ChoreographyResourceKind Kind = ChoreographyResourceKind.Generic;

        [Tooltip("Optional grouping tag, e.g. a lifetime bucket. Leave empty for none.")]
        [SerializeField] private string Tag;

        public ChoreographyResourceSource SourceMode => Source;

        public AssetRef AssetReference => Source == ChoreographyResourceSource.AssetReference ? Asset : default;

        public string EffectiveLocation
        {
            get
            {
                if (Source == ChoreographyResourceSource.AssetReference)
                {
                    return Asset.IsValid ? Asset.Location : (Address ?? string.Empty);
                }

                return Address ?? string.Empty;
            }
        }

        public bool IsConfigured
        {
            get
            {
                if (Source == ChoreographyResourceSource.AssetReference)
                {
                    return Asset.IsValid;
                }

                return !string.IsNullOrEmpty(Address);
            }
        }

        public ChoreographyResourceReference ToRuntime()
        {
            return new ChoreographyResourceReference(EffectiveLocation, Kind, string.IsNullOrEmpty(Tag) ? null : Tag);
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
        [SerializeField] private List<ChoreographyTrackAuthoring> Tracks = new List<ChoreographyTrackAuthoring>();
        [SerializeField] private List<ChoreographyEventAuthoring> Events = new List<ChoreographyEventAuthoring>();

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

            return new ChoreographySection(Id, Duration, runtimeTracks, runtimeEvents, Interruptible, PreferredMode);
        }
    }
}
