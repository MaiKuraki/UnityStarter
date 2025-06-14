using CycloneGames.GameplayTags.Runtime;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace CycloneGames.GameplayTags.Editor
{
    [CustomPropertyDrawer(typeof(GameplayTagContainer))]
    public class GameplayTagContainerPropertyDrawer : PropertyDrawer
    {
        private const float k_Gap = 2.0f;
        private const float k_ButtonsWidth = 110f;
        private static readonly GUIContent s_TempContent = new();
        private static readonly GUIContent s_RemoveTagContent = new("-", "Remove tag");
        private static readonly GUIContent s_EditTagsContent = new("Edit Tags...", "Edit tags in a popup window.");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty tagNamesProperty = property.FindPropertyRelative("m_SerializedExplicitTags");
            if (tagNamesProperty.hasMultipleDifferentValues)
            {
                return (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 2;
            }

            if (tagNamesProperty.arraySize > 0)
            {
                // The height is the maximum of the space needed for all tags and the space for the two buttons.
                float tagsHeight = tagNamesProperty.arraySize * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
                float buttonsHeight = (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 2;
                return Mathf.Max(tagsHeight, buttonsHeight);
            }
            // Default height for the "Edit Tags..." button.
            return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);

            int oldIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            SerializedProperty explicitTagsProperty = property.FindPropertyRelative("m_SerializedExplicitTags");

            EditorGUI.BeginDisabledGroup(!property.editable);

            // --- "Edit Tags..." Button ---
            Rect editButtonRect = position;
            editButtonRect.width = k_ButtonsWidth;
            editButtonRect.height = EditorGUIUtility.singleLineHeight;
            if (GUI.Button(editButtonRect, s_EditTagsContent, EditorStyles.popup))
            {
                GameplayTagContainerTreeView tagTreeView = new(new TreeViewState(), explicitTagsProperty);
                // The popup will be anchored to the button but span the full available width.
                Rect activatorRect = position;
                activatorRect.height = editButtonRect.height;
                // This ShowPopupWindow method is a custom extension method provided in your original code.
                TreeViewMethodExtensions.ShowPopupWindow(tagTreeView, activatorRect, 280f);
            }

            // --- "Clear All" Button ---
            if (explicitTagsProperty.arraySize > 0)
            {
                DrawClearAllButton(position, explicitTagsProperty);
            }

            // --- Display Tags ---
            if (explicitTagsProperty.hasMultipleDifferentValues)
            {
                OnMultipleValuesGUI(position);
            }
            else
            {
                OnAddedTagsGUI(position, explicitTagsProperty);
            }

            EditorGUI.EndDisabledGroup();

            EditorGUI.indentLevel = oldIndentLevel;
            EditorGUI.EndProperty();
        }

        private static void OnMultipleValuesGUI(Rect position)
        {
            s_TempContent.text = "—"; // Use em dash for multi-editing
            s_TempContent.tooltip = "Multiple different values.";

            Rect rect = position;
            rect.xMin += k_ButtonsWidth + k_Gap;
            rect.height = EditorGUIUtility.singleLineHeight;

            // Draw a label inside an outline to match the tag display style
            GUI.Label(rect, s_TempContent, EditorStyles.label);
            DrawOutline(rect, new Color(1, 1, 1, 0.15f));
        }

        private static void OnAddedTagsGUI(Rect position, SerializedProperty explicitTagsProperty)
        {
            if (explicitTagsProperty.arraySize <= 0)
            {
                return;
            }

            Rect tagsListRect = position;
            tagsListRect.xMin += k_ButtonsWidth + k_Gap;

            Rect tagRect = tagsListRect;
            tagRect.height = EditorGUIUtility.singleLineHeight;

            float totalTagWidth = 0;

            for (int i = explicitTagsProperty.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = explicitTagsProperty.GetArrayElementAtIndex(i);
                GameplayTag tag = GameplayTagManager.RequestTag(element.stringValue);
                s_TempContent.text = element.stringValue;

                s_TempContent.tooltip = (tag != GameplayTag.None) ? tag.Description : "Tag not found.";

                // Calculate width for this tag element
                float currentTagWidth = EditorStyles.label.CalcSize(s_TempContent).x + 22f; // 22 is for padding and button
                tagRect.width = currentTagWidth;
                totalTagWidth = Mathf.Max(totalTagWidth, currentTagWidth);

                // --- Remove Button ("-") ---
                Rect removeButtonRect = tagRect;
                removeButtonRect.width = 18f;
                removeButtonRect.x = tagRect.x;

                if (GUI.Button(removeButtonRect, s_RemoveTagContent))
                {
                    explicitTagsProperty.DeleteArrayElementAtIndex(i);
                }
                else
                {
                    // --- Tag Label ---
                    Rect labelRect = tagRect;
                    labelRect.xMin = removeButtonRect.xMax;
                    GUI.Label(labelRect, s_TempContent);
                }

                // Move to the next line for the next tag
                tagRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            // Draw a single outline around all the tags.
            Rect outlineRect = tagsListRect;
            outlineRect.width = totalTagWidth;
            outlineRect.height = explicitTagsProperty.arraySize * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) - EditorGUIUtility.standardVerticalSpacing;
            DrawOutline(outlineRect, new Color(1, 1, 1, 0.15f));
        }

        private static void DrawClearAllButton(Rect position, SerializedProperty explicitTagsProperty)
        {
            Rect clearButtonRect = new Rect(
                position.x,
                position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                k_ButtonsWidth,
                EditorGUIUtility.singleLineHeight
            );

            if (GUI.Button(clearButtonRect, "Clear All"))
            {
                explicitTagsProperty.ClearArray();
            }
        }

        private static void DrawOutline(Rect rect, Color color, float thickness = 1)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var savedColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), EditorGUIUtility.whiteTexture); // Top
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), EditorGUIUtility.whiteTexture); // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), EditorGUIUtility.whiteTexture); // Left
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), EditorGUIUtility.whiteTexture); // Right
            GUI.color = savedColor;
        }
    }
}