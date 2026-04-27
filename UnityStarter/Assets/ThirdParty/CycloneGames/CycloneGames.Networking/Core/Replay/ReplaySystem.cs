using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Replay
{
    /// <summary>
    /// Records and replays network game state for spectator mode and replay systems.
    /// 
    /// Two modes:
    /// 1. Spectator: real-time observation with configurable delay
    /// 2. Replay: post-game playback with seek/pause/speed control
    /// 
    /// Records frames as (tick, serialized state delta). Full snapshots at intervals
    /// serve as keyframes for seeking.
    /// </summary>
    public sealed class ReplayRecorder
    {
        private readonly List<ReplayFrame> _frames = new List<ReplayFrame>(4096);
        private readonly List<int> _keyframeIndices = new List<int>(128);
        private readonly int _keyframeInterval;
        private readonly int _maxFrameCount;
        private int _lastKeyframeTick;
        private bool _isRecording;

        public int FrameCount => _frames.Count;
        public bool IsRecording => _isRecording;
        public int StartTick { get; private set; }
        public int EndTick { get; private set; }

        /// <param name="keyframeInterval">Ticks between full state snapshots (default 300 = 10s at 30Hz)</param>
        /// <param name="maxFrameCount">Maximum frames to keep in memory (0 = unlimited, default 108000 = 1hr at 30Hz)</param>
        public ReplayRecorder(int keyframeInterval = 300, int maxFrameCount = 108000)
        {
            _keyframeInterval = keyframeInterval;
            _maxFrameCount = maxFrameCount;
        }

        public void StartRecording(int startTick)
        {
            _frames.Clear();
            _keyframeIndices.Clear();
            _isRecording = true;
            StartTick = startTick;
            EndTick = startTick;
            _lastKeyframeTick = int.MinValue;
        }

        public void StopRecording()
        {
            _isRecording = false;
        }

        /// <summary>
        /// Record a frame. Pass full state data for keyframes, delta data otherwise.
        /// The recorder auto-flags keyframes based on interval.
        /// </summary>
        public void RecordFrame(int tick, byte[] data, int dataLength)
        {
            if (!_isRecording) return;

            bool isKeyframe = (tick - _lastKeyframeTick) >= _keyframeInterval;
            if (isKeyframe) _lastKeyframeTick = tick;

            // Copy data to avoid external mutation
            var copy = new byte[dataLength];
            Buffer.BlockCopy(data, 0, copy, 0, dataLength);

            if (isKeyframe)
                _keyframeIndices.Add(_frames.Count);

            _frames.Add(new ReplayFrame
            {
                Tick = tick,
                IsKeyframe = isKeyframe,
                Data = copy
            });

            EndTick = tick;

            // Evict oldest frames if exceeding max capacity
            if (_maxFrameCount > 0 && _frames.Count > _maxFrameCount)
            {
                EvictOldFrames(_frames.Count - _maxFrameCount);
            }
        }

        /// <summary>
        /// Record a keyframe explicitly (forced full snapshot).
        /// </summary>
        public void RecordKeyframe(int tick, byte[] data, int dataLength)
        {
            if (!_isRecording) return;

            var copy = new byte[dataLength];
            Buffer.BlockCopy(data, 0, copy, 0, dataLength);

            _keyframeIndices.Add(_frames.Count);

            _frames.Add(new ReplayFrame
            {
                Tick = tick,
                IsKeyframe = true,
                Data = copy
            });

            _lastKeyframeTick = tick;
            EndTick = tick;

            if (_maxFrameCount > 0 && _frames.Count > _maxFrameCount)
            {
                EvictOldFrames(_frames.Count - _maxFrameCount);
            }
        }

        /// <summary>
        /// Get all frames between two ticks (inclusive).
        /// </summary>
        public int GetFrames(int fromTick, int toTick, List<ReplayFrame> output)
        {
            output.Clear();
            int count = 0;

            for (int i = 0; i < _frames.Count; i++)
            {
                if (_frames[i].Tick >= fromTick && _frames[i].Tick <= toTick)
                {
                    output.Add(_frames[i]);
                    count++;
                }
                else if (_frames[i].Tick > toTick)
                    break;
            }
            return count;
        }

        /// <summary>
        /// Find the nearest keyframe at or before the given tick (for seeking).
        /// Uses binary search on the keyframe index for O(log n) performance.
        /// Returns -1 if not found.
        /// </summary>
        public int FindNearestKeyframe(int tick)
        {
            if (_keyframeIndices.Count == 0) return -1;

            // Binary search for the last keyframe with Tick <= target tick
            int lo = 0, hi = _keyframeIndices.Count - 1;
            int best = -1;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                int frameIdx = _keyframeIndices[mid];
                if (_frames[frameIdx].Tick <= tick)
                {
                    best = frameIdx;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return best;
        }

        /// <summary>
        /// Export all frames for serialization to disk.
        /// </summary>
        public IReadOnlyList<ReplayFrame> GetAllFrames() => _frames;

        /// <summary>
        /// Import frames (from disk file).
        /// </summary>
        public void ImportFrames(IList<ReplayFrame> frames)
        {
            _frames.Clear();
            _keyframeIndices.Clear();
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].IsKeyframe)
                    _keyframeIndices.Add(_frames.Count);
                _frames.Add(frames[i]);
            }

            if (_frames.Count > 0)
            {
                StartTick = _frames[0].Tick;
                EndTick = _frames[_frames.Count - 1].Tick;
            }
        }

        private void EvictOldFrames(int count)
        {
            _frames.RemoveRange(0, count);

            // Rebuild keyframe index
            _keyframeIndices.Clear();
            for (int i = 0; i < _frames.Count; i++)
            {
                if (_frames[i].IsKeyframe)
                    _keyframeIndices.Add(i);
            }

            if (_frames.Count > 0)
                StartTick = _frames[0].Tick;
        }
    }

    public struct ReplayFrame
    {
        public int Tick;
        public bool IsKeyframe;
        public byte[] Data;
    }

    /// <summary>
    /// Playback controller for replays with seek, pause, and speed control.
    /// </summary>
    public sealed class ReplayPlayer
    {
        private readonly ReplayRecorder _recorder;
        private readonly List<ReplayFrame> _tempFrames = new List<ReplayFrame>(16);

        private int _currentTick;
        private float _playbackSpeed = 1f;
        private float _accumulator;
        private bool _isPlaying;
        private bool _isPaused;
        private float _tickInterval;

        public int CurrentTick => _currentTick;
        public float PlaybackSpeed => _playbackSpeed;
        public bool IsPlaying => _isPlaying;
        public bool IsPaused => _isPaused;
        public float Progress => _recorder.FrameCount == 0 ? 0 :
            (float)(_currentTick - _recorder.StartTick) / Math.Max(1, _recorder.EndTick - _recorder.StartTick);

        public event Action<ReplayFrame> OnFramePlayback;
        public event Action OnPlaybackFinished;

        public ReplayPlayer(ReplayRecorder recorder, int tickRate = 30)
        {
            _recorder = recorder;
            _tickInterval = 1f / tickRate;
        }

        public void Play()
        {
            _isPlaying = true;
            _isPaused = false;
            _currentTick = _recorder.StartTick;
            _accumulator = 0;
        }

        public void Pause() => _isPaused = true;
        public void Resume() => _isPaused = false;
        public void Stop() { _isPlaying = false; _isPaused = false; }

        public void SetSpeed(float speed) => _playbackSpeed = Math.Max(0.1f, Math.Min(speed, 8f));

        /// <summary>
        /// Seek to a specific tick. Finds nearest keyframe and replays delta frames forward.
        /// </summary>
        public void SeekTo(int tick)
        {
            int keyIdx = _recorder.FindNearestKeyframe(tick);
            if (keyIdx < 0) return;

            var allFrames = _recorder.GetAllFrames();
            var keyframe = allFrames[keyIdx];

            // Apply keyframe
            OnFramePlayback?.Invoke(keyframe);

            // Apply delta frames from keyframe to target tick
            for (int i = keyIdx + 1; i < allFrames.Count && allFrames[i].Tick <= tick; i++)
                OnFramePlayback?.Invoke(allFrames[i]);

            _currentTick = tick;
        }

        /// <summary>
        /// Call each frame to advance playback.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!_isPlaying || _isPaused) return;

            _accumulator += deltaTime * _playbackSpeed;

            while (_accumulator >= _tickInterval)
            {
                _accumulator -= _tickInterval;
                _currentTick++;

                if (_currentTick > _recorder.EndTick)
                {
                    _isPlaying = false;
                    OnPlaybackFinished?.Invoke();
                    return;
                }

                // Get frames for this tick
                int count = _recorder.GetFrames(_currentTick, _currentTick, _tempFrames);
                for (int i = 0; i < count; i++)
                    OnFramePlayback?.Invoke(_tempFrames[i]);
            }
        }
    }

    /// <summary>
    /// Spectator connection manager: delayed live observation.
    /// Spectators receive the same data as players but time-shifted.
    /// </summary>
    public sealed class SpectatorManager
    {
        private readonly ReplayRecorder _liveRecorder;
        private readonly List<SpectatorConnection> _spectators = new List<SpectatorConnection>(32);
        private readonly int _delayTicks;

        public int SpectatorCount => _spectators.Count;
        public int DelayTicks => _delayTicks;

        public event Action<INetConnection, ReplayFrame> OnSendToSpectator;

        /// <param name="delayTicks">Observation delay in ticks (e.g. 5400 = 3 minutes at 30Hz)</param>
        public SpectatorManager(int delayTicks = 5400)
        {
            _delayTicks = delayTicks;
            _liveRecorder = new ReplayRecorder(keyframeInterval: 300);
        }

        public ReplayRecorder LiveRecorder => _liveRecorder;

        public void AddSpectator(INetConnection connection)
        {
            _spectators.Add(new SpectatorConnection
            {
                Connection = connection,
                LastSentTick = _liveRecorder.EndTick - _delayTicks - 1
            });

            // Send keyframe to new spectator for initial state
            int keyIdx = _liveRecorder.FindNearestKeyframe(_liveRecorder.EndTick - _delayTicks);
            if (keyIdx >= 0)
            {
                var allFrames = _liveRecorder.GetAllFrames();
                OnSendToSpectator?.Invoke(connection, allFrames[keyIdx]);
            }
        }

        public void RemoveSpectator(INetConnection connection)
        {
            for (int i = _spectators.Count - 1; i >= 0; i--)
                if (_spectators[i].Connection.ConnectionId == connection.ConnectionId)
                    _spectators.RemoveAt(i);
        }

        /// <summary>
        /// Call each server tick. Sends delayed frames to all spectators.
        /// </summary>
        public void Update(int currentTick)
        {
            int delayedTick = currentTick - _delayTicks;
            if (delayedTick < _liveRecorder.StartTick) return;

            var tempFrames = new List<ReplayFrame>(4);

            for (int s = 0; s < _spectators.Count; s++)
            {
                var spec = _spectators[s];
                int fromTick = spec.LastSentTick + 1;

                if (fromTick > delayedTick) continue;

                int count = _liveRecorder.GetFrames(fromTick, delayedTick, tempFrames);
                for (int i = 0; i < count; i++)
                    OnSendToSpectator?.Invoke(spec.Connection, tempFrames[i]);

                spec.LastSentTick = delayedTick;
                _spectators[s] = spec;
            }
        }

        private struct SpectatorConnection
        {
            public INetConnection Connection;
            public int LastSentTick;
        }
    }
}
