using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   [CustomPropertyDrawer(typeof(GameplayTagContainer))]
   public class GameplayTagContainerPropertyDrawer : PropertyDrawer
   {
      private const float k_Gap = 3.0f;
      private const float k_TagGap = 4.0f;
      private const float k_ButtonsWidth = 96f;
      private const float k_ButtonHeight = 20f;
      private const float k_SummaryHeight = 18f;
      private const float k_TagHeight = 18f;
      private const float k_RemoveButtonWidth = 22f;
      private const float k_RemoveButtonGap = 4f;
      private const float k_ViewAllButtonWidth = 58f;
      private const int k_MaxVisibleTags = 6;

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

         int visibleCount = Mathf.Min(tags.arraySize, k_MaxVisibleTags);
         float height = k_SummaryHeight + k_TagGap + visibleCount * (k_TagHeight + k_TagGap) - k_TagGap;
         if (tags.arraySize > k_MaxVisibleTags)
            height += k_TagHeight + k_TagGap;

         return height;
      }

      public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
      {
         label = EditorGUI.BeginProperty(position, label, property);
         position = EditorGUI.PrefixLabel(position, label);
         int oldIndentLevel = EditorGUI.indentLevel;
         EditorGUI.indentLevel = 0;

         SerializedProperty explicitTagsProperty = property.FindPropertyRelative("m_SerializedExplicitTags");
         float buttonsWidth = GetButtonsWidth(position.width);

         Rect editButtonRect = new(position.x, position.y, buttonsWidth, k_ButtonHeight);
         using (new EditorGUI.DisabledScope(explicitTagsProperty.hasMultipleDifferentValues))
         {
            UpdateEditTagsContent(explicitTagsProperty);
            if (GUI.Button(editButtonRect, m_EditTagsContent))
            {
               ShowTagsPopup(editButtonRect, position.width, explicitTagsProperty);
            }
         }

         Rect clearButtonRect = new(
            position.x,
            position.y + k_ButtonHeight + k_Gap,
            buttonsWidth,
            k_ButtonHeight
         );

         using (new EditorGUI.DisabledScope(explicitTagsProperty.hasMultipleDifferentValues || explicitTagsProperty.arraySize == 0))
         {
            if (GUI.Button(clearButtonRect, "Clear All"))
            {
               explicitTagsProperty.arraySize = 0;
               property.serializedObject.ApplyModifiedProperties();
            }
         }

         float boxX = position.x + buttonsWidth + k_Gap;
         float boxWidth = position.width - buttonsWidth - k_Gap;
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

            int totalCount = explicitTagsProperty.arraySize;
            int visibleCount = Mathf.Min(explicitTagsProperty.arraySize, k_MaxVisibleTags);
            SetSummaryContent(totalCount, visibleCount);
            EditorGUI.LabelField(tagRect, s_TempContent, EditorStyles.miniBoldLabel);
            tagRect.y += k_SummaryHeight + k_TagGap;

            for (int i = 0; i < visibleCount; i++)
            {
               SerializedProperty element = explicitTagsProperty.GetArrayElementAtIndex(i);
               GameplayTag tag = GameplayTagManager.RequestTag(element.stringValue, false);

               bool isValid = tag.IsValid;
               SetTagLabelContent(element.stringValue, isValid, tag.Description);

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

            int remainingCount = explicitTagsProperty.arraySize - visibleCount;
            if (remainingCount > 0)
            {
               DrawHiddenTagsRow(tagRect, remainingCount, explicitTagsProperty.arraySize, editButtonRect, position.width, explicitTagsProperty);
            }
         }

         GUI.color = prevColor;

         EditorGUI.indentLevel = oldIndentLevel;
         EditorGUI.EndProperty();
      }

      private static float GetButtonsWidth(float totalWidth)
      {
         return Mathf.Min(k_ButtonsWidth, Mathf.Max(74f, totalWidth * 0.32f));
      }

      private static void ShowTagsPopup(Rect activatorRect, float fullWidth, SerializedProperty explicitTagsProperty)
      {
         GameplayTagContainerTreeView tagTreeView = new(new TreeViewState(), explicitTagsProperty);
         activatorRect.width = fullWidth;
         tagTreeView.ShowPopupWindow(activatorRect, 460f, 420f, 860f);
      }

      private static void DrawHiddenTagsRow(
         Rect rowRect,
         int remainingCount,
         int totalCount,
         Rect popupAnchorRect,
         float fullWidth,
         SerializedProperty explicitTagsProperty)
      {
         GUI.color = s_DimmedColor;
         SetHiddenTagsContent(remainingCount, totalCount);

         Rect labelRect = rowRect;
         if (rowRect.width >= 180f)
         {
            labelRect.width -= k_ViewAllButtonWidth + k_RemoveButtonGap;
         }

         EditorGUI.LabelField(labelRect, s_TempContent, EditorStyles.miniLabel);

         if (rowRect.width < 180f)
         {
            return;
         }

         Color previousColor = GUI.color;
         GUI.color = Color.white;

         Rect viewAllRect = new(rowRect.xMax - k_ViewAllButtonWidth, rowRect.y, k_ViewAllButtonWidth, rowRect.height);
         if (GUI.Button(viewAllRect, new GUIContent("View All", "Open the full tag list."), EditorStyles.miniButton))
         {
            ShowTagsPopup(popupAnchorRect, fullWidth, explicitTagsProperty);
         }

         GUI.color = previousColor;
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
            m_EditTagsContent.text = "Edit Tags";
            m_EditTagsContent.tooltip = "Edit tags. Multiple selected objects have different values.";
            return;
         }

         if (count <= 0)
         {
            m_EditTagsContent.text = "Edit Tags";
            m_EditTagsContent.tooltip = "Edit tags.";
            return;
         }

         s_LabelBuilder.Clear();
         s_LabelBuilder.Append("Edit (");
         s_LabelBuilder.Append(count);
         s_LabelBuilder.Append(')');
         m_EditTagsContent.text = s_LabelBuilder.ToString();

         s_LabelBuilder.Clear();
         s_LabelBuilder.Append("Edit all ");
         s_LabelBuilder.Append(count);
         s_LabelBuilder.Append(count == 1 ? " tag." : " tags.");
         m_EditTagsContent.tooltip = s_LabelBuilder.ToString();
      }

      private static void SetSummaryContent(int totalCount, int visibleCount)
      {
         s_LabelBuilder.Clear();
         s_LabelBuilder.Append(totalCount);
         s_LabelBuilder.Append(totalCount == 1 ? " tag total" : " tags total");

         if (totalCount > visibleCount)
         {
            s_LabelBuilder.Append(" - showing first ");
            s_LabelBuilder.Append(visibleCount);
         }

         s_TempContent.text = s_LabelBuilder.ToString();
         s_TempContent.tooltip = totalCount > visibleCount
            ? "Open the full tag list to inspect every tag."
            : "All tags are visible in this Inspector.";
      }

      private static void SetHiddenTagsContent(int remainingCount, int totalCount)
      {
         s_LabelBuilder.Clear();
         s_LabelBuilder.Append("+ ");
         s_LabelBuilder.Append(remainingCount);
         s_LabelBuilder.Append(remainingCount == 1 ? " hidden tag" : " hidden tags");
         s_LabelBuilder.Append(" (");
         s_LabelBuilder.Append(totalCount);
         s_LabelBuilder.Append(" total)");

         s_TempContent.text = s_LabelBuilder.ToString();
         s_TempContent.tooltip = "Open the full tag list to inspect or edit hidden tags.";
      }

      private static void SetTagLabelContent(string tagName, bool isValid, string description)
      {
         if (isValid)
         {
            s_TempContent.text = tagName;
         }
         else
         {
            s_LabelBuilder.Clear();
            s_LabelBuilder.Append(tagName);
            s_LabelBuilder.Append(" (Invalid)");
            s_TempContent.text = s_LabelBuilder.ToString();
         }

         s_LabelBuilder.Clear();
         s_LabelBuilder.Append(tagName);
         if (isValid)
         {
            if (!string.IsNullOrEmpty(description))
            {
               s_LabelBuilder.Append('\n');
               s_LabelBuilder.Append(description);
            }
         }
         else
         {
            s_LabelBuilder.Append('\n');
            s_LabelBuilder.Append("This tag is not registered.");
         }

         s_TempContent.tooltip = s_LabelBuilder.ToString();
      }
   }
}
