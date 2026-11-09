using UnityEditor;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>
    /// Draws an <see cref="AnimationClipReference"/> as a mode popup plus only the field that mode needs,
    /// matching how the audio and UI authoring references are presented in this repository.
    /// The editor-only GUID is serialized but not drawn: it is relocation metadata, not authoring input.
    /// </summary>
    [CustomPropertyDrawer(typeof(AnimationClipReference))]
    public sealed class AnimationClipReferenceDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return (EditorGUIUtility.singleLineHeight * 2f) + Spacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty mode = property.FindPropertyRelative("_mode");
            SerializedProperty embedded = property.FindPropertyRelative("_embeddedClip");
            SerializedProperty location = property.FindPropertyRelative("_location");

            if (mode == null || embedded == null || location == null)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            float lineHeight = EditorGUIUtility.singleLineHeight;

            EditorGUI.BeginProperty(position, label, property);

            var line = new Rect(position.x, position.y, position.width, lineHeight);
            EditorGUI.PropertyField(line, mode, label);

            line.y += lineHeight + Spacing;
            var modeValue = (AnimationClipReferenceMode)mode.enumValueIndex;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.PropertyField(
                    line,
                    modeValue == AnimationClipReferenceMode.Embedded ? embedded : location,
                    GUIContent.none);
            }

            EditorGUI.EndProperty();
        }
    }
}
