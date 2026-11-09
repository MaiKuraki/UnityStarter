using UnityEditor;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>
    /// Package-local IMGUI primitives shared by the AnimationRuntime inspectors.
    /// Keeps titles, section headers, status rows and the throttled runtime refresh consistent with the other
    /// CycloneGames inspectors without taking a dependency on another package's editor assembly.
    /// </summary>
    internal static class InspectorUiUtility
    {
        internal const double RuntimeRefreshIntervalSeconds = 0.25d;

        internal static readonly Color SuccessColor = new Color(0.18f, 0.62f, 0.38f);
        internal static readonly Color WarningColor = new Color(0.82f, 0.51f, 0.12f);
        internal static readonly Color NeutralColor = new Color(0.38f, 0.42f, 0.47f);
        internal static readonly Color RuntimeColor = new Color(0.42f, 0.36f, 0.68f);

        private const float TitleHeight = 34f;
        private const float SectionHeight = 22f;
        private const float BarHeight = 14f;
        private const float HeaderHorizontalPadding = 4f;
        private const float HeaderArrowWidth = 13f;

        private static GUIStyle _titleStyle;
        private static GUIStyle _subtitleStyle;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _valueStyle;
        private static readonly Vector3[] _trianglePoints = new Vector3[3];

        internal static void DrawTitle(string title, string subtitle, Color accent)
        {
            EnsureStyles();

            Rect rect = GUILayoutUtility.GetRect(0f, TitleHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(accent.r, accent.g, accent.b, 0.12f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);

            var titleRect = new Rect(rect.x + 10f, rect.y + 4f, rect.width - 14f, 18f);
            GUI.Label(titleRect, title, _titleStyle);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleRect = new Rect(rect.x + 10f, rect.y + 19f, rect.width - 14f, 14f);
                GUI.Label(subtitleRect, subtitle, _subtitleStyle);
            }
        }

        internal static bool DrawSectionHeader(ref bool foldout, string label, Color accent, string hint = null)
        {
            EnsureStyles();

            Rect rect = GUILayoutUtility.GetRect(0f, SectionHeight, GUILayout.ExpandWidth(true));
            float shade = foldout ? 0.12f : 0.06f;
            EditorGUI.DrawRect(rect, new Color(accent.r, accent.g, accent.b, shade));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.12f));

            var arrowRect = new Rect(
                rect.x + HeaderHorizontalPadding,
                rect.y + 2f,
                HeaderArrowWidth,
                rect.height - 4f);

            var labelRect = new Rect(
                arrowRect.xMax + 1f,
                rect.y,
                rect.width - (arrowRect.xMax - rect.x) - HeaderHorizontalPadding - 1f,
                rect.height);

            DrawFoldoutTriangle(arrowRect, foldout);
            EditorGUI.LabelField(labelRect, label, _sectionStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }

            if (foldout && !string.IsNullOrEmpty(hint))
            {
                EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
            }

            return foldout;
        }

        /// <summary>
        /// Vector triangle, matching the section headers used by the other CycloneGames inspectors. A glyph in a
        /// small button clips against the label style's line height; a polygon has no font metrics to fight.
        /// </summary>
        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            Vector2 center = rect.center;

            if (expanded)
            {
                _trianglePoints[0] = new Vector3(center.x - 4f, center.y - 2f);
                _trianglePoints[1] = new Vector3(center.x + 4f, center.y - 2f);
                _trianglePoints[2] = new Vector3(center.x, center.y + 3f);
            }
            else
            {
                _trianglePoints[0] = new Vector3(center.x - 2f, center.y - 4f);
                _trianglePoints[1] = new Vector3(center.x - 2f, center.y + 4f);
                _trianglePoints[2] = new Vector3(center.x + 3f, center.y);
            }

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = EditorGUIUtility.isProSkin
                ? new Color(0.90f, 0.90f, 0.90f, 0.95f)
                : new Color(0.16f, 0.16f, 0.18f, 0.90f);
            Handles.DrawAAConvexPolygon(_trianglePoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }

        internal static void BeginPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        }

        internal static void EndPanel()
        {
            EditorGUILayout.EndVertical();
        }

        internal static void DrawStatusRow(string label, string value, Color valueColor)
        {
            EnsureStyles();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(150f));
            Color previous = GUI.contentColor;
            GUI.contentColor = valueColor;
            EditorGUILayout.LabelField(value, _valueStyle);
            GUI.contentColor = previous;
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Draws a thin weight bar. Used for layer weight and per-playback weight.</summary>
        internal static void DrawBar(float value, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, BarHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.08f));

            float clamped = value < 0f ? 0f : (value > 1f ? 1f : value);
            if (clamped > 0f)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * clamped, rect.height), color);
            }

            GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height), clamped.ToString("0.000"), EditorStyles.miniLabel);
        }

        /// <summary>
        /// Returns true when the throttled runtime block should refresh and advances the timer.
        /// Editors call this instead of repainting every frame.
        /// </summary>
        internal static bool ShouldRefresh(ref double nextRefreshTime)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextRefreshTime)
            {
                return false;
            }

            nextRefreshTime = now + RuntimeRefreshIntervalSeconds;
            return true;
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };

            _subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft
            };

            _valueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold
            };
        }
    }
}
