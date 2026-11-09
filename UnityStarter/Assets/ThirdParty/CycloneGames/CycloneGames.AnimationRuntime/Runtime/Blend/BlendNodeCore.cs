using System;
using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>How a blend node maps its time parameter onto child clips.</summary>
    public enum BlendTimeMode : byte
    {
        /// <summary>Every child samples the same absolute seconds. Correct for equal-length clips.</summary>
        Absolute = 0,

        /// <summary>
        /// Every child samples the same normalized phase (0..1 of its own length). Required for gait blends where
        /// walk and run have different lengths, otherwise the feet slide.
        /// </summary>
        Normalized = 1
    }

    /// <summary>
    /// Shared child management for blend nodes: clip playables, phase-correct time and weight writes.
    /// <c>AnimationMixerPlayable</c> evaluates connected inputs at weight 0, so idle children are disconnected.
    /// </summary>
    internal sealed class BlendNodeCore : IDisposable
    {
        /// <summary>Parameter updates a child must stay at zero before it is disconnected.</summary>
        private const byte IdleUpdatesBeforeDisconnect = 8;

        private readonly PlayableGraph _graph;
        private readonly AnimationMixerPlayable _mixer;
        private readonly AnimationClip[] _clips;
        private readonly Playable[] _playables;
        private readonly bool[] _connected;
        private readonly byte[] _idle;
        private readonly float[] _applied;
        private readonly BlendTimeMode _timeMode;
        private readonly int _count;

        private double _time;
        private bool _disposed;
        private int _connectCount;
        private int _disconnectCount;

        internal BlendNodeCore(
            PlayableGraph graph,
            IReadOnlyList<AnimationClip> clips,
            BlendTimeMode timeMode,
            bool applyFootIK,
            bool applyPlayableIK)
        {
            if (!graph.IsValid())
            {
                throw new ArgumentException("The graph must be valid.", nameof(graph));
            }

            if (clips == null || clips.Count == 0)
            {
                throw new ArgumentException("At least one clip is required.", nameof(clips));
            }

            _graph = graph;
            _count = clips.Count;
            _timeMode = timeMode;

            _clips = new AnimationClip[_count];
            _playables = new Playable[_count];
            _connected = new bool[_count];
            _idle = new byte[_count];
            _applied = new float[_count];

            _mixer = AnimationMixerPlayable.Create(graph, _count);

            for (int i = 0; i < _count; i++)
            {
                AnimationClip clip = clips[i];
                _clips[i] = clip;
                if (clip == null)
                {
                    continue;
                }

                AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetSpeed(0d);
                playable.SetApplyFootIK(applyFootIK);
                playable.SetApplyPlayableIK(applyPlayableIK);
                _playables[i] = playable;
            }
        }

        internal AnimationMixerPlayable Mixer => _mixer;

        internal int Count => _count;

        /// <summary>Lifetime connect count, for diagnostics.</summary>
        internal int ConnectCount => _connectCount;

        /// <summary>Lifetime disconnect operations.</summary>
        internal int DisconnectCount => _disconnectCount;

        /// <summary>Current local time of a child playable, in seconds. Used by tests and tooling.</summary>
        internal double GetChildTime(int index)
        {
            return index >= 0 && index < _count && _playables[index].IsValid()
                ? _playables[index].GetTime()
                : 0d;
        }

        internal float GetAppliedWeight(int index)
        {
            return index >= 0 && index < _count ? _applied[index] : 0f;
        }

        internal bool IsConnected(int index)
        {
            return index >= 0 && index < _count && _connected[index];
        }

        /// <summary>
        /// Absolute seconds, or normalized phase when the node is in <see cref="BlendTimeMode.Normalized"/>.
        /// This is the path every frame takes, so it also advances the idle counters: a child that stays at zero
        /// weight is disconnected even when the host never re-applies weights.
        /// </summary>
        internal void SetTime(double value)
        {
            _time = value;
            for (int i = 0; i < _count; i++)
            {
                if (!_connected[i])
                {
                    continue;
                }

                ApplyTime(i);
                AdvanceIdle(i);
            }
        }

        /// <summary>
        /// Counts consecutive zero-weight updates for one connected child and disconnects it once the threshold
        /// is reached. Called from both <see cref="SetTime"/> and <see cref="ApplyWeights"/>.
        /// </summary>
        private void AdvanceIdle(int index)
        {
            if (_applied[index] > 0f)
            {
                _idle[index] = 0;
                return;
            }

            if (_idle[index] < IdleUpdatesBeforeDisconnect)
            {
                _idle[index]++;
                return;
            }

            Disconnect(index);
        }

        internal void ApplyWeights(ReadOnlySpan<float> weights)
        {
            if (_disposed)
            {
                return;
            }

            for (int i = 0; i < _count; i++)
            {
                if (!_playables[i].IsValid())
                {
                    continue;
                }

                float weight = i < weights.Length ? weights[i] : 0f;

                if (weight <= 0f)
                {
                    if (!_connected[i])
                    {
                        continue;
                    }

                    // Publish the zero weight once, otherwise _applied keeps the previous non-zero value and
                    // the idle counter would be reset on every later update.
                    if (_applied[i] > 0f)
                    {
                        _mixer.SetInputWeight(i, 0f);
                        _applied[i] = 0f;
                    }

                    AdvanceIdle(i);
                    continue;
                }

                _idle[i] = 0;

                if (!_connected[i])
                {
                    Connect(i);
                }

                if (Math.Abs(_applied[i] - weight) > AnimationRuntimeLimits.WeightEpsilon)
                {
                    _mixer.SetInputWeight(i, weight);
                    _applied[i] = weight;
                }
            }
        }

        private void Connect(int index)
        {
            ApplyTime(index);
            _mixer.ConnectInput(index, _playables[index], 0);
            _mixer.SetInputWeight(index, 0f);
            _applied[index] = 0f;
            _connected[index] = true;
            _connectCount++;
        }

        private void Disconnect(int index)
        {
            if (_mixer.IsValid())
            {
                _mixer.SetInputWeight(index, 0f);
                _mixer.DisconnectInput(index);
            }

            _connected[index] = false;
            _applied[index] = 0f;
            _idle[index] = 0;
            _disconnectCount++;
        }

        private void ApplyTime(int index)
        {
            if (!_playables[index].IsValid())
            {
                return;
            }

            double time = _time;
            if (_timeMode == BlendTimeMode.Normalized)
            {
                float phase = (float)_time;
                if (phase < 0f) phase = 0f;
                else if (phase > 1f) phase = 1f;

                AnimationClip clip = _clips[index];
                float length = clip != null ? clip.length : 0f;
                time = phase * length;
            }

            _playables[index].SetTime(time);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (!_graph.IsValid())
            {
                return;
            }

            for (int i = 0; i < _count; i++)
            {
                if (!_playables[i].IsValid())
                {
                    continue;
                }

                if (_connected[i] && _mixer.IsValid())
                {
                    _mixer.DisconnectInput(i);
                }

                _graph.DestroyPlayable(_playables[i]);
                _playables[i] = default;
                _connected[i] = false;
            }

            if (_mixer.IsValid())
            {
                _graph.DestroyPlayable(_mixer);
            }
        }
    }
}
