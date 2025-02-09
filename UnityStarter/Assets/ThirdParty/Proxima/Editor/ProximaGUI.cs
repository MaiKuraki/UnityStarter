using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Proxima.Editor
{
    internal static class ProximaGUI
    {
        private static Dictionary<string, List<Texture2D>> _bgTextures = new Dictionary<string, List<Texture2D>>();

        internal static void Vertical(Action action)
        {
            EditorGUILayout.BeginVertical();
            action();
            EditorGUILayout.EndVertical();
        }

        internal static void Vertical(float width, Action action)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width));
            action();
            EditorGUILayout.EndVertical();
        }

        internal static void Vertical(GUIStyle style, Action action)
        {
            EditorGUILayout.BeginVertical(style, GUILayout.ExpandWidth(false));
            action();
            EditorGUILayout.EndVertical();
        }

        internal static void Vertical(GUIStyle style, float width, Action action)
        {
            EditorGUILayout.BeginVertical(style, GUILayout.Width(width));
            action();
            EditorGUILayout.EndVertical();
        }

        internal static void VerticalExpanded(GUIStyle style, Action action)
        {
            EditorGUILayout.BeginVertical(style, GUILayout.ExpandHeight(true));
            action();
            EditorGUILayout.EndVertical();
        }

        internal static void Horizontal(Action action)
        {
            EditorGUILayout.BeginHorizontal();
            action();
            EditorGUILayout.EndHorizontal();
        }

        internal static void Horizontal(GUIStyle style, Action action)
        {
            EditorGUILayout.BeginHorizontal(style);
            action();
            EditorGUILayout.EndHorizontal();
        }

        internal static void HorizontalExpanded(Action action)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            action();
            EditorGUILayout.EndHorizontal();
        }

        internal static void HorizontalCentered(Action action)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            action();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        internal static void HorizontalCentered(GUIStyle style, Action action)
        {
            EditorGUILayout.BeginHorizontal(style);
            GUILayout.FlexibleSpace();
            action();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        internal static Vector2 Scroll(Vector2 scrollPosition, Action action)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandWidth(true));
            action();
            EditorGUILayout.EndScrollView();
            return scrollPosition;
        }

        internal static Vector2 Scroll(Vector2 scrollPosition, float height, Action action)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(height), GUILayout.ExpandWidth(true));
            action();
            EditorGUILayout.EndScrollView();
            return scrollPosition;
        }

        internal static Vector2 Scroll(Vector2 scrollPosition, GUIStyle style, Action action)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, style, GUILayout.ExpandWidth(true));
            action();
            EditorGUILayout.EndScrollView();
            return scrollPosition;
        }

        internal static void DisableGroup(bool disable, Action action)
        {
            EditorGUI.BeginDisabledGroup(disable);
            action();
            EditorGUI.EndDisabledGroup();
        }

        private static Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

        internal static bool ImageButton(string guid, int width, int height)
        {
            if (!_textures.TryGetValue(guid, out var texture))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                _textures[guid] = texture;
            }

            return GUILayout.Button(texture, GUILayout.Width(width), GUILayout.Height(height));
        }

        public static void Image(string guid, int width, int height)
        {
            if (!_textures.TryGetValue(guid, out var texture))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                _textures[guid] = texture;
            }

            GUILayout.Label(texture, GUILayout.Width(width), GUILayout.Height(height));
        }

        public static bool Checkbox(bool value, Action action)
        {
            Horizontal(() =>
            {
                GUILayout.Space(10);
                value = EditorGUILayout.Toggle(value, GUILayout.Width(20));
                action();
            });

            return value;
        }

        public static bool Checkbox(bool value, string label, GUIStyle labelStyle)
        {
            return Checkbox(value, () => GUILayout.Label(label, labelStyle));
        }

        public static bool Button(string label, GUIStyle style, int width, int height)
        {
            var labelContent = new GUIContent(label);
            var position = GUILayoutUtility.GetRect(width, height, style);
            EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);
            return GUI.Button(position, labelContent, style);
        }

        public static Rect GetLinkRect(GUIContent labelContent, GUIStyle style, Vector2 position)
        {
            var size = style.CalcSize(labelContent);
            return new Rect(position.x - size.x * 0.5f, position.y - size.y * 0.5f, size.x, size.y);
        }

        public static bool Link(string label, GUIStyle style, float lineThickness = 0.5f)
        {
            var labelContent = new GUIContent(label);
            var rect = GUILayoutUtility.GetRect(labelContent, style, GUILayout.ExpandWidth(false));
            return Link(labelContent, style, rect, lineThickness);
        }

        public static bool Link(GUIContent labelContent, GUIStyle style, Rect rect, float lineThickness = 0.5f)
        {
            // Draw a box for the underline
            var lineRect = new Rect(rect.xMin, rect.yMax - lineThickness / 2, rect.width, lineThickness);
            EditorGUI.DrawRect(lineRect, style.normal.textColor);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return GUI.Button(rect, labelContent, style);
        }

        public static void HorizontalLine()
        {
            HorizontalLine(Color.white);
        }

        public static void HorizontalLine(Color color, float thickness = 0.5f)
        {
            var position = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true));
            HorizontalLine(position.xMin, position.xMax, position.y, color, thickness);
        }

        public static void HorizontalLine(float startX, float endX, float y)
        {
            HorizontalLine(startX, endX, y, Color.white);
        }

        public static void HorizontalLine(float startX, float endX, float y, Color color, float thickness = 0.5f)
        {
            EditorGUI.DrawRect(new Rect(startX, y - thickness / 2, endX - startX, thickness), color);
        }

        public static void VerticalLine()
        {
            VerticalLine(Color.white);
        }

        public static void VerticalLine(Color color, float thickness = 0.5f)
        {
            var position = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandHeight(true));
            VerticalLine(position.x, position.yMin, position.yMax, color, thickness);
        }

        public static void VerticalLine(float x, float startY, float endY)
        {
            VerticalLine(x, startY, endY, Color.white);
        }

        public static void VerticalLine(float x, float startY, float endY, Color color, float thickness = 0.5f)
        {
            EditorGUI.DrawRect(new Rect(x - thickness / 2, startY, thickness, endY - startY), color);
        }

        public static int StyleFontSize;
        public static string StyleTag;
        public static Font StyleFont = null;

        public static GUIStyle CreateStyle()
        {
            var style = new GUIStyle();
            style.wordWrap = true;
            style.richText = true;
            style.fontSize = StyleFontSize;
            style.font = StyleFont;
            return style;
        }

        public static GUIStyle CreateStyle(string textColor)
        {
            return CreateStyle(HexColor(textColor));
        }

        public static GUIStyle CreateStyle(Color textColor)
        {
            var style = CreateStyle();
            SetTextColor(style, textColor);
            return style;
        }

        public static GUIStyle CreateStyle(string textColor, string backgroundColor)
        {
            return CreateStyle(HexColor(textColor), HexColor(backgroundColor));
        }

        public static GUIStyle CreateStyle(Color textColor, Color backgroundColor)
        {
            var style = CreateStyle(textColor);
            SetBackgroundColor(style, backgroundColor);
            return style;
        }

        public static void SetBackgroundColor(GUIStyle style, Color color)
        {
            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, color);
            bgTex.Apply();

            style.normal.background =
                style.active.background =
                style.focused.background =
                style.hover.background = bgTex;

            bgTex.hideFlags = HideFlags.DontSave;

            if (!_bgTextures.TryGetValue(StyleTag, out var textures))
            {
                textures = new List<Texture2D>();
                _bgTextures[StyleTag] = textures;
            }

            textures.Add(bgTex);
        }

        public static void SetBackgroundImage(GUIStyle style, Texture2D image)
        {
            style.normal.background =
                style.active.background =
                style.focused.background =
                style.hover.background = image;
        }

        public static void CleanupBackgroundTextures(string styleTag)
        {
            if (_bgTextures.TryGetValue(styleTag, out var textures))
            {
                foreach (var texture in textures)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                _bgTextures.Remove(styleTag);
            }
        }

        public static void SetTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor =
                style.active.textColor =
                style.focused.textColor =
                style.hover.textColor = color;
        }

        public static Color Gray(int gray)
        {
            return new Color(gray / 255f, gray / 255f, gray / 255f);
        }

        public static Color HexColor(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            return color;
        }

        public static bool HelpBoxLinkButton(string message, MessageType type)
        {
            Texture icon = null;

            switch (type)
            {
                case MessageType.Info:
                    icon = EditorGUIUtility.IconContent("console.infoicon").image;
                    break;
                case MessageType.Warning:
                    icon = EditorGUIUtility.IconContent("console.warnicon").image;
                    break;
                case MessageType.Error:
                    icon = EditorGUIUtility.IconContent("console.erroricon").image;
                    break;
            }

            var style = new GUIStyle(EditorStyles.helpBox)
            {
                richText = true,
                fontSize = EditorStyles.helpBox.fontSize
            };
            EditorGUILayout.LabelField(GUIContent.none, new GUIContent(message, icon), style);
            var position = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);
            return GUI.Button(position, GUIContent.none, EditorStyles.linkLabel);
        }

        public static void Box(int width, int height, Color backgroundColor, Color borderColor, int borderThickness)
        {
            var rect = GUILayoutUtility.GetRect(width, height);
            Rect outer = new Rect(rect);
            Rect inner = new Rect(rect.x + borderThickness,
                rect.y + borderThickness,
                rect.width - borderThickness * 2,
                rect.height - borderThickness * 2);

            EditorGUI.DrawRect(outer, borderColor);
            EditorGUI.DrawRect(inner, backgroundColor);
        }

        public static Rect GetRect(int width, int height)
        {
            return GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
        }

        public static bool BoxButton(GUIContent content, Rect rect, GUIStyle style, Color backgroundColor, Color borderColor, int borderThickness)
        {
            Rect inner = new Rect(
                rect.x + borderThickness,
                rect.y + borderThickness,
                rect.width - borderThickness * 2,
                rect.height - borderThickness * 2);

            EditorGUI.DrawRect(rect, borderColor);
            EditorGUI.DrawRect(inner, backgroundColor);

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return GUI.Button(rect, content, style);
        }
    }
}