// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(UILayer), true)]
    [CanEditMultipleObjects]
    public sealed class UILayerEditor : UnityEditor.Editor
    {
        private SerializedProperty _layerName;

        private bool _showAuthoring = true;
        private bool _showRuntime = true;

        private void OnEnable()
        {
            _layerName = serializedObject.FindProperty("layerName");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            string subtitle = _layerName.hasMultipleDifferentValues || string.IsNullOrWhiteSpace(_layerName.stringValue)
                ? "Ordered owner for a group of UI windows"
                : $"Runtime layer: {_layerName.stringValue}";
            InspectorUiUtility.DrawInspectorTitle(
                "UI Layer",
                subtitle,
                InspectorUiUtility.SetupColor);

            bool isValid = _layerName.hasMultipleDifferentValues || !string.IsNullOrWhiteSpace(_layerName.stringValue);
            _showAuthoring = InspectorUiUtility.DrawFoldoutHeader(
                "Layer Authoring",
                _showAuthoring,
                InspectorUiUtility.SetupColor,
                isValid ? "READY" : "REQUIRED",
                isValid ? InspectorUiUtility.SuccessColor : InspectorUiUtility.WarningColor);
            if (_showAuthoring)
            {
                InspectorUiUtility.BeginPanel();
                EditorGUILayout.PropertyField(_layerName, new GUIContent(
                    "Layer Name",
                    "Stable name used by layer configurations and runtime lookup."));

                if (!_layerName.hasMultipleDifferentValues && string.IsNullOrWhiteSpace(_layerName.stringValue))
                {
                    EditorGUILayout.HelpBox(
                        "Layer Name is required and must match the name stored by its UILayerConfiguration.",
                        MessageType.Warning);
                }

                InspectorUiUtility.EndPanel();
            }

            string runtimeBadge = EditorApplication.isPlaying ? "LIVE" : "PLAY MODE";
            _showRuntime = InspectorUiUtility.DrawFoldoutHeader(
                "Runtime Status",
                _showRuntime,
                InspectorUiUtility.RuntimeColor,
                runtimeBadge,
                EditorApplication.isPlaying ? InspectorUiUtility.SuccessColor : InspectorUiUtility.NeutralColor);
            if (_showRuntime)
            {
                InspectorUiUtility.BeginPanel();
                if (EditorApplication.isPlaying && !serializedObject.isEditingMultipleObjects)
                {
                    UILayer layer = target as UILayer;
                    InspectorUiUtility.DrawStatusRow(
                        "Registered Windows",
                        layer == null ? "Unavailable" : layer.WindowCount.ToString(),
                        InspectorUiUtility.SuccessColor);
                    InspectorUiUtility.DrawStatusRow(
                        "Layer Name",
                        layer == null || string.IsNullOrWhiteSpace(layer.LayerName) ? "Empty" : layer.LayerName,
                        layer == null || string.IsNullOrWhiteSpace(layer.LayerName)
                            ? InspectorUiUtility.WarningColor
                            : InspectorUiUtility.SetupColor);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Enter Play Mode to inspect the registered window count. Runtime values refresh only when Unity repaints this Inspector.",
                        MessageType.Info);
                }

                InspectorUiUtility.EndPanel();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
