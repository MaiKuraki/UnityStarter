using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(InteractionDetector))]
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

        private SerializedProperty _nearDistance;
        private SerializedProperty _farDistance;
        private SerializedProperty _disableDistance;
        private SerializedProperty _nearIntervalMs;
        private SerializedProperty _farIntervalMs;
        private SerializedProperty _veryFarIntervalMs;
        private SerializedProperty _sleepIntervalMs;
        private SerializedProperty _sleepEnterMs;

        private SerializedProperty _detectionOrigin;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private SerializedProperty _showDebugGUI;
#endif

        private static bool _detectionFoldout = true;
        private static bool _scoringFoldout = true;
        private static bool _lodFoldout = true;
        private static bool _nearbyFoldout = true;
        private static bool _debugFoldout = true;

        private static readonly Color ColorDetectionRange = new(0.2f, 0.8f, 0.4f, 0.6f);
        private static readonly Color ColorNearRange = new(0.4f, 0.9f, 0.4f, 0.4f);
        private static readonly Color ColorFarRange = new(0.9f, 0.7f, 0.3f, 0.3f);
        private static readonly Color ColorVeryFarRange = new(0.9f, 0.4f, 0.3f, 0.2f);
        private static readonly Color ColorCurrentTarget = new(0f, 1f, 0.5f, 1f);
        private static readonly Color ColorCandidate = new(1f, 0.8f, 0.2f, 0.8f);

        // LOD status colors — cached to avoid per-frame Color allocation
        private static readonly Color ColorLODActive = new(0.3f, 0.9f, 0.4f);
        private static readonly Color ColorLODSleeping = new(0.6f, 0.6f, 0.9f);

        // Cached reflection — avoid per-frame GetField allocations
        private static System.Reflection.FieldInfo s_detectionOriginField;
        private static System.Reflection.FieldInfo s_detectionOffsetField;
        private static System.Reflection.FieldInfo s_detectionRadiusField;

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

            _nearDistance = serializedObject.FindProperty("nearDistance");
            _farDistance = serializedObject.FindProperty("farDistance");
            _disableDistance = serializedObject.FindProperty("disableDistance");
            _nearIntervalMs = serializedObject.FindProperty("nearIntervalMs");
            _farIntervalMs = serializedObject.FindProperty("farIntervalMs");
            _veryFarIntervalMs = serializedObject.FindProperty("veryFarIntervalMs");
            _sleepIntervalMs = serializedObject.FindProperty("sleepIntervalMs");
            _sleepEnterMs = serializedObject.FindProperty("sleepEnterMs");

            _detectionOrigin = serializedObject.FindProperty("detectionOrigin");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _showDebugGUI = serializedObject.FindProperty("showDebugGUI");
