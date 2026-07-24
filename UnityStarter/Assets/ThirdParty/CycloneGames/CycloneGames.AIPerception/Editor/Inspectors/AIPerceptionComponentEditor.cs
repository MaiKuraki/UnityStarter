using CycloneGames.AIPerception.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AIPerception.Editor
{
    [CustomEditor(typeof(AIPerceptionComponent), true)]
    [CanEditMultipleObjects]
    public class AIPerceptionComponentEditor : UnityEditor.Editor
    {
        private const string AuthoringLockedMessage =
            "Sensor authoring is locked in Play Mode because runtime sensor instances capture these values during initialization.";

        private static readonly Color HeaderColor = new Color(0.18f, 0.5f, 0.78f, 1f);
        private static readonly Color SightColor = new Color(0.92f, 0.69f, 0.12f, 1f);
        private static readonly Color HearingColor = new Color(0.26f, 0.66f, 0.9f, 1f);
        private static readonly Color ProximityColor = new Color(0.92f, 0.4f, 0.16f, 1f);
        private static readonly Color SceneDebugColor = new Color(0.16f, 0.58f, 0.66f, 1f);
        private static readonly Color DebugColor = new Color(0.3f, 0.7f, 0.4f, 1f);
        private static readonly Color AdditionalColor = new Color(0.58f, 0.42f, 0.74f, 1f);

        private static readonly GUIContent HeaderTitle = new GUIContent("AI Perception");
        private static readonly GUIContent SightTitle = new GUIContent("Sight Sensor");
        private static readonly GUIContent HearingTitle = new GUIContent("Hearing Sensor");
        private static readonly GUIContent ProximityTitle = new GUIContent("Proximity Sensor");
        private static readonly GUIContent SceneDebugTitle = new GUIContent("Scene Diagnostics");
        private static readonly GUIContent RuntimeTitle = new GUIContent("Runtime Diagnostics");
        private static readonly GUIContent AdditionalFieldsTitle = new GUIContent("Additional Fields");
        private static readonly GUIContent HalfAngleLabel = new GUIContent("Half Angle (degrees)");
        private static readonly GUIContent MaxDistanceLabel = new GUIContent("Max Distance");
        private static readonly GUIContent UpdateIntervalLabel = new GUIContent("Update Interval");
        private static readonly GUIContent ObstacleLayerLabel = new GUIContent("Obstacle Layer");
        private static readonly GUIContent UseLineOfSightLabel = new GUIContent("Use Line of Sight");
        private static readonly GUIContent FilterByTypeLabel = new GUIContent("Filter by Type");
        private static readonly GUIContent TargetTypeIdLabel = new GUIContent("Target Type ID");
        private static readonly GUIContent RadiusLabel = new GUIContent("Detection Radius");
        private static readonly GUIContent UseOcclusionLabel = new GUIContent("Enable Occlusion");
        private static readonly GUIContent OcclusionLayerLabel = new GUIContent("Occlusion Layer");
        private static readonly GUIContent OcclusionAttenuationLabel = new GUIContent("Wall Attenuation");
        private static readonly GUIContent MaximumOcclusionChecksLabel = new GUIContent(
            "Maximum Occlusion Checks (0 = Unlimited)",
            "Maximum Physics occlusion checks performed by one hearing sensor update. Zero checks every filtered candidate without a per-update limit.");
        private static readonly GUIContent MemoryDurationLabel = new GUIContent(
            "Memory Duration",
            "Seconds to retain a stimulus after direct detection ends. Zero disables memory.");
        private static readonly GUIContent MaximumLosChecksLabel = new GUIContent(
            "Maximum LOS Checks",
            "Maximum Physics line-of-sight checks performed by this sensor update.");
        private static readonly GUIContent CapacityLabel = new GUIContent(
            "Capacity Limits",
            "Preallocated and maximum candidate, result, and memory capacities for this sensor.");
        private static readonly GUIContent SelectedComponentsLabel = new GUIContent("Selected Components");
        private static readonly GUIContent InitializedComponentsLabel = new GUIContent("Initialized Components");
        private static readonly GUIContent SightDetectionsLabel = new GUIContent("Sight Detections");
        private static readonly GUIContent HearingDetectionsLabel = new GUIContent("Hearing Detections");
        private static readonly GUIContent ProximityDetectionsLabel = new GUIContent("Proximity Detections");
        private static readonly GUIContent SightStatusLabel = new GUIContent("Sight Status");
        private static readonly GUIContent HearingStatusLabel = new GUIContent("Hearing Status");
        private static readonly GUIContent ProximityStatusLabel = new GUIContent("Proximity Status");
        private static readonly GUIContent PinSceneGizmosLabel = new GUIContent(
            "Pin Scene Gizmos",
            "Keep this component's gizmos visible when it is not selected. Selected components are always previewed.");

        private SerializedProperty _enableSight;
        private SerializedProperty _sightConfig;
        private SerializedProperty _sightHalfAngle;
        private SerializedProperty _sightMaxDistance;
        private SerializedProperty _sightUpdateInterval;
        private SerializedProperty _sightObstacleLayer;
        private SerializedProperty _sightUseLineOfSight;
        private SerializedProperty _sightFilterByType;
        private SerializedProperty _sightTargetTypeId;
        private SerializedProperty _sightMemoryDuration;
        private SerializedProperty _sightMaximumLosChecks;
        private SerializedProperty _sightCapacity;

        private SerializedProperty _enableHearing;
        private SerializedProperty _hearingConfig;
        private SerializedProperty _hearingRadius;
        private SerializedProperty _hearingUpdateInterval;
        private SerializedProperty _hearingUseOcclusion;
        private SerializedProperty _hearingOcclusionLayer;
        private SerializedProperty _hearingOcclusionAttenuation;
        private SerializedProperty _hearingMaximumOcclusionChecks;
        private SerializedProperty _hearingFilterByType;
        private SerializedProperty _hearingTargetTypeId;
        private SerializedProperty _hearingMemoryDuration;
        private SerializedProperty _hearingCapacity;

        private SerializedProperty _enableProximity;
        private SerializedProperty _proximityConfig;
        private SerializedProperty _proximityRadius;
        private SerializedProperty _proximityUpdateInterval;
        private SerializedProperty _proximityFilterByType;
        private SerializedProperty _proximityTargetTypeId;
        private SerializedProperty _proximityMemoryDuration;
        private SerializedProperty _proximityCapacity;

        private SerializedProperty _showDebugOverlay;
        private SerializedProperty[] _remainingProperties;
        private SerializedProperty[] _remainingSightProperties;
        private SerializedProperty[] _remainingHearingProperties;
        private SerializedProperty[] _remainingProximityProperties;
        private bool _propertiesValid;

        private bool _showSight = true;
        private bool _showHearing = true;
        private bool _showProximity = true;
        private bool _showSceneDiagnostics = true;
        private bool _showRuntimeDiagnostics = true;
        private bool _showAdditionalFields = true;

        private double _nextRuntimeRepaintTime;
        private bool _runtimeSnapshotDirty = true;
        private int _runtimeComponentCount;
        private int _runtimeInitializedComponentCount;
        private int _runtimeSightDetections;
        private int _runtimeHearingDetections;
        private int _runtimeProximityDetections;
        private bool _runtimeHasAnyDetection;
        private bool _runtimeHasCapacityIssue;
        private readonly GUIContent _runtimeSightStatus = new GUIContent();
        private readonly GUIContent _runtimeHearingStatus = new GUIContent();
        private readonly GUIContent _runtimeProximityStatus = new GUIContent();

        protected virtual void OnEnable()
        {
            _enableSight = serializedObject.FindProperty("_enableSight");
            _sightConfig = serializedObject.FindProperty("_sightConfig");
            _enableHearing = serializedObject.FindProperty("_enableHearing");
            _hearingConfig = serializedObject.FindProperty("_hearingConfig");
            _enableProximity = serializedObject.FindProperty("_enableProximity");
            _proximityConfig = serializedObject.FindProperty("_proximityConfig");
            _showDebugOverlay = serializedObject.FindProperty("_showDebugOverlay");

            CacheSightProperties();
            CacheHearingProperties();
            CacheProximityProperties();

            _propertiesValid = InspectorUiUtility.AreAssigned(
                _enableSight,
                _sightConfig,
                _sightHalfAngle,
                _sightMaxDistance,
                _sightUpdateInterval,
                _sightObstacleLayer,
                _sightUseLineOfSight,
                _sightFilterByType,
                _sightTargetTypeId,
                _sightMemoryDuration,
                _sightMaximumLosChecks,
                _sightCapacity,
                _enableHearing,
                _hearingConfig,
                _hearingRadius,
                _hearingUpdateInterval,
                _hearingUseOcclusion,
                _hearingOcclusionLayer,
                _hearingOcclusionAttenuation,
                _hearingMaximumOcclusionChecks,
                _hearingFilterByType,
                _hearingTargetTypeId,
                _hearingMemoryDuration,
                _hearingCapacity,
                _enableProximity,
                _proximityConfig,
                _proximityRadius,
                _proximityUpdateInterval,
                _proximityFilterByType,
                _proximityTargetTypeId,
                _proximityMemoryDuration,
                _proximityCapacity,
                _showDebugOverlay);

            if (_propertiesValid)
            {
                _remainingProperties = InspectorUiUtility.CacheRemainingProperties(
                    serializedObject,
                    _enableSight,
                    _sightConfig,
                    _enableHearing,
                    _hearingConfig,
                    _enableProximity,
                    _proximityConfig,
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
                "Bounded sight, hearing, and proximity sensing",
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
                DrawSightSection();
                DrawHearingSection();
                DrawProximitySection();
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

        private void CacheSightProperties()
        {
            if (_sightConfig == null)
            {
                return;
            }

            _sightHalfAngle = _sightConfig.FindPropertyRelative("HalfAngle");
            _sightMaxDistance = _sightConfig.FindPropertyRelative("MaxDistance");
            _sightUpdateInterval = _sightConfig.FindPropertyRelative("UpdateInterval");
            _sightObstacleLayer = _sightConfig.FindPropertyRelative("ObstacleLayer");
            _sightUseLineOfSight = _sightConfig.FindPropertyRelative("UseLineOfSight");
            _sightFilterByType = _sightConfig.FindPropertyRelative("FilterByType");
            _sightTargetTypeId = _sightConfig.FindPropertyRelative("TargetTypeId");
            _sightMemoryDuration = _sightConfig.FindPropertyRelative("MemoryDuration");
            _sightMaximumLosChecks = _sightConfig.FindPropertyRelative("MaximumLineOfSightChecksPerUpdate");
            _sightCapacity = _sightConfig.FindPropertyRelative("Capacity");
            _remainingSightProperties = InspectorUiUtility.CacheRemainingChildren(
                _sightConfig,
                _sightHalfAngle,
                _sightMaxDistance,
                _sightUpdateInterval,
                _sightObstacleLayer,
                _sightUseLineOfSight,
                _sightFilterByType,
                _sightTargetTypeId,
                _sightMemoryDuration,
                _sightMaximumLosChecks,
                _sightCapacity);
        }

        private void CacheHearingProperties()
        {
            if (_hearingConfig == null)
            {
                return;
            }

            _hearingRadius = _hearingConfig.FindPropertyRelative("Radius");
            _hearingUpdateInterval = _hearingConfig.FindPropertyRelative("UpdateInterval");
            _hearingUseOcclusion = _hearingConfig.FindPropertyRelative("UseOcclusion");
            _hearingOcclusionLayer = _hearingConfig.FindPropertyRelative("OcclusionLayer");
            _hearingOcclusionAttenuation = _hearingConfig.FindPropertyRelative("OcclusionAttenuation");
            _hearingMaximumOcclusionChecks = _hearingConfig.FindPropertyRelative("MaximumOcclusionChecksPerUpdate");
            _hearingFilterByType = _hearingConfig.FindPropertyRelative("FilterByType");
            _hearingTargetTypeId = _hearingConfig.FindPropertyRelative("TargetTypeId");
            _hearingMemoryDuration = _hearingConfig.FindPropertyRelative("MemoryDuration");
            _hearingCapacity = _hearingConfig.FindPropertyRelative("Capacity");
            _remainingHearingProperties = InspectorUiUtility.CacheRemainingChildren(
                _hearingConfig,
                _hearingRadius,
                _hearingUpdateInterval,
                _hearingUseOcclusion,
                _hearingOcclusionLayer,
                _hearingOcclusionAttenuation,
                _hearingMaximumOcclusionChecks,
                _hearingFilterByType,
                _hearingTargetTypeId,
                _hearingMemoryDuration,
                _hearingCapacity);
        }

        private void CacheProximityProperties()
        {
            if (_proximityConfig == null)
            {
                return;
            }

            _proximityRadius = _proximityConfig.FindPropertyRelative("Radius");
            _proximityUpdateInterval = _proximityConfig.FindPropertyRelative("UpdateInterval");
            _proximityFilterByType = _proximityConfig.FindPropertyRelative("FilterByType");
            _proximityTargetTypeId = _proximityConfig.FindPropertyRelative("TargetTypeId");
            _proximityMemoryDuration = _proximityConfig.FindPropertyRelative("MemoryDuration");
            _proximityCapacity = _proximityConfig.FindPropertyRelative("Capacity");
            _remainingProximityProperties = InspectorUiUtility.CacheRemainingChildren(
                _proximityConfig,
                _proximityRadius,
                _proximityUpdateInterval,
                _proximityFilterByType,
                _proximityTargetTypeId,
                _proximityMemoryDuration,
                _proximityCapacity);
        }

        private void DrawSightSection()
        {
            InspectorUiUtility.DrawSectionHeader(ref _showSight, SightTitle, SightColor, _enableSight);
            if (!_showSight)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(!InspectorUiUtility.IsEnabledOrMixed(_enableSight)))
            {
                InspectorUiUtility.DrawSubsectionLabel("Geometry and Scheduling");
                EditorGUILayout.PropertyField(_sightHalfAngle, HalfAngleLabel);
                EditorGUILayout.PropertyField(_sightMaxDistance, MaxDistanceLabel);
                EditorGUILayout.PropertyField(_sightUpdateInterval, UpdateIntervalLabel);

                InspectorUiUtility.DrawSubsectionLabel("Line of Sight");
                EditorGUILayout.PropertyField(_sightUseLineOfSight, UseLineOfSightLabel);
                if (InspectorUiUtility.IsEnabledOrMixed(_sightUseLineOfSight))
                {
                    EditorGUILayout.PropertyField(_sightObstacleLayer, ObstacleLayerLabel);
                    EditorGUILayout.PropertyField(_sightMaximumLosChecks, MaximumLosChecksLabel);
                }

                InspectorUiUtility.DrawSubsectionLabel("Filtering");
                EditorGUILayout.PropertyField(_sightFilterByType, FilterByTypeLabel);
                if (InspectorUiUtility.IsEnabledOrMixed(_sightFilterByType))
                {
                    EditorGUILayout.PropertyField(_sightTargetTypeId, TargetTypeIdLabel);
                }

                InspectorUiUtility.DrawSubsectionLabel("Memory and Capacity");
                EditorGUILayout.PropertyField(_sightMemoryDuration, MemoryDurationLabel);
                EditorGUILayout.PropertyField(_sightCapacity, CapacityLabel, true);
                InspectorUiUtility.DrawProperties(_remainingSightProperties);
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawHearingSection()
        {
            InspectorUiUtility.DrawSectionHeader(ref _showHearing, HearingTitle, HearingColor, _enableHearing);
            if (!_showHearing)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(!InspectorUiUtility.IsEnabledOrMixed(_enableHearing)))
            {
                InspectorUiUtility.DrawSubsectionLabel("Range and Scheduling");
                EditorGUILayout.PropertyField(_hearingRadius, RadiusLabel);
                EditorGUILayout.PropertyField(_hearingUpdateInterval, UpdateIntervalLabel);

                InspectorUiUtility.DrawSubsectionLabel("Sound Occlusion");
                EditorGUILayout.PropertyField(_hearingUseOcclusion, UseOcclusionLabel);
                if (InspectorUiUtility.IsEnabledOrMixed(_hearingUseOcclusion))
                {
                    EditorGUILayout.PropertyField(_hearingOcclusionLayer, OcclusionLayerLabel);
                    EditorGUILayout.PropertyField(_hearingOcclusionAttenuation, OcclusionAttenuationLabel);
                    EditorGUILayout.PropertyField(_hearingMaximumOcclusionChecks, MaximumOcclusionChecksLabel);
                }

                InspectorUiUtility.DrawSubsectionLabel("Filtering");
                EditorGUILayout.PropertyField(_hearingFilterByType, FilterByTypeLabel);
                if (InspectorUiUtility.IsEnabledOrMixed(_hearingFilterByType))
                {
                    EditorGUILayout.PropertyField(_hearingTargetTypeId, TargetTypeIdLabel);
                }

                InspectorUiUtility.DrawSubsectionLabel("Memory and Capacity");
                EditorGUILayout.PropertyField(_hearingMemoryDuration, MemoryDurationLabel);
                EditorGUILayout.PropertyField(_hearingCapacity, CapacityLabel, true);
                InspectorUiUtility.DrawProperties(_remainingHearingProperties);
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawProximitySection()
        {
            InspectorUiUtility.DrawSectionHeader(ref _showProximity, ProximityTitle, ProximityColor, _enableProximity);
            if (!_showProximity)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(!InspectorUiUtility.IsEnabledOrMixed(_enableProximity)))
            {
                InspectorUiUtility.DrawSubsectionLabel("Range and Scheduling");
                EditorGUILayout.PropertyField(_proximityRadius, RadiusLabel);
                EditorGUILayout.PropertyField(_proximityUpdateInterval, UpdateIntervalLabel);

                InspectorUiUtility.DrawSubsectionLabel("Filtering");
                EditorGUILayout.PropertyField(_proximityFilterByType, FilterByTypeLabel);
                if (InspectorUiUtility.IsEnabledOrMixed(_proximityFilterByType))
                {
                    EditorGUILayout.PropertyField(_proximityTargetTypeId, TargetTypeIdLabel);
                }

                InspectorUiUtility.DrawSubsectionLabel("Memory and Capacity");
                EditorGUILayout.PropertyField(_proximityMemoryDuration, MemoryDurationLabel);
                EditorGUILayout.PropertyField(_proximityCapacity, CapacityLabel, true);
                InspectorUiUtility.DrawProperties(_remainingProximityProperties);
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
                "Selected components are always previewed. Pin this component to keep it visible, or use Tools > CycloneGames > AI Perception > Scene Gizmos to pin a mixed selection. Runtime detection links are capped at 64 per sensor.",
                MessageType.None);
            InspectorUiUtility.EndPanel();
        }

        private void DrawRuntimeDiagnostics()
        {
            if (_runtimeSnapshotDirty)
            {
                RefreshRuntimeSnapshot();
            }

            InspectorUiUtility.DrawSectionHeader(
                ref _showRuntimeDiagnostics,
                RuntimeTitle,
                DebugColor,
                badge: _runtimeHasCapacityIssue ? "BUDGET WARNING" : "LIVE",
                badgeColor: _runtimeHasCapacityIssue
                    ? InspectorUiUtility.WarningColor
                    : InspectorUiUtility.SuccessColor);
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
                InitializedComponentsLabel.text,
                _runtimeInitializedComponentCount.ToString(),
                _runtimeInitializedComponentCount == _runtimeComponentCount
                    ? InspectorUiUtility.SuccessColor
                    : InspectorUiUtility.WarningColor);
            InspectorUiUtility.DrawStatusRow(SightDetectionsLabel.text, _runtimeSightDetections.ToString(), SightColor);
            InspectorUiUtility.DrawStatusRow(HearingDetectionsLabel.text, _runtimeHearingDetections.ToString(), HearingColor);
            InspectorUiUtility.DrawStatusRow(ProximityDetectionsLabel.text, _runtimeProximityDetections.ToString(), ProximityColor);

            if (_runtimeComponentCount == 1)
            {
                InspectorUiUtility.DrawSubsectionLabel("Sensor Status");
                InspectorUiUtility.DrawStatusRow(SightStatusLabel.text, _runtimeSightStatus.text, SightColor);
                InspectorUiUtility.DrawStatusRow(HearingStatusLabel.text, _runtimeHearingStatus.text, HearingColor);
                InspectorUiUtility.DrawStatusRow(ProximityStatusLabel.text, _runtimeProximityStatus.text, ProximityColor);
            }

            if (_runtimeHasAnyDetection)
            {
                EditorGUILayout.HelpBox(
                    "One or more selected components currently report a detection.",
                    MessageType.Info);
            }

            if (_runtimeHasCapacityIssue)
            {
                EditorGUILayout.HelpBox(
                    "A selected sensor exhausted a configured candidate, result, line-of-sight, or hearing occlusion budget. Review Capacity Limits and the sensor workload.",
                    MessageType.Warning);
            }
            InspectorUiUtility.EndPanel();
        }

        private void RefreshRuntimeSnapshot()
        {
            _runtimeComponentCount = 0;
            _runtimeInitializedComponentCount = 0;
            _runtimeSightDetections = 0;
            _runtimeHearingDetections = 0;
            _runtimeProximityDetections = 0;
            _runtimeHasAnyDetection = false;
            _runtimeHasCapacityIssue = false;

            Object[] selectedTargets = targets;
            for (int i = 0; i < selectedTargets.Length; i++)
            {
                var perception = selectedTargets[i] as AIPerceptionComponent;
                if (perception == null)
                {
                    continue;
                }

                _runtimeComponentCount++;
                if (perception.IsInitialized)
                {
                    _runtimeInitializedComponentCount++;
                }

                _runtimeSightDetections += perception.SightDetectedCount;
                _runtimeHearingDetections += perception.HearingDetectedCount;
                _runtimeProximityDetections += perception.ProximityDetectedCount;
                _runtimeHasAnyDetection |= perception.HasAnyDetection;
                _runtimeHasCapacityIssue |= HasCapacityIssue(perception.SightSensor);
                _runtimeHasCapacityIssue |= HasCapacityIssue(perception.HearingSensor);
                _runtimeHasCapacityIssue |= HasCapacityIssue(perception.ProximitySensor);

                if (selectedTargets.Length == 1)
                {
                    _runtimeSightStatus.text = GetStatusText(perception.SightSensor);
                    _runtimeHearingStatus.text = GetStatusText(perception.HearingSensor);
                    _runtimeProximityStatus.text = GetStatusText(perception.ProximitySensor);
                }
            }

            _runtimeSnapshotDirty = false;
        }

        private static bool HasCapacityIssue(ISensor sensor)
        {
            if (sensor == null)
            {
                return false;
            }

            SensorUpdateStatus status = sensor.LastUpdateStatus;
            return status == SensorUpdateStatus.CandidateCapacityExceeded ||
                   status == SensorUpdateStatus.ResultCapacityExceeded ||
                   status == SensorUpdateStatus.LineOfSightBudgetExceeded ||
                   status == SensorUpdateStatus.OcclusionBudgetExceeded;
        }

        private static string GetStatusText(ISensor sensor)
        {
            if (sensor == null)
            {
                return "Disabled";
            }

            switch (sensor.LastUpdateStatus)
            {
                case SensorUpdateStatus.Uninitialized:
                    return "Uninitialized";
                case SensorUpdateStatus.Ready:
                    return "Ready";
                case SensorUpdateStatus.NoTargets:
                    return "No Targets";
                case SensorUpdateStatus.CandidateCapacityExceeded:
                    return "Candidate Capacity Exceeded";
                case SensorUpdateStatus.ResultCapacityExceeded:
                    return "Result Capacity Exceeded";
                case SensorUpdateStatus.LineOfSightBudgetExceeded:
                    return "LOS Budget Exceeded";
                case SensorUpdateStatus.OcclusionBudgetExceeded:
                    return "Occlusion Budget Exceeded";
                case SensorUpdateStatus.InvalidConfiguration:
                    return "Invalid Configuration";
                case SensorUpdateStatus.Disposed:
                    return "Disposed";
                default:
                    return "Unknown";
            }
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
