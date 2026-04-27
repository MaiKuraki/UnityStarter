using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using System.Text;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   [CustomPropertyDrawer(typeof(GameplayTagContainer))]
   public class GameplayTagContainerPropertyDrawer : PropertyDrawer
   {
      private const float k_Gap = 3.0f;
      private const float k_TagGap = 4.0f;
      private const float k_ButtonsWidth = 90f;
      private const float k_ButtonHeight = 20f;
      private const float k_TagHeight = 18f;
      private const float k_RemoveButtonWidth = 22f;
      private const float k_RemoveButtonGap = 4f;

      private static GUIContent s_TempContent = new();

      private static GUIStyle s_TagBoxStyle;
      private static readonly StringBuilder s_LabelBuilder = new(128);
      private static readonly Color s_DimmedColor = new(1f, 1f, 1f, 0.7f);
      private static readonly Color s_InvalidTagColor = new(1f, 0.4f, 0.4f, 1f);

      private GUIContent m_RemoveTagContent;
      private GUIContent m_EditTagsContent;
      private int m_LastEditTagsCount = -1;
      private bool m_LastEditTagsMixed;

      public GameplayTagContainerPropertyDrawer()
      {
         m_RemoveTagContent = new GUIContent
         {
            image = EditorGUIUtility.IconContent("Toolbar Minus").image,
            text = null,
            tooltip = "Remove this tag."
         };

         m_EditTagsContent = new GUIContent();

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
            UpdateEditTagsContent(explicitTagsProperty);
            if (GUI.Button(editButtonRect, m_EditTagsContent))
            {
               GameplayTagContainerTreeView tagTreeView = new(new TreeViewState(), explicitTagsProperty);
               Rect activatorRect = editButtonRect;
               activatorRect.width = position.width;
               tagTreeView.ShowPopupWindow(activatorRect, 460f, 420f, 860f);
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
            GUI.color = s_DimmedColor;
            EditorGUI.LabelField(tagRect, "Tags have different values.");
         }
         else if (explicitTagsProperty.arraySize == 0)
         {
            GUI.color = s_DimmedColor;
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
               SetTagLabelContent(element.stringValue, isValid);
               s_TempContent.tooltip = tag.Description ?? "No description";

               Rect removeButtonRect = new(tagRect.x, tagRect.y, k_RemoveButtonWidth, tagRect.height);
               if (GUI.Button(removeButtonRect, m_RemoveTagContent))
               {
                  explicitTagsProperty.DeleteArrayElementAtIndex(i);
                  property.serializedObject.ApplyModifiedProperties();
                  break;
               }

               float labelX = removeButtonRect.xMax + k_RemoveButtonGap;
               Rect labelRect = new(labelX, tagRect.y, tagRect.xMax - labelX, tagRect.height);

               Color previousColor = GUI.color;
               if (!isValid)
               {
                  Color invalidColor = s_InvalidTagColor;
                  invalidColor.a = previousColor.a;
                  GUI.color = invalidColor;
               }

               EditorGUI.LabelField(labelRect, s_TempContent);

               GUI.color = previousColor;

               tagRect.y += k_TagHeight + k_TagGap;
            }
         }

         GUI.color = prevColor;

         EditorGUI.indentLevel = oldIndentLevel;
         EditorGUI.EndProperty();
      }

      private void UpdateEditTagsContent(SerializedProperty explicitTagsProperty)
      {
         bool mixed = explicitTagsProperty.hasMultipleDifferentValues;
         int count = explicitTagsProperty.arraySize;

         if (mixed == m_LastEditTagsMixed && count == m_LastEditTagsCount)
            return;

         m_LastEditTagsMixed = mixed;
         m_LastEditTagsCount = count;

         if (mixed)
         {
            m_EditTagsContent.text = "Edit Tags...";
            return;
         }

         if (count <= 0)
         {
            m_EditTagsContent.text = "Edit Tags...";
            return;
         }

         s_LabelBuilder.Clear();
         s_LabelBuilder.Append("Edit Tags (");
         s_LabelBuilder.Append(count);
         s_LabelBuilder.Append(")...");
         m_EditTagsContent.text = s_LabelBuilder.ToString();
      }

      private static void SetTagLabelContent(string tagName, bool isValid)
      {
         if (isValid)
         {
            s_TempContent.text = tagName;
            return;
         }

         s_LabelBuilder.Clear();
         s_LabelBuilder.Append(tagName);
         s_LabelBuilder.Append(" (Invalid)");
         s_TempContent.text = s_LabelBuilder.ToString();
      }
   }
}
