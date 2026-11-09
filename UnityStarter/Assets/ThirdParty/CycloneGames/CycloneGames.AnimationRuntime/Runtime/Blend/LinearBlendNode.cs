using System;
using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using UnityEngine;
using UnityEngine.Playables;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// 1D threshold blend, the node behind a gait blend (idle / walk / run / sprint).
    /// Connect it to a layer with <see cref="AnimationRuntimeGraph.SetLayerSource"/>.
    ///
    /// Time is a parameter of the node, not of the graph: the host sets the phase and the node samples every
    /// connected child at that phase. Child clips occupy mixer inputs; zero-weight children are disconnected
    /// after a few updates so they stop being evaluated.
    /// </summary>
    public sealed class LinearBlendNode : IDisposable
    {
        private const float ParameterEpsilon = 1e-4f;

        private readonly BlendNodeCore _core;
        private readonly float[] _thresholds;
        private readonly float[] _weights;

        private float _parameter = float.NaN;
        private bool _disposed;

        private LinearBlendNode(BlendNodeCore core, float[] thresholds)
        {
            _core = core;
            _thresholds = thresholds;
            _weights = new float[thresholds.Length];
        }

        /// <summary>
        /// Creates a blend node inside an existing graph. <paramref name="thresholds"/> must be ascending and the
        /// same length as <paramref name="clips"/>; a null clip disables that slot.
        /// </summary>
        public static LinearBlendNode Create(
            PlayableGraph graph,
            IReadOnlyList<AnimationClip> clips,
            IReadOnlyList<float> thresholds,
            BlendTimeMode timeMode = BlendTimeMode.Normalized,
            bool applyFootIK = false,
            bool applyPlayableIK = false)
        {
            if (clips == null)
            {
                throw new ArgumentNullException(nameof(clips));
            }

            if (thresholds == null)
            {
                throw new ArgumentNullException(nameof(thresholds));
            }

            if (thresholds.Count != clips.Count)
            {
                throw new ArgumentException("Thresholds and clips must have the same length.", nameof(thresholds));
            }

            var copy = new float[thresholds.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = thresholds[i];
                if (i > 0 && copy[i] <= copy[i - 1])
                {
                    throw new ArgumentException("Thresholds must be strictly ascending.", nameof(thresholds));
                }
            }

            var core = new BlendNodeCore(graph, clips, timeMode, applyFootIK, applyPlayableIK);
            return new LinearBlendNode(core, copy);
        }

        /// <summary>The node to hand to <see cref="AnimationRuntimeGraph.SetLayerSource"/>.</summary>
        public Playable Playable => _core.Mixer;

        public int Count => _core.Count;

        public float Parameter => _parameter;

        /// <summary>Absolute seconds, or a 0..1 phase when the node uses <see cref="BlendTimeMode.Normalized"/>.</summary>
        public void SetTime(double value)
        {
            _core.SetTime(value);
        }

        /// <summary>Updates the parameter and re-applies weights. Returns false when the value did not move.</summary>
        public bool SetParameter(float value)
        {
            if (!float.IsNaN(_parameter) && Math.Abs(value - _parameter) <= ParameterEpsilon)
            {
                return false;
            }

            _parameter = value;
            Apply();
            return true;
        }

        /// <summary>Recomputes and applies weights from the current parameter. Also drives the idle counters.</summary>
        public void Apply()
        {
            if (_disposed)
            {
                return;
            }

            LinearBlendMath.Evaluate(_thresholds, _parameter, _weights);
            _core.ApplyWeights(_weights);
        }

        public float GetAppliedWeight(int index) => _core.GetAppliedWeight(index);

        public bool IsChildConnected(int index) => _core.IsConnected(index);

        /// <summary>Lifetime connect count, for diagnostics.</summary>
        public int ConnectCount => _core.ConnectCount;

        public int DisconnectCount => _core.DisconnectCount;

        /// <summary>Current local time of a child, in seconds.</summary>
        public double GetChildTime(int index) => _core.GetChildTime(index);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _core.Dispose();
        }
    }
}
