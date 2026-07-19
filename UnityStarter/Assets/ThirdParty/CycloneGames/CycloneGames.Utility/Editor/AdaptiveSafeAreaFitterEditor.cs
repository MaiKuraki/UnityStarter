using CycloneGames.Utility.Runtime;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AdaptiveSafeAreaFitter))]
    public sealed class AdaptiveSafeAreaFitterEditor : UnityEditor.Editor
    {
        private static readonly Color BehaviorColor = new Color(0.18f, 0.56f, 0.45f, 1f);
        private static readonly Color PaddingColor = new Color(0.50f, 0.36f, 0.68f, 1f);
        private static readonly GUIContent ExtendBottomContent = new GUIContent(
            "Extend Into Bottom",
            "Clears the bottom system inset before optional top-inset balancing is applied.");
        private static readonly GUIContent BalanceTopContent = new GUIContent(
            "Balance Top Inset At Bottom",
            "Makes the bottom inset at least the top inset. This runs after bottom extension.");
        private static readonly GUIContent HorizontalSymmetryContent = new GUIContent("Horizontal Symmetry");
        private static readonly GUIContent TopContent = new GUIContent("Top");
        private static readonly GUIContent BottomContent = new GUIContent("Bottom");
        private static readonly GUIContent LeftContent = new GUIContent("Left");
        private static readonly GUIContent RightContent = new GUIContent("Right");

        private SerializedProperty _script;
        private SerializedProperty _extendIntoBottomSafeArea;
        private SerializedProperty _enforceVerticalSymmetry;
        private SerializedProperty _enforceHorizontalSymmetry;
        private SerializedProperty _manualTopPadding;
        private SerializedProperty _manualBottomPadding;
        private SerializedProperty _manualLeftPadding;
        private SerializedProperty _manualRightPadding;

        private bool _behaviorExpanded = true;
        private bool _paddingExpanded = true;
        private int _cachedScreenWidth = -1;
        private int _cachedScreenHeight = -1;
        private Rect _cachedSafeArea;
        private bool _hasCachedSafeArea;
        private string _screenText = string.Empty;
        private string _safeAreaText = string.Empty;

        private void OnEnable()
        {
            _script = serializedObject.FindProperty("m_Script");
            _extendIntoBottomSafeArea = serializedObject.FindProperty("_extendIntoBottomSafeArea");
            _enforceVerticalSymmetry = serializedObject.FindProperty("_enforceVerticalSymmetry");
            _enforceHorizontalSymmetry = serializedObject.FindProperty("_enforceHorizontalSymmetry");
            _manualTopPadding = serializedObject.FindProperty("_manualTopPadding");
            _manualBottomPadding = serializedObject.FindProperty("_manualBottomPadding");
            _manualLeftPadding = serializedObject.FindProperty("_manualLeftPadding");
            _manualRightPadding = serializedObject.FindProperty("_manualRightPadding");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InspectorUiUtility.DrawScriptProperty(_script);
            InspectorUiUtility.DrawModuleHeader(
                "Adaptive Safe Area",
                "Drives anchors and offsets from Screen.safeArea. Padding is bounded so anchors cannot invert.");

            _behaviorExpanded = InspectorUiUtility.DrawFoldoutHeader("Behavior", _behaviorExpanded, BehaviorColor);
            if (_behaviorExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_extendIntoBottomSafeArea, ExtendBottomContent);
                    EditorGUILayout.PropertyField(_enforceVerticalSymmetry, BalanceTopContent);
                    EditorGUILayout.PropertyField(_enforceHorizontalSymmetry, HorizontalSymmetryContent);
                }

                if (!_extendIntoBottomSafeArea.hasMultipleDifferentValues &&
                    !_enforceVerticalSymmetry.hasMultipleDifferentValues &&
                    _extendIntoBottomSafeArea.boolValue &&
                    _enforceVerticalSymmetry.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Top-inset balancing runs after bottom extension and can add bottom inset back to balance a top cutout.",
                        MessageType.Info);
                }
            }

            _paddingExpanded = InspectorUiUtility.DrawFoldoutHeader("Manual Padding (Pixels)", _paddingExpanded, PaddingColor);
            if (_paddingExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_manualTopPadding, TopContent);
                    EditorGUILayout.PropertyField(_manualBottomPadding, BottomContent);
                    EditorGUILayout.PropertyField(_manualLeftPadding, LeftContent);
                    EditorGUILayout.PropertyField(_manualRightPadding, RightContent);
                }
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("Refresh Safe Area"))
            {
                RefreshTargetsWithUndo();
            }

            if (!serializedObject.isEditingMultipleObjects)
            {
                UpdateScreenStats();
                InspectorUiUtility.DrawReadOnlyStat("Screen", _screenText);
                InspectorUiUtility.DrawReadOnlyStat("Safe Area", _safeAreaText);
            }
        }

        private void UpdateScreenStats()
        {
            int width = Screen.width;
            int height = Screen.height;
            Rect safeArea = Screen.safeArea;
            if (width != _cachedScreenWidth || height != _cachedScreenHeight)
            {
                _cachedScreenWidth = width;
                _cachedScreenHeight = height;
                _screenText = string.Concat(width, " x ", height);
            }

            if (!_hasCachedSafeArea || safeArea != _cachedSafeArea)
            {
                _hasCachedSafeArea = true;
                _cachedSafeArea = safeArea;
                _safeAreaText = safeArea.ToString();
            }
        }

        private void RefreshTargetsWithUndo()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                AdaptiveSafeAreaFitter fitter = targets[i] as AdaptiveSafeAreaFitter;
                if (fitter == null || !fitter.TryGetComponent(out RectTransform rectTransform))
                {
                    continue;
                }

                Undo.RecordObject(rectTransform, "Refresh Safe Area");
                fitter.Refresh();
                PrefabUtility.RecordPrefabInstancePropertyModifications(rectTransform);
            }
        }
    }
}
