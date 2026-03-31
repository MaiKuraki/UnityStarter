using UnityEngine;
using UnityEditor;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.Utility.Editor
{
    /// <summary>
    /// Editor window displaying a visual preview of all CSS3 colors in the Colors class.
    /// Uses responsive layout that automatically adjusts columns based on window width.
    /// Caches all GUIContent and strings to avoid per-frame GC allocations.
    /// </summary>
    public class ColorsPreviewWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private float _swatchSize = 64f;
        private string _searchFilter = "";
        private GUIStyle _labelStyle;
        private GUIStyle _indexStyle;
        private bool _stylesInitialized;

        // Cached data to avoid per-frame allocations
        private GUIContent[] _cachedLabelContents;
        private GUIContent[] _cachedIndexContents;
        private string[] _cachedHexStrings;
        private string[] _cachedIndexStrings;
        private string[] _cachedCopyStrings;
        private string _cachedSearchLower;
        private bool _cacheBuilt;

        private static readonly string[] ColorNames =
        {
            "AliceBlue", "AntiqueWhite", "Aqua", "Aquamarine", "Azure", "Beige", "Bisque", "Black", "BlanchedAlmond", "Blue",
            "BlueViolet", "Brown", "Burlywood", "CadetBlue", "Chartreuse", "Chocolate", "Coral", "CornflowerBlue", "Cornsilk", "Crimson",
            "Cyan", "DarkBlue", "DarkCyan", "DarkGoldenrod", "DarkGray", "DarkGreen", "DarkKhaki", "DarkMagenta", "DarkOliveGreen", "DarkOrange",
            "DarkOrchid", "DarkRed", "DarkSalmon", "DarkSeaGreen", "DarkSlateBlue", "DarkSlateGray", "DarkTurquoise", "DarkViolet", "DeepPink", "DeepSkyBlue",
            "DimGray", "DodgerBlue", "FireBrick", "FloralWhite", "ForestGreen", "Fuchsia", "Gainsboro", "GhostWhite", "Gold", "Goldenrod",
            "Gray", "Green", "GreenYellow", "Honeydew", "HotPink", "IndianRed", "Indigo", "Ivory", "Khaki", "Lavender",
            "Lavenderblush", "LawnGreen", "LemonChiffon", "LightBlue", "LightCoral", "LightCyan", "LightGoldenrodYellow", "LightGray", "LightGreen", "LightPink",
            "LightSalmon", "LightSeaGreen", "LightSkyBlue", "LightSlateGray", "LightSteelBlue", "LightYellow", "Lime", "LimeGreen", "Linen", "Magenta",
            "Maroon", "MediumAquamarine", "MediumBlue", "MediumOrchid", "MediumPurple", "MediumSeaGreen", "MediumSlateBlue", "MediumSpringGreen", "MediumTurquoise", "MediumVioletRed",
            "MidnightBlue", "Mintcream", "MistyRose", "Moccasin", "NavajoWhite", "Navy", "OldLace", "Olive", "Olivedrab", "Orange",
            "Orangered", "Orchid", "PaleGoldenrod", "PaleGreen", "PaleTurquoise", "PaleVioletred", "PapayaWhip", "PeachPuff", "Peru", "Pink",
            "Plum", "PowderBlue", "Purple", "Red", "RosyBrown", "RoyalBlue", "SaddleBrown", "Salmon", "SandyBrown", "SeaGreen",
            "Seashell", "Sienna", "Silver", "SkyBlue", "SlateBlue", "SlateGray", "Snow", "SpringGreen", "SteelBlue", "Tan",
            "Teal", "Thistle", "Tomato", "Turquoise", "Violet", "Wheat", "White", "WhiteSmoke", "Yellow", "YellowGreen"
        };

        private const float SWATCH_PADDING = 6f;
        private const float LABEL_HEIGHT = 36f;
        private const float SCROLLBAR_WIDTH = 16f;

        [MenuItem("Tools/CycloneGames/Colors Preview")]
        public static void ShowWindow()
        {
            var window = GetWindow<ColorsPreviewWindow>("Colors Preview");
            window.minSize = new Vector2(300, 200);
            window.Show();
        }

        private void BuildCache()
        {
            if (_cacheBuilt) return;

            int colorCount = Colors.ColorCount;
            _cachedLabelContents = new GUIContent[colorCount];
            _cachedIndexContents = new GUIContent[colorCount];
            _cachedHexStrings = new string[colorCount];
            _cachedIndexStrings = new string[colorCount];
            _cachedCopyStrings = new string[colorCount];

            for (int i = 0; i < colorCount; i++)
            {
                string colorName = i < ColorNames.Length ? ColorNames[i] : string.Concat("Color", i.ToString());
                Color color = Colors.GetColorAt(i);
                Color32 c32 = color;
                string hex = string.Concat("#", c32.r.ToString("X2"), c32.g.ToString("X2"), c32.b.ToString("X2"));
                string indexStr = i.ToString();
                string copyStr = string.Concat("Colors.", colorName);
                string tooltip = string.Concat(colorName, " [", indexStr, "]\n", hex, "\nClick to copy: ", copyStr);

                _cachedHexStrings[i] = hex;
                _cachedIndexStrings[i] = indexStr;
                _cachedCopyStrings[i] = copyStr;
                _cachedLabelContents[i] = new GUIContent(string.Concat(colorName, "\n", hex), tooltip);
                _cachedIndexContents[i] = new GUIContent(indexStr);
            }

            _cacheBuilt = true;
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
            BuildCache();

            DrawToolbar();
            DrawColorGrid();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            string newFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(100), GUILayout.MaxWidth(200));
            if (newFilter != _searchFilter)
            {
                _searchFilter = newFilter;
                _cachedSearchLower = string.IsNullOrEmpty(_searchFilter) ? null : _searchFilter.ToLowerInvariant();
            }

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
                // Apply search filter (0GC: uses cached lowercase string)
                if (_cachedSearchLower != null)
                {
                    string colorName = i < ColorNames.Length ? ColorNames[i] : _cachedCopyStrings[i];
                    if (colorName.IndexOf(_cachedSearchLower, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                        _cachedIndexStrings[i].IndexOf(_cachedSearchLower, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                        _cachedHexStrings[i].IndexOf(_cachedSearchLower, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                DrawColorSwatch(i, actualSwatchWidth);

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

        private void DrawColorSwatch(int index, float swatchWidth)
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
            GUI.Label(indexRect, _cachedIndexContents[index], _indexStyle);

            // Draw color name + hex below with tooltip
            Rect labelRect = new Rect(swatchRect.x, colorRect.yMax + 2, swatchRect.width, LABEL_HEIGHT);
            _labelStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.2f, 0.2f, 0.2f);
            GUI.Label(labelRect, _cachedLabelContents[index], _labelStyle);

            // Handle click - copy to clipboard
            if (Event.current.type == EventType.MouseDown && colorRect.Contains(Event.current.mousePosition))
            {
                EditorGUIUtility.systemCopyBuffer = _cachedCopyStrings[index];
                // GUIContent allocation here is acceptable (only on click, not per-frame)
                ShowNotification(new GUIContent(string.Concat("Copied: ", _cachedCopyStrings[index])), 1.5f);
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
            return bgColor.GetLuminance() > 0.5f ? Color.black : Color.white;
        }
    }
}
