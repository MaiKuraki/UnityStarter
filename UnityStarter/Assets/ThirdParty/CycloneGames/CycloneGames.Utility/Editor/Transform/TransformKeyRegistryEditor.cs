using System;
using System.Collections.Generic;
using System.Globalization;

using CycloneGames.Utility.Runtime;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(TransformKeyRegistry))]
    public sealed class TransformKeyRegistryEditor : UnityEditor.Editor
    {
        private const float SmallButtonWidth = 24f;
        private const float Spacing = 4f;

        private static readonly Color IndexColor = new Color(0.17f, 0.49f, 0.67f, 1f);
        private static readonly Color EntriesColor = new Color(0.45f, 0.32f, 0.68f, 1f);
        private static readonly GUIContent RemoveContent = new GUIContent("-", "Remove this entry.");
        private static readonly GUIContent AddContent = new GUIContent("Add Entry");
        private static readonly GUIContent BuildOnAwakeContent = new GUIContent("Build On Awake");
        private static readonly GUIContent IncludeNestedContent = new GUIContent("Include Nested Registries");
        private static readonly GUIContent FindFallbackContent = new GUIContent("Transform.Find Fallback");
        private static readonly GUIContent CollectContent = new GUIContent("Collect Direct Children");
        private static readonly GUIContent RebuildContent = new GUIContent("Rebuild Runtime Index");

        private readonly HashSet<string> _validationKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<Transform> _collectTransforms = new HashSet<Transform>();

        private SerializedProperty _script;
        private SerializedProperty _entries;
        private SerializedProperty _autoBuild;
        private SerializedProperty _includeNested;
        private SerializedProperty _findFallback;

        private bool _indexExpanded = true;
        private bool _entriesExpanded = true;
        private bool _validationDirty = true;
        private ValidationSummary _validation;
        private string _emptyKeyMessage;
        private string _missingTransformMessage;
        private string _duplicateKeyMessage;
        private string _externalTransformMessage;
        private int _cachedEntryCount = int.MinValue;
        private int _cachedDuplicateCount = int.MinValue;
        private int _cachedInvalidCount = int.MinValue;
        private string _entryCountText = string.Empty;
        private string _duplicateCountText = string.Empty;
        private string _invalidCountText = string.Empty;

        private void OnEnable()
        {
            _script = serializedObject.FindProperty("m_Script");
            _entries = serializedObject.FindProperty("Entries");
            _autoBuild = serializedObject.FindProperty("AutoBuildOnAwake");
            _includeNested = serializedObject.FindProperty("IncludeNestedRegistries");
            _findFallback = serializedObject.FindProperty("UseTransformFindFallback");
            Undo.undoRedoPerformed += InvalidateValidation;
            EditorApplication.hierarchyChanged += InvalidateValidation;
            _validationDirty = true;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= InvalidateValidation;
            EditorApplication.hierarchyChanged -= InvalidateValidation;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InspectorUiUtility.DrawScriptProperty(_script);
            InspectorUiUtility.DrawModuleHeader(
                "Transform Key Registry",
                "Builds a deterministic key index. Empty entries are ignored and the first valid duplicate key wins.");

            EditorGUI.BeginChangeCheck();

            _indexExpanded = InspectorUiUtility.DrawFoldoutHeader("Index Policy", _indexExpanded, IndexColor);
            if (_indexExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_autoBuild, BuildOnAwakeContent);
                    EditorGUILayout.PropertyField(_includeNested, IncludeNestedContent);
                    EditorGUILayout.PropertyField(_findFallback, FindFallbackContent);
                }

                if (!_findFallback.hasMultipleDifferentValues && _findFallback.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Transform.Find is an unbounded compatibility slow path. Do not call it from a frame hot path.",
                        MessageType.Warning);
                }
            }

            _entriesExpanded = InspectorUiUtility.DrawFoldoutHeader("Entries", _entriesExpanded, EntriesColor);
            if (_entriesExpanded)
            {
                if (serializedObject.isEditingMultipleObjects)
                {
                    EditorGUILayout.HelpBox(
                        "Multi-object editing uses Unity's standard array editor so different array sizes remain explicit.",
                        MessageType.Info);
                    EditorGUILayout.PropertyField(_entries, true);
                }
                else
                {
                    DrawSingleTargetEntries();
                }
            }

            bool controlsChanged = EditorGUI.EndChangeCheck();
            bool applied = serializedObject.ApplyModifiedProperties();
            if (controlsChanged || applied)
            {
                InvalidateValidation();
            }

            DrawValidation();
            DrawActions();
            DrawRuntimeStatus();
        }

        private void DrawSingleTargetEntries()
        {
            Rect sizeRect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginChangeCheck();
            int requestedSize = EditorGUI.IntField(sizeRect, "Size", _entries.arraySize);
            if (EditorGUI.EndChangeCheck())
            {
                _entries.arraySize = Mathf.Max(0, requestedSize);
            }

            Rect headerRect = EditorGUILayout.GetControlRect();
            float keyWidth = (headerRect.width - SmallButtonWidth - Spacing * 2f) * 0.38f;
            float transformWidth = headerRect.width - keyWidth - SmallButtonWidth - Spacing * 2f;
            Rect keyHeader = new Rect(headerRect.x, headerRect.y, keyWidth, headerRect.height);
            Rect transformHeader = new Rect(keyHeader.xMax + Spacing, headerRect.y, transformWidth, headerRect.height);
            EditorGUI.LabelField(keyHeader, "Key");
            EditorGUI.LabelField(transformHeader, "Transform");

            for (int i = 0; i < _entries.arraySize; i++)
            {
                SerializedProperty entry = _entries.GetArrayElementAtIndex(i);
                SerializedProperty key = entry.FindPropertyRelative("Key");
                SerializedProperty transformProperty = entry.FindPropertyRelative("Transform");
                Rect row = EditorGUILayout.GetControlRect();
                Rect keyRect = new Rect(row.x, row.y, keyWidth, row.height);
                Rect transformRect = new Rect(keyRect.xMax + Spacing, row.y, transformWidth, row.height);
                Rect removeRect = new Rect(transformRect.xMax + Spacing, row.y, SmallButtonWidth, row.height);
                EditorGUI.PropertyField(keyRect, key, GUIContent.none);
                EditorGUI.PropertyField(transformRect, transformProperty, GUIContent.none);

                if (GUI.Button(removeRect, RemoveContent))
                {
                    _entries.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            Rect addRect = EditorGUILayout.GetControlRect();
            addRect.width = 100f;
            if (GUI.Button(addRect, AddContent))
            {
                int index = _entries.arraySize;
                _entries.arraySize++;
                SerializedProperty entry = _entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Key").stringValue = string.Empty;
                entry.FindPropertyRelative("Transform").objectReferenceValue = null;
            }
        }

        private void DrawValidation()
        {
            if (_validationDirty && Event.current.type == EventType.Layout)
            {
                RebuildValidation();
            }

            if (_validation.EmptyKeyCount > 0)
            {
                EditorGUILayout.HelpBox(_emptyKeyMessage, MessageType.Warning);
            }

            if (_validation.MissingTransformCount > 0)
            {
                EditorGUILayout.HelpBox(_missingTransformMessage, MessageType.Warning);
            }

            if (_validation.DuplicateKeyCount > 0)
            {
                EditorGUILayout.HelpBox(_duplicateKeyMessage, MessageType.Error);
            }

            if (_validation.ExternalTransformCount > 0)
            {
                EditorGUILayout.HelpBox(_externalTransformMessage, MessageType.Info);
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(4f);
            Rect row = EditorGUILayout.GetControlRect();
            float halfWidth = (row.width - Spacing) * 0.5f;
            Rect collectRect = new Rect(row.x, row.y, halfWidth, row.height);
            Rect rebuildRect = new Rect(collectRect.xMax + Spacing, row.y, halfWidth, row.height);

            if (GUI.Button(collectRect, CollectContent))
            {
                CollectDirectChildrenForTargets();
            }

            if (GUI.Button(rebuildRect, RebuildContent))
            {
                RebuildTargets();
            }
        }

        private void DrawRuntimeStatus()
        {
            if (serializedObject.isEditingMultipleObjects || !(target is TransformKeyRegistry registry))
            {
                return;
            }

            EditorGUILayout.Space(3f);
            UpdateRuntimeStatusText(registry);
            InspectorUiUtility.DrawReadOnlyStat("Index State", registry.IsBuilt ? "Built" : "Not built");
            InspectorUiUtility.DrawReadOnlyStat("Indexed Entries", _entryCountText);
            if (registry.IsBuilt)
            {
                InspectorUiUtility.DrawReadOnlyStat("Ignored Duplicates", _duplicateCountText);
                InspectorUiUtility.DrawReadOnlyStat("Invalid Entries", _invalidCountText);
            }
        }

        private void UpdateRuntimeStatusText(TransformKeyRegistry registry)
        {
            if (_cachedEntryCount != registry.EntryCount)
            {
                _cachedEntryCount = registry.EntryCount;
                _entryCountText = _cachedEntryCount.ToString(CultureInfo.InvariantCulture);
            }

            if (_cachedDuplicateCount != registry.DuplicateKeyCount)
            {
                _cachedDuplicateCount = registry.DuplicateKeyCount;
                _duplicateCountText = _cachedDuplicateCount.ToString(CultureInfo.InvariantCulture);
            }

            if (_cachedInvalidCount != registry.InvalidEntryCount)
            {
                _cachedInvalidCount = registry.InvalidEntryCount;
                _invalidCountText = _cachedInvalidCount.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void RebuildValidation()
        {
            ValidationSummary summary = default;
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                TransformKeyRegistry registry = targets[targetIndex] as TransformKeyRegistry;
                if (registry == null)
                {
                    continue;
                }

                using (var registryObject = new SerializedObject(registry))
                {
                    registryObject.UpdateIfRequiredOrScript();
                    SerializedProperty entries = registryObject.FindProperty("Entries");
                    _validationKeys.Clear();
                    Transform root = registry.transform;

                    for (int i = 0; i < entries.arraySize; i++)
                    {
                        SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                        string key = entry.FindPropertyRelative("Key").stringValue;
                        Transform value = entry.FindPropertyRelative("Transform").objectReferenceValue as Transform;
                        if (string.IsNullOrEmpty(key))
                        {
                            summary.EmptyKeyCount++;
                        }

                        if (value == null)
                        {
                            summary.MissingTransformCount++;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(key) && !_validationKeys.Add(key))
                            {
                                summary.DuplicateKeyCount++;
                            }

                            if (value != root && !value.IsChildOf(root))
                            {
                                summary.ExternalTransformCount++;
                            }
                        }
                    }
                }
            }

            _validation = summary;
            _emptyKeyMessage = summary.EmptyKeyCount > 0
                ? string.Concat(summary.EmptyKeyCount, " entries have empty keys and are ignored.")
                : null;
            _missingTransformMessage = summary.MissingTransformCount > 0
                ? string.Concat(summary.MissingTransformCount, " entries have no Transform and are ignored.")
                : null;
            _duplicateKeyMessage = summary.DuplicateKeyCount > 0
                ? string.Concat(
                    summary.DuplicateKeyCount,
                    " duplicate keys are ignored after the first valid authored entry.")
                : null;
            _externalTransformMessage = summary.ExternalTransformCount > 0
                ? string.Concat(
                    summary.ExternalTransformCount,
                    " entries reference Transforms outside their registry root. Verify ownership and lifetime.")
                : null;
            _validationDirty = false;
        }

        private void CollectDirectChildrenForTargets()
        {
            serializedObject.ApplyModifiedProperties();
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                TransformKeyRegistry registry = targets[targetIndex] as TransformKeyRegistry;
                if (registry == null)
                {
                    continue;
                }

                using (var registryObject = new SerializedObject(registry))
                {
                    registryObject.UpdateIfRequiredOrScript();
                    SerializedProperty entries = registryObject.FindProperty("Entries");
                    _collectTransforms.Clear();
                    for (int i = 0; i < entries.arraySize; i++)
                    {
                        Transform existing = entries
                            .GetArrayElementAtIndex(i)
                            .FindPropertyRelative("Transform")
                            .objectReferenceValue as Transform;
                        if (existing != null)
                        {
                            _collectTransforms.Add(existing);
                        }
                    }

                    Transform root = registry.transform;
                    int childCount = root.childCount;
                    for (int childIndex = 0; childIndex < childCount; childIndex++)
                    {
                        Transform child = root.GetChild(childIndex);
                        if (!_collectTransforms.Add(child))
                        {
                            continue;
                        }

                        int entryIndex = entries.arraySize;
                        entries.arraySize++;
                        SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
                        entry.FindPropertyRelative("Key").stringValue = child.name;
                        entry.FindPropertyRelative("Transform").objectReferenceValue = child;
                    }

                    registryObject.ApplyModifiedProperties();
                }
            }

            serializedObject.Update();
            InvalidateValidation();
        }

        private void RebuildTargets()
        {
            serializedObject.ApplyModifiedProperties();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is TransformKeyRegistry registry)
                {
                    registry.BuildIndex();
                }
            }

            Repaint();
        }

        private void InvalidateValidation()
        {
            _validationDirty = true;
            Repaint();
        }

        private struct ValidationSummary
        {
            public int EmptyKeyCount;
            public int MissingTransformCount;
            public int DuplicateKeyCount;
            public int ExternalTransformCount;
        }
    }
}