#endif

            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeader();
            DrawRuntimeStatus();

            EditorGUILayout.Space(8);

            DrawDetectionSettings();
            DrawScoringSettings();
            DrawLODSettings();
            DrawNearbySettings();
            DrawDebugSettings();

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
                Repaint();
        }

        private new void DrawHeader()
        {
            EditorGUILayout.LabelField("Interaction Detector", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Detects nearby Interactable objects using Physics3D, Physics2D, or SpatialHash.\n" +
                "Attach to player character or camera.",
                MessageType.None);
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying) return;

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

                IInteractable current = _target.CurrentInteractable.CurrentValue;
                string targetName = current != null ? ((MonoBehaviour)current).name : "None";

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Current Target:", GUILayout.Width(100));
                GUI.color = current != null ? ColorCurrentTarget : Color.gray;
                EditorGUILayout.LabelField(targetName, EditorStyles.boldLabel);
                GUI.color = Color.white;
                EditorGUILayout.EndHorizontal();

                if (current != null)
                {
                    EditorGUILayout.LabelField($"  Prompt: {current.InteractionPrompt}");
                    EditorGUILayout.LabelField($"  State: {current.CurrentState}");
                    EditorGUILayout.LabelField($"  Priority: {current.Priority}");
                    EditorGUILayout.LabelField($"  Channel: {current.Channel}");
                }

                // Nearby candidates count
                var nearby = _target.NearbyInteractables;
                if (nearby.Count > 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField($"Nearby Candidates: {nearby.Count}", EditorStyles.boldLabel);
                    int displayMax = Mathf.Min(nearby.Count, 8);
                    for (int i = 0; i < displayMax; i++)
                    {
                        var c = nearby[i];
                        string name = c.Interactable is MonoBehaviour mb ? mb.name : "?";
                        bool isCurrent = c.Interactable == current;
                        string marker = isCurrent ? "▶ " : "  ";
                        GUI.color = isCurrent ? ColorCurrentTarget : Color.white;
                        EditorGUILayout.LabelField($"{marker}{name}  (Score: {c.Score:F1}, Dist²: {c.DistanceSqr:F1})");
                        GUI.color = Color.white;
                    }
                    if (nearby.Count > 8)
                        EditorGUILayout.LabelField($"  ... +{nearby.Count - 8} more");
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = current != null && current.IsInteractable;
                if (GUILayout.Button("🎮 Trigger Interact", GUILayout.Height(24)))
                {
                    _target.TryInteract();
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawDetectionSettings()
        {
            _detectionFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_detectionFoldout, "Detection");
            if (_detectionFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(_detectionMode, new GUIContent("Mode", "Physics3D, Physics2D, or SpatialHash"));

                    var mode = (DetectionMode)_detectionMode.enumValueIndex;
                    if (mode == DetectionMode.SpatialHash)
                    {
                        EditorGUILayout.HelpBox(
                            "SpatialHash mode uses the InteractionSystem's spatial grid.\n" +
                            "Best for scenes with thousands of interactables.\n" +
                            "LayerMask / MaxCandidates are not used in this mode.",
                            MessageType.Info);
                    }

                    EditorGUILayout.Space(4);

                    EditorGUILayout.PropertyField(_detectionOrigin, new GUIContent("Origin", "Transform to use as detection center"));
                    EditorGUILayout.PropertyField(_detectionRadius, new GUIContent("Radius", "Maximum detection range"));
                    EditorGUILayout.PropertyField(_detectionOffset, new GUIContent("Offset", "Local offset from origin"));

                    EditorGUILayout.Space(4);

                    EditorGUILayout.PropertyField(_channelMask, new GUIContent("Channel Mask", "Only detect interactables on these channels"));

                    if (mode != DetectionMode.SpatialHash)
                    {
                        EditorGUILayout.PropertyField(_interactableLayer, new GUIContent("Interactable Layer", "LayerMask for interactable objects"));
                        EditorGUILayout.PropertyField(_obstructionLayer, new GUIContent("Obstruction Layer", "LayerMask for line-of-sight blocking"));
                        EditorGUILayout.PropertyField(_maxInteractables, new GUIContent("Max Candidates", "Buffer size for physics queries"));
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawScoringSettings()
        {
            _scoringFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_scoringFoldout, "⚖️ Scoring Weights");
            if (_scoringFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        $"Score = Priority×{_priorityWeight.floatValue:F0} + Angle×AngleWeight - Distance×DistanceWeight\n" +
                        "Higher score = more likely to be selected",
                        MessageType.None);

                    EditorGUILayout.PropertyField(_priorityWeight, new GUIContent("Priority Weight", "Multiplier for Priority in scoring. Higher = Priority dominates."));
                    EditorGUILayout.PropertyField(_distanceWeight, new GUIContent("Distance Weight", "Penalty for farther objects"));
                    EditorGUILayout.PropertyField(_angleWeight, new GUIContent("Angle Weight", "Bonus for objects in front"));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawLODSettings()
        {
            _lodFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_lodFoldout, "📊 LOD & Sleep Mode");
            if (_lodFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "Time-based detection intervals ensure consistent behavior\n" +
                        "across all frame rates (30/60/120 FPS).",
                        MessageType.None);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Distance Thresholds (meters)", EditorStyles.boldLabel);

                    EditorGUILayout.PropertyField(_nearDistance, new GUIContent("Near", "Full update rate within this distance"));
                    EditorGUILayout.PropertyField(_farDistance, new GUIContent("Far", "Reduced update rate within this distance"));
                    EditorGUILayout.PropertyField(_disableDistance, new GUIContent("Disable", "Beyond this distance, target is defocused"));

                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("Update Intervals (milliseconds)", EditorStyles.boldLabel);

                    // Near Interval
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"Near (< {_nearDistance.floatValue:F0}m)", GUILayout.Width(140));
                    EditorGUILayout.PropertyField(_nearIntervalMs, GUIContent.none);
                    EditorGUILayout.EndHorizontal();

                    // Far Interval
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"Far (< {_farDistance.floatValue:F0}m)", GUILayout.Width(140));
                    EditorGUILayout.PropertyField(_farIntervalMs, GUIContent.none);
                    EditorGUILayout.EndHorizontal();

                    // Very Far Interval
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"Very Far (< {_disableDistance.floatValue:F0}m)", GUILayout.Width(140));
                    EditorGUILayout.PropertyField(_veryFarIntervalMs, GUIContent.none);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("Sleep Mode", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "When no interactable is found, detector enters sleep mode\n" +
                        "to reduce CPU usage. Wakes up automatically when target appears.",
                        MessageType.None);

                    // Sleep Interval
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Sleep Interval", GUILayout.Width(140));
                    EditorGUILayout.PropertyField(_sleepIntervalMs, GUIContent.none);
                    EditorGUILayout.EndHorizontal();

                    // Sleep Enter Time
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Sleep Enter Time", GUILayout.Width(140));
                    EditorGUILayout.PropertyField(_sleepEnterMs, GUIContent.none);
                    EditorGUILayout.EndHorizontal();

                    // Calculate and show detection rate info
                    EditorGUILayout.Space(4);
                    float nearHz = _nearIntervalMs.floatValue > 0 ? 1000f / _nearIntervalMs.floatValue : 0;
                    float sleepHz = _sleepIntervalMs.floatValue > 0 ? 1000f / _sleepIntervalMs.floatValue : 0;
                    EditorGUILayout.HelpBox(
                        $"Near: {nearHz:F0} checks/sec | Sleep: {sleepHz:F1} checks/sec",
                        MessageType.None);

                    // Runtime LOD status
                    if (Application.isPlaying)
                    {
                        IInteractable currentTarget = _target.CurrentInteractable.CurrentValue;
                        bool hastarget = currentTarget != null;

                        EditorGUILayout.Space(4);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Status:", GUILayout.Width(60));
                        GUI.color = hastarget ? ColorLODActive : ColorLODSleeping;
                        EditorGUILayout.LabelField(hastarget ? "👁 Active" : "💤 No Target", EditorStyles.boldLabel);
                        GUI.color = Color.white;
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawNearbySettings()
        {
            _nearbyFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_nearbyFoldout, "📋 Nearby List");
            if (_nearbyFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(_maxNearbyCandidates, new GUIContent("Max Candidates", "Maximum number of candidates to track in the nearby list"));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDebugSettings()
        {
            _debugFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_debugFoldout, "🐛 Debug");
            if (_debugFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    EditorGUILayout.PropertyField(_showDebugGUI, new GUIContent("Show Runtime GUI", "Display debug overlay in game view"));
#endif
                    DetectorGizmoSettings.ShowDetectionRange = EditorGUILayout.Toggle("Show Detection Range", DetectorGizmoSettings.ShowDetectionRange);
                    DetectorGizmoSettings.ShowLODRanges = EditorGUILayout.Toggle("Show LOD Ranges", DetectorGizmoSettings.ShowLODRanges);
                    DetectorGizmoSettings.ShowCandidateLines = EditorGUILayout.Toggle("Show Candidate Lines", DetectorGizmoSettings.ShowCandidateLines);
                    DetectorGizmoSettings.ShowForwardCone = EditorGUILayout.Toggle("Show Forward Cone", DetectorGizmoSettings.ShowForwardCone);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_target == null) return;

            EnsureReflectionCached();
            Transform origin = _target.GetComponent<Transform>();
            if (s_detectionOriginField?.GetValue(_target) is Transform customOrigin && customOrigin != null)
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