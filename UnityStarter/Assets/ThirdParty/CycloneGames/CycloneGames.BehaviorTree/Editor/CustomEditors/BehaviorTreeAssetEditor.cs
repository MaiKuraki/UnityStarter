using System.Collections.Generic;
using CycloneGames.BehaviorTree.Editor.CustomEditors.NodeEditors;
using CycloneGames.BehaviorTree.Runtime.Compilation;
using CycloneGames.BehaviorTree.Runtime.Core;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors
{
    [CustomEditor(typeof(Runtime.BehaviorTree), true)]
    [CanEditMultipleObjects]
    internal sealed class BehaviorTreeAssetEditor : UnityEditor.Editor
    {
        private const float LineSpacing = 2f;

        private SerializedProperty _schemaEnabled;
        private SerializedProperty _schemaFormatVersion;
        private SerializedProperty _contractVersion;
        private SerializedProperty _keys;
        private ReorderableList _keyList;
        private string _schemaStatus;
        private MessageType _schemaStatusType;
        private readonly List<string> _treeValidationErrors = new List<string>(4);
        private static readonly HashSet<string> ExplicitProperties = new HashSet<string>
        {
            "Root",
            "Nodes",
            "_blackboardSchemaEnabled",
            "_blackboardSchemaFormatVersion",
            "_blackboardContractVersion",
            "_blackboardKeys"
        };

        private static class Styles
        {
            internal static readonly GUIContent DefaultValue = new GUIContent("Default");
            internal static readonly GUIContent HasDefault = new GUIContent(
                "Has Default",
                "Object keys cannot own authoring defaults; inject instance objects from the runner.");

            internal static readonly GUIStyle Header = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 8, 4)
            };

            internal static readonly GUIStyle Box = new GUIStyle("HelpBox")
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 4, 4)
            };
        }

        private void OnEnable()
        {
            _schemaEnabled = serializedObject.FindProperty("_blackboardSchemaEnabled");
            _schemaFormatVersion = serializedObject.FindProperty("_blackboardSchemaFormatVersion");
            _contractVersion = serializedObject.FindProperty("_blackboardContractVersion");
            _keys = serializedObject.FindProperty("_blackboardKeys");
            BuildKeyList();
            RefreshSchemaStatus();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            DrawOverview();
            DrawSchema();
            DrawValidation();
            BehaviorTreeInspectorUi.DrawRemainingProperties(serializedObject, ExplicitProperties);

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i] is Runtime.BehaviorTree tree)
                    {
                        tree.OnValidate();
                    }
                }
                RefreshSchemaStatus();
                _treeValidationErrors.Clear();
            }
        }

        private void DrawOverview()
        {
            EditorGUILayout.LabelField("Behavior Tree", Styles.Header);
            EditorGUILayout.BeginVertical(Styles.Box);

            if (targets.Length == 1 && target is Runtime.BehaviorTree tree)
            {
                int nodeCount = tree.Nodes != null ? tree.Nodes.Count : 0;
                EditorGUILayout.LabelField("Root", tree.Root != null ? tree.Root.name : "Missing");
                EditorGUILayout.LabelField("Authoring Nodes", nodeCount.ToString());
                if (GUILayout.Button("Open Graph Editor", GUILayout.Height(24f)))
                {
                    Selection.activeObject = tree;
                    BehaviorTreeEditor.OpenWindow();
                }
            }
            else
            {
                EditorGUILayout.LabelField($"{targets.Length} behavior tree assets selected.");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSchema()
        {
            EditorGUILayout.LabelField("Blackboard Contract", Styles.Header);
            EditorGUILayout.BeginVertical(Styles.Box);

            EditorGUILayout.PropertyField(
                _schemaEnabled,
                new GUIContent(
                    "Strict Schema",
                    "Open mode preserves dynamic string keys. Strict mode validates names and types at compile time and runtime."));

            if (_schemaEnabled.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox(
                    "Selected assets use different blackboard modes. Schema lists are not batch-edited.",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (!_schemaEnabled.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Legacy Open mode preserves existing dynamic blackboard behavior. Enable Strict Schema explicitly when the key contract is ready.",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_schemaFormatVersion, new GUIContent("Format Version"));
            }
            EditorGUILayout.PropertyField(
                _contractVersion,
                new GUIContent(
                    "Contract Version",
                    "Increment when a released key name, type, sync policy, or default changes incompatibly."));

            if (targets.Length == 1)
            {
                _keyList?.DoLayoutList();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Edit schema entries on one behavior tree at a time to preserve entry order and Undo clarity.",
                    MessageType.Info);
            }

            if (!string.IsNullOrEmpty(_schemaStatus))
            {
                EditorGUILayout.HelpBox(_schemaStatus, _schemaStatusType);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("Validation", Styles.Header);
            EditorGUILayout.BeginVertical(Styles.Box);
            using (new EditorGUI.DisabledScope(targets.Length != 1))
            {
                if (GUILayout.Button("Validate Tree", GUILayout.Height(22f)) &&
                    target is Runtime.BehaviorTree tree)
                {
                    _treeValidationErrors.Clear();
                    _treeValidationErrors.AddRange(BehaviorTreeCompiler.Validate(tree));
                }
            }

            if (_treeValidationErrors.Count == 0)
            {
                EditorGUILayout.LabelField("No validation errors recorded.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                for (int i = 0; i < _treeValidationErrors.Count; i++)
                {
                    EditorGUILayout.HelpBox(_treeValidationErrors[i], MessageType.Error);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void BuildKeyList()
        {
            if (_keys == null)
            {
                return;
            }

            _keyList = new ReorderableList(serializedObject, _keys, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Keys (LocalOnly unless explicitly synchronized)"),
                elementHeightCallback = GetElementHeight,
                drawElementCallback = DrawKeyElement,
                onAddCallback = AddKey
            };
        }

        private float GetElementHeight(int index)
        {
            if (_keys == null || index < 0 || index >= _keys.arraySize)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            SerializedProperty element = _keys.GetArrayElementAtIndex(index);
            SerializedProperty hasDefault = element.FindPropertyRelative("_hasDefaultValue");
            int lineCount = hasDefault != null && hasDefault.boolValue ? 4 : 3;
            return lineCount * EditorGUIUtility.singleLineHeight + (lineCount - 1) * LineSpacing + 4f;
        }

        private void DrawKeyElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (_keys == null || index < 0 || index >= _keys.arraySize)
            {
                return;
            }

            SerializedProperty element = _keys.GetArrayElementAtIndex(index);
            SerializedProperty name = element.FindPropertyRelative("_name");
            SerializedProperty valueType = element.FindPropertyRelative("_valueType");
            SerializedProperty syncFlags = element.FindPropertyRelative("_syncFlags");
            SerializedProperty hasDefault = element.FindPropertyRelative("_hasDefaultValue");

            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(rect, name, GUIContent.none);

            rect.y += rect.height + LineSpacing;
            float halfWidth = (rect.width - 6f) * 0.5f;
            Rect typeRect = new Rect(rect.x, rect.y, halfWidth, rect.height);
            Rect syncRect = new Rect(typeRect.xMax + 6f, rect.y, halfWidth, rect.height);
            EditorGUI.PropertyField(typeRect, valueType, GUIContent.none);
            EditorGUI.PropertyField(syncRect, syncFlags, GUIContent.none);

            rect.y += rect.height + LineSpacing;
            EditorGUI.PropertyField(
                rect,
                hasDefault,
                Styles.HasDefault);

            if (hasDefault.boolValue)
            {
                rect.y += rect.height + LineSpacing;
                DrawDefaultValue(rect, element, valueType.intValue);
            }
        }

        private static void DrawDefaultValue(
            Rect rect,
            SerializedProperty element,
            int valueTypeIndex)
        {
            RuntimeBlackboardValueType valueType = (RuntimeBlackboardValueType)valueTypeIndex;
            switch (valueType)
            {
                case RuntimeBlackboardValueType.Int:
                    EditorGUI.PropertyField(rect, element.FindPropertyRelative("_intDefaultValue"), Styles.DefaultValue);
                    break;
                case RuntimeBlackboardValueType.Float:
                    EditorGUI.PropertyField(rect, element.FindPropertyRelative("_floatDefaultValue"), Styles.DefaultValue);
                    break;
                case RuntimeBlackboardValueType.Bool:
                    EditorGUI.PropertyField(rect, element.FindPropertyRelative("_boolDefaultValue"), Styles.DefaultValue);
                    break;
                case RuntimeBlackboardValueType.Vector3:
                    EditorGUI.PropertyField(rect, element.FindPropertyRelative("_vector3DefaultValue"), Styles.DefaultValue);
                    break;
                case RuntimeBlackboardValueType.Long:
                    EditorGUI.PropertyField(rect, element.FindPropertyRelative("_longDefaultValue"), Styles.DefaultValue);
                    break;
                case RuntimeBlackboardValueType.Long2:
                    DrawLongTuple(
                        rect,
                        element,
                        "_long2X",
                        "_long2Y");
                    break;
                case RuntimeBlackboardValueType.Long3:
                    DrawLongTuple(
                        rect,
                        element,
                        "_long3X",
                        "_long3Y",
                        "_long3Z");
                    break;
                case RuntimeBlackboardValueType.Object:
                    EditorGUI.HelpBox(rect, "Object defaults are intentionally unsupported.", MessageType.Warning);
                    break;
            }
        }

        private static void DrawLongTuple(
            Rect rect,
            SerializedProperty element,
            string firstName,
            string secondName)
        {
            Rect contentRect = EditorGUI.PrefixLabel(rect, Styles.DefaultValue);
            const float spacing = 3f;
            float width = (contentRect.width - spacing) * 0.5f;
            EditorGUI.PropertyField(
                new Rect(contentRect.x, contentRect.y, width, contentRect.height),
                element.FindPropertyRelative(firstName),
                GUIContent.none);
            EditorGUI.PropertyField(
                new Rect(contentRect.x + width + spacing, contentRect.y, width, contentRect.height),
                element.FindPropertyRelative(secondName),
                GUIContent.none);
        }

        private static void DrawLongTuple(
            Rect rect,
            SerializedProperty element,
            string firstName,
            string secondName,
            string thirdName)
        {
            Rect contentRect = EditorGUI.PrefixLabel(rect, Styles.DefaultValue);
            const float spacing = 3f;
            float width = (contentRect.width - spacing * 2f) / 3f;
            EditorGUI.PropertyField(
                new Rect(contentRect.x, contentRect.y, width, contentRect.height),
                element.FindPropertyRelative(firstName),
                GUIContent.none);
            EditorGUI.PropertyField(
                new Rect(contentRect.x + width + spacing, contentRect.y, width, contentRect.height),
                element.FindPropertyRelative(secondName),
                GUIContent.none);
            EditorGUI.PropertyField(
                new Rect(contentRect.x + (width + spacing) * 2f, contentRect.y, width, contentRect.height),
                element.FindPropertyRelative(thirdName),
                GUIContent.none);
        }

        private void AddKey(ReorderableList list)
        {
            int index = _keys.arraySize;
            _keys.InsertArrayElementAtIndex(index);
            SerializedProperty element = _keys.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("_name").stringValue = string.Empty;
            element.FindPropertyRelative("_valueType").intValue = (int)RuntimeBlackboardValueType.Int;
            element.FindPropertyRelative("_syncFlags").intValue = (int)RuntimeBlackboardSyncFlags.LocalOnly;
            element.FindPropertyRelative("_hasDefaultValue").boolValue = false;
            element.FindPropertyRelative("_intDefaultValue").intValue = 0;
            element.FindPropertyRelative("_floatDefaultValue").floatValue = 0f;
            element.FindPropertyRelative("_boolDefaultValue").boolValue = false;
            element.FindPropertyRelative("_vector3DefaultValue").vector3Value = Vector3.zero;
            element.FindPropertyRelative("_longDefaultValue").longValue = 0L;
            element.FindPropertyRelative("_long2X").longValue = 0L;
            element.FindPropertyRelative("_long2Y").longValue = 0L;
            element.FindPropertyRelative("_long3X").longValue = 0L;
            element.FindPropertyRelative("_long3Y").longValue = 0L;
            element.FindPropertyRelative("_long3Z").longValue = 0L;
            list.index = index;
        }

        private void RefreshSchemaStatus()
        {
            _schemaStatus = null;
            _schemaStatusType = MessageType.None;
            if (targets == null || targets.Length != 1 || !(target is Runtime.BehaviorTree tree))
            {
                return;
            }

            if (!tree.BlackboardSchemaEnabled)
            {
                _schemaStatus = "Legacy Open mode: runtime writes are not restricted by an authoring schema.";
                _schemaStatusType = MessageType.Info;
                return;
            }

            if (!tree.TryGetRuntimeBlackboardSchema(
                    out RuntimeBlackboardSchema schema,
                    out string error))
            {
                _schemaStatus = error;
                _schemaStatusType = MessageType.Error;
                return;
            }

            _schemaStatus =
                $"Strict schema ready: {schema.Count} key(s), contract version {schema.ContractVersion}.";
            _schemaStatusType = MessageType.Info;
        }
    }
}
