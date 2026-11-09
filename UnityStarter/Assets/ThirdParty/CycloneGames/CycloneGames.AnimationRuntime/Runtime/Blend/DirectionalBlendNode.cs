using System;
using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using UnityEngine;
using UnityEngine.Playables;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// Polar blend by direction and magnitude, the node behind 8-way strafe and directional aim offsets.
    /// Each child declares an angle in degrees; the parameter picks the two children bracketing that angle.
    /// Below the dead zone the center child wins, which keeps idle stable when the stick is released.
    /// </summary>
    public sealed class DirectionalBlendNode : IDisposable
    {
        private const float AngleEpsilon = 1e-2f;
        private const float MagnitudeEpsilon = 1e-3f;

        private readonly BlendNodeCore _core;
        private readonly float[] _angles;
        private readonly float[] _weights;
        private readonly int _centerIndex;
        private readonly float _deadZone;

        private float _angle = float.NaN;
        private float _magnitude = float.NaN;
        private bool _disposed;

        private DirectionalBlendNode(BlendNodeCore core, float[] angles, int centerIndex, float deadZone)
        {
            _core = core;
            _angles = angles;
            _weights = new float[angles.Length];
            _centerIndex = centerIndex;
            _deadZone = deadZone < 0f ? 0f : deadZone;
        }

        /// <summary>
        /// Creates a directional node. <paramref name="angles"/> are ascending degrees in [0, 360);
        /// <paramref name="centerIndex"/> is the child used below the dead zone, or -1 for none.
        /// </summary>
        public static DirectionalBlendNode Create(
            PlayableGraph graph,
            IReadOnlyList<AnimationClip> clips,
            IReadOnlyList<float> angles,
            int centerIndex = -1,
            float deadZone = 0.1f,
            BlendTimeMode timeMode = BlendTimeMode.Absolute,
            bool applyFootIK = false,
            bool applyPlayableIK = false)
        {
            if (clips == null)
            {
                throw new ArgumentNullException(nameof(clips));
            }

            if (angles == null)
            {
                throw new ArgumentNullException(nameof(angles));
            }

            if (angles.Count != clips.Count)
            {
                throw new ArgumentException("Angles and clips must have the same length.", nameof(angles));
            }

            if (centerIndex < -1 || centerIndex >= clips.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(centerIndex));
            }

            var copy = new float[angles.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = DirectionalBlendMath.NormalizeAngle(angles[i]);
                if (i > 0 && copy[i] <= copy[i - 1])
                {
                    throw new ArgumentException("Angles must be strictly ascending after normalization.", nameof(angles));
                }
            }

            var core = new BlendNodeCore(graph, clips, timeMode, applyFootIK, applyPlayableIK);
            return new DirectionalBlendNode(core, copy, centerIndex, deadZone);
        }

        public Playable Playable => _core.Mixer;

        public int Count => _core.Count;

        public float Angle => _angle;

        public float Magnitude => _magnitude;

        public void SetTime(double value)
        {
            _core.SetTime(value);
        }

        /// <summary>Updates direction and magnitude, then re-applies weights.</summary>
        public bool SetParameter(float angleDegrees, float magnitude)
        {
            if (!float.IsNaN(_angle)
                && Math.Abs(angleDegrees - _angle) <= AngleEpsilon
                && Math.Abs(magnitude - _magnitude) <= MagnitudeEpsilon)
            {
                return false;
            }

            _angle = angleDegrees;
            _magnitude = magnitude;
            Apply();
            return true;
        }

        public void Apply()
        {
            if (_disposed)
            {
                return;
            }

            DirectionalBlendMath.Evaluate(_angles, _centerIndex, _angle, _magnitude, _deadZone, _weights);
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
