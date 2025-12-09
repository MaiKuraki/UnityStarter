using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using UnityEditor;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors.NodeEditors
{
    [CustomEditor(typeof(WaitNode))]
    public class WaitNodeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var waitNode = (WaitNode)target;
            serializedObject.Update();
            
            var useRandomProp = serializedObject.FindProperty("_useRandomBetweenTwoConstants");
            var rangeProp = serializedObject.FindProperty("_range");
            var durationProp = serializedObject.FindProperty("_duration");
            
            EditorGUILayout.PropertyField(useRandomProp, new GUIContent("Use Random Between Two", "Enable random duration between two values"));
            
            if (waitNode.UseRandomBetweenTwoConstants)
            {
                EditorGUILayout.PropertyField(rangeProp, new GUIContent("Range", "Min and max duration values"));
            }
            else
            {
                EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration", "Wait duration in seconds"));
            }
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}