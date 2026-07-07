using System;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Describes which authority a section prefers for its timeline samples.
    /// The value is authoring metadata; concrete availability is decided by the active clock driver.
    /// </summary>
    public enum ChoreographySectionClockSource : byte
    {
        Inherit = 0,
        InternalTimeline = 1,
        FixedFrame = 2,
        Audio = 3,
        Animation = 4,
        Timeline = 5,
        External = 6
    }

    /// <summary>
    /// Defines what happens when a section asks for an external clock but that authority has no active sample.
    /// </summary>
    public enum ChoreographyExternalClockEndPolicy : byte
    {
        ContinueInternal = 0,
        Hold = 1,
        CompleteSection = 2,
        CompleteTimeline = 3
    }

    /// <summary>
    /// Immutable timing policy for one authored section.
    /// </summary>
    public readonly struct ChoreographySectionClock
    {
        public readonly ChoreographySectionClockSource Source;
        public readonly ChoreographyExternalClockEndPolicy ExternalEndPolicy;
        public readonly double FrameRate;

        public ChoreographySectionClock(
            ChoreographySectionClockSource source,
            ChoreographyExternalClockEndPolicy externalEndPolicy = ChoreographyExternalClockEndPolicy.ContinueInternal,
            double frameRate = 60d)
        {
            Source = source;
            ExternalEndPolicy = externalEndPolicy;
            FrameRate = frameRate > 0d && !double.IsNaN(frameRate) && !double.IsInfinity(frameRate) ? frameRate : 60d;
        }

        public static ChoreographySectionClock Default => new ChoreographySectionClock(
            ChoreographySectionClockSource.Inherit,
            ChoreographyExternalClockEndPolicy.ContinueInternal,
            60d);
    }

    /// <summary>
    /// Allocation-free state snapshot passed to clock drivers before a player is advanced.
    /// </summary>
    public readonly struct ChoreographyClockState
    {
        public readonly double TimelineTime;
        public readonly double TotalDuration;
        public readonly int SectionIndex;
        public readonly double SectionStart;
        public readonly double SectionDuration;
        public readonly double SectionLocalTime;
        public readonly double Speed;
        public readonly ChoreographySectionClock SectionClock;

        public ChoreographyClockState(
            double timelineTime,
            double totalDuration,
            int sectionIndex,
            double sectionStart,
            double sectionDuration,
            double speed,
            in ChoreographySectionClock sectionClock)
        {
            TimelineTime = SanitizeNonNegative(timelineTime);
            TotalDuration = SanitizeNonNegative(totalDuration);
            SectionIndex = sectionIndex;
            SectionStart = SanitizeNonNegative(sectionStart);
            SectionDuration = SanitizeNonNegative(sectionDuration);
            Speed = speed > 0d && !double.IsNaN(speed) && !double.IsInfinity(speed) ? speed : 1d;
            SectionClock = sectionClock;

            double local = TimelineTime - SectionStart;
            if (local < 0d)
            {
                local = 0d;
            }
            if (SectionDuration > 0d && local > SectionDuration)
            {
                local = SectionDuration;
            }
            SectionLocalTime = local;
        }

        private static double SanitizeNonNegative(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                return 0d;
            }

            return value;
        }
    }

    /// <summary>
    /// External local-time sample provided by audio, animation, Timeline, Playables, or custom integrations.
    /// LocalTime is section-local seconds, not global choreography time.
    /// </summary>
    public readonly struct ChoreographyExternalClockSample
    {
        public readonly bool HasTime;
        public readonly bool Completed;
        public readonly double LocalTime;
        public readonly double SourceTime;
        public readonly long TickIndex;
        public readonly double TickRate;

        public ChoreographyExternalClockSample(
            bool hasTime,
            double localTime,
            bool completed = false,
            double sourceTime = 0d,
            long tickIndex = ChoreographyTimelineStep.UnspecifiedTickIndex,
            double tickRate = 0d)
        {
            HasTime = hasTime;
            Completed = completed;
            LocalTime = SanitizeNonNegative(localTime);
            SourceTime = double.IsNaN(sourceTime) || double.IsInfinity(sourceTime) ? 0d : sourceTime;
            TickIndex = tickIndex;
            TickRate = tickRate > 0d && !double.IsNaN(tickRate) && !double.IsInfinity(tickRate) ? tickRate : 0d;
        }

        public static ChoreographyExternalClockSample Unavailable => default;

        public static ChoreographyExternalClockSample FromLocalTime(
            double localTime,
            bool completed = false,
            double sourceTime = 0d,
            long tickIndex = ChoreographyTimelineStep.UnspecifiedTickIndex,
            double tickRate = 0d)
        {
            return new ChoreographyExternalClockSample(true, localTime, completed, sourceTime, tickIndex, tickRate);
        }

        private static double SanitizeNonNegative(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                return 0d;
            }

            return value;
        }
    }

    /// <summary>
    /// Source adapter used by <see cref="ExternalSectionClockDriver"/>. Implementations must be non-blocking.
    /// </summary>
    public interface IChoreographyExternalClockSource
    {
        bool TryGetSample(in ChoreographyClockState state, out ChoreographyExternalClockSample sample);
    }

    /// <summary>
    /// Runtime/editor clock driver that converts an outer update step into the next player timeline step.
    /// Drivers own time authority, while preview/render providers only consume samples.
    /// </summary>
    public interface IChoreographyClockDriver
    {
        ChoreographyClockKind ClockKind { get; }

        void Reset(in ChoreographyClockState state);

        bool TryEvaluate(in ChoreographyClockState state, in ChoreographyTimelineStep outerStep, out ChoreographyTimelineStep resolvedStep);
    }

    /// <summary>
    /// Default clock: pass the outer scheduler/player step through unchanged.
    /// </summary>
    public sealed class InternalTimelineClockDriver : IChoreographyClockDriver
    {
        public static readonly InternalTimelineClockDriver Instance = new InternalTimelineClockDriver();

        private InternalTimelineClockDriver()
        {
        }

        public ChoreographyClockKind ClockKind => ChoreographyClockKind.ManualDelta;

        public void Reset(in ChoreographyClockState state)
        {
        }

        public bool TryEvaluate(in ChoreographyClockState state, in ChoreographyTimelineStep outerStep, out ChoreographyTimelineStep resolvedStep)
        {
            resolvedStep = outerStep;
            return true;
        }
    }

    /// <summary>
    /// Quantizes timeline progression to a fixed frame rate while still accepting variable editor/runtime deltas.
    /// </summary>
    public sealed class FixedFrameClockDriver : IChoreographyClockDriver
    {
        private const double TIME_EPSILON = 0.000000001d;

        private readonly double _fallbackFrameRate;
        private double _accumulatedTime;
        private double _lastResolvedTime;
        private long _lastFrameIndex = long.MinValue;

        public FixedFrameClockDriver(double fallbackFrameRate = 60d)
        {
            _fallbackFrameRate = fallbackFrameRate > 0d && !double.IsNaN(fallbackFrameRate) && !double.IsInfinity(fallbackFrameRate)
                ? fallbackFrameRate
                : 60d;
        }

        public ChoreographyClockKind ClockKind => ChoreographyClockKind.FixedTick;

        public void Reset(in ChoreographyClockState state)
        {
            _accumulatedTime = state.TimelineTime;
            _lastResolvedTime = state.TimelineTime;
            _lastFrameIndex = long.MinValue;
        }

        public bool TryEvaluate(in ChoreographyClockState state, in ChoreographyTimelineStep outerStep, out ChoreographyTimelineStep resolvedStep)
        {
            double frameRate = state.SectionClock.FrameRate > 0d ? state.SectionClock.FrameRate : _fallbackFrameRate;
            if (outerStep.Mode == ChoreographyTimelineStepMode.Absolute)
            {
                _accumulatedTime = outerStep.TargetTime;
            }
            else
            {
                if (Math.Abs(state.TimelineTime - _lastResolvedTime) > TIME_EPSILON)
                {
                    _accumulatedTime = state.TimelineTime;
                    _lastFrameIndex = long.MinValue;
                }

                _accumulatedTime += outerStep.DeltaTime * state.Speed;
            }

            if (_accumulatedTime > state.TotalDuration)
            {
                _accumulatedTime = state.TotalDuration;
            }

            if (_accumulatedTime < 0d)
            {
                _accumulatedTime = 0d;
            }

            long frameIndex = (long)Math.Floor(_accumulatedTime * frameRate + 0.000001d);
            if (frameIndex == _lastFrameIndex && _accumulatedTime < state.TotalDuration)
            {
                resolvedStep = default;
                return false;
            }

            _lastFrameIndex = frameIndex;
            double snappedTime = frameIndex / frameRate;
            if (snappedTime > state.TotalDuration)
            {
                snappedTime = state.TotalDuration;
            }

            _lastResolvedTime = snappedTime;
            resolvedStep = ChoreographyTimelineStep.FromAbsolute(
                snappedTime,
                ChoreographyClockKind.FixedTick,
                frameIndex,
                frameRate,
                outerStep.SourceTime);
            return true;
        }
    }

    /// <summary>
    /// Default per-instance driver that honors section-local internal and fixed-frame policies.
    /// External sections fall back to their configured end policy without requiring an external source.
    /// </summary>
    public sealed class SectionClockDriver : IChoreographyClockDriver
    {
        private readonly FixedFrameClockDriver _fixedFrameDriver;

        public SectionClockDriver(double fallbackFrameRate = 60d)
        {
            _fixedFrameDriver = new FixedFrameClockDriver(fallbackFrameRate);
        }

        public ChoreographyClockKind ClockKind => ChoreographyClockKind.ManualDelta;

        public void Reset(in ChoreographyClockState state)
        {
            _fixedFrameDriver.Reset(in state);
        }

        public bool TryEvaluate(in ChoreographyClockState state, in ChoreographyTimelineStep outerStep, out ChoreographyTimelineStep resolvedStep)
        {
            if (state.SectionClock.Source == ChoreographySectionClockSource.FixedFrame)
            {
                return _fixedFrameDriver.TryEvaluate(in state, in outerStep, out resolvedStep);
            }

            if (IsExternalSection(state.SectionClock.Source))
            {
                return ResolveExternalEndPolicy(in state, in outerStep, out resolvedStep);
            }

            resolvedStep = outerStep;
            return true;
        }

        private static bool ResolveExternalEndPolicy(
            in ChoreographyClockState state,
            in ChoreographyTimelineStep outerStep,
            out ChoreographyTimelineStep resolvedStep)
        {
            switch (state.SectionClock.ExternalEndPolicy)
            {
                case ChoreographyExternalClockEndPolicy.Hold:
                    resolvedStep = default;
                    return false;

                case ChoreographyExternalClockEndPolicy.CompleteSection:
                    resolvedStep = ChoreographyTimelineStep.FromAbsolute(
                        state.SectionStart + state.SectionDuration,
                        ChoreographyClockKind.ExternalAbsolute,
                        outerStep.TickIndex,
                        outerStep.TickRate,
                        outerStep.SourceTime);
                    return true;

                case ChoreographyExternalClockEndPolicy.CompleteTimeline:
                    resolvedStep = ChoreographyTimelineStep.FromAbsolute(
                        state.TotalDuration,
                        ChoreographyClockKind.ExternalAbsolute,
                        outerStep.TickIndex,
                        outerStep.TickRate,
                        outerStep.SourceTime);
                    return true;

                default:
                    resolvedStep = outerStep;
                    return true;
            }
        }

        private static bool IsExternalSection(ChoreographySectionClockSource source)
        {
            return source == ChoreographySectionClockSource.Audio
                || source == ChoreographySectionClockSource.Animation
                || source == ChoreographySectionClockSource.Timeline
                || source == ChoreographySectionClockSource.External;
        }
    }

    /// <summary>
    /// Section-aware driver for audio, animation, Timeline, or custom sources. External samples drive only sections
    /// that request an external source; all other sections continue with internal delta time.
    /// </summary>
    public sealed class ExternalSectionClockDriver : IChoreographyClockDriver
    {
        private readonly IChoreographyExternalClockSource _source;
        private readonly ChoreographyClockKind _clockKind;
        private readonly FixedFrameClockDriver _fixedFrameDriver;

        public ExternalSectionClockDriver(IChoreographyExternalClockSource source, ChoreographyClockKind clockKind, double fallbackFrameRate = 60d)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _clockKind = clockKind;
            _fixedFrameDriver = new FixedFrameClockDriver(fallbackFrameRate);
        }

        public ChoreographyClockKind ClockKind => _clockKind;

        public void Reset(in ChoreographyClockState state)
        {
            _fixedFrameDriver.Reset(in state);
        }

        public bool TryEvaluate(in ChoreographyClockState state, in ChoreographyTimelineStep outerStep, out ChoreographyTimelineStep resolvedStep)
        {
            if (state.SectionClock.Source == ChoreographySectionClockSource.FixedFrame)
            {
                return _fixedFrameDriver.TryEvaluate(in state, in outerStep, out resolvedStep);
            }

            if (!IsExternalSection(state.SectionClock.Source))
            {
                resolvedStep = ResolveInternalStep(in state, in outerStep);
                return true;
            }

            if (_source.TryGetSample(in state, out ChoreographyExternalClockSample sample) && sample.HasTime)
            {
                if (sample.Completed && sample.LocalTime < state.SectionDuration)
                {
                    return ResolveExternalEndPolicy(in state, in outerStep, out resolvedStep);
                }

                double localTime = sample.LocalTime;
                if (localTime > state.SectionDuration)
                {
                    localTime = state.SectionDuration;
                }

                resolvedStep = ChoreographyTimelineStep.FromAbsolute(
                    state.SectionStart + localTime,
                    _clockKind,
                    sample.TickIndex,
                    sample.TickRate,
                    sample.SourceTime);
                return true;
            }

            return ResolveExternalEndPolicy(in state, in outerStep, out resolvedStep);
        }

        private bool ResolveExternalEndPolicy(
            in ChoreographyClockState state,
            in ChoreographyTimelineStep outerStep,
            out ChoreographyTimelineStep resolvedStep)
        {
            switch (state.SectionClock.ExternalEndPolicy)
            {
                case ChoreographyExternalClockEndPolicy.Hold:
                    resolvedStep = default;
                    return false;

                case ChoreographyExternalClockEndPolicy.CompleteSection:
                    resolvedStep = ChoreographyTimelineStep.FromAbsolute(
                        state.SectionStart + state.SectionDuration,
                        _clockKind,
                        outerStep.TickIndex,
                        outerStep.TickRate,
                        outerStep.SourceTime);
                    return true;

                case ChoreographyExternalClockEndPolicy.CompleteTimeline:
                    resolvedStep = ChoreographyTimelineStep.FromAbsolute(
                        state.TotalDuration,
                        _clockKind,
                        outerStep.TickIndex,
                        outerStep.TickRate,
                        outerStep.SourceTime);
                    return true;

                default:
                    resolvedStep = ResolveInternalStep(in state, in outerStep);
                    return true;
            }
        }

        private static ChoreographyTimelineStep ResolveInternalStep(
            in ChoreographyClockState state,
            in ChoreographyTimelineStep outerStep)
        {
            if (outerStep.Mode == ChoreographyTimelineStepMode.Absolute)
            {
                return outerStep;
            }

            return ChoreographyTimelineStep.FromAbsolute(
                state.TimelineTime + outerStep.DeltaTime * state.Speed,
                ChoreographyClockKind.ManualDelta,
                outerStep.TickIndex,
                outerStep.TickRate,
                outerStep.SourceTime);
        }

        private static bool IsExternalSection(ChoreographySectionClockSource source)
        {
            return source == ChoreographySectionClockSource.Audio
                || source == ChoreographySectionClockSource.Animation
                || source == ChoreographySectionClockSource.Timeline
                || source == ChoreographySectionClockSource.External;
        }
    }
}
