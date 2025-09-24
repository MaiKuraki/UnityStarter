using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
   [CustomPropertyDrawer(typeof(GameplayTagContainer))]
   public class GameplayTagContainerPropertyDrawer : PropertyDrawer
   {
      private const float k_Gap = 3.0f;
      private const float k_TagGap = 4.0f;
      private const float k_ButtonsWidth = 90f;
      private const float k_ButtonHeight = 20f;
      private const float k_TagHeight = 18f;

      private static GUIContent s_TempContent = new();
      private static GUIContent s_EditTagsContent;

      private static GUIStyle s_TagBoxStyle;

      private GUIContent m_RemoveTagContent;

      public GameplayTagContainerPropertyDrawer()
      {
         m_RemoveTagContent = new GUIContent
         {
            image = EditorGUIUtility.IconContent("Toolbar Minus").image,
            text = null,
            tooltip = "Remove this tag."
         };

         s_EditTagsContent = new GUIContent("Edit Tags...", "Edit tags in a popup window.");
         if (s_TagBoxStyle == null)
         {
            s_TagBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
               padding = new RectOffset(6, 6, 5, 5),
               margin = new RectOffset(0, 0, 0, 0)
            };
         }
      }

      private static float CalcContentHeight(GUIStyle style, float innerHeight)
      {
         return innerHeight + style.padding.vertical + style.margin.vertical;
      }

      private static Rect GetPaddedRect(Rect rect, GUIStyle style)
      {
         return new Rect(
            rect.x + style.padding.left,
            rect.y + style.padding.top,
            rect.width - style.padding.horizontal,
            rect.height - style.padding.vertical
         );
      }

      public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
      {
         float buttonsHeight = k_ButtonHeight * 2 + k_Gap;
         float tagsInnerHeight = CalcTagsInnerHeight(property);
         float tagsBoxHeight = CalcContentHeight(s_TagBoxStyle, tagsInnerHeight);

         return Mathf.Max(buttonsHeight, tagsBoxHeight);
      }

      private float CalcTagsInnerHeight(SerializedProperty property)
      {
         SerializedProperty tags = property.FindPropertyRelative("m_SerializedExplicitTags");
         if (tags.hasMultipleDifferentValues || tags.arraySize == 0)
            return EditorGUIUtility.singleLineHeight;

         return tags.arraySize * (k_TagHeight + k_TagGap) - k_TagGap;
      }

      public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
      {
         label = EditorGUI.BeginProperty(position, label, property);
         position = EditorGUI.PrefixLabel(position, label);
         int oldIndentLevel = EditorGUI.indentLevel;
         EditorGUI.indentLevel = 0;

         SerializedProperty explicitTagsProperty = property.FindPropertyRelative("m_SerializedExplicitTags");

         Rect editButtonRect = new(position.x, position.y, k_ButtonsWidth, k_ButtonHeight);
         using (new EditorGUI.DisabledScope(explicitTagsProperty.hasMultipleDifferentValues))
         {
            if (GUI.Button(editButtonRect, s_EditTagsContent))
            {
               GameplayTagContainerTreeView tagTreeView = new(new TreeViewState(), explicitTagsProperty);
               Rect activatorRect = editButtonRect;
               activatorRect.width = position.width;
               tagTreeView.ShowPopupWindow(activatorRect, 280f);
            }
         }

         Rect clearButtonRect = new(
            position.x,
            position.y + k_ButtonHeight + k_Gap,
            k_ButtonsWidth,
            k_ButtonHeight
         );

         using (new EditorGUI.DisabledScope(explicitTagsProperty.arraySize == 0))
         {
            if (GUI.Button(clearButtonRect, "Clear All"))
               explicitTagsProperty.arraySize = 0;
         }

         float boxX = position.x + k_ButtonsWidth + k_Gap;
         float boxWidth = position.width - k_ButtonsWidth - k_Gap;
         float tagsInnerHeight = CalcTagsInnerHeight(property);
         float tagsBoxHeight = CalcContentHeight(s_TagBoxStyle, tagsInnerHeight);
         Rect boxRect = new(boxX, position.y, boxWidth, tagsBoxHeight);

         GUI.Box(boxRect, GUIContent.none, s_TagBoxStyle);

         Rect inner = GetPaddedRect(boxRect, s_TagBoxStyle);
         Rect tagRect = new(inner.x, inner.y, inner.width, k_TagHeight);

         Color prevColor = GUI.color;
         if (explicitTagsProperty.hasMultipleDifferentValues)
         {
            GUI.color = new Color(1, 1, 1, 0.7f);
            EditorGUI.LabelField(tagRect, "Tags have different values.");
         }
         else if (explicitTagsProperty.arraySize == 0)
         {
            GUI.color = new Color(1, 1, 1, 0.7f);
            EditorGUI.LabelField(tagRect, "No tags added.");
         }
         else
         {
            GUI.color = Color.white;

            for (int i = 0; i < explicitTagsProperty.arraySize; i++)
            {
               SerializedProperty element = explicitTagsProperty.GetArrayElementAtIndex(i);
               GameplayTag tag = GameplayTagManager.RequestTag(element.stringValue);

               bool isValid = tag.IsValid;
               s_TempContent.text = isValid ? element.stringValue : element.stringValue + " (Invalid)";
               s_TempContent.tooltip = tag.Description ?? "No description";

               Rect removeButtonRect = new(tagRect.x, tagRect.y, 22, tagRect.height);
               if (GUI.Button(removeButtonRect, m_RemoveTagContent))
               {
                  explicitTagsProperty.DeleteArrayElementAtIndex(i);
                  property.serializedObject.ApplyModifiedProperties();
                  break;
               }

               Rect labelRect = new(removeButtonRect.xMax + 4, tagRect.y, tagRect.width - 20, tagRect.height);

               Color previousColor = GUI.color;
               if (!isValid)
                  GUI.color = new Color(previousColor.g, previousColor.g, previousColor.b, previousColor.a * 0.5f);

               EditorGUI.LabelField(labelRect, s_TempContent);

               GUI.color = previousColor;

               tagRect.y += k_TagHeight + k_TagGap;
            }
         }

         GUI.color = prevColor;

         EditorGUI.indentLevel = oldIndentLevel;
         EditorGUI.EndProperty();
      }
   }
}
