// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(UIAssetContextProvider), true)]
    [CanEditMultipleObjects]
    public sealed class UIAssetContextProviderEditor : UnityEditor.Editor
    {
        private SerializedProperty _contextAsset;
        private SerializedProperty _useEmbeddedSnapshot;
        private SerializedProperty _snapshotConfigBucket;
        private SerializedProperty _snapshotConfigTag;
        private SerializedProperty _snapshotConfigOwner;
        private SerializedProperty _snapshotPrefabBucket;
        private SerializedProperty _snapshotPrefabTag;
        private SerializedProperty _snapshotPrefabOwner;

        private bool _showSource = true;
        private bool _showSnapshot = true;
        private bool _showStatus = true;

        private void OnEnable()
        {
            _contextAsset = serializedObject.FindProperty("contextAsset");
            _useEmbeddedSnapshot = serializedObject.FindProperty("useEmbeddedSnapshot");
            _snapshotConfigBucket = serializedObject.FindProperty("snapshotConfigBucket");
            _snapshotConfigTag = serializedObject.FindProperty("snapshotConfigTag");
            _snapshotConfigOwner = serializedObject.FindProperty("snapshotConfigOwner");
            _snapshotPrefabBucket = serializedObject.FindProperty("snapshotPrefabBucket");
            _snapshotPrefabTag = serializedObject.FindProperty("snapshotPrefabTag");
            _snapshotPrefabOwner = serializedObject.FindProperty("snapshotPrefabOwner");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InspectorUiUtility.DrawInspectorTitle(
                "UI Asset Context Provider",
                "Explicit loading metadata for UI configuration and prefab providers",
                InspectorUiUtility.AssetColor);

            bool hasContext = !_contextAsset.hasMultipleDifferentValues && _contextAsset.objectReferenceValue != null;
            _showSource = InspectorUiUtility.DrawFoldoutHeader(
                "Context Source",
                _showSource,
                InspectorUiUtility.AssetColor,
                hasContext ? "ASSIGNED" : "OPTIONAL",
                hasContext ? InspectorUiUtility.SuccessColor : InspectorUiUtility.NeutralColor);
            if (_showSource)
            {
                InspectorUiUtility.BeginPanel();
                EditorGUILayout.PropertyField(_contextAsset, new GUIContent(
                    "Context Asset",
                    "Optional project asset containing the default UI loading metadata."));
                EditorGUILayout.HelpBox(
                    "The context asset is the shared authoring source. Enable the embedded snapshot when this component must carry an explicit local fallback.",
                    MessageType.None);
                InspectorUiUtility.EndPanel();
            }

            string snapshotBadge = _useEmbeddedSnapshot.hasMultipleDifferentValues
                ? "MIXED"
                : (_useEmbeddedSnapshot.boolValue ? "ENABLED" : "DISABLED");
            Color snapshotColor = !_useEmbeddedSnapshot.hasMultipleDifferentValues && _useEmbeddedSnapshot.boolValue
                ? InspectorUiUtility.SuccessColor
                : InspectorUiUtility.NeutralColor;
            _showSnapshot = InspectorUiUtility.DrawFoldoutHeader(
                "Embedded Snapshot",
                _showSnapshot,
                InspectorUiUtility.SetupColor,
                snapshotBadge,
                snapshotColor);
            if (_showSnapshot)
            {
                InspectorUiUtility.BeginPanel();
                EditorGUILayout.PropertyField(_useEmbeddedSnapshot, new GUIContent(
                    "Use Embedded Snapshot",
                    "Use the serialized metadata below when no context asset is assigned."));

                using (new EditorGUI.DisabledScope(
                           !_useEmbeddedSnapshot.hasMultipleDifferentValues && !_useEmbeddedSnapshot.boolValue))
                {
                    EditorGUILayout.Space(2f);
                    EditorGUILayout.LabelField("Configuration Assets", EditorStyles.miniBoldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.PropertyField(_snapshotConfigBucket, new GUIContent("Bucket"));
                        EditorGUILayout.PropertyField(_snapshotConfigTag, new GUIContent("Tag"));
                        EditorGUILayout.PropertyField(_snapshotConfigOwner, new GUIContent("Owner"));
                    }

                    EditorGUILayout.Space(3f);
                    EditorGUILayout.LabelField("Window Prefabs", EditorStyles.miniBoldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.PropertyField(_snapshotPrefabBucket, new GUIContent("Bucket"));
                        EditorGUILayout.PropertyField(_snapshotPrefabTag, new GUIContent("Tag"));
                        EditorGUILayout.PropertyField(_snapshotPrefabOwner, new GUIContent("Owner"));
                    }
                }

                InspectorUiUtility.EndPanel();
            }

            _showStatus = InspectorUiUtility.DrawFoldoutHeader(
                "Validation & Resolution",
                _showStatus,
                InspectorUiUtility.RuntimeColor,
                GetStatusBadge(),
                GetStatusColor());
            if (_showStatus)
            {
                InspectorUiUtility.BeginPanel();
                DrawStatus();
                InspectorUiUtility.EndPanel();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatus()
        {
            if (serializedObject.isEditingMultipleObjects ||
                _contextAsset.hasMultipleDifferentValues ||
                _useEmbeddedSnapshot.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox(
                    "Multiple providers are selected. Shared fields remain editable; validation is shown for a single provider only.",
                    MessageType.Info);
                return;
            }

            UIAssetContextAsset contextAsset = _contextAsset.objectReferenceValue as UIAssetContextAsset;
            if (contextAsset != null)
            {
                InspectorUiUtility.DrawStatusRow(
                    "Resolution",
                    "Context Asset",
                    InspectorUiUtility.SuccessColor);
                InspectorUiUtility.DrawStatusRow(
                    "Asset Metadata",
                    contextAsset.HasAnyMetadata ? "Available" : "Empty",
                    contextAsset.HasAnyMetadata ? InspectorUiUtility.SuccessColor : InspectorUiUtility.WarningColor);
                if (!contextAsset.HasAnyMetadata)
                {
                    EditorGUILayout.HelpBox(
                        "The assigned context asset does not contain any loading metadata.",
                        MessageType.Info);
                }

                return;
            }

            InspectorUiUtility.DrawStatusRow(
                "Resolution",
                _useEmbeddedSnapshot.boolValue ? "Embedded Snapshot" : "Empty Context",
                _useEmbeddedSnapshot.boolValue ? InspectorUiUtility.SetupColor : InspectorUiUtility.NeutralColor);

            if (!_useEmbeddedSnapshot.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "No context asset is assigned and the embedded snapshot is disabled. The root will use an empty load context.",
                    MessageType.Info);
                return;
            }

            bool empty = IsSnapshotEmpty();
            InspectorUiUtility.DrawStatusRow(
                "Snapshot Metadata",
                empty ? "Empty" : "Available",
                empty ? InspectorUiUtility.WarningColor : InspectorUiUtility.SuccessColor);
            if (empty)
            {
                EditorGUILayout.HelpBox(
                    "The embedded snapshot is enabled but all metadata fields are empty.",
                    MessageType.Info);
            }
        }

        private string GetStatusBadge()
        {
            if (serializedObject.isEditingMultipleObjects ||
                _contextAsset.hasMultipleDifferentValues ||
                _useEmbeddedSnapshot.hasMultipleDifferentValues)
            {
                return "MULTI-EDIT";
            }

            UIAssetContextAsset contextAsset = _contextAsset.objectReferenceValue as UIAssetContextAsset;
            if (contextAsset != null && contextAsset.HasAnyMetadata)
            {
                return "READY";
            }

            return _useEmbeddedSnapshot.boolValue && !IsSnapshotEmpty() ? "READY" : "OPTIONAL";
        }

        private Color GetStatusColor()
        {
            return GetStatusBadge() == "READY"
                ? InspectorUiUtility.SuccessColor
                : InspectorUiUtility.NeutralColor;
        }

        private bool IsSnapshotEmpty()
        {
            return string.IsNullOrWhiteSpace(_snapshotConfigBucket.stringValue) &&
                   string.IsNullOrWhiteSpace(_snapshotConfigTag.stringValue) &&
                   string.IsNullOrWhiteSpace(_snapshotConfigOwner.stringValue) &&
                   string.IsNullOrWhiteSpace(_snapshotPrefabBucket.stringValue) &&
                   string.IsNullOrWhiteSpace(_snapshotPrefabTag.stringValue) &&
                   string.IsNullOrWhiteSpace(_snapshotPrefabOwner.stringValue);
        }
    }
}
