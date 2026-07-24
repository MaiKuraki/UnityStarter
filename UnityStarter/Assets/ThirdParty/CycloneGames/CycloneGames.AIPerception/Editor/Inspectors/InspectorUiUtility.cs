using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace CycloneGames.AIPerception.Editor
{
    /// <summary>
    /// Package-local IMGUI primitives shared by the AI Perception inspectors.
    /// Keeps presentation, mixed-value handling, and low-frequency diagnostics consistent.
    /// </summary>
    internal static class InspectorUiUtility
    {
        internal const double RuntimeRefreshIntervalSeconds = 0.25d;

        internal static readonly Color SuccessColor = new Color(0.18f, 0.62f, 0.38f);
        internal static readonly Color WarningColor = new Color(0.82f, 0.51f, 0.12f);
        internal static readonly Color NeutralColor = new Color(0.38f, 0.42f, 0.47f);
        internal static readonly Color RuntimeColor = new Color(0.42f, 0.36f, 0.68f);

        private const float HeaderHorizontalPadding = 6f;
        private const float HeaderArrowWidth = 13f;
        private const float BadgeHorizontalPadding = 7f;

        private static readonly SerializedProperty[] EmptyProperties = new SerializedProperty[0];
        private static readonly Vector3[] TrianglePoints = new Vector3[3];

        private static GUIStyle _titleStyle;
        private static GUIStyle _subtitleStyle;
        private static GUIStyle _foldoutLabelStyle;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _statusLabelStyle;
        private static GUIStyle _statusValueStyle;
        private static GUIStyle _subsectionStyle;
        private static GUIStyle _centeredMiniLabelStyle;

        internal static GUIStyle CenteredMiniLabelStyle
        {
            get
            {
                EnsureStyles();
                return _centeredMiniLabelStyle;
            }
        }

        internal static void DrawInspectorTitle(string title, string subtitle, Color accentColor)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 42f);
            Color panelColor = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.19f, 1f)
                : new Color(0.82f, 0.84f, 0.87f, 1f);
            EditorGUI.DrawRect(rect, panelColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accentColor);
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                new Color(0f, 0f, 0f, 0.25f));

            Rect titleRect = new Rect(rect.x + 12f, rect.y + 4f, rect.width - 18f, 20f);
            Rect subtitleRect = new Rect(rect.x + 12f, rect.y + 23f, rect.width - 18f, 15f);
            EditorGUI.LabelField(titleRect, title, _titleStyle);
            EditorGUI.LabelField(subtitleRect, subtitle, _subtitleStyle);
            EditorGUILayout.Space(3f);
        }

        internal static bool DrawSectionHeader(
            ref bool expanded,
            GUIContent title,
            Color accent,
            SerializedProperty enabledProperty = null,
            string badge = null,
            Color? badgeColor = null)
        {
            EnsureStyles();

            if (enabledProperty != null && string.IsNullOrEmpty(badge))
            {
                if (enabledProperty.hasMultipleDifferentValues)
                {
                    badge = "MIXED";
                    badgeColor = NeutralColor;
                }
                else if (enabledProperty.boolValue)
                {
                    badge = "ENABLED";
                    badgeColor = SuccessColor;
                }
                else
                {
                    badge = "DISABLED";
                    badgeColor = NeutralColor;
                }
            }

            EditorGUILayout.Space(2f);
            Rect rect = EditorGUILayout.GetControlRect(false, 23f);
            float shade = expanded ? 1f : 0.72f;
            EditorGUI.DrawRect(
                rect,
                new Color(accent.r * shade, accent.g * shade, accent.b * shade, 0.96f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, 1f),
                new Color(1f, 1f, 1f, 0.10f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                new Color(0f, 0f, 0f, 0.24f));

            Rect arrowRect = new Rect(
                rect.x + HeaderHorizontalPadding,
                rect.y + 2f,
                HeaderArrowWidth,
                rect.height - 4f);
            float toggleWidth = enabledProperty == null ? 0f : 24f;
            float badgeWidth = string.IsNullOrEmpty(badge)
                ? 0f
                : Mathf.Clamp(26f + badge.Length * 6f, 52f, 112f);
            Rect labelRect = new Rect(
                arrowRect.xMax + 2f,
                rect.y,
                Mathf.Max(
                    0f,
                    rect.width - (arrowRect.xMax - rect.x) - badgeWidth - toggleWidth - HeaderHorizontalPadding - 5f),
                rect.height);

            DrawFoldoutTriangle(arrowRect, expanded);
            EditorGUI.LabelField(labelRect, title, _foldoutLabelStyle);

            float trailingX = rect.xMax - 5f;
            Rect toggleRect = default;
            if (enabledProperty != null)
            {
                toggleRect = new Rect(trailingX - 18f, rect.y + 2f, 18f, 18f);
                DrawMixedToggle(toggleRect, enabledProperty);
                trailingX = toggleRect.x - 5f;
            }

            if (badgeWidth > 0f)
            {
                Rect badgeRect = new Rect(trailingX - badgeWidth, rect.y + 3f, badgeWidth, rect.height - 6f);
                DrawStatusBadge(badgeRect, badge, badgeColor ?? new Color(0f, 0f, 0f, 0.28f));
            }

            Event current = Event.current;
            if (current.type == EventType.MouseDown &&
                rect.Contains(current.mousePosition) &&
                (enabledProperty == null || !toggleRect.Contains(current.mousePosition)))
            {
                expanded = !expanded;
                current.Use();
            }

            return expanded;
        }

        internal static void BeginPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(2f);
        }

        internal static void EndPanel()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.EndVertical();
        }

        internal static void DrawSubsectionLabel(string label)
        {
            EnsureStyles();
            EditorGUILayout.Space(4f);
            Rect rect = EditorGUILayout.GetControlRect(false, 17f);
            EditorGUI.LabelField(
                new Rect(rect.x + 2f, rect.y, rect.width - 2f, rect.height),
                label,
                _subsectionStyle);
        }

        internal static void DrawStatusRow(string label, string value, Color valueColor)
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

        internal static void DrawAuthoringLockedHelpBox(string message)
        {
            EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        internal static bool IsEnabledOrMixed(SerializedProperty property)
        {
            return property != null && (property.hasMultipleDifferentValues || property.boolValue);
        }

        internal static bool AreAssigned(params SerializedProperty[] properties)
        {
            if (properties == null)
            {
                return false;
            }

            for (int i = 0; i < properties.Length; i++)
            {
                if (properties[i] == null)
                {
                    return false;
                }
            }

            return true;
        }

        internal static SerializedProperty[] CacheRemainingProperties(
            SerializedObject serializedObject,
            params SerializedProperty[] drawnProperties)
        {
            if (serializedObject == null)
            {
                return EmptyProperties;
            }

            var properties = new List<SerializedProperty>(4);
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script" || ContainsPath(drawnProperties, iterator.propertyPath))
                {
                    continue;
                }

                properties.Add(iterator.Copy());
            }

            return properties.Count == 0 ? EmptyProperties : properties.ToArray();
        }

        internal static SerializedProperty[] CacheRemainingChildren(
            SerializedProperty parent,
            params SerializedProperty[] drawnProperties)
        {
            if (parent == null)
            {
                return EmptyProperties;
            }

            var properties = new List<SerializedProperty>(2);
            SerializedProperty iterator = parent.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                if (iterator.depth != parent.depth + 1 || ContainsPath(drawnProperties, iterator.propertyPath))
                {
                    continue;
                }

                properties.Add(iterator.Copy());
            }

            return properties.Count == 0 ? EmptyProperties : properties.ToArray();
        }

        internal static void DrawProperties(SerializedProperty[] properties)
        {
            if (properties == null)
            {
                return;
            }

            for (int i = 0; i < properties.Length; i++)
            {
                EditorGUILayout.PropertyField(properties[i], true);
            }
        }

        internal static void DrawRemainingProperties(
            SerializedProperty[] properties,
            ref bool expanded,
            GUIContent title,
            Color accent)
        {
            if (properties == null || properties.Length == 0)
            {
                return;
            }

            DrawSectionHeader(ref expanded, title, accent, badge: properties.Length + " FIELDS");
            if (!expanded)
            {
                return;
            }

            BeginPanel();
            InspectorUiUtility.DrawProperties(properties);
            EndPanel();
        }

        internal static void RequestRuntimeRepaint(
            UnityEditor.Editor editor,
            bool diagnosticsVisible,
            ref double nextRefreshTime)
        {
            if (!Application.isPlaying || !diagnosticsVisible || editor == null)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = now + RuntimeRefreshIntervalSeconds;
            editor.Repaint();
            SceneView.RepaintAll();
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void DrawStatusBadge(Rect rect, string label, Color color)
        {
            EnsureStyles();

            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.92f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, 1f),
                new Color(1f, 1f, 1f, 0.12f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                new Color(0f, 0f, 0f, 0.20f));

            Rect labelRect = new Rect(
                rect.x + BadgeHorizontalPadding,
                rect.y,
                Mathf.Max(0f, rect.width - BadgeHorizontalPadding * 2f),
                rect.height);
            EditorGUI.LabelField(labelRect, label, _badgeStyle);
        }

        private static void DrawMixedToggle(Rect rect, SerializedProperty property)
        {
            EditorGUI.BeginProperty(rect, GUIContent.none, property);
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            EditorGUI.BeginChangeCheck();
            bool value = EditorGUI.Toggle(rect, property.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                property.boolValue = value;
            }

            EditorGUI.showMixedValue = previousMixedValue;
            EditorGUI.EndProperty();
        }

        private static bool ContainsPath(SerializedProperty[] properties, string propertyPath)
        {
            if (properties == null)
            {
                return false;
            }

            for (int i = 0; i < properties.Length; i++)
            {
                SerializedProperty property = properties[i];
                if (property != null && property.propertyPath == propertyPath)
                {
                    return true;
                }
            }

            return false;
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
                clipping = TextClipping.Clip
            };
            _foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
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
            _subsectionStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _centeredMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
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
