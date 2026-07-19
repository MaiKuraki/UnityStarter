using System;
using System.Reflection;

using CycloneGames.Utility.Runtime;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Editor
{
    /// <summary>
    /// Searchable Editor-only preview of the public named colors declared by <see cref="Colors"/>.
    /// </summary>
    public sealed class ColorsPreviewWindow : EditorWindow
    {
        private const float ToolbarHeight = 22f;
        private const float SwatchPadding = 6f;
        private const float LabelHeight = 36f;
        private const float ScrollbarWidth = 16f;

        private Vector2 _scrollPosition;
        private float _swatchSize = 64f;
        private string _searchFilter = string.Empty;
        private GUIStyle _labelStyle;
        private GUIStyle _indexStyle;
        private ColorDefinition[] _definitions;
        private GUIContent[] _labelContents;
        private GUIContent[] _indexContents;
        private string[] _hexStrings;
        private string[] _copyStrings;

        [MenuItem("Tools/CycloneGames/Colors Preview")]
        public static void ShowWindow()
        {
            ColorsPreviewWindow window = GetWindow<ColorsPreviewWindow>("Colors Preview");
            window.minSize = new Vector2(320f, 220f);
            window.Show();
        }

        private void OnGUI()
        {
            EnsureStyles();
            EnsureCache();

            Rect toolbarRect = new Rect(0f, 0f, position.width, ToolbarHeight);
            DrawToolbar(toolbarRect);
            Rect gridRect = new Rect(
                0f,
                ToolbarHeight,
                position.width,
                Mathf.Max(0f, position.height - ToolbarHeight));
            DrawColorGrid(gridRect);
        }

        private void DrawToolbar(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);
            Rect searchLabelRect = new Rect(rect.x + 6f, rect.y + 2f, 46f, rect.height - 4f);
            float searchWidth = Mathf.Max(80f, rect.width - 240f);
            Rect searchRect = new Rect(searchLabelRect.xMax, rect.y + 2f, searchWidth, rect.height - 4f);
            Rect sizeLabelRect = new Rect(searchRect.xMax + 6f, rect.y + 2f, 34f, rect.height - 4f);
            Rect sizeSliderRect = new Rect(sizeLabelRect.xMax, rect.y + 2f, Mathf.Max(40f, rect.xMax - sizeLabelRect.xMax - 8f), rect.height - 4f);

            EditorGUI.LabelField(searchLabelRect, "Search");
            _searchFilter = EditorGUI.TextField(searchRect, _searchFilter, EditorStyles.toolbarSearchField);
            EditorGUI.LabelField(sizeLabelRect, "Size");
            _swatchSize = GUI.HorizontalSlider(sizeSliderRect, _swatchSize, 48f, 120f);
        }

        private void DrawColorGrid(Rect viewport)
        {
            float availableWidth = Mathf.Max(1f, viewport.width - ScrollbarWidth - 10f);
            float desiredCellWidth = _swatchSize + SwatchPadding;
            int columnCount = Mathf.Max(1, Mathf.FloorToInt(availableWidth / desiredCellWidth));
            float cellWidth = availableWidth / columnCount;
            float swatchWidth = Mathf.Max(1f, cellWidth - SwatchPadding);
            float cellHeight = _swatchSize + LabelHeight + 4f;
            int visibleCount = CountVisibleDefinitions();
            int rowCount = Mathf.Max(1, Mathf.CeilToInt(visibleCount / (float)columnCount));
            Rect contentRect = new Rect(0f, 0f, availableWidth, rowCount * cellHeight + 4f);

            _scrollPosition = GUI.BeginScrollView(viewport, _scrollPosition, contentRect);
            if (visibleCount == 0)
            {
                GUI.Label(
                    new Rect(8f, 12f, availableWidth - 16f, EditorGUIUtility.singleLineHeight),
                    "No named colors match the current search.",
                    EditorStyles.centeredGreyMiniLabel);
                GUI.EndScrollView();
                return;
            }

            float visibleTop = _scrollPosition.y;
            float visibleBottom = visibleTop + viewport.height;
            int visibleIndex = 0;
            for (int i = 0; i < _definitions.Length; i++)
            {
                if (!IsVisible(i))
                {
                    continue;
                }

                int row = visibleIndex / columnCount;
                int column = visibleIndex - row * columnCount;
                Rect cellRect = new Rect(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
                if (cellRect.yMax >= visibleTop && cellRect.yMin <= visibleBottom)
                {
                    DrawColorSwatch(i, cellRect, swatchWidth);
                }

                visibleIndex++;
            }

            GUI.EndScrollView();
        }

        private void DrawColorSwatch(int index, Rect cellRect, float swatchWidth)
        {
            Color color = _definitions[index].Value;
            float xOffset = (cellRect.width - swatchWidth) * 0.5f;
            Rect colorRect = new Rect(cellRect.x + xOffset, cellRect.y + 2f, swatchWidth, _swatchSize);
            EditorGUI.DrawRect(colorRect, color);
            Color border = EditorGUIUtility.isProSkin
                ? new Color(0.15f, 0.15f, 0.15f)
                : new Color(0.5f, 0.5f, 0.5f);
            DrawRectBorder(colorRect, border);

            _indexStyle.normal.textColor = GetContrastColor(color);
            GUI.Label(
                new Rect(colorRect.x + 3f, colorRect.y + 2f, colorRect.width - 6f, 18f),
                _indexContents[index],
                _indexStyle);

            _labelStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.85f, 0.85f, 0.85f)
                : new Color(0.2f, 0.2f, 0.2f);
            GUI.Label(
                new Rect(cellRect.x, colorRect.yMax + 2f, cellRect.width, LabelHeight),
                _labelContents[index],
                _labelStyle);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && colorRect.Contains(current.mousePosition))
            {
                EditorGUIUtility.systemCopyBuffer = _copyStrings[index];
                ShowNotification(new GUIContent(string.Concat("Copied: ", _copyStrings[index])), 1.5f);
                current.Use();
            }
        }

        private int CountVisibleDefinitions()
        {
            int count = 0;
            for (int i = 0; i < _definitions.Length; i++)
            {
                if (IsVisible(i))
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsVisible(int index)
        {
            if (string.IsNullOrEmpty(_searchFilter))
            {
                return true;
            }

            return _definitions[index].Name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   _hexStrings[index].IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   _indexContents[index].text.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void EnsureCache()
        {
            if (_definitions != null)
            {
                return;
            }

            FieldInfo[] fields = typeof(Colors).GetFields(BindingFlags.Public | BindingFlags.Static);
            Array.Sort(fields, PublicColorFieldComparer.Instance);
            int colorCount = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType == typeof(Color) && fields[i].IsInitOnly)
                {
                    colorCount++;
                }
            }

            _definitions = new ColorDefinition[colorCount];
            _labelContents = new GUIContent[colorCount];
            _indexContents = new GUIContent[colorCount];
            _hexStrings = new string[colorCount];
            _copyStrings = new string[colorCount];

            int writeIndex = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.FieldType != typeof(Color) || !field.IsInitOnly)
                {
                    continue;
                }

                Color color = (Color)field.GetValue(null);
                Span<char> hexBuffer = stackalloc char[7];
                color.TryFormatHex(hexBuffer, out int charsWritten, includeAlpha: false);
                string hex = new string(hexBuffer.Slice(0, charsWritten));
                string indexText = writeIndex.ToString();
                string copyText = string.Concat("Colors.", field.Name);
                string tooltip = string.Concat(
                    field.Name,
                    " [",
                    indexText,
                    "]\n",
                    hex,
                    "\nClick to copy: ",
                    copyText);

                _definitions[writeIndex] = new ColorDefinition(field.Name, color);
                _hexStrings[writeIndex] = hex;
                _copyStrings[writeIndex] = copyText;
                _indexContents[writeIndex] = new GUIContent(indexText);
                _labelContents[writeIndex] = new GUIContent(
                    string.Concat(field.Name, "\n", hex),
                    tooltip);
                writeIndex++;
            }
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

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
        }

        private static Color GetContrastColor(Color background)
        {
            return background.GetContrastRatio(Color.black) >= background.GetContrastRatio(Color.white)
                ? Color.black
                : Color.white;
        }

        private static void DrawRectBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private readonly struct ColorDefinition
        {
            public readonly string Name;
            public readonly Color Value;

            public ColorDefinition(string name, Color value)
            {
                Name = name;
                Value = value;
            }
        }

        private sealed class PublicColorFieldComparer : System.Collections.Generic.IComparer<FieldInfo>
        {
            public static readonly PublicColorFieldComparer Instance = new PublicColorFieldComparer();

            private PublicColorFieldComparer()
            {
            }

            public int Compare(FieldInfo x, FieldInfo y)
            {
                return string.Compare(x.Name, y.Name, StringComparison.Ordinal);
            }
        }
    }
}
