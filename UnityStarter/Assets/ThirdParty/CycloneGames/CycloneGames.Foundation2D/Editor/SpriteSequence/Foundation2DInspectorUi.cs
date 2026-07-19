#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Foundation2D.Editor
{
    internal static class Foundation2DInspectorUi
    {
        internal enum BadgeTone
        {
            Neutral,
            Good,
            Warning,
            Error,
        }

        internal readonly struct CardScope : IDisposable
        {
            internal CardScope(GUIStyle style)
            {
                Color previous = GUI.backgroundColor;
                try
                {
                    GUI.backgroundColor = EditorGUIUtility.isProSkin
                        ? new Color(0.82f, 0.90f, 0.96f, 1f)
                        : new Color(0.96f, 0.98f, 1f, 1f);
                    EditorGUILayout.BeginVertical(style);
                }
                finally
                {
                    GUI.backgroundColor = previous;
                }
            }

            public void Dispose()
            {
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(3f);
            }
        }

        internal readonly struct ActionLayoutScope : IDisposable
        {
            private readonly bool _horizontal;

            internal ActionLayoutScope(bool horizontal)
            {
                _horizontal = horizontal;
                if (_horizontal)
                {
                    EditorGUILayout.BeginHorizontal();
                }
                else
                {
                    EditorGUILayout.BeginVertical();
                }
            }

            public void Dispose()
            {
                if (_horizontal)
                {
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private static GUIStyle _moduleHeaderStyle;
        private static GUIStyle _moduleTitleStyle;
        private static GUIStyle _moduleSubtitleStyle;
        private static GUIStyle _sectionFoldoutStyle;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _cardStyle;
        private static bool _stylesUseProSkin;
        private static bool _stylesInitialized;

        internal static void DrawModuleHeader(GUIContent title, GUIContent subtitle)
        {
            EnsureStyles();

            Color previous = GUI.backgroundColor;
            try
            {
                GUI.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.72f, 0.84f, 0.92f, 1f)
                    : new Color(0.88f, 0.95f, 0.99f, 1f);
                using (new EditorGUILayout.VerticalScope(_moduleHeaderStyle))
                {
                    GUILayout.Label(title, _moduleTitleStyle);
                    GUILayout.Label(subtitle, _moduleSubtitleStyle);
                }
            }
            finally
            {
                GUI.backgroundColor = previous;
            }

            EditorGUILayout.Space(3f);
        }

        internal static bool DrawSectionHeader(
            ref bool expanded,
            GUIContent title,
            GUIContent badge = null,
            BadgeTone tone = BadgeTone.Neutral)
        {
            EnsureStyles();

            float height = Mathf.Max(23f, EditorGUIUtility.singleLineHeight + 5f);
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            Color sectionColor = GetSectionColor(tone);
            if (!expanded)
            {
                sectionColor = Color.Lerp(sectionColor, EditorGUIUtility.isProSkin
                    ? new Color(0.16f, 0.16f, 0.16f, sectionColor.a)
                    : new Color(0.88f, 0.88f, 0.88f, sectionColor.a), 0.28f);
            }

            EditorGUI.DrawRect(rect, sectionColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0f, 0f, 0f, 0.18f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.18f));

            bool drawBadge = badge != null &&
                             !string.IsNullOrEmpty(badge.text) &&
                             rect.width >= 170f;
            Rect badgeRect = default;
            if (drawBadge)
            {
                Vector2 badgeSize = _badgeStyle.CalcSize(badge);
                float badgeWidth = Mathf.Min(rect.width * 0.38f, badgeSize.x + 14f);
                badgeRect = new Rect(rect.xMax - badgeWidth - 6f, rect.y + 3f, badgeWidth, rect.height - 6f);
            }

            float foldoutRight = drawBadge ? badgeRect.xMin - 4f : rect.xMax - 5f;
            float foldoutLeft = rect.x + 5f;
            Rect desiredFoldoutRect = new(
                foldoutLeft,
                rect.y,
                Mathf.Max(0f, foldoutRight - foldoutLeft),
                rect.height);
            float hierarchyOffset = Mathf.Max(
                0f,
                EditorStyles.foldout.padding.left - EditorStyles.label.padding.left);
            Rect foldoutRect = CalculateSectionFoldoutControlRect(
                desiredFoldoutRect,
                _sectionFoldoutStyle.margin,
                EditorGUIUtility.hierarchyMode,
                hierarchyOffset);

            Rect clippedFoldoutRect = new(
                foldoutRect.x - rect.x,
                foldoutRect.y - rect.y,
                foldoutRect.width,
                foldoutRect.height);
            GUI.BeginClip(rect);
            try
            {
                expanded = EditorGUI.Foldout(clippedFoldoutRect, expanded, title, true, _sectionFoldoutStyle);
            }
            finally
            {
                GUI.EndClip();
            }

            if (drawBadge)
            {
                EditorGUI.DrawRect(badgeRect, GetBadgeColor(tone));
                GUI.Label(badgeRect, badge, _badgeStyle);
            }

            return expanded;
        }

        internal static Rect CalculateSectionFoldoutControlRect(
            Rect desiredResolvedRect,
            RectOffset styleMargin,
            bool hierarchyMode,
            float hierarchyOffset)
        {
            Rect beforeHierarchyAdjustment = desiredResolvedRect;
            if (hierarchyMode)
            {
                beforeHierarchyAdjustment.xMin = Mathf.Min(
                    beforeHierarchyAdjustment.xMax,
                    beforeHierarchyAdjustment.xMin + Mathf.Max(0f, hierarchyOffset));
            }

            // EditorGUI.Foldout removes the style margin, then subtracts the hierarchy
            // offset from xMin. Apply the inverse transform so its resolved draw rect
            // remains the requested rect fully inside the colored header.
            return styleMargin.Add(beforeHierarchyAdjustment);
        }

        internal static CardScope BeginCard()
        {
            EnsureStyles();
            return new CardScope(_cardStyle);
        }

        internal static ActionLayoutScope BeginActionLayout(int actionCount, float minimumButtonWidth)
        {
            float availableWidth = Mathf.Max(0f, EditorGUIUtility.currentViewWidth - 50f);
            bool horizontal = actionCount <= 1 || availableWidth >= actionCount * minimumButtonWidth;
            return new ActionLayoutScope(horizontal);
        }

        internal static bool ValidateRequiredProperties(
            SerializedObject serializedObject,
            string inspectorName,
            string[] requiredPropertyPaths,
            out string error)
        {
            if (serializedObject == null)
            {
                error = $"{inspectorName} cannot access its SerializedObject.";
                return false;
            }

            string missing = null;
            for (int i = 0; i < requiredPropertyPaths.Length; i++)
            {
                string path = requiredPropertyPaths[i];
                if (serializedObject.FindProperty(path) != null)
                {
                    continue;
                }

                missing = string.IsNullOrEmpty(missing) ? path : missing + ", " + path;
            }

            if (string.IsNullOrEmpty(missing))
            {
                error = null;
                return true;
            }

            error = $"{inspectorName} is out of sync with the serialized component schema. Missing properties: {missing}. Reimport scripts and update the custom Inspector before editing this component.";
            return false;
        }

        internal static void DrawInvalidSerializedPropertyState(
            SerializedObject serializedObject,
            GUIContent title,
            GUIContent subtitle,
            string error)
        {
            DrawModuleHeader(title, subtitle);
            EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.HelpBox(
                "The safe fallback below exposes the remaining serialized fields without running custom actions.",
                MessageType.Info);
            DrawRemainingProperties(serializedObject, null);
        }

        internal static void DrawMultiObjectActionNotice()
        {
            EditorGUILayout.HelpBox(
                "Common serialized settings can be edited together. Preview, asset, renderer-selection, and frame-structure actions require a single selected object.",
                MessageType.Info);
        }

        internal static void DrawRemainingProperties(SerializedObject serializedObject, string[] explicitlyDrawnPaths)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script" || Contains(explicitlyDrawnPaths, iterator.propertyPath))
                {
                    continue;
                }

                bool includeChildren = iterator.hasVisibleChildren &&
                                       iterator.propertyType != SerializedPropertyType.ObjectReference;
                EditorGUILayout.PropertyField(iterator, includeChildren);
            }
        }

        private static bool Contains(string[] paths, string propertyPath)
        {
            if (paths == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] == propertyPath)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureStyles()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            if (_stylesInitialized && _stylesUseProSkin == proSkin)
            {
                return;
            }

            _stylesUseProSkin = proSkin;
            _stylesInitialized = true;

            _moduleHeaderStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 7, 7),
                margin = new RectOffset(0, 0, 0, 2),
            };
            _moduleTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
            };
            _moduleSubtitleStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            _sectionFoldoutStyle = new GUIStyle(EditorStyles.foldout)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
            };
            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };
            _cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 0, 3),
            };
        }

        private static Color GetSectionColor(BadgeTone tone)
        {
            Color baseColor = GetBadgeColor(tone);
            return EditorGUIUtility.isProSkin
                ? new Color(baseColor.r * 0.48f, baseColor.g * 0.48f, baseColor.b * 0.48f, 0.90f)
                : new Color(baseColor.r, baseColor.g, baseColor.b, 0.22f);
        }

        private static Color GetBadgeColor(BadgeTone tone)
        {
            return tone switch
            {
                BadgeTone.Good => new Color(0.18f, 0.50f, 0.31f, 0.96f),
                BadgeTone.Warning => new Color(0.68f, 0.40f, 0.08f, 0.96f),
                BadgeTone.Error => new Color(0.66f, 0.20f, 0.20f, 0.96f),
                _ => new Color(0.22f, 0.40f, 0.54f, 0.96f),
            };
        }
    }
}
#endif
