using System.Collections.Generic;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using UnityEditor;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors.NodeEditors
{
    [CustomEditor(typeof(WaitNode), true)]
    [CanEditMultipleObjects]
    public class WaitNodeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var useRandomProp = serializedObject.FindProperty("_useRandomBetweenTwoConstants");
            var rangeProp = serializedObject.FindProperty("_range");
            var durationProp = serializedObject.FindProperty("_duration");

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            EditorGUILayout.PropertyField(useRandomProp, new GUIContent("Use Random Between Two", "Enable random duration between two values"));

            if (useRandomProp.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(rangeProp, new GUIContent("Range", "Min and max duration values"));
                EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration", "Wait duration in seconds"));
            }
            else if (useRandomProp.boolValue)
            {
                EditorGUILayout.PropertyField(rangeProp, new GUIContent("Range", "Min and max duration values"));
            }
            else
            {
                EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration", "Wait duration in seconds"));
            }

            BehaviorTreeInspectorUi.DrawRemainingProperties(
                serializedObject,
                "_useRandomBetweenTwoConstants",
                "_range",
                "_duration");
            serializedObject.ApplyModifiedProperties();
        }
    }

    internal static class BehaviorTreeInspectorUi
    {
        public static void DrawRemainingProperties(
            SerializedObject serializedObject,
            string firstExcluded,
            string secondExcluded,
            string thirdExcluded)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                string propertyPath = iterator.propertyPath;
                if (propertyPath == "m_Script" ||
                    propertyPath == firstExcluded ||
                    propertyPath == secondExcluded ||
                    propertyPath == thirdExcluded)
                {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        public static void DrawRemainingProperties(
            SerializedObject serializedObject,
            ISet<string> excludedProperties)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script" ||
                    (excludedProperties != null && excludedProperties.Contains(iterator.propertyPath)))
                {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }
        }
    }
}
