using System.Globalization;
using CycloneGames.AIPerception.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AIPerception.Editor
{
    [CustomEditor(typeof(PerceptionManagerComponent), true)]
    [CanEditMultipleObjects]
    public class PerceptionManagerComponentEditor : UnityEditor.Editor
    {
        private const string AuthoringLockedMessage =
            "Manager authoring is locked in Play Mode to keep serialized settings and the live scheduler state consistent.";
        private const string EmptyLodMessage =
            "No LOD levels are configured. Distance-based sensor throttling is disabled.";
        private const string InvalidDistanceMessage =
            "Every LOD distance must be a finite value greater than zero.";
        private const string InvalidFrequencyMessage =
            "Every frequency multiplier must be finite and in the range (0, 1].";
        private const string UnsortedDistanceMessage =
            "LOD distances must be strictly increasing. Duplicate or descending thresholds are not supported.";
        private const string IncreasingFrequencyMessage =
            "Frequency multipliers should not increase at farther thresholds. Increasing values make distant sensors update more often.";
        private const string MixedLodMessage =
            "The selected managers have different LOD values. Validation and preview are available when their values match.";

        private static readonly Color HeaderColor = new Color(0.38f, 0.48f, 0.82f, 1f);
        private static readonly Color PerformanceColor = new Color(0.82f, 0.58f, 0.16f, 1f);
        private static readonly Color CapacityColor = new Color(0.72f, 0.42f, 0.24f, 1f);
        private static readonly Color LodColor = new Color(0.22f, 0.7f, 0.44f, 1f);
        private static readonly Color RuntimeColor = new Color(0.36f, 0.65f, 0.84f, 1f);
        private static readonly Color AdditionalColor = new Color(0.58f, 0.42f, 0.74f, 1f);
        private static readonly Color[] LodBandColors =
        {
            new Color(0.2f, 0.72f, 0.34f, 0.28f),
            new Color(0.72f, 0.7f, 0.2f, 0.28f),
            new Color(0.84f, 0.5f, 0.18f, 0.28f),
            new Color(0.8f, 0.26f, 0.2f, 0.28f)
        };

        private static readonly GUIContent HeaderTitle = new GUIContent("Perception Manager");
        private static readonly GUIContent PerformanceTitle = new GUIContent("Scheduling");
        private static readonly GUIContent CapacityTitle = new GUIContent("World Capacity");
        private static readonly GUIContent LodTitle = new GUIContent("Distance LOD");
        private static readonly GUIContent RuntimeTitle = new GUIContent("Runtime Diagnostics");
        private static readonly GUIContent AdditionalFieldsTitle = new GUIContent("Additional Fields");
        private static readonly GUIContent DeferredJobLabel = new GUIContent(
            "Deferred Job Completion",
            "Schedules sensor jobs in Update and completes them in LateUpdate.");
        private static readonly GUIContent LodReferenceLabel = new GUIContent(
            "LOD Reference",
            "Reference transform used for sensor distance. Null disables distance LOD.");
        private static readonly GUIContent LodLevelsLabel = new GUIContent(
            "LOD Levels",
            "Each frequency multiplier applies up to its maximum distance. Beyond the last tier, the last multiplier remains active. Effective interval equals base interval divided by the multiplier.");
        private static readonly GUIContent MaximumPerceptiblesLabel = new GUIContent(
            "Maximum Perceptibles",
            "Hard registry capacity. Zero allows unbounded safe-point growth and should be used only with an external world budget.");
        private static readonly GUIContent SpatialCellSizeLabel = new GUIContent(
            "Spatial Cell Size",
            "World-space cell width used by broad-phase candidate queries.");
        private static readonly GUIContent LodPreviewLabel = new GUIContent("Frequency and Effective Interval Scale");
        private static readonly GUIContent ManagerInitializedLabel = new GUIContent("Sensor Manager Initialized");
        private static readonly GUIContent DeferredModeLabel = new GUIContent("Deferred Mode");
        private static readonly GUIContent ActiveSensorsLabel = new GUIContent("Active Sensors");
        private static readonly GUIContent PerceptiblesLabel = new GUIContent("Registered Perceptibles");
        private static readonly GUIContent MaximumCapacityLabel = new GUIContent("Registry Maximum");

        private SerializedProperty _useDeferredJobCompletion;
        private SerializedProperty _maximumPerceptibles;
        private SerializedProperty _spatialCellSize;
        private SerializedProperty _lodReference;
        private SerializedProperty _lodLevels;
        private SerializedProperty[] _remainingProperties;
        private bool _propertiesValid;

        private SerializedProperty[] _lodDistanceProperties;
        private SerializedProperty[] _lodFrequencyProperties;
        private float[] _cachedLodDistances;
        private float[] _cachedLodFrequencies;
        private GUIContent[] _lodBandLabels;
        private GUIContent[] _lodThresholdLabels;
        private bool _lodModelInitialized;
        private bool _lodValuesMixed;
        private bool _lodPreviewAvailable;
        private string _lodValidationMessage;
        private MessageType _lodValidationType;

        private bool _showPerformance = true;
        private bool _showCapacity = true;
        private bool _showLod = true;
        private bool _showRuntimeDiagnostics = true;
        private bool _showAdditionalFields = true;

        private double _nextRuntimeRepaintTime;
        private bool _runtimeSnapshotDirty = true;
        private bool _runtimeManagerInitialized;
        private bool _runtimeDeferredMode;
        private int _runtimeSensorCount;
        private int _runtimePerceptibleCount;
        private int _runtimeMaximumCapacity;

        protected virtual void OnEnable()
        {
            _useDeferredJobCompletion = serializedObject.FindProperty("_useDeferredJobCompletion");
            _maximumPerceptibles = serializedObject.FindProperty("_maximumPerceptibles");
            _spatialCellSize = serializedObject.FindProperty("_spatialCellSize");
            _lodReference = serializedObject.FindProperty("_lodReference");
            _lodLevels = serializedObject.FindProperty("_lodLevels");
            _propertiesValid = InspectorUiUtility.AreAssigned(
                _useDeferredJobCompletion,
                _maximumPerceptibles,
                _spatialCellSize,
                _lodReference,
                _lodLevels);

            if (_propertiesValid)
            {
                _remainingProperties = InspectorUiUtility.CacheRemainingProperties(
                    serializedObject,
                    _useDeferredJobCompletion,
                    _maximumPerceptibles,
                    _spatialCellSize,
                    _lodReference,
                    _lodLevels);
                EnsureLodPropertyCache();
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
                "World capacity, scheduling, and distance LOD",
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

            using (new EditorGUI.DisabledScope(authoringLocked))
            {
                DrawPerformanceSection();
                DrawCapacitySection();
                DrawLodSection();
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

        private void DrawPerformanceSection()
        {
            string badge = _useDeferredJobCompletion.hasMultipleDifferentValues
                ? "MIXED"
                : _useDeferredJobCompletion.boolValue ? "DEFERRED" : "IMMEDIATE";
            InspectorUiUtility.DrawSectionHeader(
                ref _showPerformance,
                PerformanceTitle,
                PerformanceColor,
                badge: badge,
                badgeColor: _useDeferredJobCompletion.hasMultipleDifferentValues
                    ? InspectorUiUtility.NeutralColor
                    : InspectorUiUtility.SuccessColor);
            if (!_showPerformance)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_useDeferredJobCompletion, DeferredJobLabel);
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawLodSection()
        {
            string badge = _lodLevels.hasMultipleDifferentValues ? "MIXED" : _lodLevels.arraySize + " LEVELS";
            InspectorUiUtility.DrawSectionHeader(
                ref _showLod,
                LodTitle,
                LodColor,
                badge: badge,
                badgeColor: _lodLevels.hasMultipleDifferentValues
                    ? InspectorUiUtility.NeutralColor
                    : LodColor);
            if (!_showLod)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_lodReference, LodReferenceLabel);
                if (!_lodReference.hasMultipleDifferentValues && _lodReference.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "No LOD reference is assigned. Sensors update at their base frequency.",
                        MessageType.Info);
                }

                EditorGUILayout.PropertyField(_lodLevels, LodLevelsLabel, true);
                EnsureLodPropertyCache();
                RefreshLodModel();
                DrawLodValidation();
                DrawLodPreview();

                if (!_lodReference.hasMultipleDifferentValues && _lodReference.objectReferenceValue != null)
                {
                    EditorGUILayout.HelpBox(
                        "Select this manager to preview the validated LOD thresholds as Scene view rings around the LOD Reference.",
                        MessageType.None);
                }
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawCapacitySection()
        {
            string badge = _maximumPerceptibles.hasMultipleDifferentValues
                ? "MIXED"
                : _maximumPerceptibles.intValue == 0 ? "UNBOUNDED" : "BOUNDED";
            InspectorUiUtility.DrawSectionHeader(
                ref _showCapacity,
                CapacityTitle,
                CapacityColor,
                badge: badge,
                badgeColor: !_maximumPerceptibles.hasMultipleDifferentValues && _maximumPerceptibles.intValue == 0
                    ? InspectorUiUtility.WarningColor
                    : InspectorUiUtility.SuccessColor);
            if (!_showCapacity)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_maximumPerceptibles, MaximumPerceptiblesLabel);
                EditorGUILayout.PropertyField(_spatialCellSize, SpatialCellSizeLabel);

                if (!_maximumPerceptibles.hasMultipleDifferentValues && _maximumPerceptibles.intValue == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Unbounded registry growth is enabled. Establish and profile an external world population budget before using this setting in production.",
                        MessageType.Warning);
                }
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawLodValidation()
        {
            if (!string.IsNullOrEmpty(_lodValidationMessage))
            {
                EditorGUILayout.HelpBox(_lodValidationMessage, _lodValidationType);
            }
        }

        private void DrawLodPreview()
        {
            if (!_lodPreviewAvailable || _cachedLodDistances.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(LodPreviewLabel, EditorStyles.miniBoldLabel);

            Rect previewRect = EditorGUILayout.GetControlRect(false, 66f);
            float lastDistance = _cachedLodDistances[_cachedLodDistances.Length - 1];
            float previousDistance = _cachedLodDistances.Length > 1
                ? _cachedLodDistances[_cachedLodDistances.Length - 2]
                : 0f;
            float tailLength = Mathf.Max((lastDistance - previousDistance) * 0.5f, 10f);
            float previewMaxDistance = lastDistance + tailLength;
            Rect bandArea = new Rect(previewRect.x, previewRect.y + 2f, previewRect.width, 38f);

            int bandCount = _cachedLodDistances.Length + 1;
            for (int i = 0; i < bandCount; i++)
            {
                float startDistance = i == 0 ? 0f : _cachedLodDistances[i - 1];
                float endDistance = i < _cachedLodDistances.Length
                    ? _cachedLodDistances[i]
                    : previewMaxDistance;
                float xMin = bandArea.x + (startDistance / previewMaxDistance) * bandArea.width;
                float xMax = bandArea.x + (endDistance / previewMaxDistance) * bandArea.width;
                Rect bandRect = new Rect(xMin, bandArea.y, Mathf.Max(0f, xMax - xMin), bandArea.height);
                EditorGUI.DrawRect(bandRect, LodBandColors[Mathf.Min(i, LodBandColors.Length - 1)]);

                if (bandRect.width >= 52f)
                {
                    GUI.Label(bandRect, _lodBandLabels[i], InspectorUiUtility.CenteredMiniLabelStyle);
                }
            }

            Color borderColor = EditorGUIUtility.isProSkin
                ? new Color(0.72f, 0.72f, 0.72f, 0.45f)
                : new Color(0.22f, 0.22f, 0.22f, 0.45f);
            EditorGUI.DrawRect(new Rect(bandArea.x, bandArea.y, bandArea.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(bandArea.x, bandArea.yMax - 1f, bandArea.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(bandArea.x, bandArea.y, 1f, bandArea.height), borderColor);
            EditorGUI.DrawRect(new Rect(bandArea.xMax - 1f, bandArea.y, 1f, bandArea.height), borderColor);

            for (int i = 0; i < _cachedLodDistances.Length; i++)
            {
                float markerX = bandArea.x + (_cachedLodDistances[i] / previewMaxDistance) * bandArea.width;
                EditorGUI.DrawRect(new Rect(markerX, bandArea.y, 1f, bandArea.height + 4f), borderColor);
                Rect labelRect = new Rect(markerX - 28f, bandArea.yMax + 4f, 56f, 16f);
                GUI.Label(labelRect, _lodThresholdLabels[i], InspectorUiUtility.CenteredMiniLabelStyle);
            }
        }

        private void EnsureLodPropertyCache()
        {
            int count = _lodLevels.arraySize;
            if (_lodDistanceProperties != null && _lodDistanceProperties.Length == count)
            {
                return;
            }

            _lodDistanceProperties = new SerializedProperty[count];
            _lodFrequencyProperties = new SerializedProperty[count];
            _cachedLodDistances = new float[count];
            _cachedLodFrequencies = new float[count];
            _lodBandLabels = new GUIContent[count + 1];
            _lodThresholdLabels = new GUIContent[count];

            for (int i = 0; i < count; i++)
            {
                SerializedProperty level = _lodLevels.GetArrayElementAtIndex(i);
                _lodDistanceProperties[i] = level.FindPropertyRelative("Distance");
                _lodFrequencyProperties[i] = level.FindPropertyRelative("FrequencyMultiplier");
                _lodThresholdLabels[i] = new GUIContent();
            }

            for (int i = 0; i < _lodBandLabels.Length; i++)
            {
                _lodBandLabels[i] = new GUIContent();
            }

            _lodModelInitialized = false;
        }

        private void RefreshLodModel()
        {
            int count = _lodDistanceProperties.Length;
            bool mixed = _lodLevels.hasMultipleDifferentValues;
            bool changed = !_lodModelInitialized || mixed != _lodValuesMixed;

            for (int i = 0; i < count; i++)
            {
                SerializedProperty distanceProperty = _lodDistanceProperties[i];
                SerializedProperty frequencyProperty = _lodFrequencyProperties[i];
                if (distanceProperty == null || frequencyProperty == null)
                {
                    _lodValidationMessage =
                        "An LOD entry is missing its Distance or FrequencyMultiplier field. The runtime and inspector contracts do not match.";
                    _lodValidationType = MessageType.Error;
                    _lodPreviewAvailable = false;
                    return;
                }

                mixed |= distanceProperty.hasMultipleDifferentValues || frequencyProperty.hasMultipleDifferentValues;
                float distance = distanceProperty.floatValue;
                float frequency = frequencyProperty.floatValue;
                if (!Mathf.Approximately(_cachedLodDistances[i], distance) ||
                    !Mathf.Approximately(_cachedLodFrequencies[i], frequency))
                {
                    _cachedLodDistances[i] = distance;
                    _cachedLodFrequencies[i] = frequency;
                    changed = true;
                }
            }

            if (!changed && mixed == _lodValuesMixed)
            {
                return;
            }

            _lodModelInitialized = true;
            _lodValuesMixed = mixed;
            ValidateLodModel();
            UpdateLodLabels();
        }

        private void ValidateLodModel()
        {
            _lodValidationMessage = null;
            _lodValidationType = MessageType.None;
            _lodPreviewAvailable = false;

            if (_lodValuesMixed)
            {
                _lodValidationMessage = MixedLodMessage;
                _lodValidationType = MessageType.Info;
                return;
            }

            if (_cachedLodDistances.Length == 0)
            {
                _lodValidationMessage = EmptyLodMessage;
                _lodValidationType = MessageType.Warning;
                return;
            }

            bool frequencyIncreases = false;
            for (int i = 0; i < _cachedLodDistances.Length; i++)
            {
                float distance = _cachedLodDistances[i];
                float frequency = _cachedLodFrequencies[i];
                if (!InspectorUiUtility.IsFinite(distance) || distance <= 0f)
                {
                    _lodValidationMessage = InvalidDistanceMessage;
                    _lodValidationType = MessageType.Error;
                    return;
                }

                if (!InspectorUiUtility.IsFinite(frequency) || frequency <= 0f || frequency > 1f)
                {
                    _lodValidationMessage = InvalidFrequencyMessage;
                    _lodValidationType = MessageType.Error;
                    return;
                }

                if (i > 0)
                {
                    if (distance <= _cachedLodDistances[i - 1])
                    {
                        _lodValidationMessage = UnsortedDistanceMessage;
                        _lodValidationType = MessageType.Error;
                        return;
                    }

                    frequencyIncreases |= frequency > _cachedLodFrequencies[i - 1];
                }
            }

            _lodPreviewAvailable = true;
            if (frequencyIncreases)
            {
                _lodValidationMessage = IncreasingFrequencyMessage;
                _lodValidationType = MessageType.Warning;
            }
        }

        private void UpdateLodLabels()
        {
            if (_lodBandLabels == null || _lodBandLabels.Length == 0 || _cachedLodFrequencies.Length == 0)
            {
                return;
            }

            for (int i = 0; i < _cachedLodFrequencies.Length; i++)
            {
                float frequency = _cachedLodFrequencies[i];
                float intervalScale = frequency > 0f ? 1f / frequency : 0f;
                _lodBandLabels[i].text =
                    "F x" + frequency.ToString("0.##", CultureInfo.InvariantCulture) +
                    " | I x" + intervalScale.ToString("0.##", CultureInfo.InvariantCulture);
                _lodThresholdLabels[i].text =
                    _cachedLodDistances[i].ToString("0.#", CultureInfo.InvariantCulture) + " m";
            }

            _lodBandLabels[_lodBandLabels.Length - 1].text =
                _lodBandLabels[_lodBandLabels.Length - 2].text;
        }

        private void DrawRuntimeDiagnostics()
        {
            if (_runtimeSnapshotDirty)
            {
                RefreshRuntimeSnapshot();
            }

            bool atCapacity = _runtimeMaximumCapacity > 0 &&
                              _runtimePerceptibleCount >= _runtimeMaximumCapacity;
            InspectorUiUtility.DrawSectionHeader(
                ref _showRuntimeDiagnostics,
                RuntimeTitle,
                RuntimeColor,
                badge: atCapacity ? "AT CAPACITY" : _runtimeManagerInitialized ? "LIVE" : "OFFLINE",
                badgeColor: atCapacity
                    ? InspectorUiUtility.WarningColor
                    : _runtimeManagerInitialized
                        ? InspectorUiUtility.SuccessColor
                        : InspectorUiUtility.NeutralColor);
            if (!_showRuntimeDiagnostics)
            {
                return;
            }

            InspectorUiUtility.BeginPanel();
            InspectorUiUtility.DrawStatusRow(
                ManagerInitializedLabel.text,
                _runtimeManagerInitialized ? "Ready" : "Not Initialized",
                _runtimeManagerInitialized
                    ? InspectorUiUtility.SuccessColor
                    : InspectorUiUtility.WarningColor);
            InspectorUiUtility.DrawStatusRow(
                DeferredModeLabel.text,
                _runtimeDeferredMode ? "Deferred" : "Immediate",
                RuntimeColor);
            InspectorUiUtility.DrawStatusRow(ActiveSensorsLabel.text, _runtimeSensorCount.ToString(), RuntimeColor);
            InspectorUiUtility.DrawStatusRow(PerceptiblesLabel.text, _runtimePerceptibleCount.ToString(), LodColor);
            InspectorUiUtility.DrawStatusRow(
                MaximumCapacityLabel.text,
                _runtimeMaximumCapacity == 0 ? "Unbounded" : _runtimeMaximumCapacity.ToString(),
                _runtimeMaximumCapacity == 0
                    ? InspectorUiUtility.WarningColor
                    : CapacityColor);

            if (atCapacity)
            {
                EditorGUILayout.HelpBox(
                    "The perceptible registry has reached its configured maximum. New registrations are rejected.",
                    MessageType.Error);
            }
            InspectorUiUtility.EndPanel();
        }

        private void OnSceneGUI()
        {
            if (!_propertiesValid || targets.Length != 1)
            {
                return;
            }

            serializedObject.UpdateIfRequiredOrScript();
            if (_lodReference.hasMultipleDifferentValues || _lodReference.objectReferenceValue == null)
            {
                return;
            }

            EnsureLodPropertyCache();
            RefreshLodModel();
            if (!_lodPreviewAvailable)
            {
                return;
            }

            var reference = _lodReference.objectReferenceValue as Transform;
            if (reference == null)
            {
                return;
            }

            Color previousColor = Handles.color;
            try
            {
                Vector3 center = reference.position;
                for (int i = _cachedLodDistances.Length - 1; i >= 0; i--)
                {
                    Color bandColor = LodBandColors[Mathf.Min(i, LodBandColors.Length - 1)];
                    bandColor.a = 0.88f;
                    Handles.color = bandColor;
                    Handles.DrawWireDisc(center, Vector3.up, _cachedLodDistances[i]);
                    Handles.Label(
                        center + Vector3.forward * _cachedLodDistances[i],
                        _lodThresholdLabels[i],
                        EditorStyles.miniBoldLabel);
                }
            }
            finally
            {
                Handles.color = previousColor;
            }
        }

        private void RefreshRuntimeSnapshot()
        {
            _runtimeManagerInitialized = SensorManager.HasInstance;
            _runtimeDeferredMode = false;
            _runtimeSensorCount = 0;
            _runtimePerceptibleCount = 0;
            _runtimeMaximumCapacity = 0;

            if (SensorManager.HasInstance)
            {
                SensorManager manager = SensorManager.Instance;
                _runtimeDeferredMode = manager.UseDeferredJobCompletion;
                _runtimeSensorCount = manager.SensorCount;
            }

            if (PerceptibleRegistry.HasInstance)
            {
                PerceptibleRegistry registry = PerceptibleRegistry.Instance;
                _runtimePerceptibleCount = registry.Count;
                _runtimeMaximumCapacity = registry.MaximumCapacity;
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
