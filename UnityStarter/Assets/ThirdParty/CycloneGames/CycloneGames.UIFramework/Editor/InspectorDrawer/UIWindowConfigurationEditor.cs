// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(UIWindowConfiguration), true)]
    [CanEditMultipleObjects]
    public sealed class UIWindowConfigurationEditor : UnityEditor.Editor
    {
        private static readonly Color DirectSourceColor = new Color(0.16f, 0.62f, 0.39f);
        private static readonly Color AssetSourceColor = new Color(0.10f, 0.55f, 0.82f);
        private static readonly Color PathSourceColor = new Color(0.86f, 0.50f, 0.10f);

        private static readonly GUIContent DirectSourceContent = new GUIContent(
            "Direct Ref",
            "Use a direct UIWindow prefab reference. Suitable for always-available UI and tests.");
        private static readonly GUIContent AssetSourceContent = new GUIContent(
            "Asset Ref",
            "Use a provider-neutral runtime location with an optional Editor prefab for authoring validation.");
        private static readonly GUIContent PathSourceContent = new GUIContent(
            "Path",
            "Pass an exact provider-specific runtime address to the configured asset provider.");

        private SerializedProperty _windowId;
        private SerializedProperty _source;
        private SerializedProperty _windowPrefab;
        private SerializedProperty _prefabAssetRef;
        private SerializedProperty _prefabLocation;
        private SerializedProperty _layer;
        private SerializedProperty _priority;
        private SerializedProperty _isSceneBound;
        private SerializedProperty _subCanvasPolicy;

        private bool _showIdentity = true;
        private bool _showPrefab = true;
        private bool _showPlacement = true;
        private bool _showValidation = true;

        private string _cachedEditorGuid;
        private GameObject _cachedTrackedPrefab;
        private bool _cachedTrackedPrefabHasWindow;

        private void OnEnable()
        {
            _windowId = serializedObject.FindProperty("windowId");
            _source = serializedObject.FindProperty("source");
            _windowPrefab = serializedObject.FindProperty("windowPrefab");
            _prefabAssetRef = serializedObject.FindProperty("prefabAssetRef");
            _prefabLocation = serializedObject.FindProperty("prefabLocation");
            _layer = serializedObject.FindProperty("layer");
            _priority = serializedObject.FindProperty("priority");
            _isSceneBound = serializedObject.FindProperty("isSceneBound");
            _subCanvasPolicy = serializedObject.FindProperty("subCanvasPolicy");
            _cachedEditorGuid = null;
            _cachedTrackedPrefab = null;
            _cachedTrackedPrefabHasWindow = false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            string subtitle = _windowId.hasMultipleDifferentValues || string.IsNullOrWhiteSpace(_windowId.stringValue)
                ? "Stable identity, loading source, and layer placement"
                : $"Window contract: {_windowId.stringValue}";
            InspectorUiUtility.DrawInspectorTitle(
                "UI Window Configuration",
                subtitle,
                InspectorUiUtility.AssetColor);

            bool hasId = _windowId.hasMultipleDifferentValues || !string.IsNullOrWhiteSpace(_windowId.stringValue);
            _showIdentity = InspectorUiUtility.DrawFoldoutHeader(
                "Identity",
                _showIdentity,
                InspectorUiUtility.SetupColor,
                hasId ? "READY" : "REQUIRED",
                hasId ? InspectorUiUtility.SuccessColor : InspectorUiUtility.WarningColor);
            if (_showIdentity)
            {
                InspectorUiUtility.BeginPanel();
                EditorGUILayout.PropertyField(_windowId, new GUIContent(
                    "Window ID",
                    "Stable identifier used for registration, lookup, and navigation. Keep it stable across content updates."));
                if (!_windowId.hasMultipleDifferentValues && string.IsNullOrWhiteSpace(_windowId.stringValue))
                {
                    EditorGUILayout.HelpBox(
                        "Window ID is required. Use a stable product-facing identifier rather than a scene object name.",
                        MessageType.Warning);
                }
                InspectorUiUtility.EndPanel();
            }

            _showPrefab = InspectorUiUtility.DrawFoldoutHeader(
                "Prefab Source",
                _showPrefab,
                GetSourceColor(),
                GetSourceBadge(),
                GetSourceColor());
            if (_showPrefab)
            {
                InspectorUiUtility.BeginPanel();
                DrawSourceSelector();
                EditorGUILayout.Space(4f);
                if (_source.hasMultipleDifferentValues)
                {
                    EditorGUILayout.HelpBox(
                        "The selected configurations use different prefab sources. Choose a source above to edit all selected assets together.",
                        MessageType.Info);
                }
                else
                {
                    DrawSource((UIWindowConfiguration.PrefabSource)_source.enumValueIndex);
                }
                InspectorUiUtility.EndPanel();
            }

            _showPlacement = InspectorUiUtility.DrawFoldoutHeader(
                "Layer & Lifetime",
                _showPlacement,
                InspectorUiUtility.RuntimeColor,
                _layer.hasMultipleDifferentValues || _layer.objectReferenceValue != null ? "PLACED" : "REQUIRED",
                _layer.hasMultipleDifferentValues || _layer.objectReferenceValue != null
                    ? InspectorUiUtility.RuntimeColor
                    : InspectorUiUtility.WarningColor);
            if (_showPlacement)
            {
                InspectorUiUtility.BeginPanel();
                EditorGUILayout.PropertyField(_layer, new GUIContent(
                    "Layer",
                    "Layer configuration that owns this window at runtime."));
                EditorGUILayout.PropertyField(_priority, new GUIContent(
                    "Priority",
                    "Ascending order within the target layer. Equal priorities retain deterministic registration order."));
                EditorGUILayout.PropertyField(_isSceneBound, new GUIContent(
                    "Scene Bound",
                    "Close the window when the active scene changes away from the scene that opened it."));
                EditorGUILayout.PropertyField(_subCanvasPolicy, new GUIContent(
                    "Sub-Canvas Policy",
                    "Inherit the layer canvas for batching, or isolate the window when it needs its own Canvas boundary."));

                if (!_subCanvasPolicy.hasMultipleDifferentValues &&
                    _subCanvasPolicy.enumValueIndex == (int)UIWindowConfiguration.SubCanvasPolicy.IsolatedCanvas)
                {
                    EditorGUILayout.HelpBox(
                        "An isolated Canvas can reduce rebuild propagation for frequently changing windows, but may add draw calls. Verify the trade-off with the Unity Profiler and Frame Debugger.",
                        MessageType.Info);
                }
                InspectorUiUtility.EndPanel();
            }

            int issueCount = CountValidationIssues();
            _showValidation = InspectorUiUtility.DrawFoldoutHeader(
                "Validation & Quick Actions",
                _showValidation,
                InspectorUiUtility.AssetColor,
                GetValidationBadge(issueCount),
                issueCount == 0 ? InspectorUiUtility.SuccessColor : InspectorUiUtility.WarningColor);
            if (_showValidation)
            {
                InspectorUiUtility.BeginPanel();
                DrawValidation(issueCount);
                DrawQuickActions();
                InspectorUiUtility.EndPanel();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSourceSelector()
        {
            EditorGUILayout.LabelField("Loading Contract", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            DrawSourceButton(
                DirectSourceContent,
                UIWindowConfiguration.PrefabSource.PrefabReference,
                DirectSourceColor,
                EditorStyles.miniButtonLeft);
            DrawSourceButton(
                AssetSourceContent,
                UIWindowConfiguration.PrefabSource.AssetReference,
                AssetSourceColor,
                EditorStyles.miniButtonMid);
            DrawSourceButton(
                PathSourceContent,
                UIWindowConfiguration.PrefabSource.PathLocation,
                PathSourceColor,
                EditorStyles.miniButtonRight);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSourceButton(
            GUIContent content,
            UIWindowConfiguration.PrefabSource source,
            Color activeColor,
            GUIStyle style)
        {
            bool active = !_source.hasMultipleDifferentValues && _source.enumValueIndex == (int)source;
            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = active ? activeColor : InspectorUiUtility.NeutralColor;
            if (GUILayout.Button(content, style, GUILayout.Height(24f)))
            {
                _source.enumValueIndex = (int)source;
            }
            GUI.backgroundColor = previousColor;
        }

        private void DrawSource(UIWindowConfiguration.PrefabSource source)
        {
            switch (source)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    InspectorUiUtility.DrawStatusRow("Contract", "Direct Reference", DirectSourceColor);
                    EditorGUILayout.PropertyField(_windowPrefab, new GUIContent(
                        "Window Prefab",
                        "Direct UIWindow component stored in the project asset."));
                    EditorGUILayout.HelpBox(
                        "The prefab is available without a provider lease. Use this for built-in or always-loaded windows.",
                        MessageType.None);
                    break;

                case UIWindowConfiguration.PrefabSource.AssetReference:
                    InspectorUiUtility.DrawStatusRow("Contract", "Asset Reference", AssetSourceColor);
                    DrawAssetReference();
                    EditorGUILayout.HelpBox(
                        "Runtime Location is passed unchanged to the configured IUIWindowAssetProvider. Editor Prefab is authoring-only and does not define the runtime address.",
                        MessageType.None);
                    break;

                case UIWindowConfiguration.PrefabSource.PathLocation:
                    InspectorUiUtility.DrawStatusRow("Contract", "Provider Path", PathSourceColor);
                    EditorGUILayout.PropertyField(_prefabLocation, new GUIContent(
                        "Prefab Location",
                        "Exact provider-specific runtime address. UIFramework does not reinterpret this value."));
                    EditorGUILayout.HelpBox(
                        "Use the exact address expected by the runtime provider. It may be a project path, Addressables key, YooAsset location, or another provider-specific identifier.",
                        MessageType.None);
                    break;

                default:
                    EditorGUILayout.HelpBox("Prefab Source is not supported.", MessageType.Error);
                    break;
            }
        }

        private void DrawAssetReference()
        {
            SerializedProperty location = _prefabAssetRef.FindPropertyRelative("location");
            SerializedProperty editorGuid = _prefabAssetRef.FindPropertyRelative("editorGuid");

            EditorGUILayout.PropertyField(location, new GUIContent(
                "Runtime Location",
                "Exact address consumed by the configured runtime asset provider."));

            GameObject trackedPrefab = ResolveTrackedPrefab(editorGuid);
            EditorGUI.showMixedValue = editorGuid.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            GameObject selectedPrefab = EditorGUILayout.ObjectField(
                new GUIContent(
                    "Editor Prefab",
                    "Optional project prefab used only for authoring validation. Its GUID is not a runtime address."),
                trackedPrefab,
                typeof(GameObject),
                false) as GameObject;
            if (EditorGUI.EndChangeCheck())
            {
                editorGuid.stringValue = selectedPrefab == null
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedPrefab));
                _cachedEditorGuid = null;
                _cachedTrackedPrefab = selectedPrefab;
                _cachedTrackedPrefabHasWindow =
                    selectedPrefab != null && selectedPrefab.TryGetComponent(out UIWindow _);
            }

            EditorGUI.showMixedValue = false;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(editorGuid, new GUIContent(
                    "Editor GUID",
                    "Stable Editor-only project identity retained when the prefab moves."));
            }
        }

        private GameObject ResolveTrackedPrefab(SerializedProperty editorGuid)
        {
            if (editorGuid.hasMultipleDifferentValues || string.IsNullOrWhiteSpace(editorGuid.stringValue))
            {
                return null;
            }

            if (string.Equals(_cachedEditorGuid, editorGuid.stringValue, System.StringComparison.Ordinal))
            {
                return _cachedTrackedPrefab;
            }

            _cachedEditorGuid = editorGuid.stringValue;
            string assetPath = AssetDatabase.GUIDToAssetPath(_cachedEditorGuid);
            _cachedTrackedPrefab = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            _cachedTrackedPrefabHasWindow =
                _cachedTrackedPrefab != null && _cachedTrackedPrefab.TryGetComponent(out UIWindow _);
            return _cachedTrackedPrefab;
        }

        private int CountValidationIssues()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                return -1;
            }

            int count = string.IsNullOrWhiteSpace(_windowId.stringValue) ? 1 : 0;
            UILayerConfiguration layer = _layer.objectReferenceValue as UILayerConfiguration;
            if (layer == null || !layer.IsValid)
            {
                count++;
            }

            if (_source.hasMultipleDifferentValues)
            {
                return count;
            }

            switch ((UIWindowConfiguration.PrefabSource)_source.enumValueIndex)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    if (_windowPrefab.objectReferenceValue == null)
                    {
                        count++;
                    }
                    break;

                case UIWindowConfiguration.PrefabSource.AssetReference:
                    SerializedProperty location = _prefabAssetRef.FindPropertyRelative("location");
                    SerializedProperty editorGuid = _prefabAssetRef.FindPropertyRelative("editorGuid");
                    if (string.IsNullOrWhiteSpace(location.stringValue))
                    {
                        count++;
                    }
                    if (!string.IsNullOrWhiteSpace(editorGuid.stringValue))
                    {
                        GameObject prefab = ResolveTrackedPrefab(editorGuid);
                        if (prefab == null || !_cachedTrackedPrefabHasWindow)
                        {
                            count++;
                        }
                    }
                    break;

                case UIWindowConfiguration.PrefabSource.PathLocation:
                    if (string.IsNullOrWhiteSpace(_prefabLocation.stringValue))
                    {
                        count++;
                    }
                    break;

                default:
                    count++;
                    break;
            }

            return count;
        }

        private void DrawValidation(int issueCount)
        {
            if (issueCount < 0)
            {
                EditorGUILayout.HelpBox(
                    "Multiple configurations are selected. Shared properties remain editable; detailed validation is shown for a single configuration only.",
                    MessageType.Info);
                return;
            }

            InspectorUiUtility.DrawStatusRow(
                "Configuration",
                issueCount == 0 ? "Ready" : $"{issueCount} Issue(s)",
                issueCount == 0 ? InspectorUiUtility.SuccessColor : InspectorUiUtility.WarningColor);

            if (string.IsNullOrWhiteSpace(_windowId.stringValue))
            {
                EditorGUILayout.HelpBox("Window ID is required.", MessageType.Warning);
            }

            UILayerConfiguration layer = _layer.objectReferenceValue as UILayerConfiguration;
            if (layer == null)
            {
                EditorGUILayout.HelpBox("Layer is required.", MessageType.Warning);
            }
            else if (!layer.IsValid)
            {
                EditorGUILayout.HelpBox("The assigned layer configuration has an empty Layer Name.", MessageType.Warning);
            }

            if (_source.hasMultipleDifferentValues)
            {
                return;
            }

            switch ((UIWindowConfiguration.PrefabSource)_source.enumValueIndex)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    if (_windowPrefab.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox("Window Prefab is required.", MessageType.Warning);
                    }
                    break;

                case UIWindowConfiguration.PrefabSource.AssetReference:
                    ValidateAssetReference();
                    break;

                case UIWindowConfiguration.PrefabSource.PathLocation:
                    if (string.IsNullOrWhiteSpace(_prefabLocation.stringValue))
                    {
                        EditorGUILayout.HelpBox("Prefab Location is required.", MessageType.Warning);
                    }
                    break;
            }

            if (issueCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "Identity, loading source, and layer placement are configured.",
                    MessageType.Info);
            }
        }

        private void ValidateAssetReference()
        {
            SerializedProperty location = _prefabAssetRef.FindPropertyRelative("location");
            SerializedProperty editorGuid = _prefabAssetRef.FindPropertyRelative("editorGuid");

            if (string.IsNullOrWhiteSpace(location.stringValue))
            {
                EditorGUILayout.HelpBox("Runtime Location is required.", MessageType.Warning);
            }

            if (string.IsNullOrWhiteSpace(editorGuid.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "Editor Prefab is optional. Assign it to enable prefab component validation without changing the runtime address.",
                    MessageType.Info);
                return;
            }

            GameObject prefab = ResolveTrackedPrefab(editorGuid);
            if (prefab == null)
            {
                EditorGUILayout.HelpBox(
                    "Editor GUID does not resolve to a project prefab.",
                    MessageType.Warning);
                return;
            }

            if (!_cachedTrackedPrefabHasWindow)
            {
                EditorGUILayout.HelpBox(
                    "The tracked Editor prefab does not contain a UIWindow component on its root.",
                    MessageType.Warning);
            }
        }

        private void DrawQuickActions()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            Object prefab = GetAuthoringPrefab();
            Object layer = _layer.objectReferenceValue;
            if (prefab == null && layer == null)
            {
                return;
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(prefab == null))
            {
                if (GUILayout.Button("Select Prefab", EditorStyles.miniButtonLeft))
                {
                    Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                }
            }
            using (new EditorGUI.DisabledScope(layer == null))
            {
                if (GUILayout.Button("Select Layer", EditorStyles.miniButtonRight))
                {
                    Selection.activeObject = layer;
                    EditorGUIUtility.PingObject(layer);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private Object GetAuthoringPrefab()
        {
            if (_source.hasMultipleDifferentValues)
            {
                return null;
            }

            switch ((UIWindowConfiguration.PrefabSource)_source.enumValueIndex)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    return _windowPrefab.objectReferenceValue;
                case UIWindowConfiguration.PrefabSource.AssetReference:
                    return ResolveTrackedPrefab(_prefabAssetRef.FindPropertyRelative("editorGuid"));
                default:
                    return null;
            }
        }

        private string GetSourceBadge()
        {
            if (_source.hasMultipleDifferentValues)
            {
                return "MIXED";
            }

            switch ((UIWindowConfiguration.PrefabSource)_source.enumValueIndex)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    return "DIRECT";
                case UIWindowConfiguration.PrefabSource.AssetReference:
                    return "ASSET REF";
                case UIWindowConfiguration.PrefabSource.PathLocation:
                    return "PATH";
                default:
                    return "UNKNOWN";
            }
        }

        private Color GetSourceColor()
        {
            if (_source.hasMultipleDifferentValues)
            {
                return InspectorUiUtility.NeutralColor;
            }

            switch ((UIWindowConfiguration.PrefabSource)_source.enumValueIndex)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    return DirectSourceColor;
                case UIWindowConfiguration.PrefabSource.AssetReference:
                    return AssetSourceColor;
                case UIWindowConfiguration.PrefabSource.PathLocation:
                    return PathSourceColor;
                default:
                    return InspectorUiUtility.NeutralColor;
            }
        }

        private static string GetValidationBadge(int issueCount)
        {
            if (issueCount < 0)
            {
                return "MULTI-EDIT";
            }

            return issueCount == 0 ? "READY" : $"{issueCount} ISSUES";
        }
    }
}
