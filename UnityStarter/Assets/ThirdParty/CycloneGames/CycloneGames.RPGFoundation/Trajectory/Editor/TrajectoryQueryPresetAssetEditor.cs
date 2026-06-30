using CycloneGames.RPGFoundation.Editor;
using CycloneGames.RPGFoundation.Trajectory.Core;
using CycloneGames.RPGFoundation.Trajectory.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Editor
{
    [CustomEditor(typeof(TrajectoryQueryPresetAsset), true)]
    [CanEditMultipleObjects]
    public class TrajectoryQueryPresetAssetEditor : UnityEditor.Editor
    {
        private static readonly string[] ExcludedProperties =
        {
            "m_Script",
            "CollisionLayerMask",
            "MaxDistance",
            "Radius",
            "MaxReflectionCount",
            "MaxPierceCount",
            "MaxHitCount",
            "MaxIterationCount",
            "SurfaceOffset",
            "InitialIgnoredTargetEntityId",
            "InitialIgnoredTargetObjectId"
        };

        private SerializedProperty _collisionLayerMask;
        private SerializedProperty _maxDistance;
        private SerializedProperty _radius;
        private SerializedProperty _maxReflectionCount;
        private SerializedProperty _maxPierceCount;
        private SerializedProperty _maxHitCount;
        private SerializedProperty _maxIterationCount;
        private SerializedProperty _surfaceOffset;
        private SerializedProperty _initialIgnoredTargetEntityId;
        private SerializedProperty _initialIgnoredTargetObjectId;

        private readonly TrajectoryQueryValidationIssue[] _validationIssues =
            new TrajectoryQueryValidationIssue[TrajectoryQueryValidator.RECOMMENDED_ISSUE_CAPACITY];

        private static bool s_validationFoldout = true;
        private static bool s_shapeFoldout = true;
        private static bool s_traversalFoldout = true;
        private static bool s_ignoreFoldout;

        private void OnEnable()
        {
            _collisionLayerMask = serializedObject.FindProperty("CollisionLayerMask");
            _maxDistance = serializedObject.FindProperty("MaxDistance");
            _radius = serializedObject.FindProperty("Radius");
            _maxReflectionCount = serializedObject.FindProperty("MaxReflectionCount");
            _maxPierceCount = serializedObject.FindProperty("MaxPierceCount");
            _maxHitCount = serializedObject.FindProperty("MaxHitCount");
            _maxIterationCount = serializedObject.FindProperty("MaxIterationCount");
            _surfaceOffset = serializedObject.FindProperty("SurfaceOffset");
            _initialIgnoredTargetEntityId = serializedObject.FindProperty("InitialIgnoredTargetEntityId");
            _initialIgnoredTargetObjectId = serializedObject.FindProperty("InitialIgnoredTargetObjectId");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawInspectorHeader();
            DrawValidation();
            DrawShape();
            DrawTraversal();
            DrawInitialIgnore();

            RPGFoundationEditorUiUtility.DrawDerivedProperties(
                serializedObject,
                target.GetType().Name + " Extension Fields",
                ExcludedProperties);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInspectorHeader()
        {
            EditorGUILayout.LabelField("Trajectory Query Preset", EditorStyles.boldLabel);
            RPGFoundationEditorUiUtility.DrawHelpBox(
                "Reusable authoring data for hitscan, beams, ricochet traces, and target preview queries. Runtime code copies these values into a pure Core TrajectoryQuery.",
                MessageType.None);
            DrawPresetButtons();
        }

        private void DrawValidation()
        {
            RPGFoundationEditorUiUtility.DrawSection("Validation", RPGFoundationEditorUiUtility.ColorWarning, ref s_validationFoldout, () =>
            {
                DrawQueryValidation();
            });
        }

        private void DrawShape()
        {
            RPGFoundationEditorUiUtility.DrawSection("Shape", RPGFoundationEditorUiUtility.ColorCore, ref s_shapeFoldout, () =>
            {
                EditorGUILayout.PropertyField(_collisionLayerMask);
                EditorGUILayout.PropertyField(_maxDistance);
                EditorGUILayout.PropertyField(_radius);

                if (!HasMixedValues(_radius) && _radius.floatValue <= 0f)
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox("Radius zero uses a raycast. Positive radius uses sphere or circle sweep adapters.", MessageType.None);
                }
            });
        }

        private void DrawTraversal()
        {
            RPGFoundationEditorUiUtility.DrawSection("Traversal", RPGFoundationEditorUiUtility.ColorRuntime, ref s_traversalFoldout, () =>
            {
                EditorGUILayout.PropertyField(_maxReflectionCount);
                EditorGUILayout.PropertyField(_maxPierceCount);
                EditorGUILayout.PropertyField(_maxHitCount);
                EditorGUILayout.PropertyField(_maxIterationCount);
                EditorGUILayout.PropertyField(_surfaceOffset);
            });
        }

        private void DrawInitialIgnore()
        {
            RPGFoundationEditorUiUtility.DrawSection("Initial Ignore", RPGFoundationEditorUiUtility.ColorDebug, ref s_ignoreFoldout, () =>
            {
                EditorGUILayout.PropertyField(_initialIgnoredTargetEntityId);
                EditorGUILayout.PropertyField(_initialIgnoredTargetObjectId);
                RPGFoundationEditorUiUtility.DrawHelpBox(
                    "Use initial ignore values when the trace starts inside an owner, weapon muzzle, shield, or previously confirmed hit surface.",
                MessageType.None);
            });
        }

        private void DrawPresetButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Hitscan"))
            {
                ApplyPreset(
                    maxDistance: 60f,
                    radius: 0f,
                    reflectionCount: 0,
                    pierceCount: 0,
                    hitCount: 1,
                    iterationCount: 1,
                    surfaceOffset: TrajectoryQuery.DEFAULT_SURFACE_OFFSET);
            }

            if (GUILayout.Button("Ricochet Beam"))
            {
                ApplyPreset(
                    maxDistance: 80f,
                    radius: 0.03f,
                    reflectionCount: 4,
                    pierceCount: 0,
                    hitCount: 8,
                    iterationCount: 8,
                    surfaceOffset: TrajectoryQuery.DEFAULT_SURFACE_OFFSET);
            }

            if (GUILayout.Button("Piercing Beam"))
            {
                ApplyPreset(
                    maxDistance: 70f,
                    radius: 0.04f,
                    reflectionCount: 0,
                    pierceCount: 8,
                    hitCount: 12,
                    iterationCount: 12,
                    surfaceOffset: TrajectoryQuery.DEFAULT_SURFACE_OFFSET);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawQueryValidation()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                DrawMultiObjectValidation();
                return;
            }

            var asset = (TrajectoryQueryPresetAsset)target;
            TrajectoryQuery query = asset.BuildAuthoringQuery(0, 0UL, Vector3.zero, Vector3.forward);
            int issueCount = TrajectoryQueryValidator.Validate(in query, _validationIssues);
            if (issueCount == 0)
            {
                RPGFoundationEditorUiUtility.DrawStatusRow("Status", "Ready", RPGFoundationEditorUiUtility.ColorBehavior);
                return;
            }

            DrawValidationStatus(_validationIssues, issueCount);
        }

        private void DrawMultiObjectValidation()
        {
            int errorCount = 0;
            int warningCount = 0;
            int infoCount = 0;

            for (int i = 0; i < targets.Length; i++)
            {
                var asset = (TrajectoryQueryPresetAsset)targets[i];
                TrajectoryQuery query = asset.BuildAuthoringQuery(0, 0UL, Vector3.zero, Vector3.forward);
                int issueCount = TrajectoryQueryValidator.Validate(in query, _validationIssues);
                int max = Mathf.Min(issueCount, _validationIssues.Length);
                for (int issueIndex = 0; issueIndex < max; issueIndex++)
                {
                    switch (_validationIssues[issueIndex].Severity)
                    {
                        case TrajectoryValidationSeverity.Error:
                            errorCount++;
                            break;
                        case TrajectoryValidationSeverity.Warning:
                            warningCount++;
                            break;
                        default:
                            infoCount++;
                            break;
                    }
                }
            }

            if (errorCount == 0 && warningCount == 0 && infoCount == 0)
            {
                RPGFoundationEditorUiUtility.DrawStatusRow("Status", "Ready", RPGFoundationEditorUiUtility.ColorBehavior);
                return;
            }

            RPGFoundationEditorUiUtility.DrawStatusRow("Errors", errorCount.ToString(), errorCount > 0 ? RPGFoundationEditorUiUtility.ColorError : RPGFoundationEditorUiUtility.ColorBehavior);
            RPGFoundationEditorUiUtility.DrawStatusRow("Warnings", warningCount.ToString(), warningCount > 0 ? RPGFoundationEditorUiUtility.ColorWarning : RPGFoundationEditorUiUtility.ColorBehavior);
            RPGFoundationEditorUiUtility.DrawStatusRow("Info", infoCount.ToString(), RPGFoundationEditorUiUtility.ColorDebug);
        }

        private static void DrawValidationStatus(
            TrajectoryQueryValidationIssue[] issues,
            int issueCount)
        {
            int max = Mathf.Min(issueCount, issues.Length);
            for (int i = 0; i < max; i++)
            {
                RPGFoundationEditorUiUtility.DrawHelpBox(
                    issues[i].Message,
                    ToMessageType(issues[i].Severity));
            }

            if (issueCount > issues.Length)
            {
                RPGFoundationEditorUiUtility.DrawHelpBox("Validation issue buffer is full. Increase the editor issue buffer to see every issue.", MessageType.Warning);
            }
        }

        private void ApplyPreset(
            float maxDistance,
            float radius,
            int reflectionCount,
            int pierceCount,
            int hitCount,
            int iterationCount,
            float surfaceOffset)
        {
            Undo.RecordObjects(targets, "Apply Trajectory Query Preset");
            _maxDistance.floatValue = maxDistance;
            _radius.floatValue = radius;
            _maxReflectionCount.intValue = reflectionCount;
            _maxPierceCount.intValue = pierceCount;
            _maxHitCount.intValue = hitCount;
            _maxIterationCount.intValue = iterationCount;
            _surfaceOffset.floatValue = surfaceOffset;
        }

        private static MessageType ToMessageType(TrajectoryValidationSeverity severity)
        {
            switch (severity)
            {
                case TrajectoryValidationSeverity.Error:
                    return MessageType.Error;
                case TrajectoryValidationSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }

        private static bool HasMixedValues(SerializedProperty property)
        {
            return property == null || property.hasMultipleDifferentValues;
        }
    }
}
