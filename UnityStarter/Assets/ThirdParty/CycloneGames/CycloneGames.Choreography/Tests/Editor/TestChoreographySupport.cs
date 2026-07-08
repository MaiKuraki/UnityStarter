using System.Collections.Generic;
using CycloneGames.Choreography.Core;

namespace CycloneGames.Choreography.Tests
{
    /// <summary>
    /// Minimal in-memory <see cref="IChoreographyAsset"/> for engine-free Core tests (no ScriptableObject needed).
    /// </summary>
    internal sealed class TestChoreographyAsset : IChoreographyAsset
    {
        private readonly List<ChoreographySection> _sections;

        public TestChoreographyAsset(string id, params ChoreographySection[] sections)
        {
            Id = id;
            _sections = new List<ChoreographySection>(sections);
            double total = 0d;
            for (int i = 0; i < _sections.Count; i++)
            {
                total += _sections[i].Duration;
            }
            TotalDuration = total;
        }

        public string Id { get; }

        public double TotalDuration { get; }

        public IReadOnlyList<ChoreographySection> Sections => _sections;

        public int CollectResourceReferences(List<ChoreographyResourceReference> results)
        {
            int added = 0;
            for (int s = 0; s < _sections.Count; s++)
            {
                ChoreographyTrack[] tracks = _sections[s].Tracks;
                for (int t = 0; t < tracks.Length; t++)
                {
                    ChoreographyClip[] clips = tracks[t].Clips;
                    for (int c = 0; c < clips.Length; c++)
                    {
                        if (clips[c].Resource.IsValid)
                        {
                            results.Add(clips[c].Resource);
                            added++;
                        }
                    }
                }
            }
            return added;
        }
    }

    /// <summary>Records every provider call so tests can assert lifecycle and resolved weights.</summary>
    internal sealed class RecordingProvider : IAnimationProvider, IAudioProvider, IVfxProvider
    {
        public readonly List<string> Begun = new List<string>();
        public readonly List<string> Ended = new List<string>();
        public readonly Dictionary<string, float> LastWeight = new Dictionary<string, float>();
        public readonly Dictionary<string, double> LastTimelineTime = new Dictionary<string, double>();
        public readonly Dictionary<string, double> LastLocalTime = new Dictionary<string, double>();
        public readonly Dictionary<string, int> LastClipChannel = new Dictionary<string, int>();
        public readonly Dictionary<string, long> LastTickIndex = new Dictionary<string, long>();
        public readonly Dictionary<string, ChoreographyClockKind> LastClockKind = new Dictionary<string, ChoreographyClockKind>();
        public int CompletedStops;

        public void BeginClip(in ChoreographyPlaybackSample sample)
        {
            Begun.Add(sample.Clip.Id);
            LastWeight[sample.Clip.Id] = sample.Weight;
            LastTimelineTime[sample.Clip.Id] = sample.TimelineTime;
            LastLocalTime[sample.Clip.Id] = sample.LocalTime;
            LastClipChannel[sample.Clip.Id] = sample.ClipChannel;
            LastTickIndex[sample.Clip.Id] = sample.TickIndex;
            LastClockKind[sample.Clip.Id] = sample.ClockKind;
        }

        public void UpdateClip(in ChoreographyPlaybackSample sample)
        {
            LastWeight[sample.Clip.Id] = sample.Weight;
            LastTimelineTime[sample.Clip.Id] = sample.TimelineTime;
            LastLocalTime[sample.Clip.Id] = sample.LocalTime;
            LastClipChannel[sample.Clip.Id] = sample.ClipChannel;
            LastTickIndex[sample.Clip.Id] = sample.TickIndex;
            LastClockKind[sample.Clip.Id] = sample.ClockKind;
        }

        public void EndClip(in ChoreographyClipStop stop)
        {
            Ended.Add(stop.ClipId);
            if (stop.Completed)
            {
                CompletedStops++;
            }
        }
    }

    /// <summary>Provider set backed by a single <see cref="RecordingProvider"/>.</summary>
    internal sealed class RecordingProviderSet : IChoreographyProviderSet
    {
        public RecordingProviderSet(RecordingProvider provider, IResourceProvider resources = null)
        {
            Animation = provider;
            Audio = provider;
            Vfx = provider;
            Resources = resources;
        }

        public IAnimationProvider Animation { get; }
        public IAudioProvider Audio { get; }
        public IVfxProvider Vfx { get; }
        public IResourceProvider Resources { get; }
    }

    /// <summary>Deterministic fake resource provider with controllable completion for preload tests.</summary>
    internal sealed class FakeResourceProvider : IResourceProvider
    {
        private sealed class FakeHandle : IChoreographyResourceHandle
        {
            public ChoreographyResourceReference Reference { get; set; }
            public bool IsDone { get; set; }
            public bool Succeeded { get; set; }
            public float Progress { get; set; }
            public string Error { get; set; }
            public int Released;
            public void Release() => Released++;
        }

        private readonly Dictionary<ChoreographyResourceReference, FakeHandle> _handles =
            new Dictionary<ChoreographyResourceReference, FakeHandle>();
        private int _loadCount;

        public int LoadCount => _loadCount;

        public IChoreographyResourceHandle Load(in ChoreographyResourceReference reference)
        {
            _loadCount++;
            if (!_handles.TryGetValue(reference, out FakeHandle handle))
            {
                handle = new FakeHandle { Reference = reference };
                _handles[reference] = handle;
            }
            return handle;
        }

        public bool TryGet(in ChoreographyResourceReference reference, out IChoreographyResourceHandle handle)
        {
            if (_handles.TryGetValue(reference, out FakeHandle found))
            {
                handle = found;
                return true;
            }
            handle = null;
            return false;
        }

        public void Release(in ChoreographyResourceReference reference)
        {
            if (_handles.TryGetValue(reference, out FakeHandle handle))
            {
                handle.Release();
            }
        }

        public void Complete(ChoreographyResourceReference reference, bool succeeded, string error = null)
        {
            if (_handles.TryGetValue(reference, out FakeHandle handle))
            {
                handle.IsDone = true;
                handle.Succeeded = succeeded;
                handle.Progress = 1f;
                handle.Error = error;
            }
        }
    }

    internal sealed class NullResourceProvider : IResourceProvider
    {
        public IChoreographyResourceHandle Load(in ChoreographyResourceReference reference)
        {
            return null;
        }

        public bool TryGet(in ChoreographyResourceReference reference, out IChoreographyResourceHandle handle)
        {
            handle = null;
            return false;
        }

        public void Release(in ChoreographyResourceReference reference)
        {
        }
    }

    /// <summary>Helpers for building Core model objects in tests.</summary>
    internal static class TestFactory
    {
        public static ChoreographyClip Clip(string id, double start, double duration, float weight = 1f, int channel = 0, bool loop = false)
        {
            return new ChoreographyClip(id, new ChoreographyResourceReference(id + ".asset", ChoreographyResourceKind.Animation), start, duration, weight, channel, loop);
        }

        public static ChoreographyTrack Track(ChoreographyTrackKind kind, params ChoreographyClip[] clips)
        {
            return new ChoreographyTrack("track", kind, clips);
        }

        public static ChoreographySection Section(
            string id,
            double duration,
            ChoreographyTrack[] tracks,
            ChoreographyEvent[] events = null,
            bool interruptible = true,
            ChoreographyPlaybackMode mode = ChoreographyPlaybackMode.Inherit,
            ChoreographyEventState[] eventStates = null,
            ChoreographySectionClock clock = default)
        {
            return new ChoreographySection(id, duration, tracks, events, interruptible, mode, eventStates, clock);
        }
    }
}
