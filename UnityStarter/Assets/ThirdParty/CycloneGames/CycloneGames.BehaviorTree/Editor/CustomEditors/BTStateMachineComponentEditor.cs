using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEditor;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors
{
    [CustomEditor(typeof(BTStateMachineComponent))]
    public class BTStateMachineComponentEditor : BTRunnerComponentEditor
    {
        protected override void SingleBehaviorTreePanel()
        {
            //base.SingleBlackBoardPanel();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_initialState"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_states"));
        }
    }
}