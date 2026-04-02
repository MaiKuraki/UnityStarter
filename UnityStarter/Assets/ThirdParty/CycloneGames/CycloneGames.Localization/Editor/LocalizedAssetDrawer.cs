#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    [CustomPropertyDrawer(typeof(LocalizedAsset<>), true)]
    public sealed class LocalizedAssetDrawer : PropertyDrawer
    {
        private static readonly GUIContent s_TableLabel = new GUIContent("Table");
        private static readonly GUIContent s_KeyLabel = new GUIContent("Key");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var tableIdProp = property.FindPropertyRelative("m_TableId");
            var entryKeyProp = property.FindPropertyRelative("m_EntryKey");

            EditorGUI.BeginProperty(position, label, property);

            position = EditorGUI.PrefixLabel(position, label);

            float lineH = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            var tableRect = new Rect(position.x, position.y, position.width, lineH);
            var keyRect = new Rect(position.x, position.y + lineH + spacing, position.width, lineH);

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // Table dropdown
            LocalizedFieldHelper.DrawTablePopup(tableRect, tableIdProp, s_TableLabel,
                LocalizedFieldHelper.TableType.Asset);

            // Key dropdown (from selected AssetTable)
            LocalizedFieldHelper.DrawAssetKeyPopup(keyRect, entryKeyProp, s_KeyLabel,
                tableIdProp.stringValue);

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
#endif
