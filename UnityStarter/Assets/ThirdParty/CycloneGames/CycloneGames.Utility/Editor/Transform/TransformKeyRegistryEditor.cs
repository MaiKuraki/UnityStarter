using UnityEditor;
using UnityEngine;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.Utility.Editor
{
    [CustomEditor(typeof(TransformKeyRegistry))]
    [CanEditMultipleObjects]
    public sealed class TransformKeyRegistryEditor : UnityEditor.Editor
    {
        private const float SMALL_BUTTON_WIDTH = 24f;
        private const float SPACING = 4f;

        private static readonly GUIContent AutoBuildLabel = new GUIContent("Auto Build On Awake");
        private static readonly GUIContent IncludeNestedLabel = new GUIContent("Include Nested Registries");
        private static readonly GUIContent FindFallbackLabel = new GUIContent("Use Transform.Find Fallback");
        private static readonly GUIContent EntriesLabel = new GUIContent("Entries");
        private static readonly GUIContent AddLabel = new GUIContent("Add");
        private static readonly GUIContent RemoveLabel = new GUIContent("-");
        private static readonly GUIContent CollectChildrenLabel = new GUIContent("Collect Direct Children");
        private static readonly GUIContent RebuildLabel = new GUIContent("Rebuild Runtime Index");
        private static readonly GUIContent KeyLabel = new GUIContent("Key");
        private static readonly GUIContent TransformLabel = new GUIContent("Transform");

        private SerializedProperty _scriptProperty;
        private SerializedProperty _entriesProperty;
        private SerializedProperty _autoBuildProperty;
        private SerializedProperty _includeNestedProperty;
        private SerializedProperty _findFallbackProperty;

        private void OnEnable()
        {
            _scriptProperty = serializedObject.FindProperty("m_Script");
            _entriesProperty = serializedObject.FindProperty("Entries");
            _autoBuildProperty = serializedObject.FindProperty("AutoBuildOnAwake");
            _includeNestedProperty = serializedObject.FindProperty("IncludeNestedRegistries");
            _findFallbackProperty = serializedObject.FindProperty("UseTransformFindFallback");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptField();
            EditorGUILayout.PropertyField(_autoBuildProperty, AutoBuildLabel);
            EditorGUILayout.PropertyField(_includeNestedProperty, IncludeNestedLabel);
            EditorGUILayout.PropertyField(_findFallbackProperty, FindFallbackLabel);

            DrawEntries();
            DrawValidation();
            DrawActions();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptField()
        {
            if (_scriptProperty == null)
            {
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_scriptProperty);
            }
        }

        private void DrawEntries()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(EntriesLabel, EditorStyles.boldLabel);

            Rect sizeRect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginChangeCheck();
            int newSize = EditorGUI.IntField(sizeRect, EntriesLabel, _entriesProperty.arraySize);
            if (EditorGUI.EndChangeCheck())
            {
                _entriesProperty.arraySize = newSize < 0 ? 0 : newSize;
            }

            Rect headerRect = EditorGUILayout.GetControlRect();
            float keyWidth = (headerRect.width - SMALL_BUTTON_WIDTH - SPACING * 2f) * 0.38f;
            float transformWidth = headerRect.width - keyWidth - SMALL_BUTTON_WIDTH - SPACING * 2f;
            Rect keyHeaderRect = new Rect(headerRect.x, headerRect.y, keyWidth, headerRect.height);
            Rect transformHeaderRect = new Rect(keyHeaderRect.xMax + SPACING, headerRect.y, transformWidth, headerRect.height);

            EditorGUI.LabelField(keyHeaderRect, KeyLabel);
            EditorGUI.LabelField(transformHeaderRect, TransformLabel);

            for (int i = 0; i < _entriesProperty.arraySize; i++)
            {
                SerializedProperty entryProperty = _entriesProperty.GetArrayElementAtIndex(i);
                SerializedProperty keyProperty = entryProperty.FindPropertyRelative("Key");
                SerializedProperty transformProperty = entryProperty.FindPropertyRelative("Transform");

                Rect rowRect = EditorGUILayout.GetControlRect();
                Rect keyRect = new Rect(rowRect.x, rowRect.y, keyWidth, rowRect.height);
                Rect transformRect = new Rect(keyRect.xMax + SPACING, rowRect.y, transformWidth, rowRect.height);
                Rect removeRect = new Rect(transformRect.xMax + SPACING, rowRect.y, SMALL_BUTTON_WIDTH, rowRect.height);

                EditorGUI.PropertyField(keyRect, keyProperty, GUIContent.none);
                EditorGUI.PropertyField(transformRect, transformProperty, GUIContent.none);

                if (GUI.Button(removeRect, RemoveLabel))
                {
                    _entriesProperty.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            Rect addRect = EditorGUILayout.GetControlRect();
            if (GUI.Button(new Rect(addRect.x, addRect.y, 86f, addRect.height), AddLabel))
            {
                int index = _entriesProperty.arraySize;
                _entriesProperty.arraySize++;
                SerializedProperty entryProperty = _entriesProperty.GetArrayElementAtIndex(index);
                entryProperty.FindPropertyRelative("Key").stringValue = string.Empty;
                entryProperty.FindPropertyRelative("Transform").objectReferenceValue = null;
            }
        }

        private void DrawValidation()
        {
            int emptyKeyCount = 0;
            int missingTransformCount = 0;
            int duplicateKeyCount = 0;
            int externalTransformCount = 0;

            TransformKeyRegistry registry = target as TransformKeyRegistry;
            Transform root = registry == null ? null : registry.transform;

            for (int i = 0; i < _entriesProperty.arraySize; i++)
            {
                SerializedProperty entryProperty = _entriesProperty.GetArrayElementAtIndex(i);
                SerializedProperty keyProperty = entryProperty.FindPropertyRelative("Key");
                SerializedProperty transformProperty = entryProperty.FindPropertyRelative("Transform");
                string key = keyProperty.stringValue;
                Transform value = transformProperty.objectReferenceValue as Transform;

                if (string.IsNullOrEmpty(key))
                {
                    emptyKeyCount++;
                }

                if (value == null)
                {
                    missingTransformCount++;
                }
                else if (root != null && value != root && !value.IsChildOf(root))
                {
                    externalTransformCount++;
                }

                if (!string.IsNullOrEmpty(key) && IsDuplicateKey(i, key))
                {
                    duplicateKeyCount++;
                }
            }

            if (emptyKeyCount > 0)
            {
                EditorGUILayout.HelpBox("Some entries have empty keys. They are ignored by the runtime index.", MessageType.Warning);
            }

            if (missingTransformCount > 0)
            {
                EditorGUILayout.HelpBox("Some entries have no Transform reference. They are ignored by the runtime index.", MessageType.Warning);
            }

            if (duplicateKeyCount > 0)
            {
                EditorGUILayout.HelpBox("Duplicate keys exist in this registry. The first matching entry wins at runtime.", MessageType.Error);
            }

            if (externalTransformCount > 0)
            {
                EditorGUILayout.HelpBox("Some entries reference Transforms outside this registry root. This is allowed, but check ownership carefully.", MessageType.Info);
            }

            if (_findFallbackProperty.boolValue)
            {
                EditorGUILayout.HelpBox("Transform.Find fallback is a slow path. Keep it out of runtime hot paths.", MessageType.Info);
            }
        }

        private bool IsDuplicateKey(int currentIndex, string key)
        {
            for (int i = 0; i < currentIndex; i++)
            {
                SerializedProperty entryProperty = _entriesProperty.GetArrayElementAtIndex(i);
                SerializedProperty keyProperty = entryProperty.FindPropertyRelative("Key");
                if (string.Equals(keyProperty.stringValue, key, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawActions()
        {
            EditorGUILayout.Space();
            Rect rowRect = EditorGUILayout.GetControlRect();
            float halfWidth = (rowRect.width - SPACING) * 0.5f;
            Rect collectRect = new Rect(rowRect.x, rowRect.y, halfWidth, rowRect.height);
            Rect rebuildRect = new Rect(collectRect.xMax + SPACING, rowRect.y, halfWidth, rowRect.height);

            if (GUI.Button(collectRect, CollectChildrenLabel))
            {
                serializedObject.ApplyModifiedProperties();
                CollectDirectChildrenForTargets();
            }

            if (GUI.Button(rebuildRect, RebuildLabel))
            {
                serializedObject.ApplyModifiedProperties();
                RebuildTargets();
                serializedObject.Update();
            }
        }

        private void CollectDirectChildrenForTargets()
        {
            Object[] selectedTargets = targets;
            for (int i = 0; i < selectedTargets.Length; i++)
            {
                TransformKeyRegistry registry = selectedTargets[i] as TransformKeyRegistry;
                if (registry == null)
                {
                    continue;
                }

                SerializedObject registryObject = new SerializedObject(registry);
                SerializedProperty entriesProperty = registryObject.FindProperty("Entries");
                Transform root = registry.transform;
                int childCount = root.childCount;

                for (int childIndex = 0; childIndex < childCount; childIndex++)
                {
                    Transform child = root.GetChild(childIndex);
                    if (ContainsTransform(entriesProperty, child))
                    {
                        continue;
                    }

                    int entryIndex = entriesProperty.arraySize;
                    entriesProperty.arraySize++;
                    SerializedProperty entryProperty = entriesProperty.GetArrayElementAtIndex(entryIndex);
                    entryProperty.FindPropertyRelative("Key").stringValue = child.name;
                    entryProperty.FindPropertyRelative("Transform").objectReferenceValue = child;
                }

                registryObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(registry);
            }

            serializedObject.Update();
        }

        private static bool ContainsTransform(SerializedProperty entriesProperty, Transform transform)
        {
            for (int i = 0; i < entriesProperty.arraySize; i++)
            {
                SerializedProperty entryProperty = entriesProperty.GetArrayElementAtIndex(i);
                SerializedProperty transformProperty = entryProperty.FindPropertyRelative("Transform");
                if (transformProperty.objectReferenceValue == transform)
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildTargets()
        {
            Object[] selectedTargets = targets;
            for (int i = 0; i < selectedTargets.Length; i++)
            {
                TransformKeyRegistry registry = selectedTargets[i] as TransformKeyRegistry;
                if (registry == null)
                {
                    continue;
                }

                registry.BuildIndex();
            }
        }
    }
}
