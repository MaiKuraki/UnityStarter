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
        private SerializedProperty _hitColor;
        private SerializedProperty _normalColor;

        private static bool s_validationFoldout = true;
        private static bool s_queryFoldout = true;
        private static bool s_collisionFoldout = true;
        private static bool s_bufferFoldout = true;
        private static bool s_previewFoldout = true;
        private static bool s_resultFoldout = true;

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
            _hitColor = serializedObject.FindProperty("HitColor");
            _normalColor = serializedObject.FindProperty("NormalColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

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

            if (!Application.isPlaying)
            {
                SceneView.RepaintAll();
            }
        }

        private void OnSceneGUI()
        {
            if (targets.Length != 1)
            {
                return;
            }

            var probe = (TrajectoryDebugProbe)target;
            if (!probe.ScenePreviewEnabled || !probe.TryTrace(out _))
            {
                return;
            }

            DrawTrace(probe);
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
                EditorGUILayout.PropertyField(_hitColor);
                EditorGUILayout.PropertyField(_normalColor);
            });
        }

        private void DrawTraceResult()
        {
            if (targets.Length != 1)
            {
                return;
            }

            RPGFoundationEditorUiUtility.DrawSection("Trace Result", RPGFoundationEditorUiUtility.ColorDebug, ref s_resultFoldout, () =>
            {
                var probe = (TrajectoryDebugProbe)target;
                if (!probe.TryTrace(out TrajectoryTraceResult result))
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox("No trace result. Assign a preset and make sure the query is valid.", MessageType.Info);
                    return;
                }

                RPGFoundationEditorUiUtility.DrawReadOnlyText("Flags", result.Flags.ToString());
                RPGFoundationEditorUiUtility.DrawReadOnlyText("Segments", result.SegmentCount.ToString());
                RPGFoundationEditorUiUtility.DrawReadOnlyText("Hits", result.HitCount.ToString());
                RPGFoundationEditorUiUtility.DrawReadOnlyText("Travel Distance", result.TravelDistance.ToString("F3"));
                RPGFoundationEditorUiUtility.DrawReadOnlyText("End Position", FormatVector(result.EndPosition));
            });
        }

        private static void DrawTrace(TrajectoryDebugProbe probe)
        {
            TrajectoryTraceBuffer buffer = probe.Buffer;
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
                }

                Handles.color = color;
                Handles.DrawLine(ToVector3(segment.From), ToVector3(segment.To));
            }

            for (int i = 0; i < buffer.HitCount; i++)
            {
                TrajectoryHit hit = buffer.GetHit(i);
                Vector3 position = ToVector3(hit.Position);
                Vector3 normalEnd = position + ToVector3(hit.Normal).normalized * 0.75f;

                Handles.color = probe.PreviewHitColor;
                Handles.SphereHandleCap(0, position, Quaternion.identity, 0.15f, EventType.Repaint);

                Handles.color = probe.PreviewNormalColor;
                Handles.DrawLine(position, normalEnd);
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
