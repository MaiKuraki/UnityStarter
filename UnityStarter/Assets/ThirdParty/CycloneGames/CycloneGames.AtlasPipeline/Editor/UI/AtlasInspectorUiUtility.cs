using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Atlas pipeline editor UI presentation helpers. The API is intentionally small so custom
    /// inspectors and authoring windows share one visual language without copying layout code.
    /// </summary>
    public static class AtlasInspectorUiUtility
    {
        private const float HeaderHorizontalPadding = 6f;
        private const float HeaderArrowWidth = 13f;
        private const float BadgeHorizontalPadding = 7f;

        private static readonly Vector3[] TrianglePoints = new Vector3[3];

        public static readonly Color ArtColor = new Color(0.48f, 0.42f, 0.74f);
        public static readonly Color ImportColor = new Color(0.10f, 0.58f, 0.76f);
        public static readonly Color AtlasColor = new Color(0.16f, 0.48f, 0.78f);
        public static readonly Color SuccessColor = new Color(0.18f, 0.62f, 0.38f);
        public static readonly Color WarningColor = new Color(0.82f, 0.51f, 0.12f);
        public static readonly Color NeutralColor = new Color(0.38f, 0.42f, 0.47f);

        private static GUIStyle _titleStyle;
        private static GUIStyle _subtitleStyle;
        private static GUIStyle _sectionTitleStyle;
        private static GUIStyle _foldoutLabelStyle;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _statusLabelStyle;
        private static GUIStyle _statusValueStyle;

        public static void DrawInspectorTitle(string title, string subtitle, Color accentColor)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 46f);
            Color panelColor = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.19f, 1f)
                : new Color(0.82f, 0.84f, 0.87f, 1f);
            EditorGUI.DrawRect(rect, panelColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accentColor);
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                new Color(0f, 0f, 0f, 0.25f));

            EditorGUI.LabelField(
                new Rect(rect.x + 12f, rect.y + 5f, rect.width - 18f, 21f),
                title,
                _titleStyle);
            EditorGUI.LabelField(
                new Rect(rect.x + 12f, rect.y + 26f, rect.width - 18f, 16f),
                subtitle,
                _subtitleStyle);
            EditorGUILayout.Space(3f);
        }

        public static void DrawSectionHeader(string title, string subtitle, Color accentColor)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 31f);
            EditorGUI.DrawRect(
                rect,
                EditorGUIUtility.isProSkin
                    ? new Color(0.18f, 0.20f, 0.23f, 1f)
                    : new Color(0.76f, 0.79f, 0.83f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accentColor);

            EditorGUI.LabelField(
                new Rect(rect.x + 10f, rect.y + 4f, rect.width - 18f, 14f),
                title,
                _sectionTitleStyle);
            EditorGUI.LabelField(
                new Rect(rect.x + 10f, rect.y + 18f, rect.width - 18f, 12f),
                subtitle,
                _subtitleStyle);
        }

        public static bool DrawFoldoutHeader(
            string title,
            bool foldout,
            Color color,
            string badge = null,
            Color? badgeColor = null)
        {
            EnsureStyles();

            EditorGUILayout.Space(2f);
            Rect rect = EditorGUILayout.GetControlRect(false, 23f);
            float shade = foldout ? 1f : 0.72f;
            EditorGUI.DrawRect(rect, new Color(color.r * shade, color.g * shade, color.b * shade, 0.96f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.10f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.24f));

            Rect arrowRect = new Rect(
                rect.x + HeaderHorizontalPadding,
                rect.y + 2f,
                HeaderArrowWidth,
                rect.height - 4f);

            float badgeWidth = string.IsNullOrEmpty(badge)
                ? 0f
                : Mathf.Clamp(26f + badge.Length * 6f, 52f, 112f);
            Rect labelRect = new Rect(
                arrowRect.xMax + 2f,
                rect.y,
                rect.width - (arrowRect.xMax - rect.x) - badgeWidth - HeaderHorizontalPadding - 5f,
                rect.height);

            DrawFoldoutTriangle(arrowRect, foldout);
            EditorGUI.LabelField(labelRect, title, _foldoutLabelStyle);

            if (badgeWidth > 0f)
            {
                Rect badgeRect = new Rect(
                    rect.xMax - badgeWidth - 5f,
                    rect.y + 3f,
                    badgeWidth,
                    rect.height - 6f);
                DrawStatusBadge(badgeRect, badge, badgeColor ?? new Color(0f, 0f, 0f, 0.28f));
            }

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }

            return foldout;
        }

        public static void BeginPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(2f);
        }

        public static void EndPanel()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.EndVertical();
        }

        public static void DrawStatusBadge(string label, Color color, float width)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 18f, GUILayout.Width(width));
            DrawStatusBadge(rect, label, color);
        }

        public static void DrawStatusBadge(Rect rect, string label, Color color)
        {
            EnsureStyles();

            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.92f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.12f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.20f));

            Rect labelRect = new Rect(
                rect.x + BadgeHorizontalPadding,
                rect.y,
                Mathf.Max(0f, rect.width - BadgeHorizontalPadding * 2f),
                rect.height);
            EditorGUI.LabelField(labelRect, label, _badgeStyle);
        }

        public static void DrawStatusRow(string label, string value, Color valueColor)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            Rect markerRect = new Rect(rect.x + 2f, rect.y + 5f, 8f, 8f);
            Rect labelRect = new Rect(markerRect.xMax + 7f, rect.y, rect.width * 0.48f, rect.height);
            Rect valueRect = new Rect(labelRect.xMax, rect.y, rect.xMax - labelRect.xMax - 3f, rect.height);
            EditorGUI.DrawRect(markerRect, valueColor);
            EditorGUI.LabelField(labelRect, label, _statusLabelStyle);

            Color previousColor = GUI.color;
            GUI.color = valueColor;
            EditorGUI.LabelField(valueRect, value, _statusValueStyle);
            GUI.color = previousColor;
        }

        public static void DrawMetric(string label, string value, Color color)
        {
            EnsureStyles();

            Rect rect = GUILayoutUtility.GetRect(
                72f,
                38f,
                GUILayout.MinWidth(66f),
                GUILayout.Height(38f));
            EditorGUI.DrawRect(
                rect,
                new Color(color.r, color.g, color.b, EditorGUIUtility.isProSkin ? 0.34f : 0.20f));
            GUI.Label(new Rect(rect.x, rect.y + 4f, rect.width, 17f), value, _badgeStyle);
            GUI.Label(new Rect(rect.x, rect.y + 20f, rect.width, 14f), label, _subtitleStyle);
        }

        private static void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };

            _statusLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _statusValueStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip
            };
        }

        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            Vector2 center = rect.center;
            if (expanded)
            {
                TrianglePoints[0] = new Vector3(center.x - 4f, center.y - 2f);
                TrianglePoints[1] = new Vector3(center.x + 4f, center.y - 2f);
                TrianglePoints[2] = new Vector3(center.x, center.y + 3f);
            }
            else
            {
                TrianglePoints[0] = new Vector3(center.x - 2f, center.y - 4f);
                TrianglePoints[1] = new Vector3(center.x - 2f, center.y + 4f);
                TrianglePoints[2] = new Vector3(center.x + 3f, center.y);
            }

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(0.92f, 0.92f, 0.92f, 0.96f);
            Handles.DrawAAConvexPolygon(TrianglePoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }
}
