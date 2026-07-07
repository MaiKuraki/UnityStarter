using System;
using System.Collections.Generic;
using CycloneGames.Choreography.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Choreography.Editor
{
    internal enum ChoreographyPreviewDriverMode
    {
        Seconds = 0,
        Frames = 1
    }

    internal readonly struct ChoreographyPreviewSample
    {
        public readonly double Time;
        public readonly double Duration;
        public readonly ChoreographyPreviewDriverMode DriverMode;
        public readonly double FrameRate;
        public readonly long FrameIndex;

        public ChoreographyPreviewSample(
            double time,
            double duration,
            ChoreographyPreviewDriverMode driverMode,
            double frameRate)
        {
            Time = time;
            Duration = duration;
            DriverMode = driverMode;
            FrameRate = frameRate > 0d ? frameRate : 60d;
            FrameIndex = (long)Math.Floor(Time * FrameRate + 0.000001d);
        }
    }

    internal interface IChoreographyPreviewTarget : IDisposable
    {
        string DisplayName { get; }

        bool IsValid { get; }

        bool IsExternallyDriven { get; }

        double CurrentTime { get; }

        double Duration { get; }

        void Bind(ChoreographyAsset asset, Object context);

        void SetPreviewTime(in ChoreographyPreviewSample sample);

        void Evaluate(in ChoreographyPreviewSample sample);
    }

    internal interface IChoreographyPreviewTargetFactory
    {
        string Id { get; }

        string DisplayName { get; }

        Type TargetObjectType { get; }

        bool CanCreate(ChoreographyAsset asset, Object context);

        IChoreographyPreviewTarget Create();
    }

    internal static class ChoreographyPreviewRegistry
    {
        private static readonly List<IChoreographyPreviewTargetFactory> Factories = new List<IChoreographyPreviewTargetFactory>(8);

        public static int FactoryCount => Factories.Count;

        public static void Register(IChoreographyPreviewTargetFactory factory)
        {
            if (factory == null || string.IsNullOrEmpty(factory.Id))
            {
                return;
            }

            for (int i = 0; i < Factories.Count; i++)
            {
                if (Factories[i].Id == factory.Id)
                {
                    Factories[i] = factory;
                    return;
                }
            }

            Factories.Add(factory);
        }

        public static void CollectFactories(ChoreographyAsset asset, Object context, List<IChoreographyPreviewTargetFactory> results)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();
            for (int i = 0; i < Factories.Count; i++)
            {
                IChoreographyPreviewTargetFactory factory = Factories[i];
                if (factory.CanCreate(asset, context))
                {
                    results.Add(factory);
                }
            }
        }
    }

    internal sealed class ChoreographyPreviewSession : IDisposable
    {
        private IChoreographyPreviewTarget _target;
        private readonly SectionClockDriver _sectionClockDriver = new SectionClockDriver();
        private readonly FixedFrameClockDriver _fixedFrameClockDriver = new FixedFrameClockDriver();
        private ChoreographyAsset _asset;
        private Object _context;
        private string _factoryId;
        private double _continuousTime;
        private double _lastEditorTime;
        private double _lastEvaluatedTime;
        private double _lastEvaluatedDuration;
        private long _lastEvaluatedFrame;
        private ChoreographyPreviewDriverMode _lastEvaluatedDriverMode;
        private int _lastClockSectionIndex = int.MinValue;
        private bool _hasEvaluatedSample;

        public ChoreographyPreviewDriverMode DriverMode { get; set; }

        public double FrameRate { get; set; } = 60d;

        public float Speed { get; set; } = 1f;

        public bool IsPlaying { get; private set; }

        public double CurrentTime { get; private set; }

        public string TargetName => _target != null && _target.IsValid ? _target.DisplayName : "No Target";

        public void Bind(ChoreographyAsset asset, IChoreographyPreviewTargetFactory factory, Object context)
        {
            string nextFactoryId = factory != null ? factory.Id : string.Empty;
            if (_asset == asset && _context == context && _factoryId == nextFactoryId && _target != null)
            {
                return;
            }

            DisposeTarget();

            _asset = asset;
            _context = context;
            _factoryId = nextFactoryId;
            _hasEvaluatedSample = false;

            if (asset == null || factory == null)
            {
                CurrentTime = 0d;
                _continuousTime = 0d;
                IsPlaying = false;
                return;
            }

            _target = factory.Create();
            if (_target != null)
            {
                _target.Bind(asset, context);
                ResetClockDrivers();
                SetTime(CurrentTime);
            }
        }

        public void Play(double editorTime)
        {
            if (_asset == null || _target == null || !_target.IsValid)
            {
                return;
            }

            IsPlaying = true;
            _lastEditorTime = editorTime;
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        public void Stop()
        {
            IsPlaying = false;
            SetTime(0d);
        }

        public void Step(int direction)
        {
            double frameDuration = 1d / Math.Max(1d, FrameRate);
            SetTime(CurrentTime + frameDuration * direction);
        }

        public bool Tick(double editorTime)
        {
            if (!IsPlaying)
            {
                return false;
            }

            double delta = editorTime - _lastEditorTime;
            _lastEditorTime = editorTime;
            if (delta <= 0d)
            {
                return false;
            }

            double duration = GetDuration();
            if (duration <= 0d)
            {
                Stop();
                return true;
            }

            ChoreographyClockState state = BuildClockState();
            ResetClockDriverIfSectionChanged(in state);
            ChoreographyTimelineStep outerStep = ChoreographyTimelineStep.FromDelta(
                delta,
                DriverMode == ChoreographyPreviewDriverMode.Frames ? ChoreographyClockKind.FixedTick : ChoreographyClockKind.ManualDelta,
                ChoreographyTimelineStep.UnspecifiedTickIndex,
                FrameRate,
                editorTime);

            IChoreographyClockDriver driver = DriverMode == ChoreographyPreviewDriverMode.Frames
                ? _fixedFrameClockDriver
                : _sectionClockDriver;

            if (!driver.TryEvaluate(in state, in outerStep, out ChoreographyTimelineStep resolvedStep))
            {
                return false;
            }

            double nextTime = resolvedStep.Mode == ChoreographyTimelineStepMode.Absolute
                ? resolvedStep.TargetTime
                : CurrentTime + resolvedStep.DeltaTime * Speed;

            if (nextTime >= duration)
            {
                nextTime = duration;
                IsPlaying = false;
            }

            return SetTime(nextTime);
        }

        public bool SetTime(double time)
        {
            double duration = GetDuration();
            if (duration <= 0d)
            {
                CurrentTime = 0d;
                _continuousTime = 0d;
            }
            else
            {
                CurrentTime = Math.Max(0d, Math.Min(duration, time));
                _continuousTime = CurrentTime;
            }

            if (_target == null || !_target.IsValid)
            {
                return false;
            }

            ChoreographyPreviewSample sample = new ChoreographyPreviewSample(CurrentTime, duration, DriverMode, FrameRate);
            if (!ShouldEvaluate(in sample))
            {
                return false;
            }

            _target.SetPreviewTime(in sample);
            _target.Evaluate(in sample);
            _lastEvaluatedTime = sample.Time;
            _lastEvaluatedDuration = sample.Duration;
            _lastEvaluatedFrame = sample.FrameIndex;
            _lastEvaluatedDriverMode = sample.DriverMode;
            _hasEvaluatedSample = true;
            return true;
        }

        public int GetCurrentFrame()
        {
            return (int)Math.Floor(CurrentTime * Math.Max(1d, FrameRate) + 0.000001d);
        }

        public int GetMaxFrame()
        {
            return (int)Math.Ceiling(GetDuration() * Math.Max(1d, FrameRate));
        }

        public double GetDuration()
        {
            if (_asset == null)
            {
                return 0d;
            }

            return Math.Max(0d, _asset.TotalDuration);
        }

        public void Dispose()
        {
            DisposeTarget();
        }

        private ChoreographyClockState BuildClockState()
        {
            double totalDuration = GetDuration();
            IChoreographyAsset runtimeAsset = _asset;
            if (runtimeAsset == null || runtimeAsset.Sections.Count == 0)
            {
                ChoreographySectionClock defaultClock = DriverMode == ChoreographyPreviewDriverMode.Frames
                    ? new ChoreographySectionClock(ChoreographySectionClockSource.FixedFrame, ChoreographyExternalClockEndPolicy.ContinueInternal, FrameRate)
                    : ChoreographySectionClock.Default;
                return new ChoreographyClockState(CurrentTime, totalDuration, -1, 0d, totalDuration, Speed, defaultClock);
            }

            double cursor = 0d;
            int sectionIndex = runtimeAsset.Sections.Count - 1;
            ChoreographySection section = runtimeAsset.Sections[sectionIndex];
            for (int i = 0; i < runtimeAsset.Sections.Count; i++)
            {
                ChoreographySection candidate = runtimeAsset.Sections[i];
                double end = cursor + candidate.Duration;
                if (CurrentTime < end || i == runtimeAsset.Sections.Count - 1)
                {
                    sectionIndex = i;
                    section = candidate;
                    break;
                }
                cursor = end;
            }

            ChoreographySectionClock clock = DriverMode == ChoreographyPreviewDriverMode.Frames
                ? new ChoreographySectionClock(ChoreographySectionClockSource.FixedFrame, ChoreographyExternalClockEndPolicy.ContinueInternal, FrameRate)
                : section.Clock;

            return new ChoreographyClockState(CurrentTime, totalDuration, sectionIndex, cursor, section.Duration, Speed, clock);
        }

        private void ResetClockDrivers()
        {
            ChoreographyClockState state = BuildClockState();
            _sectionClockDriver.Reset(in state);
            _fixedFrameClockDriver.Reset(in state);
            _lastClockSectionIndex = state.SectionIndex;
        }

        private void ResetClockDriverIfSectionChanged(in ChoreographyClockState state)
        {
            if (_lastClockSectionIndex == state.SectionIndex)
            {
                return;
            }

            _sectionClockDriver.Reset(in state);
            _fixedFrameClockDriver.Reset(in state);
            _lastClockSectionIndex = state.SectionIndex;
        }

        private void DisposeTarget()
        {
            if (_target != null)
            {
                _target.Dispose();
                _target = null;
            }

            _hasEvaluatedSample = false;
        }

        private bool ShouldEvaluate(in ChoreographyPreviewSample sample)
        {
            if (!_hasEvaluatedSample
                || _lastEvaluatedDriverMode != sample.DriverMode
                || Math.Abs(_lastEvaluatedDuration - sample.Duration) > 0.000001d)
            {
                return true;
            }

            if (sample.DriverMode == ChoreographyPreviewDriverMode.Frames)
            {
                return _lastEvaluatedFrame != sample.FrameIndex;
            }

            return Math.Abs(_lastEvaluatedTime - sample.Time) > 0.000001d;
        }
    }

    [InitializeOnLoad]
    internal static class ChoreographyDefaultPreviewRegistration
    {
        static ChoreographyDefaultPreviewRegistration()
        {
            ChoreographyPreviewRegistry.Register(new StandaloneChoreographyPreviewTargetFactory());
        }
    }

    internal sealed class StandaloneChoreographyPreviewTargetFactory : IChoreographyPreviewTargetFactory
    {
        public string Id => "cyclonegames.choreography.preview.standalone";

        public string DisplayName => "Standalone";

        public Type TargetObjectType => typeof(ChoreographyAsset);

        public bool CanCreate(ChoreographyAsset asset, Object context)
        {
            return asset != null;
        }

        public IChoreographyPreviewTarget Create()
        {
            return new StandaloneChoreographyPreviewTarget();
        }
    }

    internal sealed class StandaloneChoreographyPreviewTarget : IChoreographyPreviewTarget
    {
        private ChoreographyAsset _asset;
        private double _currentTime;
        private bool _valid;

        public string DisplayName => "Standalone";

        public bool IsValid => _valid && _asset != null;

        public bool IsExternallyDriven => false;

        public double CurrentTime => _currentTime;

        public double Duration => _asset != null ? _asset.TotalDuration : 0d;

        public void Bind(ChoreographyAsset asset, Object context)
        {
            _asset = asset;
            _currentTime = 0d;
            _valid = asset != null;
        }

        public void SetPreviewTime(in ChoreographyPreviewSample sample)
        {
            _currentTime = sample.Time;
        }

        public void Evaluate(in ChoreographyPreviewSample sample)
        {
        }

        public void Dispose()
        {
            _valid = false;
            _asset = null;
        }
    }
}
