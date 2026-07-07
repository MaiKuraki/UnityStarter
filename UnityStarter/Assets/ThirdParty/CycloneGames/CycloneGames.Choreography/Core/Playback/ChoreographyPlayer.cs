using System;
using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Deterministic, engine-free timeline driver for a single choreography instance. The player owns no
    /// providers; it advances a playhead, tracks which clips are active, and reports lifecycle transitions and
    /// events to an <see cref="IChoreographyPlaybackSink"/>. Advancement is driven by explicit
    /// <see cref="Tick"/> calls, which makes playback reproducible in CLI tests and headless simulation.
    ///
    /// Performance: after <see cref="Load"/>, the per-tick cost is O(active clips + newly started clips + crossed
    /// events). Clips are flattened once into a contiguous array sorted by absolute start time, and a monotonic
    /// cursor avoids rescanning future clips every frame. <see cref="Load"/> is the only allocating path and reuses
    /// its buffers across reloads, so pooled players stay allocation-free on the hot path.
    /// </summary>
    public sealed class ChoreographyPlayer
    {
        private struct RuntimeClip
        {
            public ChoreographyClip Clip;
            public ChoreographyTrackKind TrackKind;
            public double AbsStart;
            public double AbsEnd;
            public double Duration;
            public bool Loop;
            public bool OneShot;
        }

        private struct RuntimeEvent
        {
            public ChoreographyEvent Event;
            public double AbsTime;
        }

        private struct RuntimeEventState
        {
            public ChoreographyEventState State;
            public double AbsStart;
            public double AbsEnd;
        }

        private sealed class RuntimeClipComparer : IComparer<RuntimeClip>
        {
            public int Compare(RuntimeClip x, RuntimeClip y) => x.AbsStart.CompareTo(y.AbsStart);
        }

        private sealed class RuntimeEventComparer : IComparer<RuntimeEvent>
        {
            public int Compare(RuntimeEvent x, RuntimeEvent y) => x.AbsTime.CompareTo(y.AbsTime);
        }

        private sealed class RuntimeEventStateComparer : IComparer<RuntimeEventState>
        {
            public static readonly RuntimeEventStateComparer Instance = new RuntimeEventStateComparer();

            public int Compare(RuntimeEventState x, RuntimeEventState y) => x.AbsStart.CompareTo(y.AbsStart);
        }

        private static readonly RuntimeClipComparer ClipComparer = new RuntimeClipComparer();
        private static readonly RuntimeEventComparer EventComparer = new RuntimeEventComparer();

        private IChoreographyDiagnostics _diagnostics = NullChoreographyDiagnostics.Instance;
        private IChoreographyPlaybackSink _sink;
        private ChoreographyPlaybackContext _context;

        private RuntimeClip[] _clips = Array.Empty<RuntimeClip>();
        private int _clipCount;
        private RuntimeEvent[] _events = Array.Empty<RuntimeEvent>();
        private int _eventCount;
        private RuntimeEventState[] _eventStates = Array.Empty<RuntimeEventState>();
        private int _eventStateCount;
        private double[] _sectionStart = Array.Empty<double>();
        private int _sectionCount;

        private readonly List<int> _active = new List<int>(16);
        private readonly List<int> _activeEventStates = new List<int>(8);
        private int _startCursor;
        private int _eventCursor;
        private int _eventStateCursor;
        private int _currentSection;
        private double _time;
        private bool _hasTicked;
        private ChoreographyTimelineStep _currentStep;

        public IChoreographyAsset Asset { get; private set; }

        public PlaybackStatus Status { get; private set; } = PlaybackStatus.Idle;

        public double Time => _time;

        public double TotalDuration { get; private set; }

        public int CurrentSectionIndex => _currentSection;

        public ChoreographyClockState ClockState => BuildClockState();

        public bool CurrentSectionInterruptible
        {
            get
            {
                if (Asset == null || _currentSection < 0 || _currentSection >= Asset.Sections.Count)
                {
                    return true;
                }
                return Asset.Sections[_currentSection].Interruptible;
            }
        }

        public ChoreographySectionClock CurrentSectionClock
        {
            get
            {
                if (Asset == null || _currentSection < 0 || _currentSection >= Asset.Sections.Count)
                {
                    return ChoreographySectionClock.Default;
                }
                return Asset.Sections[_currentSection].Clock;
            }
        }

        public ChoreographyPlaybackMode CurrentSectionMode
        {
            get
            {
                if (Asset == null || _currentSection < 0 || _currentSection >= Asset.Sections.Count)
                {
                    return ChoreographyPlaybackMode.Priority;
                }
                ChoreographyPlaybackMode preferredMode = Asset.Sections[_currentSection].PreferredMode;
                return preferredMode == ChoreographyPlaybackMode.Inherit ? _context.DefaultMode : preferredMode;
            }
        }

        public void SetDiagnostics(IChoreographyDiagnostics diagnostics)
        {
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
        }

        /// <summary>
        /// Loads an asset, resets playback state, and flattens the timeline. The player does not start until
        /// <see cref="Play"/> is called. <paramref name="sink"/> receives all lifecycle callbacks.
        /// </summary>
        public void Load(IChoreographyAsset asset, in ChoreographyPlaybackContext context, IChoreographyPlaybackSink sink)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            Asset = asset;
            _context = context;
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));

            Flatten(asset);

            _time = 0d;
            _startCursor = 0;
            _eventCursor = 0;
            _eventStateCursor = 0;
            _currentSection = 0;
            _hasTicked = false;
            _currentStep = ChoreographyTimelineStep.FromDelta(0d);
            _active.Clear();
            _activeEventStates.Clear();
            Status = PlaybackStatus.Idle;
        }

        public void Play()
        {
            if (Asset == null)
            {
                _diagnostics.Log(ChoreographyLogLevel.Error, "Choreography", "Play called before Load.");
                return;
            }
            if (Status == PlaybackStatus.Playing)
            {
                return;
            }
            Status = PlaybackStatus.Playing;
        }

        public void Pause()
        {
            if (Status == PlaybackStatus.Playing)
            {
                Status = PlaybackStatus.Paused;
            }
        }

        public void Resume()
        {
            if (Status == PlaybackStatus.Paused)
            {
                Status = PlaybackStatus.Playing;
            }
        }

        /// <summary>Stops playback, reporting every active clip as interrupted (not completed).</summary>
        public void Stop()
        {
            if (Status == PlaybackStatus.Idle || Status == PlaybackStatus.Completed || Status == PlaybackStatus.Stopped)
            {
                Status = PlaybackStatus.Stopped;
                return;
            }
            StopAllActiveEventStates(true);
            StopAllActive(false);
            Status = PlaybackStatus.Stopped;
        }

        /// <summary>
        /// Advances the playhead by <paramref name="deltaTime"/> seconds (scaled by context speed), activating and
        /// completing clips and dispatching crossed events. No-op unless the player is <see cref="PlaybackStatus.Playing"/>.
        /// </summary>
        public void Tick(double deltaTime)
        {
            Tick(ChoreographyTimelineStep.FromDelta(deltaTime));
        }

        public void Tick(IChoreographyClockDriver clockDriver, in ChoreographyTimelineStep outerStep)
        {
            if (clockDriver == null)
            {
                Tick(outerStep);
                return;
            }

            if (Status != PlaybackStatus.Playing)
            {
                return;
            }

            ChoreographyClockState state = BuildClockState();
            if (clockDriver.TryEvaluate(in state, in outerStep, out ChoreographyTimelineStep resolvedStep))
            {
                Tick(resolvedStep);
            }
        }

        /// <summary>
        /// Advances or evaluates the playhead from an explicit time authority sample.
        /// Delta steps are scaled by context speed. Absolute steps directly set choreography timeline time and are
        /// intended for animation, Timeline, audio, or other external authoritative clocks.
        /// </summary>
        public void Tick(in ChoreographyTimelineStep step)
        {
            if (Status != PlaybackStatus.Playing)
            {
                return;
            }

            double previousTime = _hasTicked ? _time : -1d;
            _hasTicked = true;
            _currentStep = step;

            double targetTime = step.Mode == ChoreographyTimelineStepMode.Absolute
                ? step.TargetTime
                : _time + step.DeltaTime * _context.Speed;

            if (targetTime < _time)
            {
                StopAllActiveEventStates(true);
                StopAllActive(false);
                _startCursor = 0;
                _eventCursor = 0;
                _eventStateCursor = 0;
                _currentSection = 0;
                previousTime = -1d;
            }

            _time = targetTime;

            bool reachedEnd = _time >= TotalDuration;
            double sampleTime = reachedEnd ? TotalDuration : _time;

            UpdateCurrentSection(sampleTime);
            DispatchEvents(previousTime, sampleTime);
            int activeStateCountBeforeActivate = _activeEventStates.Count;
            ActivateEventStates(sampleTime);
            UpdateActiveEventStates(sampleTime, activeStateCountBeforeActivate);
            int activeCountBeforeActivate = _active.Count;
            ActivateClips(sampleTime);
            UpdateActiveClips(sampleTime, activeCountBeforeActivate);

            if (reachedEnd)
            {
                if (_context.Loop && TotalDuration > 0d)
                {
                    LoopReset();
                }
                else
                {
                    Complete();
                }
            }
        }

        private void Complete()
        {
            StopAllActiveEventStates(false);
            StopAllActive(true);
            Status = PlaybackStatus.Completed;
            _sink.OnPlaybackCompleted(_context.InstanceId);
        }

        private void LoopReset()
        {
            double remainder = _time - TotalDuration;
            StopAllActiveEventStates(false);
            StopAllActive(true);
            _startCursor = 0;
            _eventCursor = 0;
            _eventStateCursor = 0;
            _currentSection = 0;
            _time = remainder < 0d ? 0d : remainder;

            UpdateCurrentSection(_time);
            DispatchEvents(-1d, _time);
            int activeStateCountBeforeActivate = _activeEventStates.Count;
            ActivateEventStates(_time);
            UpdateActiveEventStates(_time, activeStateCountBeforeActivate);
            int activeCountBeforeActivate = _active.Count;
            ActivateClips(_time);
            UpdateActiveClips(_time, activeCountBeforeActivate);
        }

        private void DispatchEvents(double fromExclusive, double toInclusive)
        {
            while (_eventCursor < _eventCount && _events[_eventCursor].AbsTime <= toInclusive)
            {
                RuntimeEvent runtimeEvent = _events[_eventCursor];
                if (runtimeEvent.AbsTime > fromExclusive)
                {
                    _sink.OnEvent(new ChoreographyEventInvocation(
                        _context.InstanceId,
                        runtimeEvent.Event,
                        runtimeEvent.AbsTime,
                        toInclusive,
                        _currentStep.ClockKind,
                        _currentStep.TickIndex));
                }
                _eventCursor++;
            }
        }

        private void ActivateEventStates(double time)
        {
            while (_eventStateCursor < _eventStateCount && _eventStates[_eventStateCursor].AbsStart <= time)
            {
                int index = _eventStateCursor;
                _eventStateCursor++;

                ref RuntimeEventState runtimeState = ref _eventStates[index];
                EmitEventState(index, EventStatePhase.Begin, time, false);

                if (time >= runtimeState.AbsEnd)
                {
                    EmitEventState(index, EventStatePhase.End, time, false);
                    continue;
                }

                _activeEventStates.Add(index);
            }
        }

        private void UpdateActiveEventStates(double time, int activeCountBeforeActivate)
        {
            int updateCount = activeCountBeforeActivate < _activeEventStates.Count ? activeCountBeforeActivate : _activeEventStates.Count;
            for (int i = updateCount - 1; i >= 0; i--)
            {
                int index = _activeEventStates[i];
                ref RuntimeEventState runtimeState = ref _eventStates[index];

                if (time >= runtimeState.AbsEnd)
                {
                    _activeEventStates.RemoveAt(i);
                    EmitEventState(index, EventStatePhase.End, time, false);
                    continue;
                }

                EmitEventState(index, EventStatePhase.Update, time, false);
            }
        }

        private void StopAllActiveEventStates(bool interrupted)
        {
            for (int i = _activeEventStates.Count - 1; i >= 0; i--)
            {
                EmitEventState(_activeEventStates[i], EventStatePhase.End, _time, interrupted);
            }
            _activeEventStates.Clear();
        }

        private void ActivateClips(double time)
        {
            while (_startCursor < _clipCount && _clips[_startCursor].AbsStart <= time)
            {
                int index = _startCursor;
                _startCursor++;

                ref RuntimeClip runtimeClip = ref _clips[index];

                if (runtimeClip.OneShot)
                {
                    EmitStarted(index, time);
                    EmitStopped(index, true);
                    continue;
                }

                if (time >= runtimeClip.AbsEnd)
                {
                    // Entire span skipped within a single large delta: fire start then complete.
                    EmitStarted(index, time);
                    EmitStopped(index, true);
                    continue;
                }

                _active.Add(index);
                EmitStarted(index, time);
            }
        }

        private void UpdateActiveClips(double time, int activeCountBeforeActivate)
        {
            int updateCount = activeCountBeforeActivate < _active.Count ? activeCountBeforeActivate : _active.Count;
            for (int i = updateCount - 1; i >= 0; i--)
            {
                int index = _active[i];
                ref RuntimeClip runtimeClip = ref _clips[index];

                if (time >= runtimeClip.AbsEnd)
                {
                    _active.RemoveAt(i);
                    EmitStopped(index, true);
                    continue;
                }

                EmitUpdated(index, time);
            }
        }

        private void StopAllActive(bool completed)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                EmitStopped(_active[i], completed);
            }
            _active.Clear();
        }

        private void EmitStarted(int index, double time)
        {
            ref RuntimeClip runtimeClip = ref _clips[index];
            ComputeLocalTime(in runtimeClip, time, out double localTime, out double normalizedTime);
            _sink.OnClipStarted(new ChoreographyPlaybackSample(
                _context.InstanceId,
                runtimeClip.TrackKind,
                runtimeClip.Clip,
                time,
                localTime,
                normalizedTime,
                runtimeClip.Clip.Weight,
                _context.Channel,
                runtimeClip.Clip.Channel,
                _currentStep.ClockKind,
                _currentStep.TickIndex,
                _currentStep.SourceTime));
        }

        private void EmitUpdated(int index, double time)
        {
            ref RuntimeClip runtimeClip = ref _clips[index];
            ComputeLocalTime(in runtimeClip, time, out double localTime, out double normalizedTime);
            _sink.OnClipUpdated(new ChoreographyPlaybackSample(
                _context.InstanceId,
                runtimeClip.TrackKind,
                runtimeClip.Clip,
                time,
                localTime,
                normalizedTime,
                runtimeClip.Clip.Weight,
                _context.Channel,
                runtimeClip.Clip.Channel,
                _currentStep.ClockKind,
                _currentStep.TickIndex,
                _currentStep.SourceTime));
        }

        private void EmitStopped(int index, bool completed)
        {
            ref RuntimeClip runtimeClip = ref _clips[index];
            _sink.OnClipStopped(new ChoreographyClipStop(
                _context.InstanceId,
                runtimeClip.TrackKind,
                runtimeClip.Clip.Id,
                _context.Channel,
                completed,
                runtimeClip.Clip.Channel,
                _time,
                _currentStep.ClockKind,
                _currentStep.TickIndex));
        }

        private void EmitEventState(int index, EventStatePhase phase, double time, bool interrupted)
        {
            ref RuntimeEventState runtimeState = ref _eventStates[index];
            ComputeEventStateTime(in runtimeState, time, out double localTime, out double normalizedTime);
            ChoreographyEventStateSignal signal = new ChoreographyEventStateSignal(
                _context.InstanceId,
                runtimeState.State,
                phase,
                time,
                localTime,
                normalizedTime,
                _currentStep.ClockKind,
                _currentStep.TickIndex,
                interrupted);

            switch (phase)
            {
                case EventStatePhase.Begin:
                    _sink.OnEventStateBegin(in signal);
                    break;
                case EventStatePhase.Update:
                    _sink.OnEventStateUpdate(in signal);
                    break;
                case EventStatePhase.End:
                    _sink.OnEventStateEnd(in signal);
                    break;
            }
        }

        private static void ComputeLocalTime(in RuntimeClip runtimeClip, double time, out double localTime, out double normalizedTime)
        {
            double raw = time - runtimeClip.AbsStart;
            if (raw < 0d)
            {
                raw = 0d;
            }

            if (runtimeClip.Loop && runtimeClip.Duration > 0d)
            {
                localTime = raw % runtimeClip.Duration;
            }
            else
            {
                localTime = raw;
            }

            normalizedTime = runtimeClip.Duration > 0d ? localTime / runtimeClip.Duration : 0d;
            if (normalizedTime > 1d)
            {
                normalizedTime = 1d;
            }
        }

        private static void ComputeEventStateTime(in RuntimeEventState runtimeState, double time, out double localTime, out double normalizedTime)
        {
            double duration = runtimeState.AbsEnd - runtimeState.AbsStart;
            localTime = time - runtimeState.AbsStart;
            if (localTime < 0d)
            {
                localTime = 0d;
            }
            if (duration > 0d && localTime > duration)
            {
                localTime = duration;
            }

            normalizedTime = duration > 0d ? localTime / duration : 1d;
            if (normalizedTime > 1d)
            {
                normalizedTime = 1d;
            }
        }

        private void UpdateCurrentSection(double time)
        {
            while (_currentSection + 1 < _sectionCount && _sectionStart[_currentSection + 1] <= time)
            {
                _currentSection++;
            }
        }

        private ChoreographyClockState BuildClockState()
        {
            if (Asset == null || _sectionCount == 0 || _currentSection < 0 || _currentSection >= Asset.Sections.Count)
            {
                return new ChoreographyClockState(0d, 0d, -1, 0d, 0d, _context.Speed, ChoreographySectionClock.Default);
            }

            ChoreographySection section = Asset.Sections[_currentSection];
            return new ChoreographyClockState(
                _time,
                TotalDuration,
                _currentSection,
                _sectionStart[_currentSection],
                section.Duration,
                _context.Speed,
                section.Clock);
        }

        private void Flatten(IChoreographyAsset asset)
        {
            IReadOnlyList<ChoreographySection> sections = asset.Sections;
            _sectionCount = sections.Count;
            EnsureSectionCapacity(_sectionCount);

            int clipTotal = 0;
            int eventTotal = 0;
            int eventStateTotal = 0;
            double cursor = 0d;
            for (int s = 0; s < _sectionCount; s++)
            {
                ChoreographySection section = sections[s];
                _sectionStart[s] = cursor;
                cursor += section.Duration;

                ChoreographyTrack[] tracks = section.Tracks;
                for (int t = 0; t < tracks.Length; t++)
                {
                    clipTotal += tracks[t].Clips.Length;
                }
                eventTotal += section.Events.Length;
                eventStateTotal += section.EventStates.Length;
            }
            TotalDuration = cursor;

            EnsureClipCapacity(clipTotal);
            EnsureEventCapacity(eventTotal);
            EnsureEventStateCapacity(eventStateTotal);

            _clipCount = 0;
            _eventCount = 0;
            _eventStateCount = 0;
            for (int s = 0; s < _sectionCount; s++)
            {
                ChoreographySection section = sections[s];
                double sectionStart = _sectionStart[s];
                double sectionEnd = sectionStart + section.Duration;

                ChoreographyTrack[] tracks = section.Tracks;
                for (int t = 0; t < tracks.Length; t++)
                {
                    ChoreographyTrack track = tracks[t];
                    ChoreographyClip[] clips = track.Clips;
                    for (int c = 0; c < clips.Length; c++)
                    {
                        AddRuntimeClip(track.Kind, clips[c], sectionStart, sectionEnd);
                    }
                }

                ChoreographyEvent[] events = section.Events;
                for (int e = 0; e < events.Length; e++)
                {
                    _events[_eventCount].Event = events[e];
                    _events[_eventCount].AbsTime = sectionStart + events[e].Time;
                    _eventCount++;
                }

                ChoreographyEventState[] eventStates = section.EventStates;
                for (int e = 0; e < eventStates.Length; e++)
                {
                    AddRuntimeEventState(eventStates[e], sectionStart, sectionEnd);
                }
            }

            if (_clipCount > 1)
            {
                Array.Sort(_clips, 0, _clipCount, ClipComparer);
            }
            if (_eventCount > 1)
            {
                Array.Sort(_events, 0, _eventCount, EventComparer);
            }
            if (_eventStateCount > 1)
            {
                Array.Sort(_eventStates, 0, _eventStateCount, RuntimeEventStateComparer.Instance);
            }
        }

        private void AddRuntimeClip(ChoreographyTrackKind kind, ChoreographyClip clip, double sectionStart, double sectionEnd)
        {
            double absStart = sectionStart + clip.StartTime;
            bool oneShot = !clip.HasDuration;
            double absEnd;
            if (oneShot)
            {
                absEnd = absStart;
            }
            else if (clip.Loop)
            {
                absEnd = sectionEnd;
            }
            else
            {
                absEnd = sectionStart + clip.EndTime;
                if (absEnd > sectionEnd)
                {
                    absEnd = sectionEnd;
                    if (_diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
                    {
                        _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography",
                            "Clip '" + clip.Id + "' end exceeds its section; clamped to the section boundary.");
                    }
                }
            }

            _clips[_clipCount].Clip = clip;
            _clips[_clipCount].TrackKind = kind;
            _clips[_clipCount].AbsStart = absStart;
            _clips[_clipCount].AbsEnd = absEnd;
            _clips[_clipCount].Duration = clip.Duration;
            _clips[_clipCount].Loop = clip.Loop;
            _clips[_clipCount].OneShot = oneShot;
            _clipCount++;
        }

        private void AddRuntimeEventState(ChoreographyEventState state, double sectionStart, double sectionEnd)
        {
            double absStart = sectionStart + state.StartTime;
            double absEnd = sectionStart + state.EndTime;
            if (absEnd > sectionEnd)
            {
                absEnd = sectionEnd;
            }
            if (absEnd < absStart)
            {
                absEnd = absStart;
            }

            _eventStates[_eventStateCount].State = state;
            _eventStates[_eventStateCount].AbsStart = absStart;
            _eventStates[_eventStateCount].AbsEnd = absEnd;
            _eventStateCount++;
        }

        private void EnsureClipCapacity(int required)
        {
            if (_clips.Length < required)
            {
                _clips = new RuntimeClip[NextCapacity(_clips.Length, required)];
            }
        }

        private void EnsureEventCapacity(int required)
        {
            if (_events.Length < required)
            {
                _events = new RuntimeEvent[NextCapacity(_events.Length, required)];
            }
        }

        private void EnsureEventStateCapacity(int required)
        {
            if (_eventStates.Length < required)
            {
                _eventStates = new RuntimeEventState[NextCapacity(_eventStates.Length, required)];
            }
        }

        private void EnsureSectionCapacity(int required)
        {
            if (_sectionStart.Length < required)
            {
                _sectionStart = new double[NextCapacity(_sectionStart.Length, required)];
            }
        }

        private static int NextCapacity(int current, int required)
        {
            int capacity = current < 4 ? 4 : current;
            while (capacity < required)
            {
                capacity <<= 1;
            }
            return capacity;
        }
    }
}
