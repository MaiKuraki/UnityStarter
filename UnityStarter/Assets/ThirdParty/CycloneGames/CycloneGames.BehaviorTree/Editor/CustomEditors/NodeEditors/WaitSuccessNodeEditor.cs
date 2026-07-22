using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEditor;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors.NodeEditors
{
    [CustomEditor(typeof(WaitSuccessNode), true)]
    [CanEditMultipleObjects]
    public class WaitSuccessNodeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty useRandomProperty = serializedObject.FindProperty("_useRandomBetweenTwoConstants");
            SerializedProperty rangeProperty = serializedObject.FindProperty("_waitTimeRange");
            SerializedProperty waitTimeProperty = serializedObject.FindProperty("_waitTime");

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            EditorGUILayout.PropertyField(useRandomProperty);
            if (useRandomProperty.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(rangeProperty);
                EditorGUILayout.PropertyField(waitTimeProperty);
            }
            else if (useRandomProperty.boolValue)
            {
                EditorGUILayout.PropertyField(rangeProperty);
            }
            else
            {
                EditorGUILayout.PropertyField(waitTimeProperty);
            }

            BehaviorTreeInspectorUi.DrawRemainingProperties(
                serializedObject,
                "_useRandomBetweenTwoConstants",
                "_waitTimeRange",
                "_waitTime");
            serializedObject.ApplyModifiedProperties();
        }
    }
}
