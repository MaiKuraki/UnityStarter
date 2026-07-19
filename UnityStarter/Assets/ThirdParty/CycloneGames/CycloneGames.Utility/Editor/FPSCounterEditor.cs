using System.Globalization;

using CycloneGames.Utility.Runtime;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(FPSCounter))]
    public sealed class FPSCounterEditor : UnityEditor.Editor
    {
        private static readonly Color SamplingColor = new Color(0.16f, 0.48f, 0.72f, 1f);
        private static readonly Color LayoutColor = new Color(0.42f, 0.32f, 0.70f, 1f);
        private static readonly Color SafeAreaColor = new Color(0.18f, 0.56f, 0.45f, 1f);
        private static readonly GUIContent VisibleContent = new GUIContent("Visible");
        private static readonly GUIContent PersistContent = new GUIContent("Persist Across Scenes");
        private static readonly GUIContent UpdateIntervalContent = new GUIContent("Update Interval");
        private static readonly GUIContent AverageWindowContent = new GUIContent("Average Window");
        private static readonly GUIContent ThresholdsContent = new GUIContent("Color Thresholds");
        private static readonly GUIContent ForegroundContent = new GUIContent("Foreground");
        private static readonly GUIContent OutlineContent = new GUIContent("Outline");
        private static readonly GUIContent PositionContent = new GUIContent("Position");
        private static readonly GUIContent MarginContent = new GUIContent("Margin");
        private static readonly GUIContent FontRatioContent = new GUIContent("Font Size Ratio");
        private static readonly GUIContent AdjustSafeAreaContent = new GUIContent("Adjust For Safe Area");
        private static readonly GUIContent ExtendBottomContent = new GUIContent(
            "Extend Into Bottom",
            "Clears the bottom system inset before optional top-inset balancing is applied.");
        private static readonly GUIContent BalanceTopContent = new GUIContent(
            "Balance Top Inset At Bottom",
            "Makes the bottom inset at least the top inset. This runs after bottom extension.");
        private static readonly GUIContent HorizontalSymmetryContent = new GUIContent("Horizontal Symmetry");

        private SerializedProperty _script;
        private SerializedProperty _isVisible;
        private SerializedProperty _persistAcrossScenes;
        private SerializedProperty _adjustForSafeArea;
        private SerializedProperty _extendIntoBottomSafeArea;
        private SerializedProperty _enforceVerticalSymmetry;
        private SerializedProperty _enforceHorizontalSymmetry;
        private SerializedProperty _updateInterval;
        private SerializedProperty _movingAverageSampleCount;
        private SerializedProperty _mode;
        private SerializedProperty _defaultForegroundColor;
        private SerializedProperty _outlineColor;
        private SerializedProperty _outlineOffset;
        private SerializedProperty _positionPreset;
        private SerializedProperty _presetPositionMargin;
        private SerializedProperty _customPosition;
        private SerializedProperty _fontSizeRatio;
        private SerializedProperty _fpsColors;

        private bool _samplingExpanded = true;
        private bool _layoutExpanded = true;
        private bool _safeAreaExpanded;
        private int _cachedCurrentFps = -1;
        private int _cachedAverageFps = -1;
        private string _currentFpsText = string.Empty;
        private string _averageFpsText = string.Empty;

        private void OnEnable()
        {
            _script = serializedObject.FindProperty("m_Script");
            _isVisible = serializedObject.FindProperty("_isVisible");
            _persistAcrossScenes = serializedObject.FindProperty("_persistAcrossScenes");
            _adjustForSafeArea = serializedObject.FindProperty("_adjustForSafeArea");
            _extendIntoBottomSafeArea = serializedObject.FindProperty("_extendIntoBottomSafeArea");
            _enforceVerticalSymmetry = serializedObject.FindProperty("_enforceVerticalSymmetry");
            _enforceHorizontalSymmetry = serializedObject.FindProperty("_enforceHorizontalSymmetry");
            _updateInterval = serializedObject.FindProperty("_updateInterval");
            _movingAverageSampleCount = serializedObject.FindProperty("_movingAverageSampleCount");
            _mode = serializedObject.FindProperty("_mode");
            _defaultForegroundColor = serializedObject.FindProperty("_defaultForegroundColor");
            _outlineColor = serializedObject.FindProperty("_outlineColor");
            _outlineOffset = serializedObject.FindProperty("_outlineOffset");
            _positionPreset = serializedObject.FindProperty("_positionPreset");
            _presetPositionMargin = serializedObject.FindProperty("_presetPositionMargin");
            _customPosition = serializedObject.FindProperty("_customPosition");
            _fontSizeRatio = serializedObject.FindProperty("_fontSizeRatio");
            _fpsColors = serializedObject.FindProperty("_fpsColors");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InspectorUiUtility.DrawScriptProperty(_script);
            InspectorUiUtility.DrawModuleHeader(
                "Frame Rate Diagnostics",
                "A bounded sampler and IMGUI overlay. Keep the component on an explicitly owned diagnostic GameObject.");

            EditorGUILayout.PropertyField(_isVisible, VisibleContent);
            EditorGUILayout.PropertyField(_persistAcrossScenes, PersistContent);
            if (!_persistAcrossScenes.hasMultipleDifferentValues && _persistAcrossScenes.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "DontDestroyOnLoad affects the entire GameObject. Use a dedicated diagnostic owner to avoid retaining unrelated components.",
                    MessageType.Warning);
            }

            _samplingExpanded = InspectorUiUtility.DrawFoldoutHeader("Sampling", _samplingExpanded, SamplingColor);
            if (_samplingExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_updateInterval, UpdateIntervalContent);
                    EditorGUILayout.PropertyField(_mode);
                    EditorGUILayout.PropertyField(_movingAverageSampleCount, AverageWindowContent);
                    EditorGUILayout.PropertyField(_fpsColors, ThresholdsContent, true);
                }
            }

            _layoutExpanded = InspectorUiUtility.DrawFoldoutHeader("Presentation", _layoutExpanded, LayoutColor);
            if (_layoutExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_defaultForegroundColor, ForegroundContent);
                    EditorGUILayout.PropertyField(_outlineColor, OutlineContent);
                    EditorGUILayout.PropertyField(_outlineOffset);
                    EditorGUILayout.PropertyField(_positionPreset, PositionContent);
                    EditorGUILayout.PropertyField(_presetPositionMargin, MarginContent);

                    if (!_positionPreset.hasMultipleDifferentValues &&
                        _positionPreset.enumValueIndex == (int)FPSCounter.ScreenPosition.Custom)
                    {
                        EditorGUILayout.PropertyField(_customPosition);
                    }

                    EditorGUILayout.PropertyField(_fontSizeRatio, FontRatioContent);
                }
            }

            _safeAreaExpanded = InspectorUiUtility.DrawFoldoutHeader("Safe Area", _safeAreaExpanded, SafeAreaColor);
            if (_safeAreaExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_adjustForSafeArea, AdjustSafeAreaContent);
                    using (new EditorGUI.DisabledScope(
                               !_adjustForSafeArea.hasMultipleDifferentValues && !_adjustForSafeArea.boolValue))
                    {
                        EditorGUILayout.PropertyField(_extendIntoBottomSafeArea, ExtendBottomContent);
                        EditorGUILayout.PropertyField(_enforceVerticalSymmetry, BalanceTopContent);
                        EditorGUILayout.PropertyField(_enforceHorizontalSymmetry, HorizontalSymmetryContent);
                    }
                }

                if (!_adjustForSafeArea.hasMultipleDifferentValues &&
                    _adjustForSafeArea.boolValue &&
                    !_extendIntoBottomSafeArea.hasMultipleDifferentValues &&
                    !_enforceVerticalSymmetry.hasMultipleDifferentValues &&
                    _extendIntoBottomSafeArea.boolValue &&
                    _enforceVerticalSymmetry.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Top-inset balancing runs after bottom extension and can add bottom inset back to balance a top cutout.",
                        MessageType.Info);
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (!serializedObject.isEditingMultipleObjects && Application.isPlaying && target is FPSCounter counter)
            {
                EditorGUILayout.Space(4f);
                UpdateRuntimeStats(counter);
                InspectorUiUtility.DrawReadOnlyStat("Current FPS", _currentFpsText);
                InspectorUiUtility.DrawReadOnlyStat("Average FPS", _averageFpsText);
                if (GUILayout.Button("Reset Average"))
                {
                    counter.ResetAverage();
                }
            }
        }

        private void UpdateRuntimeStats(FPSCounter counter)
        {
            int current = counter.CurrentFPS;
            if (current != _cachedCurrentFps)
            {
                _cachedCurrentFps = current;
                _currentFpsText = current.ToString(CultureInfo.InvariantCulture);
            }

            int average = counter.AverageFPS;
            if (average != _cachedAverageFps)
            {
                _cachedAverageFps = average;
                _averageFpsText = average.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
