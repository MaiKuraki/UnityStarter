using System;
using CycloneGames.RPGFoundation.Editor;
using CycloneGames.RPGFoundation.Trajectory.Core;
using CycloneGames.RPGFoundation.Trajectory.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Editor
{
    [CustomEditor(typeof(TrajectoryDebugProbe), true)]
    [CanEditMultipleObjects]
    public class TrajectoryDebugProbeEditor : UnityEditor.Editor
    {
        private static readonly string[] ExcludedProperties =
        {
            "m_Script",
            "QueryPreset",
            "OriginOverride",
            "LocalDirection",
            "CollisionMode",
            "ReflectionLayerMask",
            "PierceLayerMask",
            "QueryTriggerInteraction",
            "SegmentCapacity",
            "HitCapacity",
            "CastHitCapacity",
            "DrawScenePreview",
            "SegmentColor",
            "ReflectionColor",
            "PierceColor",
            "HitColor",
            "NormalColor"
        };

        private SerializedProperty _queryPreset;
        private SerializedProperty _originOverride;
        private SerializedProperty _localDirection;
        private SerializedProperty _collisionMode;
        private SerializedProperty _reflectionLayerMask;
        private SerializedProperty _pierceLayerMask;
        private SerializedProperty _queryTriggerInteraction;
        private SerializedProperty _segmentCapacity;
        private SerializedProperty _hitCapacity;
        private SerializedProperty _castHitCapacity;
        private SerializedProperty _drawScenePreview;
        private SerializedProperty _segmentColor;
        private SerializedProperty _reflectionColor;
        private SerializedProperty _pierceColor;
        private SerializedProperty _hitColor;
        private SerializedProperty _normalColor;

        private static bool s_validationFoldout = true;
        private static bool s_queryFoldout = true;
        private static bool s_collisionFoldout = true;
        private static bool s_bufferFoldout = true;
        private static bool s_previewFoldout = true;
        private static bool s_resultFoldout = true;

        private TrajectoryQueryPresetAsset _cachedPreset;
        private SerializedObject _presetSerialized;
        private SerializedProperty _presetRadius;
        private TrajectoryTraceResult _cachedResult;
        private bool _hasCachedResult;
        private double _lastTraceMs;
        private Vector3 _lastProbePosition;
        private Quaternion _lastProbeRotation;
        private Vector3 _lastOverridePosition;
        private Quaternion _lastOverrideRotation;
        private bool _hasTransformSnapshot;

        private void OnEnable()
        {
            _queryPreset = serializedObject.FindProperty("QueryPreset");
            _originOverride = serializedObject.FindProperty("OriginOverride");
            _localDirection = serializedObject.FindProperty("LocalDirection");
            _collisionMode = serializedObject.FindProperty("CollisionMode");
            _reflectionLayerMask = serializedObject.FindProperty("ReflectionLayerMask");
            _pierceLayerMask = serializedObject.FindProperty("PierceLayerMask");
            _queryTriggerInteraction = serializedObject.FindProperty("QueryTriggerInteraction");
            _segmentCapacity = serializedObject.FindProperty("SegmentCapacity");
            _hitCapacity = serializedObject.FindProperty("HitCapacity");
            _castHitCapacity = serializedObject.FindProperty("CastHitCapacity");
            _drawScenePreview = serializedObject.FindProperty("DrawScenePreview");
            _segmentColor = serializedObject.FindProperty("SegmentColor");
            _reflectionColor = serializedObject.FindProperty("ReflectionColor");
            _pierceColor = serializedObject.FindProperty("PierceColor");
            _hitColor = serializedObject.FindProperty("HitColor");
            _normalColor = serializedObject.FindProperty("NormalColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            DrawInspectorHeader();
            DrawValidation();
            DrawQuery();
            DrawCollision();
            DrawBuffers();
            DrawPreview();
            DrawTraceResult();

            RPGFoundationEditorUiUtility.DrawDerivedProperties(
                serializedObject,
                target.GetType().Name + " Extension Fields",
                ExcludedProperties);

            serializedObject.ApplyModifiedProperties();

            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            // Only a real edit invalidates the trace and asks the scene to redraw. Repainting on
            // every inspector frame multiplied the physics cost by the number of open scene views.
            _hasCachedResult = false;
            if (!Application.isPlaying)
            {
                SceneView.RepaintAll();
            }
        }

        private void OnSceneGUI()
        {
            var probe = (TrajectoryDebugProbe)target;
            if (probe == null)
            {
                return;
            }

            // Handles must run on every event so they can consume mouse drags; the trace itself and
            // the preview geometry only need to run once per repaint.
            DrawInteractiveHandles(probe);

            if (Event.current.type != EventType.Repaint || !probe.ScenePreviewEnabled)
            {
                return;
            }

            if (!EnsureTraced(probe))
            {
                return;
            }

            DrawTrace(probe);
        }

        private bool EnsureTraced(TrajectoryDebugProbe probe)
        {
            // The inspector only invalidates on serialized-field edits, so moving or rotating the probe
            // in the scene view kept showing the old path. Snapshot the transforms and compare.
            Transform probeTransform = probe.transform;
            Transform overrideTransform = probe.OriginOverrideTransform;
            bool transformChanged = !_hasTransformSnapshot
                || probeTransform.position != _lastProbePosition
                || probeTransform.rotation != _lastProbeRotation
                || (overrideTransform != null
                    && (overrideTransform.position != _lastOverridePosition
                        || overrideTransform.rotation != _lastOverrideRotation));

            if (transformChanged)
            {
                _hasCachedResult = false;
                _hasTransformSnapshot = true;
                _lastProbePosition = probeTransform.position;
                _lastProbeRotation = probeTransform.rotation;
                if (overrideTransform != null)
                {
                    _lastOverridePosition = overrideTransform.position;
                    _lastOverrideRotation = overrideTransform.rotation;
                }
            }

            if (_hasCachedResult)
            {
                return true;
            }

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            bool traced = probe.TryTrace(out _cachedResult);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            _lastTraceMs = elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            if (!traced)
            {
                _hasCachedResult = false;
                return false;
            }

            _hasCachedResult = true;
            return true;
        }

        private void DrawInteractiveHandles(TrajectoryDebugProbe probe)
        {
            Vector3 origin = probe.Origin;
            Vector3 direction = probe.Direction;
            float handleSize = HandleUtility.GetHandleSize(origin);

            // Origin: drag the override transform when one is assigned, otherwise the probe itself.
            Transform overrideTransform = probe.OriginOverrideTransform;
            Transform originTransform = overrideTransform != null ? overrideTransform : probe.transform;
            EditorGUI.BeginChangeCheck();
            Vector3 newOrigin = Handles.PositionHandle(origin, originTransform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(originTransform, "Move Trajectory Origin");
                originTransform.position = newOrigin;
                EditorUtility.SetDirty(originTransform);
            }

            // Direction: a free handle placed along the aim; the delta re-aims LocalDirection.
            float aimDistance = Math.Max(1f, handleSize * 2f);
            EditorGUI.BeginChangeCheck();
            var fmh_180_17_639304879121663574 = Quaternion.identity; Vector3 newAim = Handles.FreeMoveHandle(
                origin + direction * aimDistance,
                handleSize * 0.08f,
                Vector3.zero,
                Handles.CircleHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 newDirection = newAim - origin;
                if (newDirection.sqrMagnitude > 0.000001f)
                {
                    Undo.RecordObject(probe, "Aim Trajectory");
                    probe.SetLocalDirectionFromWorld(newDirection);
                    EditorUtility.SetDirty(probe);
                }
            }

            // Radius: edits the preset asset through its serialized property so Undo and prefab
            // overrides behave like any other inspector edit.
            TrajectoryQueryPresetAsset preset = probe.Preset;
            if (preset == null)
            {
                return;
            }

            EnsurePresetSerialized(preset);
            if (_presetRadius == null)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            float newRadius = Handles.RadiusHandle(Quaternion.identity, origin, _presetRadius.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(preset, "Change Trajectory Radius");
                _presetRadius.floatValue = newRadius;
                _presetSerialized.ApplyModifiedProperties();
            }
        }

        private void EnsurePresetSerialized(TrajectoryQueryPresetAsset preset)
        {
            if (_cachedPreset == preset && _presetSerialized != null)
            {
                return;
            }

            _cachedPreset = preset;
            _presetSerialized = new SerializedObject(preset);
            _presetRadius = _presetSerialized.FindProperty("Radius");
        }

        private void DrawInspectorHeader()
        {
            EditorGUILayout.LabelField("Trajectory Debug Probe", EditorStyles.boldLabel);
            RPGFoundationEditorUiUtility.DrawHelpBox(
                "Scene authoring probe for validating trajectory presets against actual Unity Physics adapters. It is intended for editor tuning, not as a gameplay singleton.",
                MessageType.None);
        }

        private void DrawValidation()
        {
            RPGFoundationEditorUiUtility.DrawSection("Validation", RPGFoundationEditorUiUtility.ColorWarning, ref s_validationFoldout, () =>
            {
                bool hasIssue = false;

                if (!HasMixedValues(_queryPreset) && _queryPreset.objectReferenceValue == null)
                {
                    hasIssue = true;
                    RPGFoundationEditorUiUtility.DrawHelpBox("Assign a Trajectory Query Preset before tracing.", MessageType.Error);
                }

                if (!HasMixedValues(_localDirection) && _localDirection.vector3Value.sqrMagnitude <= 0.000001f)
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox("Local Direction is zero. The probe will fall back to transform.forward.", MessageType.Info);
                }

                if (!HasMixedValues(_segmentCapacity) && _segmentCapacity.intValue <= 0)
                {
                    hasIssue = true;
                    RPGFoundationEditorUiUtility.DrawHelpBox("Segment Capacity must be greater than zero.", MessageType.Error);
                }

                if (!HasMixedValues(_hitCapacity) && _hitCapacity.intValue <= 0)
                {
                    hasIssue = true;
                    RPGFoundationEditorUiUtility.DrawHelpBox("Hit Capacity must be greater than zero.", MessageType.Error);
                }

                if (!HasMixedValues(_castHitCapacity) && _castHitCapacity.intValue <= 0)
                {
                    hasIssue = true;
                    RPGFoundationEditorUiUtility.DrawHelpBox("Cast Hit Capacity must be greater than zero.", MessageType.Error);
                }

                if (!HasMixedValues(_collisionMode) && (TrajectoryCollisionMode)_collisionMode.enumValueIndex == TrajectoryCollisionMode.None)
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox("Collision Mode is None. The solver will draw the unblocked path only.", MessageType.Info);
                }

                if (!hasIssue)
                {
                    RPGFoundationEditorUiUtility.DrawStatusRow("Status", "Ready", RPGFoundationEditorUiUtility.ColorBehavior);
                }
            });
        }

        private void DrawQuery()
        {
            RPGFoundationEditorUiUtility.DrawSection("Query", RPGFoundationEditorUiUtility.ColorCore, ref s_queryFoldout, () =>
            {
                EditorGUILayout.PropertyField(_queryPreset);
                EditorGUILayout.PropertyField(_originOverride);
                EditorGUILayout.PropertyField(_localDirection);
            });
        }

        private void DrawCollision()
        {
            RPGFoundationEditorUiUtility.DrawSection("Collision Adapter", RPGFoundationEditorUiUtility.ColorRuntime, ref s_collisionFoldout, () =>
            {
                EditorGUILayout.PropertyField(_collisionMode);
                EditorGUILayout.PropertyField(_reflectionLayerMask);
                EditorGUILayout.PropertyField(_pierceLayerMask);
                EditorGUILayout.PropertyField(_queryTriggerInteraction);

                var probe = (TrajectoryDebugProbe)target;
                if (_collisionMode.intValue != 0
                    && probe.ReflectionMask.value == 0
                    && probe.PierceMask.value == 0)
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox(
                        "Both response masks are empty, so every hit stops the trace. Reflection and pierce budgets in the preset will never be used.",
                        MessageType.Info);
                }

                RPGFoundationEditorUiUtility.DrawHelpBox(
                    "Reflection and Pierce layer masks map Unity hits to TrajectoryHitResponse values. Layers outside these masks stop the trace.",
                    MessageType.None);
            });
        }

        private void DrawBuffers()
        {
            RPGFoundationEditorUiUtility.DrawSection("Buffers", RPGFoundationEditorUiUtility.ColorBehavior, ref s_bufferFoldout, () =>
            {
                EditorGUILayout.PropertyField(_segmentCapacity);
                EditorGUILayout.PropertyField(_hitCapacity);
                EditorGUILayout.PropertyField(_castHitCapacity);

                var probe = (TrajectoryDebugProbe)target;
                TrajectoryTraceBuffer buffer = probe.Buffer;
                RPGFoundationEditorUiUtility.DrawReadOnlyText(
                    "Last usage",
                    "segments " + buffer.SegmentCount + "/" + buffer.SegmentCapacity
                    + "   hits " + buffer.HitCount + "/" + buffer.HitCapacity);

                if (buffer.SegmentCount >= buffer.SegmentCapacity || buffer.HitCount >= buffer.HitCapacity)
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox(
                        "The last trace filled a buffer. Raise the capacity or the result is truncated with a capacity flag.",
                        MessageType.Warning);
                }

                RPGFoundationEditorUiUtility.DrawHelpBox(
                    "These capacities allocate reusable editor/runtime buffers. Increase them for multi-bounce beams, pierce chains, or dense scenes.",
                    MessageType.None);
            });
        }

        private void DrawPreview()
        {
            RPGFoundationEditorUiUtility.DrawSection("Scene Preview", RPGFoundationEditorUiUtility.ColorDebug, ref s_previewFoldout, () =>
            {
                EditorGUILayout.PropertyField(_drawScenePreview);
                EditorGUILayout.PropertyField(_segmentColor);
                EditorGUILayout.PropertyField(_reflectionColor);
                EditorGUILayout.PropertyField(_pierceColor);
                EditorGUILayout.PropertyField(_hitColor);
                EditorGUILayout.PropertyField(_normalColor);
            });
        }

        private void DrawTraceResult()
        {
            RPGFoundationEditorUiUtility.DrawSection("Trace Result", RPGFoundationEditorUiUtility.ColorDebug, ref s_resultFoldout, () =>
            {
                var probe = (TrajectoryDebugProbe)target;

                if (!_hasCachedResult && !probe.TryTrace(out _cachedResult))
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox("No trace result. Assign a preset and make sure the query is valid.", MessageType.Info);
                    return;
                }

                _hasCachedResult = true;

                RPGFoundationEditorUiUtility.DrawReadOnlyText("Flags", _cachedResult.Flags.ToString());
                RPGFoundationEditorUiUtility.DrawReadOnlyText("Segments", _cachedResult.SegmentCount.ToString());
                RPGFoundationEditorUiUtility.DrawReadOnlyText("Hits", _cachedResult.HitCount.ToString());
                RPGFoundationEditorUiUtility.DrawReadOnlyText("Travel Distance", _cachedResult.TravelDistance.ToString("F3"));
                RPGFoundationEditorUiUtility.DrawReadOnlyText("End Position", FormatVector(_cachedResult.EndPosition));
                RPGFoundationEditorUiUtility.DrawReadOnlyText("Nearest Hit", _cachedResult.NearestHitIndex.ToString());
                RPGFoundationEditorUiUtility.DrawReadOnlyText("Trace Time", _lastTraceMs.ToString("F4") + " ms");

                TrajectoryTraceBuffer buffer = probe.Buffer;
                for (int i = 0; i < buffer.HitCount; i++)
                {
                    TrajectoryHit hit = buffer.GetHit(i);
                    RPGFoundationEditorUiUtility.DrawReadOnlyText(
                        "Hit " + i,
                        "#" + hit.SegmentIndex
                        + "  d=" + hit.Distance.ToString("F2")
                        + "  " + hit.Response
                        + "  p=" + FormatVector(hit.Position)
                        + "  n=" + FormatVector(hit.Normal));
                }
            });
        }

        /// <summary>
        /// Draws the sweep volume so a Box or Capsule config is visible instead of only numeric.
        ///
        /// The shapes must match what the adapters actually cast: Box is axis-aligned and Capsule is
        /// aligned to world up, regardless of travel direction. Drawing them oriented along the sweep
        /// would show a volume the solver never uses.
        /// </summary>
        private static void DrawSweepVolumes(TrajectoryDebugProbe probe, TrajectoryTraceBuffer buffer)
        {
            TrajectoryQueryPresetAsset preset = probe.Preset;
            if (preset == null || preset.QueryShape == TrajectoryShape.Ray)
            {
                return;
            }

            Vector3 origin = probe.Origin;
            float radius = preset.RadiusValue;
            float height = preset.HeightValue;
            TrajectoryVector3 half = preset.HalfExtentsValue;

            Color color = probe.PreviewSegmentColor;
            color.a = 0.5f;
            Handles.color = color;

            if (TrajectoryGizmoSettings.ShowShapeAtOrigin)
            {
                DrawShape(preset.QueryShape, origin, radius, height, half);
            }

            bool hasPath = buffer.SegmentCount > 0;
            Vector3 end = hasPath ? ToVector3(buffer.GetSegment(buffer.SegmentCount - 1).To) : origin;

            if (TrajectoryGizmoSettings.ShowShapeAtEnd && hasPath)
            {
                Color endColor = color;
                endColor.a = 0.28f;
                Handles.color = endColor;
                DrawShape(preset.QueryShape, end, radius, height, half);
                Handles.color = color;
            }

            if (TrajectoryGizmoSettings.ShowSweepWalls && hasPath)
            {
                // Dotted and dimmer than the trace: walls describe the swept volume, not the path.
                Color wallColor = color;
                wallColor.a = 0.22f;
                Handles.color = wallColor;
                DrawSweepWalls(preset.QueryShape, origin, end, radius, height, half);
                Handles.color = color;
            }
        }

        /// <summary>
        /// Draws one 90-degree meridian of a hemispherical cap: from the apex down to the equator point
        /// on <paramref name="lateralAxis"/>. A full 180-degree sweep would continue past the equator
        /// into the straight section and read as a diagonal chord through the capsule.
        /// <paramref name="upSign"/> is +1 for the upper cap and -1 for the lower one.
        /// </summary>
        /// <summary>
        /// Horizontal axis perpendicular to the scene camera's view direction, so the capsule bus bars
        /// stay on the silhouette instead of crossing the interior.
        /// </summary>
        private static Vector3 ResolveSilhouetteAxis()
        {
            SceneView view = SceneView.currentDrawingSceneView;
            if (view == null || view.camera == null)
            {
                return Vector3.right;
            }

            Vector3 side = Vector3.Cross(Vector3.up, view.camera.transform.forward);
            return side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;
        }

        private static void DrawHemisphereArc(Vector3 center, Vector3 lateralAxis, float radius, float upSign)
        {
            const int steps = 6;
            Vector3 previous = center + Vector3.up * (radius * upSign);
            for (int i = 1; i <= steps; i++)
            {
                // 0 -> 90 degrees: apex to equator.
                float angle = Mathf.PI * 0.5f * i / steps;
                Vector3 point = center
                    + Vector3.up * (Mathf.Cos(angle) * radius * upSign)
                    + lateralAxis * (Mathf.Sin(angle) * radius);
                Handles.DrawLine(previous, point);
                previous = point;
            }
        }

        private static void DrawShape(
            TrajectoryShape shape,
            Vector3 center,
            float radius,
            float height,
            TrajectoryVector3 half)
        {
            switch (shape)
            {
                case TrajectoryShape.Sphere:
                    Handles.DrawWireDisc(center, Vector3.right, radius);
                    Handles.DrawWireDisc(center, Vector3.up, radius);
                    Handles.DrawWireDisc(center, Vector3.forward, radius);
                    break;

                case TrajectoryShape.Box:
                    Handles.DrawWireCube(
                        center,
                        new Vector3(half.X * 2f, half.Y * 2f, half.Z * 2f));
                    break;

                case TrajectoryShape.Capsule:
                {
                    float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
                    Vector3 top = center + Vector3.up * halfSegment;
                    Vector3 bottom = center - Vector3.up * halfSegment;
                    Handles.DrawWireDisc(top, Vector3.up, radius);
                    Handles.DrawWireDisc(bottom, Vector3.up, radius);

                    // Bus bars sit on the silhouette, not on fixed world axes. Fixed X and Z bars are
                    // vertical in 3D but project as two crossing diagonals once the scene camera is at
                    // an angle, which reads as a drawing error.
                    Vector3 side = ResolveSilhouetteAxis();
                    Handles.DrawLine(top + side * radius, bottom + side * radius);
                    Handles.DrawLine(top - side * radius, bottom - side * radius);

                    if (TrajectoryGizmoSettings.ShowCapsuleCaps)
                    {
                        Vector3 across = Vector3.Cross(Vector3.up, side);
                        DrawHemisphereArc(top, side, radius, 1f);
                        DrawHemisphereArc(top, across, radius, 1f);
                        DrawHemisphereArc(bottom, side, radius, -1f);
                        DrawHemisphereArc(bottom, across, radius, -1f);

                        // Poles are the apex of each hemisphere, i.e. the true Height boundary. They are
                        // drawn only together with the arcs: on their own they read as stray dots.
                        float poleSize = HandleUtility.GetHandleSize(top) * 0.04f;
                        Handles.SphereHandleCap(0, top + Vector3.up * radius, Quaternion.identity, poleSize, EventType.Repaint);
                        Handles.SphereHandleCap(0, bottom - Vector3.up * radius, Quaternion.identity, poleSize, EventType.Repaint);
                    }

                    break;
                }
            }
        }

        private static void DrawSweepWalls(
            TrajectoryShape shape,
            Vector3 from,
            Vector3 to,
            float radius,
            float height,
            TrajectoryVector3 half)
        {
            switch (shape)
            {
                case TrajectoryShape.Box:
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        for (int sy = -1; sy <= 1; sy += 2)
                        {
                            for (int sz = -1; sz <= 1; sz += 2)
                            {
                                var offset = new Vector3(sx * half.X, sy * half.Y, sz * half.Z);
                                Handles.DrawDottedLine(from + offset, to + offset, 4f);
                            }
                        }
                    }
                    break;

                case TrajectoryShape.Sphere:
                    Handles.DrawDottedLine(from + Vector3.right * radius, to + Vector3.right * radius, 4f);
                    Handles.DrawDottedLine(from - Vector3.right * radius, to - Vector3.right * radius, 4f);
                    Handles.DrawDottedLine(from + Vector3.up * radius, to + Vector3.up * radius, 4f);
                    Handles.DrawDottedLine(from - Vector3.up * radius, to - Vector3.up * radius, 4f);
                    break;

                case TrajectoryShape.Capsule:
                {
                    // Walls follow the straight section only; the hemispherical caps are left to the
                    // wire discs so the drawing stays readable.
                    float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
                    Vector3 fromTop = from + Vector3.up * halfSegment;
                    Vector3 fromBottom = from - Vector3.up * halfSegment;
                    Vector3 toTop = to + Vector3.up * halfSegment;
                    Vector3 toBottom = to - Vector3.up * halfSegment;
                    Handles.DrawDottedLine(fromTop + Vector3.right * radius, toTop + Vector3.right * radius, 4f);
                    Handles.DrawDottedLine(fromTop - Vector3.right * radius, toTop - Vector3.right * radius, 4f);
                    Handles.DrawDottedLine(fromBottom + Vector3.right * radius, toBottom + Vector3.right * radius, 4f);
                    Handles.DrawDottedLine(fromBottom - Vector3.right * radius, toBottom - Vector3.right * radius, 4f);
                    break;
                }
            }
        }

        private static void DrawTrace(TrajectoryDebugProbe probe)
        {
            TrajectoryTraceBuffer buffer = probe.Buffer;

            DrawSweepVolumes(probe, buffer);
            for (int i = 0; i < buffer.SegmentCount; i++)
            {
                TrajectorySegment segment = buffer.GetSegment(i);
                Color color = probe.PreviewSegmentColor;
                if (segment.EndsWithHit)
                {
                    TrajectoryHit hit = buffer.GetHit(segment.HitIndex);
                    if (hit.Response == TrajectoryHitResponse.Reflect)
                    {
                        color = probe.PreviewReflectionColor;
                    }
                    else if (hit.Response == TrajectoryHitResponse.Pierce)
                    {
                        color = probe.PreviewPierceColor;
                    }
                }

                Handles.color = color;
                Handles.DrawLine(ToVector3(segment.From), ToVector3(segment.To));

                if (i == 0)
                {
                    Handles.Label(ToVector3(segment.From), "origin", EditorStyles.miniLabel);
                }
            }

            for (int i = 0; i < buffer.HitCount; i++)
            {
                TrajectoryHit hit = buffer.GetHit(i);
                Vector3 position = ToVector3(hit.Position);
                float handleSize = HandleUtility.GetHandleSize(position);
                Vector3 normalEnd = position + ToVector3(hit.Normal).normalized * (handleSize * 0.6f);

                Handles.color = probe.PreviewHitColor;
                Handles.SphereHandleCap(0, position, Quaternion.identity, handleSize * 0.08f, EventType.Repaint);

                Handles.color = probe.PreviewNormalColor;
                Handles.DrawLine(position, normalEnd);

                Handles.Label(
                    position + Vector3.up * (handleSize * 0.12f),
                    "#" + i + " " + hit.Distance.ToString("F2") + " " + hit.Response,
                    EditorStyles.miniLabel);
            }
        }

        private static Vector3 ToVector3(TrajectoryVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static string FormatVector(TrajectoryVector3 value)
        {
            return value.X.ToString("F2") + ", " + value.Y.ToString("F2") + ", " + value.Z.ToString("F2");
        }

        private static bool HasMixedValues(SerializedProperty property)
        {
            return property == null || property.hasMultipleDifferentValues;
        }
    }
}
