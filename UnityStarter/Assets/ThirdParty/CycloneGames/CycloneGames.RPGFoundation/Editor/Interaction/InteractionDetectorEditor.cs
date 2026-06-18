using UnityEditor;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(InteractionDetector), true)]
    public class InteractionDetectorEditor : UnityEditor.Editor
    {
        private InteractionDetector _target;

        private SerializedProperty _detectionMode;
        private SerializedProperty _detectionRadius;
        private SerializedProperty _interactableLayer;
        private SerializedProperty _obstructionLayer;
        private SerializedProperty _detectionOffset;
        private SerializedProperty _maxInteractables;
        private SerializedProperty _channelMask;
        private SerializedProperty _distanceWeight;
        private SerializedProperty _angleWeight;
        private SerializedProperty _priorityWeight;
        private SerializedProperty _maxNearbyCandidates;
        private SerializedProperty _autoInteractMinIntervalMs;
        private SerializedProperty _nearDistance;
        private SerializedProperty _farDistance;
        private SerializedProperty _disableDistance;
        private SerializedProperty _nearIntervalMs;
        private SerializedProperty _farIntervalMs;
        private SerializedProperty _veryFarIntervalMs;
        private SerializedProperty _sleepIntervalMs;
        private SerializedProperty _sleepEnterMs;
        private SerializedProperty _maxLosChecksPerFrame;
        private SerializedProperty _blockWhenLosBudgetExceeded;
        private SerializedProperty _useLosSpatialCache;
        private SerializedProperty _interactionSystem;
        private SerializedProperty _detectionOrigin;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private SerializedProperty _showDebugGUI;
#endif

        private static bool s_runtimeFoldout = true;
        private static bool s_detectionFoldout = true;
        private static bool s_scoringFoldout = true;
        private static bool s_losFoldout = true;
        private static bool s_lodFoldout = true;
        private static bool s_nearbyFoldout = true;
        private static bool s_debugFoldout;

        private static readonly Color ColorDetectionRange = new(0.2f, 0.8f, 0.4f, 0.6f);
        private static readonly Color ColorNearRange = new(0.4f, 0.9f, 0.4f, 0.4f);
        private static readonly Color ColorFarRange = new(0.9f, 0.7f, 0.3f, 0.3f);
        private static readonly Color ColorCurrentTarget = new(0f, 1f, 0.5f, 1f);
        private static readonly Color ColorIdle = new(0.5f, 0.5f, 0.5f, 1f);

        private static System.Reflection.FieldInfo s_detectionOriginField;
        private static System.Reflection.FieldInfo s_detectionOffsetField;
        private static System.Reflection.FieldInfo s_detectionRadiusField;

        private static readonly string[] ExcludedProperties =
        {
            "m_Script",
            "detectionMode",
            "detectionRadius",
            "interactableLayer",
            "obstructionLayer",
            "detectionOffset",
            "maxInteractables",
            "channelMask",
            "distanceWeight",
            "angleWeight",
            "priorityWeight",
            "maxNearbyCandidates",
            "autoInteractMinIntervalMs",
            "nearDistance",
            "farDistance",
            "disableDistance",
            "nearIntervalMs",
            "farIntervalMs",
            "veryFarIntervalMs",
            "sleepIntervalMs",
            "sleepEnterMs",
            "maxLosChecksPerFrame",
            "blockWhenLosBudgetExceeded",
            "useLosSpatialCache",
            "interactionSystem",
            "detectionOrigin",
            "showDebugGUI"
        };

        private static void EnsureReflectionCached()
        {
            if (s_detectionOriginField != null) return;
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            s_detectionOriginField = typeof(InteractionDetector).GetField("detectionOrigin", flags);
            s_detectionOffsetField = typeof(InteractionDetector).GetField("detectionOffset", flags);
            s_detectionRadiusField = typeof(InteractionDetector).GetField("detectionRadius", flags);
        }

        private void OnEnable()
        {
            _target = (InteractionDetector)target;

            _detectionMode = serializedObject.FindProperty("detectionMode");
            _detectionRadius = serializedObject.FindProperty("detectionRadius");
            _interactableLayer = serializedObject.FindProperty("interactableLayer");
            _obstructionLayer = serializedObject.FindProperty("obstructionLayer");
            _detectionOffset = serializedObject.FindProperty("detectionOffset");
            _maxInteractables = serializedObject.FindProperty("maxInteractables");
            _channelMask = serializedObject.FindProperty("channelMask");
            _distanceWeight = serializedObject.FindProperty("distanceWeight");
            _angleWeight = serializedObject.FindProperty("angleWeight");
            _priorityWeight = serializedObject.FindProperty("priorityWeight");
            _maxNearbyCandidates = serializedObject.FindProperty("maxNearbyCandidates");
            _autoInteractMinIntervalMs = serializedObject.FindProperty("autoInteractMinIntervalMs");
            _nearDistance = serializedObject.FindProperty("nearDistance");
            _farDistance = serializedObject.FindProperty("farDistance");
            _disableDistance = serializedObject.FindProperty("disableDistance");
            _nearIntervalMs = serializedObject.FindProperty("nearIntervalMs");
            _farIntervalMs = serializedObject.FindProperty("farIntervalMs");
            _veryFarIntervalMs = serializedObject.FindProperty("veryFarIntervalMs");
            _sleepIntervalMs = serializedObject.FindProperty("sleepIntervalMs");
            _sleepEnterMs = serializedObject.FindProperty("sleepEnterMs");
            _maxLosChecksPerFrame = serializedObject.FindProperty("maxLosChecksPerFrame");
            _blockWhenLosBudgetExceeded = serializedObject.FindProperty("blockWhenLosBudgetExceeded");
            _useLosSpatialCache = serializedObject.FindProperty("useLosSpatialCache");
            _interactionSystem = serializedObject.FindProperty("interactionSystem");
            _detectionOrigin = serializedObject.FindProperty("detectionOrigin");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _showDebugGUI = serializedObject.FindProperty("showDebugGUI");
#endif

            SceneView.duringSceneGui += HandleSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= HandleSceneGUI;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeader();
            InteractionComponentRules.DrawIssuesFor(targets);
            DrawRuntimeStatus();
            DrawDetectionSettings();
            DrawScoringSettings();
            DrawLosSettings();
            DrawLodSettings();
            DrawNearbySettings();
            DrawDebugSettings();

            InteractionInspectorUiUtility.DrawDerivedProperties(
                serializedObject,
                target.GetType().Name + " Settings",
                ExcludedProperties);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
                Repaint();
        }

        private new void DrawHeader()
        {
            EditorGUILayout.LabelField("Interaction Detector", EditorStyles.boldLabel);
            InteractionInspectorUiUtility.DrawHelpBox(
                "Scans nearby interactables, scores candidates, tracks the active target, and exposes candidate data for UI.",
                MessageType.None);
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying)
                return;

            s_runtimeFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Runtime Status",
                s_runtimeFoldout,
                InteractionInspectorUiUtility.ColorRuntime);
            if (!s_runtimeFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                IInteractable current = _target.CurrentInteractable.CurrentValue;
                string targetName = current is MonoBehaviour targetBehaviour ? targetBehaviour.name : "None";

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Current Target", GUILayout.Width(110f));
                Rect badgeRect = EditorGUILayout.GetControlRect(false, 18f, GUILayout.Width(160f));
                InteractionInspectorUiUtility.DrawStatusBadge(
                    badgeRect,
                    targetName,
                    current != null ? ColorCurrentTarget : ColorIdle);
                EditorGUILayout.EndHorizontal();

                if (current != null)
                {
                    EditorGUILayout.LabelField("Prompt", current.InteractionPrompt);
                    EditorGUILayout.LabelField("State", current.CurrentState.ToString());
                    EditorGUILayout.LabelField("Priority", current.Priority.ToString());
                    EditorGUILayout.LabelField("Channel", current.Channel.ToString());
                }

                var nearby = _target.NearbyInteractables;
                if (nearby.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Nearby Candidates: " + nearby.Count, EditorStyles.boldLabel);
                    int displayMax = Mathf.Min(nearby.Count, 8);
                    for (int i = 0; i < displayMax; i++)
                    {
                        var candidate = nearby[i];
                        string name = candidate.Interactable is MonoBehaviour mb ? mb.name : "Unknown";
                        bool isCurrent = candidate.Interactable == current;
                        GUI.color = isCurrent ? ColorCurrentTarget : Color.white;
                        EditorGUILayout.LabelField(name + "  Score: " + candidate.Score.ToString("F1") + "  DistSqr: " + candidate.DistanceSqr.ToString("F1"));
                        GUI.color = Color.white;
                    }
                }

                GUI.enabled = current != null && current.IsInteractable;
                if (GUILayout.Button("Trigger Interact"))
                    _target.TryInteract();
                GUI.enabled = true;
            }
        }

        private void DrawDetectionSettings()
        {
            s_detectionFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Detection",
                s_detectionFoldout,
                InteractionInspectorUiUtility.ColorCore);
            if (!s_detectionFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_detectionMode, new GUIContent("Mode"));
                DetectionMode mode = (DetectionMode)_detectionMode.enumValueIndex;
                if (mode == DetectionMode.SpatialHash)
                {
                    InteractionInspectorUiUtility.DrawHelpBox(
                        "SpatialHash uses InteractionSystem.SpatialGrid and does not require colliders. Moving interactables must call NotifyPositionChanged when their registered position changes enough.",
                        MessageType.Info);
                }

                EditorGUILayout.PropertyField(_interactionSystem, new GUIContent("Interaction System"));
                EditorGUILayout.PropertyField(_detectionOrigin, new GUIContent("Origin"));
                EditorGUILayout.PropertyField(_detectionRadius, new GUIContent("Radius"));
                EditorGUILayout.PropertyField(_detectionOffset, new GUIContent("Offset"));
                EditorGUILayout.PropertyField(_channelMask, new GUIContent("Channel Mask"));

                if (mode != DetectionMode.SpatialHash)
                {
                    EditorGUILayout.PropertyField(_interactableLayer, new GUIContent("Interactable Layer"));
                    EditorGUILayout.PropertyField(_obstructionLayer, new GUIContent("Obstruction Layer"));
                    EditorGUILayout.PropertyField(_maxInteractables, new GUIContent("Max Candidates"));
                }
            }
        }

        private void DrawScoringSettings()
        {
            s_scoringFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Scoring",
                s_scoringFoldout,
                InteractionInspectorUiUtility.ColorBehavior);
            if (!s_scoringFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                InteractionInspectorUiUtility.DrawHelpBox(
                    "Score = Priority * PriorityWeight + FacingDot * AngleWeight - NormalizedDistance * DistanceWeight. Higher scores are selected first.",
                    MessageType.None);
                EditorGUILayout.PropertyField(_priorityWeight, new GUIContent("Priority Weight"));
                EditorGUILayout.PropertyField(_distanceWeight, new GUIContent("Distance Weight"));
                EditorGUILayout.PropertyField(_angleWeight, new GUIContent("Angle Weight"));
            }
        }

        private void DrawLosSettings()
        {
            s_losFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Line Of Sight",
                s_losFoldout,
                InteractionInspectorUiUtility.ColorBehavior);
            if (!s_losFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                InteractionInspectorUiUtility.DrawHelpBox(
                    "LOS checks use non-alloc raycasts. Limiting raycasts per detection cycle protects physics-heavy scenes; stationary result caching reduces repeated checks.",
                    MessageType.None);
                EditorGUILayout.PropertyField(_maxLosChecksPerFrame, new GUIContent("Max Raycasts Per Cycle"));
                EditorGUILayout.PropertyField(_blockWhenLosBudgetExceeded, new GUIContent("Block When Budget Exhausted"));
                EditorGUILayout.PropertyField(_useLosSpatialCache, new GUIContent("Cache Stationary Results"));
            }
        }

        private void DrawLodSettings()
        {
            s_lodFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "LOD And Sleep",
                s_lodFoldout,
                InteractionInspectorUiUtility.ColorRuntime);
            if (!s_lodFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Distance Thresholds", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_nearDistance, new GUIContent("Near"));
                EditorGUILayout.PropertyField(_farDistance, new GUIContent("Far"));
                EditorGUILayout.PropertyField(_disableDistance, new GUIContent("Disable"));

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Intervals In Milliseconds", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_nearIntervalMs, new GUIContent("Near Interval"));
                EditorGUILayout.PropertyField(_farIntervalMs, new GUIContent("Far Interval"));
                EditorGUILayout.PropertyField(_veryFarIntervalMs, new GUIContent("Very Far Interval"));
                EditorGUILayout.PropertyField(_sleepIntervalMs, new GUIContent("Sleep Interval"));
                EditorGUILayout.PropertyField(_sleepEnterMs, new GUIContent("Sleep Enter Time"));
            }
        }

        private void DrawNearbySettings()
        {
            s_nearbyFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Nearby List",
                s_nearbyFoldout,
                InteractionInspectorUiUtility.ColorCore);
            if (!s_nearbyFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_maxNearbyCandidates, new GUIContent("Max Nearby Candidates"));
                EditorGUILayout.PropertyField(_autoInteractMinIntervalMs, new GUIContent("Auto Interact Min Interval"));
                InteractionInspectorUiUtility.DrawHelpBox(
                    "This list is a detector-owned buffer sorted by score. UI should read it during the change event or current frame and should not store the reference long-term.",
                    MessageType.None);
            }
        }

        private void DrawDebugSettings()
        {
            s_debugFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Debug",
                s_debugFoldout,
                InteractionInspectorUiUtility.ColorDebug);
            if (!s_debugFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                EditorGUILayout.PropertyField(_showDebugGUI, new GUIContent("Show Runtime GUI"));
#endif
                DetectorGizmoSettings.ShowDetectionRange = EditorGUILayout.Toggle("Show Detection Range", DetectorGizmoSettings.ShowDetectionRange);
                DetectorGizmoSettings.ShowLODRanges = EditorGUILayout.Toggle("Show LOD Ranges", DetectorGizmoSettings.ShowLODRanges);
                DetectorGizmoSettings.ShowCandidateLines = EditorGUILayout.Toggle("Show Candidate Lines", DetectorGizmoSettings.ShowCandidateLines);
                DetectorGizmoSettings.ShowForwardCone = EditorGUILayout.Toggle("Show Forward Cone", DetectorGizmoSettings.ShowForwardCone);
            }
        }

        private void HandleSceneGUI(SceneView sceneView)
        {
            if (_target == null) return;

            Transform origin = _target.transform;
            if (_detectionOrigin.objectReferenceValue is Transform customOrigin && customOrigin != null)
                origin = customOrigin;

            Vector3 offset = _detectionOffset.vector3Value;
            Vector3 center = origin.position + origin.TransformDirection(offset);

            if (DetectorGizmoSettings.ShowDetectionRange)
            {
                Handles.color = ColorDetectionRange;
                Handles.DrawWireDisc(center, Vector3.up, _detectionRadius.floatValue);
                Handles.DrawWireDisc(center, Vector3.forward, _detectionRadius.floatValue);
                Handles.DrawWireDisc(center, Vector3.right, _detectionRadius.floatValue);

                Handles.color = new Color(ColorDetectionRange.r, ColorDetectionRange.g, ColorDetectionRange.b, 0.05f);
                Handles.DrawSolidDisc(center, Vector3.up, _detectionRadius.floatValue);
            }

            if (DetectorGizmoSettings.ShowLODRanges)
            {
                Handles.color = ColorNearRange;
                Handles.DrawWireDisc(center, Vector3.up, _nearDistance.floatValue);
                Handles.Label(center + Vector3.right * _nearDistance.floatValue, "Near", EditorStyles.miniLabel);

                Handles.color = ColorFarRange;
                Handles.DrawWireDisc(center, Vector3.up, _farDistance.floatValue);
                Handles.Label(center + Vector3.right * _farDistance.floatValue, "Far", EditorStyles.miniLabel);
            }

            if (DetectorGizmoSettings.ShowForwardCone)
            {
                Handles.color = new Color(0.3f, 0.7f, 1f, 0.3f);
                Vector3 forward = origin.forward * _detectionRadius.floatValue;
                Vector3 right = origin.right * _detectionRadius.floatValue * 0.5f;
                Handles.DrawLine(center, center + forward + right);
                Handles.DrawLine(center, center + forward - right);
                Handles.DrawLine(center + forward + right, center + forward - right);
            }

            if (Application.isPlaying && DetectorGizmoSettings.ShowCandidateLines)
            {
                IInteractable current = _target.CurrentInteractable.CurrentValue;
                if (current != null)
                {
                    Handles.color = ColorCurrentTarget;
                    Handles.DrawLine(center, current.Position);
                    Handles.DrawWireDisc(current.Position, Vector3.up, 0.3f);
                }
            }

            EditorGUI.BeginChangeCheck();
            float newRadius = Handles.RadiusHandle(Quaternion.identity, center, _detectionRadius.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "Change Detection Radius");
                _detectionRadius.floatValue = newRadius;
                serializedObject.ApplyModifiedProperties();
            }
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawDetectorGizmos(InteractionDetector detector, GizmoType gizmoType)
        {
            if (!DetectorGizmoSettings.ShowDetectionRange) return;

            EnsureReflectionCached();
            Transform origin = detector.transform;
            if (s_detectionOriginField?.GetValue(detector) is Transform customOrigin && customOrigin != null)
                origin = customOrigin;

            Vector3 offset = s_detectionOffsetField != null ? (Vector3)s_detectionOffsetField.GetValue(detector) : Vector3.zero;
            float radius = s_detectionRadiusField != null ? (float)s_detectionRadiusField.GetValue(detector) : 3f;

            Vector3 center = origin.position + origin.TransformDirection(offset);
            bool isSelected = (gizmoType & GizmoType.Selected) != 0;
            Color color = ColorDetectionRange;
            color.a = isSelected ? 0.6f : 0.2f;

            Gizmos.color = color;
            Gizmos.DrawWireSphere(center, radius);
        }
    }

    public static class DetectorGizmoSettings
    {
        private const string Prefix = "InteractionDetector_";

        public static bool ShowDetectionRange
        {
            get => EditorPrefs.GetBool(Prefix + "ShowDetectionRange", true);
            set => EditorPrefs.SetBool(Prefix + "ShowDetectionRange", value);
        }

        public static bool ShowLODRanges
        {
            get => EditorPrefs.GetBool(Prefix + "ShowLODRanges", true);
            set => EditorPrefs.SetBool(Prefix + "ShowLODRanges", value);
        }

        public static bool ShowCandidateLines
        {
            get => EditorPrefs.GetBool(Prefix + "ShowCandidateLines", true);
            set => EditorPrefs.SetBool(Prefix + "ShowCandidateLines", value);
        }

        public static bool ShowForwardCone
        {
            get => EditorPrefs.GetBool(Prefix + "ShowForwardCone", false);
            set => EditorPrefs.SetBool(Prefix + "ShowForwardCone", value);
        }
    }
}
