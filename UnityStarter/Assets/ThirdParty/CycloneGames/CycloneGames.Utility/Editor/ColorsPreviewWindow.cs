using UnityEngine;
using UnityEditor;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.Utility.Editor
{
    /// <summary>
    /// Editor window displaying a visual preview of all CSS3 colors in the Colors class.
    /// Uses responsive layout that automatically adjusts columns based on window width.
    /// </summary>
    public class ColorsPreviewWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private float _swatchSize = 64f;
        private string _searchFilter = "";
        private GUIStyle _labelStyle;
        private GUIStyle _indexStyle;
        private bool _stylesInitialized;

        private static readonly string[] ColorNames =
        {
            "AliceBlue", "AntiqueWhite", "Aqua", "Aquamarine", "Azure", "Beige", "Bisque", "Black", "BlanchedAlmond", "Blue",
            "BlueViolet", "Brown", "Burlywood", "CadetBlue", "Chartreuse", "Chocolate", "Coral", "CornflowerBlue", "Cornsilk", "Crimson",
            "Cyan", "DarkBlue", "DarkCyan", "DarkGoldenrod", "DarkGray", "DarkGreen", "DarkKhaki", "DarkMagenta", "DarkOliveGreen", "DarkOrange",
            "DarkOrchid", "DarkRed", "DarkSalmon", "DarkSeaGreen", "DarkSlateBlue", "DarkSlateGray", "DarkTurquoise", "DarkViolet", "DeepPink", "DeepSkyBlue",
            "DimGray", "DodgerBlue", "FireBrick", "FloralWhite", "ForestGreen", "Fuchsia", "Gainsboro", "GhostWhite", "Gold", "Goldenrod",
            "Gray", "Green", "GreenYellow", "Honeydew", "HotPink", "IndianRed", "Indigo", "Ivory", "Khaki", "Lavender",
            "Lavenderblush", "LawnGreen", "LemonChiffon", "LightBlue", "LightCoral", "LightCyan", "LightGoldenodYellow", "LightGray", "LightGreen", "LightPink",
            "LightSalmon", "LightSeaGreen", "LightSkyBlue", "LightSlateGray", "LightSteelBlue", "LightYellow", "Lime", "LimeGreen", "Linen", "Magenta",
            "Maroon", "MediumAquamarine", "MediumBlue", "MediumOrchid", "MediumPurple", "MediumSeaGreen", "MediumSlateBlue", "MediumSpringGreen", "MediumTurquoise", "MediumVioletRed",
            "MidnightBlue", "Mintcream", "MistyRose", "Moccasin", "NavajoWhite", "Navy", "OldLace", "Olive", "Olivedrab", "Orange",
            "Orangered", "Orchid", "PaleGoldenrod", "PaleGreen", "PaleTurquoise", "PaleVioletred", "PapayaWhip", "PeachPuff", "Peru", "Pink",
            "Plum", "PowderBlue", "Purple", "Red", "RosyBrown", "RoyalBlue", "SaddleBrown", "Salmon", "SandyBrown", "SeaGreen",
            "Seashell", "Sienna", "Silver", "SkyBlue", "SlateBlue", "SlateGray", "Snow", "SpringGreen", "SteelBlue", "Tan",
            "Teal", "Thistle", "Tomato", "Turquoise", "Violet", "Wheat", "White", "WhiteSmoke", "Yellow", "YellowGreen"
        };

        private const float SWATCH_PADDING = 6f;
        private const float LABEL_HEIGHT = 28f;
        private const float SCROLLBAR_WIDTH = 16f;

        [MenuItem("Tools/CycloneGames/Colors Preview")]
        public static void ShowWindow()
        {
            var window = GetWindow<ColorsPreviewWindow>("Colors Preview");
            window.minSize = new Vector2(300, 200);
            window.Show();
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter,
                wordWrap = true,
                fontSize = 9,
                clipping = TextClipping.Clip
            };

            _indexStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 11
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            DrawToolbar();
            DrawColorGrid();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(100), GUILayout.MaxWidth(200));

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Size:", GUILayout.Width(35));
            _swatchSize = EditorGUILayout.Slider(_swatchSize, 48f, 120f, GUILayout.Width(120));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawColorGrid()
        {
            // Calculate responsive column count based on available width
            float availableWidth = position.width - SCROLLBAR_WIDTH - 10f;
            float cellWidth = _swatchSize + SWATCH_PADDING;
            int columnsCount = Mathf.Max(1, Mathf.FloorToInt(availableWidth / cellWidth));

            // Recalculate actual swatch width to fill space evenly
            float actualCellWidth = availableWidth / columnsCount;
            float actualSwatchWidth = actualCellWidth - SWATCH_PADDING;

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            int colorCount = Colors.ColorCount;
            int column = 0;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            for (int i = 0; i < colorCount; i++)
            {
                string colorName = i < ColorNames.Length ? ColorNames[i] : $"Color{i}";

                // Apply search filter
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    if (!colorName.ToLowerInvariant().Contains(_searchFilter.ToLowerInvariant()) &&
                        !i.ToString().Contains(_searchFilter))
                    {
                        continue;
                    }
                }

                DrawColorSwatch(i, colorName, actualSwatchWidth);

                column++;
                if (column >= columnsCount)
                {
                    column = 0;
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }

        private void DrawColorSwatch(int index, string colorName, float swatchWidth)
        {
            Color color = Colors.GetColorAt(index);

            float cellHeight = _swatchSize + LABEL_HEIGHT + 4f;
            Rect swatchRect = GUILayoutUtility.GetRect(swatchWidth + SWATCH_PADDING, cellHeight, GUILayout.Width(swatchWidth + SWATCH_PADDING));

            // Center the color box within the cell
            float xOffset = (swatchRect.width - swatchWidth) * 0.5f;
            Rect colorRect = new Rect(swatchRect.x + xOffset, swatchRect.y + 2, swatchWidth, _swatchSize);

            // Draw color box
            EditorGUI.DrawRect(colorRect, color);

            // Draw border
            Color borderColor = EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.5f, 0.5f, 0.5f);
            DrawRectBorder(colorRect, borderColor);

            // Draw index number with contrasting color
            Color textColor = GetContrastColor(color);
            _indexStyle.normal.textColor = textColor;
            Rect indexRect = new Rect(colorRect.x + 3, colorRect.y + 2, colorRect.width - 6, 18);
            GUI.Label(indexRect, index.ToString(), _indexStyle);

            // Draw color name below with tooltip for full name
            Rect labelRect = new Rect(swatchRect.x, colorRect.yMax + 2, swatchRect.width, LABEL_HEIGHT);
            _labelStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.2f, 0.2f, 0.2f);
            GUI.Label(labelRect, new GUIContent(colorName, $"{colorName} (Index: {index})\nClick to copy: Colors.{colorName}"), _labelStyle);

            // Handle click - copy to clipboard
            if (Event.current.type == EventType.MouseDown && colorRect.Contains(Event.current.mousePosition))
            {
                EditorGUIUtility.systemCopyBuffer = $"Colors.{colorName}";
                ShowNotification(new GUIContent($"Copied: Colors.{colorName} (Index: {index})"), 1.5f);
                Event.current.Use();
            }
        }

        private static void DrawRectBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }

        private static Color GetContrastColor(Color bgColor)
        {
            float luminance = 0.299f * bgColor.r + 0.587f * bgColor.g + 0.114f * bgColor.b;
            return luminance > 0.5f ? Color.black : Color.white;
        }
    }
}
