using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEditor;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors
{
    [CustomEditor(typeof(BTStateMachineComponent), true)]
    [CanEditMultipleObjects]
    public class BTStateMachineComponentEditor : BTRunnerComponentEditor
    {
        private SerializedProperty _initialState;
        private SerializedProperty _states;
        
        protected override void OnEnable()
        {
            base.OnEnable();
            _initialState = serializedObject.FindProperty("_initialState");
            _states = serializedObject.FindProperty("_states");
        }
        
        protected override void DrawBehaviorTreeSection()
        {
            EditorGUILayout.LabelField("State Machine", Styles.HeaderStyle);
            EditorGUILayout.BeginVertical(Styles.BoxStyle);
            
            EditorGUILayout.PropertyField(_initialState);
            EditorGUILayout.PropertyField(_states);
            
            EditorGUILayout.EndVertical();
        }
        
        private static class Styles
        {
            public static readonly UnityEngine.GUIStyle HeaderStyle;
            public static readonly UnityEngine.GUIStyle BoxStyle;
            
            static Styles()
            {
                HeaderStyle = new UnityEngine.GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new UnityEngine.RectOffset(0, 0, 8, 4)
                };
                
                BoxStyle = new UnityEngine.GUIStyle("HelpBox")
                {
                    padding = new UnityEngine.RectOffset(10, 10, 8, 8),
                    margin = new UnityEngine.RectOffset(0, 0, 4, 4)
                };
            }
        }
    }
}