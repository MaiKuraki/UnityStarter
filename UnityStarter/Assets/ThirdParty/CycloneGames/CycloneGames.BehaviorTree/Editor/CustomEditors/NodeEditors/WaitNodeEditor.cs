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
            //show script field
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_useRandomBetweenTwoConstants"));
            if (waitNode.UseRandomBetweenTwoConstants)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_range"));
                serializedObject.ApplyModifiedProperties();
                return;
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_duration"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}