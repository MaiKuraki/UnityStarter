using R3;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    public sealed class InputRecorder : IDisposable
    {
        private readonly List<(string ActionMapName, string ActionName)> _actionsToRecord = new();
        private readonly List<InputFrame> _recordedFrames = new();
        private CompositeDisposable _subscriptions;
        private float _recordingStartTime;
        private bool _isRecording;

        public bool IsRecording => _isRecording;

        public void RecordAction(string actionMapName, string actionName)
        {
            if (string.IsNullOrEmpty(actionMapName)) throw new ArgumentNullException(nameof(actionMapName));
            if (string.IsNullOrEmpty(actionName)) throw new ArgumentNullException(nameof(actionName));
            _actionsToRecord.Add((actionMapName, actionName));
        }

        public void StartRecording(IInputPlayer player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (_isRecording) return;

            _isRecording = true;
            _recordedFrames.Clear();
            _recordingStartTime = Time.realtimeSinceStartup;
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();

            for (int i = 0; i < _actionsToRecord.Count; i++)
            {
                var (mapName, actionName) = _actionsToRecord[i];

                player.GetButtonObservable(mapName, actionName)
                    .Subscribe(_ =>
                    {
                        _recordedFrames.Add(new InputFrame(
                            Time.realtimeSinceStartup - _recordingStartTime,
                            null, null, true));
                    })
                    .AddTo(_subscriptions);

                player.GetVector2Observable(mapName, actionName)
                    .Subscribe(v =>
                    {
                        _recordedFrames.Add(new InputFrame(
                            Time.realtimeSinceStartup - _recordingStartTime,
                            v, null, false));
                    })
                    .AddTo(_subscriptions);

                player.GetScalarObservable(mapName, actionName)
                    .Subscribe(f =>
                    {
                        _recordedFrames.Add(new InputFrame(
                            Time.realtimeSinceStartup - _recordingStartTime,
                            null, f, false));
                    })
                    .AddTo(_subscriptions);
            }
        }

        public InputRecording StopRecording()
        {
            if (!_isRecording) return null;

            _isRecording = false;
            _subscriptions?.Dispose();
            _subscriptions = null;

            var recording = _recordedFrames.Count > 0
                ? new InputRecording(new List<InputFrame>(_recordedFrames))
                : new InputRecording(new List<InputFrame>());
            _recordedFrames.Clear();
            return recording;
        }

        public void Dispose()
        {
            _isRecording = false;
            _subscriptions?.Dispose();
            _subscriptions = null;
            _recordedFrames.Clear();
        }
    }

    internal readonly struct InputFrame
    {
        internal readonly float TimeSinceStart;
        internal readonly Vector2? Vector2Value;
        internal readonly float? FloatValue;
        internal readonly bool HasUnitEvent;

        internal InputFrame(float timeSinceStart, Vector2? vector2Value, float? floatValue, bool hasUnitEvent)
        {
            TimeSinceStart = timeSinceStart;
            Vector2Value = vector2Value;
            FloatValue = floatValue;
            HasUnitEvent = hasUnitEvent;
        }
    }

    public sealed class InputRecording
    {
        private readonly List<InputFrame> _frames;

        internal InputRecording(List<InputFrame> frames)
        {
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        }

        public float Duration => _frames.Count > 0 ? _frames[_frames.Count - 1].TimeSinceStart : 0f;

        public int FrameCount => _frames.Count;

        internal List<InputFrame> Frames => _frames;

        public Observable<Unit> CreateReplayObservable()
        {
            return CreateReplayUnitObservable();
        }

        public Observable<Vector2> CreateReplayVector2Observable()
        {
            if (_frames.Count == 0) return Observable.Empty<Vector2>();

            var list = new List<Observable<Vector2>>();
            for (int i = 0; i < _frames.Count; i++)
            {
                var frame = _frames[i];
                if (frame.Vector2Value.HasValue)
                {
                    var value = frame.Vector2Value.Value;
                    list.Add(Observable.Timer(TimeSpan.FromSeconds(frame.TimeSinceStart))
                        .Select(_ => value));
                }
            }
            if (list.Count == 0) return Observable.Empty<Vector2>();
            return MergeObservables(list);
        }

        public Observable<float> CreateReplayFloatObservable()
        {
            if (_frames.Count == 0) return Observable.Empty<float>();

            var list = new List<Observable<float>>();
            for (int i = 0; i < _frames.Count; i++)
            {
                var frame = _frames[i];
                if (frame.FloatValue.HasValue)
                {
                    var value = frame.FloatValue.Value;
                    list.Add(Observable.Timer(TimeSpan.FromSeconds(frame.TimeSinceStart))
                        .Select(_ => value));
                }
            }
            if (list.Count == 0) return Observable.Empty<float>();
            return MergeObservables(list);
        }

        public Observable<Unit> CreateReplayUnitObservable()
        {
            if (_frames.Count == 0) return Observable.Empty<Unit>();

            var list = new List<Observable<Unit>>();
            for (int i = 0; i < _frames.Count; i++)
            {
                var frame = _frames[i];
                if (frame.HasUnitEvent)
                {
                    list.Add(Observable.Timer(TimeSpan.FromSeconds(frame.TimeSinceStart))
                        .Select(_ => Unit.Default));
                }
            }
            if (list.Count == 0) return Observable.Empty<Unit>();
            return MergeObservables(list);
        }

        private static Observable<T> MergeObservables<T>(List<Observable<T>> sources)
        {
            if (sources.Count == 1) return sources[0];

            var merged = sources[0];
            for (int i = 1; i < sources.Count; i++)
            {
                merged = merged.Merge(sources[i]);
            }
            return merged;
        }
    }
}
