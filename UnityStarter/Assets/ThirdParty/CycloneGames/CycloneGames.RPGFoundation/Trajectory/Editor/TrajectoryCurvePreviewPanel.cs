using System;
using CycloneGames.RPGFoundation.Trajectory.Core;
using CycloneGames.RPGFoundation.Trajectory.Curves;
using CycloneGames.RPGFoundation.Trajectory.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Editor
{
    /// <summary>
    /// Authoring panel that turns a curve description into a sampled polyline and shows it in the
    /// scene view.
    ///
    /// This exists to make the sampling layer inspectable: the analytic curve and the polyline the
    /// solver actually walks are different things, and the gap between them is what determines
    /// whether a hit is reported at the right place.
    /// </summary>
    internal sealed class TrajectoryCurvePreviewPanel
    {
        private const int MaxSamples = 256;

        private static readonly string[] s_curveKindLabels =
        {
            "Bezier (cubic)", "Sine", "Damped sine", "Parabolic"
        };

        private readonly TrajectoryVector3[] _samples = new TrajectoryVector3[MaxSamples];
        private TrajectoryTraceBuffer _traceBuffer;

        private bool _visible;
        private int _kind;                 // 0 Bezier3, 1 Sine, 2 DampedSine
        private int _sampleCount = 32;
        private Vector3 _p0 = new Vector3(0f, 0f, 0f);
        private Vector3 _p1 = new Vector3(3f, 4f, 0f);
        private Vector3 _p2 = new Vector3(7f, 4f, 0f);
        private Vector3 _p3 = new Vector3(10f, 0f, 0f);
        private float _length = 20f;
        private float _amplitude = 3f;
        private float _cycles = 2f;
        private float _decay = 2f;
        private float _duration = 2f;
        private bool _traceAgainstScene = true;
        private Vector3 _anchor;
        private bool _hasAnchor;

        private int _lastSegments;
        private int _lastHits;
        private double _lastTraceMs;
        private TrajectoryTraceFlags _lastFlags;

        public bool Visible
        {
            get { return _visible; }
        }

        /// <summary>
        /// Places the sampled curve at the selected probe's origin. Without an anchor the panel stays
        /// silent, so opening the workbench never paints a curve that belongs to no probe.
        /// </summary>
        public void SetAnchor(Vector3 origin)
        {
            _anchor = origin;
            _hasAnchor = true;
        }

        public void DrawPanel()
        {
            _visible = EditorGUILayout.Foldout(_visible, "Curve preview (Bezier / Sine / Damped)", true);
            if (!_visible)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _kind = EditorGUILayout.Popup(
                    "Curve", _kind,
                    s_curveKindLabels);
                _sampleCount = EditorGUILayout.IntSlider("Samples", _sampleCount, 2, MaxSamples);

                if (_kind == 0)
                {
                    _p0 = EditorGUILayout.Vector3Field("P0", _p0);
                    _p1 = EditorGUILayout.Vector3Field("P1", _p1);
                    _p2 = EditorGUILayout.Vector3Field("P2", _p2);
                    _p3 = EditorGUILayout.Vector3Field("P3", _p3);
                }
                else if (_kind == 3)
                {
                    _p0 = EditorGUILayout.Vector3Field("Origin", _p0);
                    _p1 = EditorGUILayout.Vector3Field("Initial Velocity", _p1);
                    _p2 = EditorGUILayout.Vector3Field("Gravity", _p2);
                    _duration = EditorGUILayout.FloatField("Duration", _duration);
                }
                else
                {
                    _p0 = EditorGUILayout.Vector3Field("Origin", _p0);
                    _p1 = EditorGUILayout.Vector3Field("Forward", _p1);
                    _p2 = EditorGUILayout.Vector3Field("Up", _p2);
                    _length = EditorGUILayout.FloatField("Length", _length);
                    _amplitude = EditorGUILayout.FloatField("Amplitude", _amplitude);
                    _cycles = EditorGUILayout.FloatField("Cycles", _cycles);
                    if (_kind == 2)
                    {
                        _decay = EditorGUILayout.FloatField("Decay", _decay);
                    }
                }

                _traceAgainstScene = EditorGUILayout.ToggleLeft(
                    "Trace against scene colliders (Box shape)", _traceAgainstScene);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Trace curve"))
                {
                    TraceCurve();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    "last: seg " + _lastSegments + ", hit " + _lastHits
                    + ", " + _lastTraceMs.ToString("F3") + " ms"
                    + (_lastFlags != TrajectoryTraceFlags.None ? ", " + _lastFlags : string.Empty),
                    EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        public void DrawScene(SceneView view)
        {
            if (!_visible || !_hasAnchor || Event.current.type != EventType.Repaint)
            {
                return;
            }

            int count = Sample();
            if (count < 2)
            {
                return;
            }

            // Dotted and translucent so it never reads as the probe's own trace, which uses solid lines.
            Handles.color = new Color(0.55f, 0.75f, 1f, 0.5f);
            for (int i = 0; i + 1 < count; i++)
            {
                Handles.DrawDottedLine(ToVector3(_samples[i]), ToVector3(_samples[i + 1]), 3f);
            }

            Handles.color = new Color(0.55f, 0.75f, 1f, 0.8f);
            Handles.Label(ToVector3(_samples[0]) + Vector3.up * 0.15f, "curve preview (not traced)");

            Handles.color = new Color(1f, 0.85f, 0.3f, 1f);
            for (int i = 0; i < count; i += Math.Max(1, count / 16))
            {
                Vector3 point = ToVector3(_samples[i]);
                Handles.SphereHandleCap(
                    0, point, Quaternion.identity,
                    HandleUtility.GetHandleSize(point) * 0.03f, EventType.Repaint);
            }
        }

        private int Sample()
        {
            int count;
            switch (_kind)
            {
                case 1:
                    count = TrajectoryCurveSampler.SampleSine(
                        ToTrajectory(_p0), ToTrajectory(_p1), ToTrajectory(_p2),
                        _length, _amplitude, _cycles, _samples.AsSpan(0, _sampleCount));
                    break;
                case 2:
                    count = TrajectoryCurveSampler.SampleDampedSine(
                        ToTrajectory(_p0), ToTrajectory(_p1), ToTrajectory(_p2),
                        _length, _amplitude, _cycles, _decay, _samples.AsSpan(0, _sampleCount));
                    break;
                case 3:
                    count = TrajectoryCurveSampler.SampleParabolic(
                        ToTrajectory(_p0), ToTrajectory(_p1), ToTrajectory(_p2), _duration,
                        _samples.AsSpan(0, _sampleCount));
                    break;
                default:
                    count = TrajectoryCurveSampler.SampleBezier3(
                        ToTrajectory(_p0), ToTrajectory(_p1), ToTrajectory(_p2), ToTrajectory(_p3),
                        _samples.AsSpan(0, _sampleCount));
                    break;
            }

            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    _samples[i] = _samples[i] + ToTrajectory(_anchor);
                }
            }

            return count;
        }

        private void TraceCurve()
        {
            int count = Sample();
            if (count < 2)
            {
                return;
            }

            if (_traceBuffer == null)
            {
                _traceBuffer = new TrajectoryTraceBuffer(256, 256, 32);
            }

            // Box shape keeps the sweep attached to the polyline volume; a zero radius would reduce
            // the preview to a ray and miss grazing geometry.
            TrajectoryQuery query = TrajectoryQuery.CreateBox(
                traceId: 1,
                ownerEntityId: 0UL,
                collisionLayerMask: ~0,
                origin: _samples[0],
                direction: ToTrajectory(Vector3.forward),
                maxDistance: 10000f,
                halfExtents: new TrajectoryVector3(0.05f, 0.05f, 0.05f));

            var world = _traceAgainstScene
                ? new UnityTrajectoryCollisionWorld3D(64)
                : null;

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            TrajectoryTraceResult result = TrajectoryPolylineSolver.Trace(
                _samples.AsSpan(0, count), in query, world, _traceBuffer);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;

            _lastSegments = result.SegmentCount;
            _lastHits = result.HitCount;
            _lastFlags = result.Flags;
            _lastTraceMs = elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            SceneView.RepaintAll();
        }

        private static TrajectoryVector3 ToTrajectory(Vector3 value)
        {
            return new TrajectoryVector3(value.x, value.y, value.z);
        }

        private static Vector3 ToVector3(TrajectoryVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }
    }
}
