#if UNITY_EDITOR
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    [CustomEditor(typeof(Locale))]
    public sealed class LocaleEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var codeProp = serializedObject.FindProperty("localeCode");
            var displayProp = serializedObject.FindProperty("displayName");
            var nativeProp = serializedObject.FindProperty("nativeName");
            var fallbacksProp = serializedObject.FindProperty("fallbacks");

            EditorGUILayout.PropertyField(codeProp, new GUIContent("Locale Code (BCP 47)"));
            EditorGUILayout.PropertyField(displayProp);
            EditorGUILayout.PropertyField(nativeProp);

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(fallbacksProp, new GUIContent("Fallback Chain"), true);

            if (!string.IsNullOrEmpty(codeProp.stringValue))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    $"Runtime ID: \"{codeProp.stringValue}\" (interned)",
                    MessageType.None);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
