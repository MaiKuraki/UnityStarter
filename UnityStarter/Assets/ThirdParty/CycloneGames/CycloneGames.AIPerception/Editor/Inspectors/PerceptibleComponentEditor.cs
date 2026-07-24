using CycloneGames.AIPerception.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AIPerception.Editor
{
    [CustomEditor(typeof(PerceptibleComponent), true)]
    [CanEditMultipleObjects]
    public class PerceptibleComponentEditor : UnityEditor.Editor
    {
        private const string AuthoringLockedMessage =
            "Perceptible authoring is locked in Play Mode because registration data is captured by the runtime registry.";

        private static readonly Color HeaderColor = new Color(0.18f, 0.66f, 0.48f, 1f);
        private static readonly Color IdentityColor = new Color(0.34f, 0.62f, 0.82f, 1f);
        private static readonly Color DetectionColor = new Color(0.84f, 0.61f, 0.18f, 1f);
        private static readonly Color SoundColor = new Color(0.3f, 0.68f, 0.9f, 1f);
        private static readonly Color SceneDebugColor = new Color(0.16f, 0.58f, 0.66f, 1f);
        private static readonly Color DebugColor = new Color(0.34f, 0.72f, 0.42f, 1f);
        private static readonly Color AdditionalColor = new Color(0.58f, 0.42f, 0.74f, 1f);

        private static readonly GUIContent HeaderTitle = new GUIContent("Perceptible");
        private static readonly GUIContent IdentityTitle = new GUIContent("Identity");
        private static readonly GUIContent DetectionTitle = new GUIContent("Detection Geometry");
        private static readonly GUIContent SoundTitle = new GUIContent("Hearing Source");
        private static readonly GUIContent SceneDebugTitle = new GUIContent("Scene Diagnostics");
        private static readonly GUIContent RuntimeTitle = new GUIContent("Runtime Diagnostics");
        private static readonly GUIContent AdditionalFieldsTitle = new GUIContent("Additional Fields");
        private static readonly GUIContent TypeIdLabel = new GUIContent(
            "Type ID",
            "Stable numeric identity used by sensor filters. Keep IDs stable across content and save data.");
        private static readonly GUIContent TypeNameLabel = new GUIContent("Resolved Type");
        private static readonly GUIContent TagLabel = new GUIContent("Tag");
        private static readonly GUIContent DetectionRadiusLabel = new GUIContent("Detection Radius");
        private static readonly GUIContent LosPointLabel = new GUIContent("LOS Point (Optional)");
        private static readonly GUIContent LoudnessLabel = new GUIContent("Loudness");
        private static readonly GUIContent SelectedComponentsLabel = new GUIContent("Selected Components");
        private static readonly GUIContent ValidHandlesLabel = new GUIContent("Valid Handles");
        private static readonly GUIContent DetectableComponentsLabel = new GUIContent("Detectable Components");
        private static readonly GUIContent SoundSourcesLabel = new GUIContent("Sound Sources");
        private static readonly GUIContent RuntimeIdLabel = new GUIContent("Runtime ID");
        private static readonly GUIContent RuntimePositionLabel = new GUIContent("Position");
        private static readonly GUIContent PinSceneGizmosLabel = new GUIContent(
            "Pin Scene Gizmos",
            "Keep this perceptible's detection volume and LOS marker visible when it is not selected.");

        private SerializedProperty _typeId;
        private SerializedProperty _tag;
        private SerializedProperty _detectionRadius;
        private SerializedProperty _isDetectable;
        private SerializedProperty _losPoint;
        private SerializedProperty _loudness;
        private SerializedProperty _isSoundSource;
        private SerializedProperty _showDebugOverlay;
        private SerializedProperty[] _remainingProperties;
        private bool _propertiesValid;

        private bool _showIdentity = true;
        private bool _showDetection = true;
        private bool _showSound = true;
        private bool _showSceneDiagnostics = true;
        private bool _showRuntimeDiagnostics = true;
        private bool _showAdditionalFields = true;

        private int _cachedTypeId = int.MinValue;
        private readonly GUIContent _cachedTypeName = new GUIContent();

        private double _nextRuntimeRepaintTime;
        private bool _runtimeSnapshotDirty = true;
        private int _runtimeComponentCount;
        private int _runtimeValidHandleCount;
        private int _runtimeDetectableCount;
        private int _runtimeSoundSourceCount;
        private int _runtimeId;
        private Vector3 _runtimePosition;

        protected virtual void OnEnable()
        {
            _typeId = serializedObject.FindProperty("_typeId");
            _tag = serializedObject.FindProperty("_tag");
            _detectionRadius = serializedObject.FindProperty("_detectionRadius");
            _isDetectable = serializedObject.FindProperty("_isDetectable");
            _losPoint = serializedObject.FindProperty("_losPoint");
            _loudness = serializedObject.FindProperty("_loudness");
            _isSoundSource = serializedObject.FindProperty("_isSoundSource");
            _showDebugOverlay = serializedObject.FindProperty("_showDebugOverlay");

            _propertiesValid = InspectorUiUtility.AreAssigned(
                _typeId,
                _tag,
                _detectionRadius,
                _isDetectable,
                _losPoint,
                _loudness,
                _isSoundSource,
                _showDebugOverlay);

            if (_propertiesValid)
            {
                _remainingProperties = InspectorUiUtility.CacheRemainingProperties(
                    serializedObject,
                    _typeId,
                    _tag,
                    _detectionRadius,
                    _isDetectable,
                    _losPoint,
                    _loudness,
                    _isSoundSource,
                    _showDebugOverlay);
            }

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        protected virtual void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InspectorUiUtility.DrawInspectorTitle(
                HeaderTitle.text,
                "Stable target identity and detection geometry",
                HeaderColor);

            if (!_propertiesValid)
            {
                EditorGUILayout.HelpBox(
                    "The custom inspector could not resolve the expected serialized fields. The default inspector is shown to prevent hidden data.",
                    MessageType.Error);
                DrawDefaultInspector();
                return;
            }

            bool authoringLocked = Application.isPlaying;
            if (authoringLocked)
            {
                InspectorUiUtility.DrawAuthoringLockedHelpBox(AuthoringLockedMessage);
            }

            DrawSceneDiagnostics();

            using (new EditorGUI.DisabledScope(authoringLocked))
            {
                DrawIdentitySection();
                DrawDetectionSection();
                DrawSoundSection();
                InspectorUiUtility.DrawRemainingProperties(
                    _remainingProperties,
                    ref _showAdditionalFields,
                    AdditionalFieldsTitle,
                    AdditionalColor);
            }

            if (Application.isPlaying)
            {
                DrawRuntimeDiagnostics();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawIdentitySection()
        {
            string badge = _typeId.hasMultipleDifferentValues ? "MIXED" : "TYPE " + _typeId.intValue;
            InspectorUiUtility.DrawSectionHeader(
                ref _showIdentity,
                IdentityTitle,
                IdentityColor,
                badge: badge,
                badgeColor: InspectorUiUtility.NeutralColor);
            if (!_showIdentity)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_typeId, TypeIdLabel);
                DrawResolvedTypeName();
                EditorGUILayout.PropertyField(_tag, TagLabel);
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawDetectionSection()
        {
            InspectorUiUtility.DrawSectionHeader(
                ref _showDetection,
                DetectionTitle,
                DetectionColor,
                _isDetectable);
            if (!_showDetection)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(!InspectorUiUtility.IsEnabledOrMixed(_isDetectable)))
            {
                EditorGUILayout.PropertyField(_detectionRadius, DetectionRadiusLabel);
                EditorGUILayout.PropertyField(_losPoint, LosPointLabel);

                if (!_losPoint.hasMultipleDifferentValues && _losPoint.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "No LOS point is assigned. Line-of-sight checks use the component transform position.",
                        MessageType.Info);
                }
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawSoundSection()
        {
            InspectorUiUtility.DrawSectionHeader(ref _showSound, SoundTitle, SoundColor, _isSoundSource);
            if (!_showSound)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(!InspectorUiUtility.IsEnabledOrMixed(_isSoundSource)))
            {
                EditorGUILayout.PropertyField(_loudness, LoudnessLabel);
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawSceneDiagnostics()
        {
            string badge;
            Color badgeColor;
            if (AIPerceptionEditorUtility.GlobalShowGizmos)
            {
                badge = "SHOWING ALL";
                badgeColor = InspectorUiUtility.SuccessColor;
            }
            else if (_showDebugOverlay.hasMultipleDifferentValues)
            {
                badge = "MIXED";
                badgeColor = InspectorUiUtility.NeutralColor;
            }
            else if (_showDebugOverlay.boolValue)
            {
                badge = "PINNED";
                badgeColor = InspectorUiUtility.SuccessColor;
            }
            else
            {
                badge = "SELECTED";
                badgeColor = InspectorUiUtility.NeutralColor;
            }

            InspectorUiUtility.DrawSectionHeader(
                ref _showSceneDiagnostics,
                SceneDebugTitle,
                SceneDebugColor,
                badge: badge,
                badgeColor: badgeColor);
            if (!_showSceneDiagnostics)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            EditorGUILayout.PropertyField(_showDebugOverlay, PinSceneGizmosLabel);
            EditorGUI.BeginChangeCheck();
            bool filledVolumes = EditorGUILayout.ToggleLeft(
                "Filled Debug Volumes (Session)",
                AIPerceptionEditorUtility.FilledVolumes);
            if (EditorGUI.EndChangeCheck())
            {
                AIPerceptionEditorUtility.FilledVolumes = filledVolumes;
            }

            string buttonLabel = AIPerceptionEditorUtility.GlobalShowGizmos
                ? "Return to Selected and Pinned"
                : "Show All Perception Gizmos (Session)";
            if (GUILayout.Button(buttonLabel))
            {
                AIPerceptionEditorUtility.GlobalShowGizmos = !AIPerceptionEditorUtility.GlobalShowGizmos;
            }

            EditorGUILayout.HelpBox(
                "Selected perceptibles are always previewed. Pin this component to keep it visible, or use Tools > CycloneGames > AI Perception > Scene Gizmos to pin a mixed selection. Sound sources receive an orange marker.",
                MessageType.None);
            InspectorUiUtility.EndPanel();
        }

        private void DrawResolvedTypeName()
        {
            if (_typeId.hasMultipleDifferentValues)
            {
                return;
            }

            int typeId = _typeId.intValue;
            if (_cachedTypeId != typeId)
            {
                _cachedTypeId = typeId;
                _cachedTypeName.text = PerceptibleTypes.GetTypeName(typeId);
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField(TypeNameLabel, _cachedTypeName);
            }
        }

        private void DrawRuntimeDiagnostics()
        {
            if (_runtimeSnapshotDirty)
            {
                RefreshRuntimeSnapshot();
            }

            bool allHandlesValid = _runtimeValidHandleCount == _runtimeComponentCount;
            InspectorUiUtility.DrawSectionHeader(
                ref _showRuntimeDiagnostics,
                RuntimeTitle,
                DebugColor,
                badge: allHandlesValid ? "REGISTERED" : "ATTENTION",
                badgeColor: allHandlesValid
                    ? InspectorUiUtility.SuccessColor
                    : InspectorUiUtility.WarningColor);
            if (!_showRuntimeDiagnostics)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            InspectorUiUtility.DrawStatusRow(
                SelectedComponentsLabel.text,
                _runtimeComponentCount.ToString(),
                InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow(
                ValidHandlesLabel.text,
                _runtimeValidHandleCount.ToString(),
                allHandlesValid ? InspectorUiUtility.SuccessColor : InspectorUiUtility.WarningColor);
            InspectorUiUtility.DrawStatusRow(
                DetectableComponentsLabel.text,
                _runtimeDetectableCount.ToString(),
                DetectionColor);
            InspectorUiUtility.DrawStatusRow(
                SoundSourcesLabel.text,
                _runtimeSoundSourceCount.ToString(),
                SoundColor);

            if (_runtimeComponentCount == 1)
            {
                InspectorUiUtility.DrawSubsectionLabel("Selected Target");
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField(RuntimeIdLabel, _runtimeId);
                    EditorGUILayout.Vector3Field(RuntimePositionLabel.text, _runtimePosition);
                }
            }
            InspectorUiUtility.EndPanel();
        }

        private void RefreshRuntimeSnapshot()
        {
            _runtimeComponentCount = 0;
            _runtimeValidHandleCount = 0;
            _runtimeDetectableCount = 0;
            _runtimeSoundSourceCount = 0;
            _runtimeId = 0;
            _runtimePosition = default;

            Object[] selectedTargets = targets;
            for (int i = 0; i < selectedTargets.Length; i++)
            {
                var perceptible = selectedTargets[i] as PerceptibleComponent;
                if (perceptible == null)
                {
                    continue;
                }

                _runtimeComponentCount++;
                if (perceptible.Handle.IsValid)
                {
                    _runtimeValidHandleCount++;
                }

                if (perceptible.IsDetectable)
                {
                    _runtimeDetectableCount++;
                }

                if (perceptible.IsSoundSource)
                {
                    _runtimeSoundSourceCount++;
                }

                if (selectedTargets.Length == 1)
                {
                    _runtimeId = perceptible.PerceptibleId;
                    _runtimePosition = perceptible.transform.position;
                }
            }

            _runtimeSnapshotDirty = false;
        }

        private void OnEditorUpdate()
        {
            double previousRefreshTime = _nextRuntimeRepaintTime;
            InspectorUiUtility.RequestRuntimeRepaint(
                this,
                _showRuntimeDiagnostics,
                ref _nextRuntimeRepaintTime);
            if (_nextRuntimeRepaintTime != previousRefreshTime)
            {
                _runtimeSnapshotDirty = true;
            }
        }
    }
}
