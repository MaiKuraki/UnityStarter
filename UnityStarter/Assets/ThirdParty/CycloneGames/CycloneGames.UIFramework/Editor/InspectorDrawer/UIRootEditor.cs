// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(UIRoot), true)]
    [CanEditMultipleObjects]
    public sealed class UIRootEditor : UnityEditor.Editor
    {
        private SerializedProperty _uiCamera;
        private SerializedProperty _rootCanvas;
        private SerializedProperty _layerList;

        private bool _showRoot = true;
        private bool _showLayers = true;
        private bool _showValidation = true;

        private void OnEnable()
        {
            _uiCamera = serializedObject.FindProperty("uiCamera");
            _rootCanvas = serializedObject.FindProperty("rootCanvas");
            _layerList = serializedObject.FindProperty("layerList");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InspectorUiUtility.DrawInspectorTitle(
                "UI Root",
                "Canvas, camera, and ordered layer composition",
                InspectorUiUtility.SetupColor);

            bool hasCanvas = _rootCanvas.hasMultipleDifferentValues || _rootCanvas.objectReferenceValue != null;
            _showRoot = InspectorUiUtility.DrawFoldoutHeader(
                "Root References",
                _showRoot,
                InspectorUiUtility.SetupColor,
                hasCanvas ? "READY" : "REQUIRED",
                hasCanvas ? InspectorUiUtility.SuccessColor : InspectorUiUtility.WarningColor);
            if (_showRoot)
            {
                InspectorUiUtility.BeginPanel();
                EditorGUILayout.PropertyField(_rootCanvas, new GUIContent(
                    "Root Canvas",
                    "Canvas that defines the root UI coordinate space."));
                EditorGUILayout.PropertyField(_uiCamera, new GUIContent(
                    "UI Camera",
                    "Optional camera used by camera-space UI and UI coordinate conversion."));
                if (!_rootCanvas.hasMultipleDifferentValues && _rootCanvas.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "Root Canvas is required. UIService rejects a root that cannot provide its coordinate space.",
                        MessageType.Warning);
                }
                else if (!_uiCamera.hasMultipleDifferentValues && _uiCamera.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "UI Camera is optional for Overlay canvases. Assign it when camera-space conversion or Camera render mode is used.",
                        MessageType.Info);
                }

                InspectorUiUtility.EndPanel();
            }

            string layerBadge = _layerList.hasMultipleDifferentValues ? "MIXED" : $"{_layerList.arraySize} LAYERS";
            _showLayers = InspectorUiUtility.DrawFoldoutHeader(
                "Layer Composition",
                _showLayers,
                InspectorUiUtility.AssetColor,
                layerBadge,
                _layerList.hasMultipleDifferentValues || _layerList.arraySize > 0
                    ? InspectorUiUtility.AssetColor
                    : InspectorUiUtility.WarningColor);
            if (_showLayers)
            {
                InspectorUiUtility.BeginPanel();
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_layerList, new GUIContent(
                        "Layers",
                        "Ordered layer components registered by this root. Each component and Layer Name must be unique."), true);
                }
                EditorGUILayout.HelpBox(
                    "List order is the root's deterministic layer registration order. Window ordering inside each layer is controlled by UIWindowConfiguration.Priority.",
                    MessageType.None);
                InspectorUiUtility.EndPanel();
            }

            int issueCount = CountValidationIssues();
            _showValidation = InspectorUiUtility.DrawFoldoutHeader(
                "Validation",
                _showValidation,
                InspectorUiUtility.RuntimeColor,
                GetValidationBadge(issueCount),
                issueCount == 0 ? InspectorUiUtility.SuccessColor : InspectorUiUtility.WarningColor);
            if (_showValidation)
            {
                InspectorUiUtility.BeginPanel();
                DrawValidation(issueCount);
                InspectorUiUtility.EndPanel();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private int CountValidationIssues()
        {
            if (serializedObject.isEditingMultipleObjects ||
                _rootCanvas.hasMultipleDifferentValues ||
                _layerList.hasMultipleDifferentValues)
            {
                return -1;
            }

            int count = _rootCanvas.objectReferenceValue == null ? 1 : 0;
            for (int i = 0; i < _layerList.arraySize; i++)
            {
                UILayer layer = GetLayer(i);
                if (layer == null || string.IsNullOrWhiteSpace(layer.LayerName))
                {
                    count++;
                    continue;
                }

                for (int previousIndex = 0; previousIndex < i; previousIndex++)
                {
                    UILayer previous = GetLayer(previousIndex);
                    if (previous == null)
                    {
                        continue;
                    }

                    if (ReferenceEquals(previous, layer) ||
                        string.Equals(previous.LayerName, layer.LayerName, StringComparison.Ordinal))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private void DrawValidation(int issueCount)
        {
            if (issueCount < 0)
            {
                EditorGUILayout.HelpBox(
                    "Multiple roots are selected. Shared properties remain editable; detailed validation is shown for a single root only.",
                    MessageType.Info);
                return;
            }

            InspectorUiUtility.DrawStatusRow(
                "Configuration",
                issueCount == 0 ? "Ready" : $"{issueCount} Issue(s)",
                issueCount == 0 ? InspectorUiUtility.SuccessColor : InspectorUiUtility.WarningColor);

            if (_rootCanvas.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Root Canvas is required.", MessageType.Warning);
            }

            for (int i = 0; i < _layerList.arraySize; i++)
            {
                UILayer layer = GetLayer(i);
                if (layer == null)
                {
                    EditorGUILayout.HelpBox($"Layer element {i} is not assigned.", MessageType.Warning);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(layer.LayerName))
                {
                    EditorGUILayout.HelpBox($"Layer element {i} has an empty Layer Name.", MessageType.Warning);
                }

                for (int previousIndex = 0; previousIndex < i; previousIndex++)
                {
                    UILayer previous = GetLayer(previousIndex);
                    if (previous == null)
                    {
                        continue;
                    }

                    if (ReferenceEquals(previous, layer))
                    {
                        EditorGUILayout.HelpBox(
                            $"Layer element {i} duplicates the component at element {previousIndex}.",
                            MessageType.Warning);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(layer.LayerName) &&
                        string.Equals(previous.LayerName, layer.LayerName, StringComparison.Ordinal))
                    {
                        EditorGUILayout.HelpBox(
                            $"Layer name '{layer.LayerName}' is duplicated at elements {previousIndex} and {i}.",
                            MessageType.Warning);
                        break;
                    }
                }
            }

            if (issueCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "The root has a Canvas and all registered layers have unique components and names.",
                    MessageType.Info);
            }
        }

        private UILayer GetLayer(int index)
        {
            return _layerList.GetArrayElementAtIndex(index).objectReferenceValue as UILayer;
        }

        private string GetValidationBadge(int issueCount)
        {
            if (issueCount < 0)
            {
                return "MULTI-EDIT";
            }

            return issueCount == 0 ? "READY" : $"{issueCount} ISSUES";
        }
    }
}
