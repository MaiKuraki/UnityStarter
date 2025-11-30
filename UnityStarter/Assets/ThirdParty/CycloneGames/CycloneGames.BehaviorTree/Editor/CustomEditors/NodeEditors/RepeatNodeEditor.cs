using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEditor;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors.NodeEditors
{
    [CustomEditor(typeof(RepeatNode))]
    public class RepeatNodeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var repeatNode = (RepeatNode)target;
            //show script field
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_repeatForever"));
            if (repeatNode.RepeatForever)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_useRandomRepeatCount"));
            if (!repeatNode.UseRandomRepeatCount)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_repeatCount"));
                serializedObject.ApplyModifiedProperties();
                return;
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_randomRepeatCountRange"));
            serializedObject.ApplyModifiedProperties();

        }
    }
}