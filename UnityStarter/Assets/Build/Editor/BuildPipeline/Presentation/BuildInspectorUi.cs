using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace Build.Pipeline.Editor
{
    internal enum BuildInspectorTone
    {
        Neutral,
        Info,
        Ready,
        Warning,
        Error,
        Busy,
        Disabled
    }

    internal enum BuildInspectorActionRole
    {
        Secondary,
        Selected,
        Primary,
        Accessory,
        Destructive
    }

    internal enum BuildInspectorFieldLayoutMode
    {
        Inline,
        Stacked,
        Vertical
    }

    internal readonly struct BuildInspectorStatus
    {
        internal BuildInspectorStatus(
            BuildInspectorTone tone,
            string label,
            string tooltip = null)
        {
            Tone = tone;
            Label = label ?? string.Empty;
            Tooltip = tooltip ?? string.Empty;
        }

        internal BuildInspectorTone Tone { get; }
        internal string Label { get; }
        internal string Tooltip { get; }
    }

    internal readonly struct BuildInspectorCommand
    {
        internal BuildInspectorCommand(
            int id,
            GUIContent content,
            bool enabled = true,
            BuildInspectorActionRole role = BuildInspectorActionRole.Secondary)
        {
            Id = id;
            Content = content ?? GUIContent.none;
            Enabled = enabled;
            Role = role;
        }

        internal int Id { get; }
        internal GUIContent Content { get; }
        internal bool Enabled { get; }
        internal BuildInspectorActionRole Role { get; }
    }

    internal readonly struct BuildInspectorObjectFieldResult
    {
        internal BuildInspectorObjectFieldResult(
            UnityEngine.Object value,
            int commandId,
            Rect commandRect)
        {
            Value = value;
            CommandId = commandId;
            CommandRect = commandRect;
        }

        internal UnityEngine.Object Value { get; }
        internal int CommandId { get; }
        internal Rect CommandRect { get; }
    }

    internal readonly struct BuildInspectorLabelWidthScope : IDisposable
    {
        private readonly float previousLabelWidth;

        internal BuildInspectorLabelWidthScope(float availableWidth)
        {
            previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(
                previousLabelWidth,
                BuildInspectorUi.ResolveLabelWidth(availableWidth, previousLabelWidth));
        }

        public void Dispose()
        {
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }
    }

    /// <summary>
    /// Reclaims only the redundant left gutter supplied by the Inspector host. Panel padding,
    /// status markers, nested foldouts, and the right scrollbar safety area remain unchanged.
    /// </summary>
    internal struct BuildInspectorOuterContentScope : IDisposable
    {
        private bool disposed;

        internal BuildInspectorOuterContentScope(float leftGutterReduction)
        {
            disposed = false;
            EditorGUILayout.BeginHorizontal(GUIStyle.none);
            GUILayout.Space(-Mathf.Max(0f, leftGutterReduction));
            EditorGUILayout.BeginVertical(GUIStyle.none);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// Owns one nested Inspector panel and closes the vertical layout group opened by
    /// <see cref="BuildInspectorUi.BeginNestedFoldout"/>.
    /// </summary>
    internal struct BuildInspectorFoldoutScope : IDisposable
    {
        private bool disposed;

        internal BuildInspectorFoldoutScope(bool expanded)
        {
            Expanded = expanded;
            disposed = false;
        }

        internal bool Expanded { get; }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            EditorGUILayout.Space(2f);
            EditorGUILayout.EndVertical();
        }
    }

    /// <summary>
    /// Build-owned IMGUI presentation primitives. The module draws semantic state and returns
    /// command identities; it never mutates build data, saves assets, or starts build work.
    /// </summary>
    internal static class BuildInspectorUi
    {
        internal const float AccessoryButtonWidth = 72f;
        internal const float CompactAccessoryButtonWidth = 60f;
        internal const float CompactGridCellWidth = 112f;
        internal const float OuterLeftGutterReduction = 8f;

        internal static readonly Color SetupColor = new Color(0.16f, 0.48f, 0.78f);
        internal static readonly Color RecipeColor = new Color(0.38f, 0.36f, 0.70f);
        internal static readonly Color ContentColor = new Color(0.10f, 0.56f, 0.72f);
        internal static readonly Color PlayerColor = new Color(0.48f, 0.34f, 0.68f);
        internal static readonly Color HotUpdateColor = new Color(0.76f, 0.45f, 0.13f);
        internal static readonly Color SafetyColor = new Color(0.12f, 0.55f, 0.48f);
        internal static readonly Color ActionColor = new Color(0.18f, 0.60f, 0.38f);
        internal static readonly Color AdvancedColor = new Color(0.38f, 0.42f, 0.47f);

        private const float HeaderHorizontalPadding = 6f;
        private const float HeaderArrowWidth = 13f;
        private const float BadgeHorizontalPadding = 7f;
        private const float GridGap = 5f;
        private const float MinimumGridCellWidth = 142f;
        private const float MinimumObjectFieldWidth = 112f;
        private const float ResponsiveHorizontalInset = 48f;
        private const float NarrowInspectorViewWidth = 360f;
        private const float NestedHeaderHorizontalPadding = 6f;
        private const float NestedHeaderHeight = 22f;
        private const float NestedMinimumTitleWidth = 72f;
        private const float NestedMinimumSummaryWidth = 180f;
        private const float NestedTextGap = 6f;

        private static readonly Vector3[] TrianglePoints = new Vector3[3];

        private static GUIStyle titleStyle;
        private static GUIStyle subtitleStyle;
        private static GUIStyle foldoutLabelStyle;
        private static GUIStyle badgeStyle;
        private static GUIStyle statusLabelStyle;
        private static GUIStyle statusValueStyle;
        private static GUIStyle mutedWrappedStyle;
        private static GUIStyle noticeStyle;
        private static GUIStyle nestedFoldoutLabelStyle;
        private static GUIStyle nestedSummaryStyle;
        private static GUIStyle primaryActionStyle;
        private static bool stylesUseProSkin;

        internal static void DrawInspectorTitle(
            string title,
            string subtitle,
            Color accentColor,
            BuildInspectorStatus status)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 46f);
            Color panelColor = EditorGUIUtility.isProSkin
                ? new Color(0.145f, 0.155f, 0.175f, 1f)
                : new Color(0.84f, 0.86f, 0.89f, 1f);
            EditorGUI.DrawRect(rect, panelColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accentColor);
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                new Color(0f, 0f, 0f, 0.24f));

            float badgeWidth = GetBadgeWidth(status.Label, rect.width);
            Rect titleRect = new Rect(
                rect.x + 12f,
                rect.y + 4f,
                Mathf.Max(0f, rect.width - badgeWidth - 27f),
                20f);
            Rect subtitleRect = new Rect(
                rect.x + 12f,
                rect.y + 24f,
                Mathf.Max(0f, rect.width - 24f),
                17f);
            EditorGUI.LabelField(titleRect, title, titleStyle);
            EditorGUI.LabelField(subtitleRect, new GUIContent(subtitle, subtitle), subtitleStyle);

            if (badgeWidth > 0f)
            {
                Rect badgeRect = new Rect(
                    rect.xMax - badgeWidth - 7f,
                    rect.y + 6f,
                    badgeWidth,
                    18f);
                DrawStatusBadge(badgeRect, status);
            }

            EditorGUILayout.Space(4f);
        }

        internal static bool DrawFoldoutHeader(
            string title,
            bool expanded,
            Color accentColor,
            BuildInspectorStatus status,
            string tooltip = null)
        {
            EnsureStyles();

            EditorGUILayout.Space(3f);
            Rect rect = EditorGUILayout.GetControlRect(false, 25f);
            float shade = expanded ? 1f : 0.74f;
            EditorGUI.DrawRect(
                rect,
                new Color(
                    accentColor.r * shade,
                    accentColor.g * shade,
                    accentColor.b * shade,
                    0.96f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, 1f),
                new Color(1f, 1f, 1f, 0.10f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                new Color(0f, 0f, 0f, 0.24f));

            Rect arrowRect = new Rect(
                rect.x + HeaderHorizontalPadding,
                rect.y + 3f,
                HeaderArrowWidth,
                rect.height - 6f);
            float badgeWidth = GetBadgeWidth(status.Label, rect.width);
            Rect labelRect = new Rect(
                arrowRect.xMax + 3f,
                rect.y,
                Mathf.Max(
                    0f,
                    rect.width - (arrowRect.xMax - rect.x) - badgeWidth - 17f),
                rect.height);

            DrawFoldoutTriangle(arrowRect, expanded);
            EditorGUI.LabelField(
                labelRect,
                new GUIContent(title, tooltip ?? string.Empty),
                foldoutLabelStyle);

            if (badgeWidth > 0f)
            {
                Rect badgeRect = new Rect(
                    rect.xMax - badgeWidth - 5f,
                    rect.y + 4f,
                    badgeWidth,
                    rect.height - 8f);
                DrawStatusBadge(badgeRect, status);
            }

            Event current = Event.current;
            if (current.type == EventType.MouseDown
                && current.button == 0
                && rect.Contains(current.mousePosition))
            {
                expanded = !expanded;
                current.Use();
            }

            return expanded;
        }

        internal static BuildInspectorFoldoutScope BeginNestedFoldout(
            GUIContent title,
            bool expanded,
            BuildInspectorStatus status = default,
            string summary = null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            try
            {
                EditorGUILayout.Space(1f);
                Rect headerRect = EditorGUILayout.GetControlRect(
                    hasLabel: false,
                    height: NestedHeaderHeight);
                expanded = DrawNestedFoldoutHeader(
                    headerRect,
                    expanded,
                    title,
                    status,
                    summary);
                if (expanded)
                {
                    EditorGUILayout.Space(3f);
                }

                return new BuildInspectorFoldoutScope(expanded);
            }
            catch
            {
                EditorGUILayout.EndVertical();
                throw;
            }
        }

        internal static bool DrawInlineFoldout(
            Rect rect,
            bool expanded,
            GUIContent title,
            BuildInspectorStatus status = default)
        {
            return DrawNestedFoldoutHeader(
                rect,
                expanded,
                title,
                status,
                summary: null);
        }

        internal static Rect GetNestedFoldoutArrowRect(Rect headerRect)
        {
            Rect normalized = NormalizeRect(headerRect);
            float horizontalInset = Mathf.Min(
                NestedHeaderHorizontalPadding,
                normalized.width * 0.5f);
            float verticalInset = Mathf.Min(3f, normalized.height * 0.5f);
            float width = Mathf.Min(
                HeaderArrowWidth,
                Mathf.Max(0f, normalized.width - horizontalInset * 2f));
            float height = Mathf.Min(
                16f,
                Mathf.Max(0f, normalized.height - verticalInset * 2f));
            return new Rect(
                normalized.xMin + horizontalInset,
                normalized.yMin + verticalInset,
                width,
                height);
        }

        internal static void BeginPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(3f);
        }

        internal static void EndPanel()
        {
            EditorGUILayout.Space(3f);
            EditorGUILayout.EndVertical();
        }

        internal static void DrawSubsectionLabel(string label)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
        }

        internal static BuildInspectorLabelWidthScope BeginResponsiveLabelWidth()
        {
            return new BuildInspectorLabelWidthScope(EditorGUIUtility.currentViewWidth);
        }

        internal static BuildInspectorOuterContentScope BeginOuterContent()
        {
            return new BuildInspectorOuterContentScope(OuterLeftGutterReduction);
        }

        internal static bool IsNarrowInspector()
        {
            return EditorGUIUtility.currentViewWidth < NarrowInspectorViewWidth;
        }

        internal static void DrawResponsivePropertyField(
            SerializedProperty property,
            GUIContent label,
            GUIContent labelWidthReference = null,
            float minimumFieldWidth = MinimumObjectFieldWidth)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            GUIContent safeLabel = label ?? GUIContent.none;
            GUIContent safeLabelWidthReference = labelWidthReference ?? safeLabel;
            float requiredLabelWidth = Mathf.Ceil(
                Mathf.Max(
                    EditorStyles.label.CalcSize(safeLabel).x,
                    EditorStyles.label.CalcSize(safeLabelWidthReference).x) + 4f);
            BuildInspectorFieldLayoutMode layout = ResolveFieldLayout(
                GetEstimatedContentWidth(),
                actionCount: 0,
                inlineLabelWidth: requiredLabelWidth,
                minimumFieldWidth: minimumFieldWidth);
            if (layout != BuildInspectorFieldLayoutMode.Inline)
            {
                EditorGUILayout.LabelField(safeLabel, EditorStyles.label);
                EditorGUILayout.PropertyField(property, GUIContent.none, true);
                return;
            }

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            try
            {
                EditorGUIUtility.labelWidth = Mathf.Max(
                    previousLabelWidth,
                    requiredLabelWidth);
                EditorGUILayout.PropertyField(property, safeLabel, true);
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        internal static BuildInspectorObjectFieldResult DrawObjectFieldWithActions(
            GUIContent label,
            UnityEngine.Object value,
            Type objectType,
            bool allowSceneObjects,
            IReadOnlyList<BuildInspectorCommand> actions)
        {
            EnsureStyles();

            GUIContent safeLabel = label ?? GUIContent.none;
            Type safeObjectType = objectType ?? typeof(UnityEngine.Object);
            int actionCount = actions?.Count ?? 0;
            float availableWidth = GetEstimatedContentWidth();
            BuildInspectorFieldLayoutMode layout = ResolveFieldLayout(
                availableWidth,
                actionCount,
                ResolveLabelWidth(
                    EditorGUIUtility.currentViewWidth,
                    EditorGUIUtility.labelWidth));
            int clickedCommandId = -1;
            Rect clickedCommandRect = default;

            if (layout == BuildInspectorFieldLayoutMode.Inline)
            {
                EditorGUILayout.BeginHorizontal();
                value = EditorGUILayout.ObjectField(
                    safeLabel,
                    value,
                    safeObjectType,
                    allowSceneObjects);
                DrawGUILayoutActions(
                    actions,
                    AccessoryButtonWidth,
                    ref clickedCommandId,
                    ref clickedCommandRect);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField(safeLabel, EditorStyles.label);
                if (layout == BuildInspectorFieldLayoutMode.Stacked)
                {
                    EditorGUILayout.BeginHorizontal();
                    value = EditorGUILayout.ObjectField(
                        GUIContent.none,
                        value,
                        safeObjectType,
                        allowSceneObjects);
                    DrawGUILayoutActions(
                        actions,
                        CompactAccessoryButtonWidth,
                        ref clickedCommandId,
                        ref clickedCommandRect);
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    value = EditorGUILayout.ObjectField(
                        GUIContent.none,
                        value,
                        safeObjectType,
                        allowSceneObjects);
                    if (actionCount > 0)
                    {
                        float rowHeight = 0f;
                        for (int index = 0; index < actionCount; index++)
                        {
                            rowHeight = Mathf.Max(
                                rowHeight,
                                GetActionHeight(actions[index].Role));
                        }

                        Rect actionRow = EditorGUILayout.GetControlRect(false, rowHeight);
                        DrawActionRow(
                            actionRow,
                            actions,
                            ref clickedCommandId,
                            ref clickedCommandRect);
                    }
                }
            }

            return new BuildInspectorObjectFieldResult(
                value,
                clickedCommandId,
                clickedCommandRect);
        }

        internal static void DrawStatusRow(
            string label,
            string value,
            BuildInspectorTone tone,
            string tooltip = null)
        {
            EnsureStyles();

            GUIContent labelContent = new GUIContent(label ?? string.Empty, tooltip);
            GUIContent valueContent = new GUIContent(value ?? string.Empty, tooltip);
            float labelWidth = statusLabelStyle.CalcSize(labelContent).x;
            float valueWidth = statusValueStyle.CalcSize(valueContent).x;
            bool stacked = ShouldStackStatusRow(
                GetEstimatedContentWidth(),
                labelWidth,
                valueWidth);
            Rect rect = EditorGUILayout.GetControlRect(false, stacked ? 36f : 19f);
            Color color = GetToneColor(tone);
            Rect markerRect = new Rect(rect.x + 2f, rect.y + 5f, 8f, 8f);
            EditorGUI.DrawRect(markerRect, color);

            if (!stacked)
            {
                float textX = markerRect.xMax + 7f;
                float textWidth = Mathf.Max(0f, rect.xMax - textX - 3f);
                float resolvedValueWidth = Mathf.Clamp(
                    valueWidth + 6f,
                    Mathf.Min(48f, textWidth),
                    textWidth * 0.68f);
                Rect labelRect = new Rect(
                    textX,
                    rect.y,
                    Mathf.Max(0f, textWidth - resolvedValueWidth),
                    19f);
                Rect valueRect = new Rect(
                    labelRect.xMax,
                    rect.y,
                    resolvedValueWidth,
                    19f);
                EditorGUI.LabelField(labelRect, labelContent, statusLabelStyle);
                DrawColoredLabel(
                    valueRect,
                    valueContent.text,
                    color,
                    statusValueStyle,
                    tooltip);
                return;
            }

            Rect stackedLabelRect = new Rect(
                markerRect.xMax + 7f,
                rect.y,
                Mathf.Max(0f, rect.xMax - markerRect.xMax - 10f),
                18f);
            Rect stackedValueRect = new Rect(
                markerRect.xMax + 7f,
                rect.y + 17f,
                Mathf.Max(0f, rect.xMax - markerRect.xMax - 10f),
                18f);
            EditorGUI.LabelField(
                stackedLabelRect,
                labelContent,
                statusLabelStyle);
            DrawColoredLabel(
                stackedValueRect,
                valueContent.text,
                color,
                statusValueStyle,
                tooltip);
        }

        internal static void DrawMutedText(string text)
        {
            EnsureStyles();
            GUIContent content = new GUIContent(text ?? string.Empty);
            float availableWidth = Mathf.Max(1f, EditorGUIUtility.currentViewWidth - 54f);
            float height = Mathf.Max(
                EditorGUIUtility.singleLineHeight,
                mutedWrappedStyle.CalcHeight(content, availableWidth));
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.LabelField(rect, content, mutedWrappedStyle);
        }

        internal static void DrawNotice(string message, BuildInspectorTone tone)
        {
            EnsureStyles();

            GUIContent content = new GUIContent(message ?? string.Empty);
            float availableWidth = Mathf.Max(1f, EditorGUIUtility.currentViewWidth - 54f);
            float textWidth = Mathf.Max(1f, availableWidth - 20f);
            float height = Mathf.Max(28f, noticeStyle.CalcHeight(content, textWidth) + 10f);
            Rect rect = EditorGUILayout.GetControlRect(false, height);

            Color toneColor = GetToneColor(tone);
            Color background = EditorGUIUtility.isProSkin
                ? new Color(toneColor.r * 0.20f, toneColor.g * 0.20f, toneColor.b * 0.20f, 0.48f)
                : new Color(
                    Mathf.Lerp(1f, toneColor.r, 0.12f),
                    Mathf.Lerp(1f, toneColor.g, 0.12f),
                    Mathf.Lerp(1f, toneColor.b, 0.12f),
                    1f);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), toneColor);
            EditorGUI.LabelField(
                new Rect(rect.x + 10f, rect.y + 5f, Mathf.Max(1f, rect.width - 20f), rect.height - 10f),
                content,
                noticeStyle);
            EditorGUILayout.Space(2f);
        }

        internal static int DrawCommandGrid(
            IReadOnlyList<BuildInspectorCommand> commands,
            int maximumColumns = 3,
            float minimumCellWidth = MinimumGridCellWidth,
            bool expandIncompleteRow = false)
        {
            if (commands == null || commands.Count == 0)
            {
                return -1;
            }

            float availableWidth = GetEstimatedContentWidth();
            int columns = ResolveColumnCount(
                availableWidth,
                commands.Count,
                minimumCellWidth,
                GridGap,
                maximumColumns);
            int clicked = -1;

            for (int rowStart = 0; rowStart < commands.Count; rowStart += columns)
            {
                float rowHeight = 0f;
                int rowEnd = Math.Min(commands.Count, rowStart + columns);
                for (int index = rowStart; index < rowEnd; index++)
                {
                    rowHeight = Mathf.Max(rowHeight, GetActionHeight(commands[index].Role));
                }

                Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);
                int rowItemCount = rowEnd - rowStart;
                float cellWidth = ResolveGridCellWidth(
                    rowRect.width,
                    columns,
                    rowItemCount,
                    GridGap,
                    expandIncompleteRow);
                for (int index = rowStart; index < rowEnd; index++)
                {
                    int column = index - rowStart;
                    Rect buttonRect = new Rect(
                        rowRect.x + column * (cellWidth + GridGap),
                        rowRect.y,
                        cellWidth,
                        rowHeight);
                    BuildInspectorCommand command = commands[index];
                    if (DrawActionButton(buttonRect, command))
                    {
                        clicked = command.Id;
                    }
                }

                EditorGUILayout.Space(2f);
            }

            return clicked;
        }

        internal static bool DrawAccessoryButton(
            GUIContent content,
            bool enabled = true,
            BuildInspectorActionRole role = BuildInspectorActionRole.Accessory)
        {
            return DrawGUILayoutAction(
                new BuildInspectorCommand(0, content, enabled, role),
                AccessoryButtonWidth);
        }

        internal static int ResolveColumnCount(
            float availableWidth,
            int itemCount,
            float minimumCellWidth = MinimumGridCellWidth,
            float gap = GridGap,
            int maximumColumns = 3)
        {
            if (itemCount <= 0)
            {
                return 0;
            }

            int safeMaximum = Mathf.Clamp(maximumColumns, 1, itemCount);
            float safeWidth = Mathf.Max(0f, availableWidth);
            float safeMinimum = Mathf.Max(1f, minimumCellWidth);
            float safeGap = Mathf.Max(0f, gap);
            for (int columns = safeMaximum; columns > 1; columns--)
            {
                float required = safeMinimum * columns + safeGap * (columns - 1);
                if (safeWidth >= required)
                {
                    return columns;
                }
            }

            return 1;
        }

        internal static float ResolveGridCellWidth(
            float rowWidth,
            int columns,
            int rowItemCount,
            float gap,
            bool expandIncompleteRow)
        {
            int safeColumns = Mathf.Max(1, columns);
            int safeItemCount = Mathf.Clamp(rowItemCount, 1, safeColumns);
            int layoutColumns = expandIncompleteRow
                ? safeItemCount
                : safeColumns;
            float safeWidth = Mathf.Max(0f, rowWidth);
            float safeGap = Mathf.Max(0f, gap);
            return Mathf.Max(
                1f,
                (safeWidth - safeGap * (layoutColumns - 1)) / layoutColumns);
        }

        internal static float ResolveLabelWidth(
            float availableWidth,
            float defaultLabelWidth = 150f)
        {
            const float minimum = 86f;
            const float standard = 150f;
            float safeWidth = Mathf.Max(0f, availableWidth);
            float safeDefault = Mathf.Max(minimum, defaultLabelWidth);
            if (safeWidth <= 280f)
            {
                return Mathf.Min(minimum, safeDefault);
            }

            if (safeWidth < 440f)
            {
                float progress = Mathf.InverseLerp(280f, 440f, safeWidth);
                return Mathf.Min(
                    Mathf.Lerp(minimum, standard, progress),
                    safeDefault);
            }

            return Mathf.Min(standard, safeDefault);
        }

        internal static BuildInspectorFieldLayoutMode ResolveFieldLayout(
            float availableWidth,
            int actionCount,
            float inlineLabelWidth = 104f,
            float minimumFieldWidth = MinimumObjectFieldWidth,
            float accessoryWidth = AccessoryButtonWidth,
            float compactAccessoryWidth = CompactAccessoryButtonWidth,
            float gap = GridGap)
        {
            float safeWidth = Mathf.Max(0f, availableWidth);
            int safeActionCount = Mathf.Max(0, actionCount);
            float safeLabelWidth = Mathf.Max(0f, inlineLabelWidth);
            float safeFieldWidth = Mathf.Max(1f, minimumFieldWidth);
            float safeAccessoryWidth = Mathf.Max(1f, accessoryWidth);
            float safeCompactWidth = Mathf.Max(1f, compactAccessoryWidth);
            float safeGap = Mathf.Max(0f, gap);
            float inlineRequired = safeLabelWidth + safeFieldWidth
                + safeActionCount * (safeAccessoryWidth + safeGap);
            if (safeWidth >= inlineRequired)
            {
                return BuildInspectorFieldLayoutMode.Inline;
            }

            float stackedRequired = safeFieldWidth
                + safeActionCount * (safeCompactWidth + safeGap);
            return safeWidth >= stackedRequired
                ? BuildInspectorFieldLayoutMode.Stacked
                : BuildInspectorFieldLayoutMode.Vertical;
        }

        internal static bool ShouldStackStatusRow(
            float availableWidth,
            float labelWidth,
            float valueWidth)
        {
            const float markerAndSpacing = 30f;
            float required = markerAndSpacing
                + Mathf.Max(0f, labelWidth)
                + Mathf.Max(0f, valueWidth);
            return Mathf.Max(0f, availableWidth) < required;
        }

        internal static Color GetPrimaryActionTint(bool proSkin)
        {
            return proSkin
                ? new Color(0.48f, 0.95f, 0.62f)
                : new Color(0.36f, 0.78f, 0.48f);
        }

        internal static Color GetToneColor(BuildInspectorTone tone)
        {
            switch (tone)
            {
                case BuildInspectorTone.Info:
                    return new Color(0.20f, 0.58f, 0.82f);
                case BuildInspectorTone.Ready:
                    return new Color(0.18f, 0.66f, 0.40f);
                case BuildInspectorTone.Warning:
                    return new Color(0.88f, 0.55f, 0.12f);
                case BuildInspectorTone.Error:
                    return new Color(0.82f, 0.24f, 0.22f);
                case BuildInspectorTone.Busy:
                    return new Color(0.28f, 0.52f, 0.86f);
                case BuildInspectorTone.Disabled:
                    return new Color(0.42f, 0.44f, 0.48f);
                default:
                    return new Color(0.48f, 0.51f, 0.56f);
            }
        }

        private static void DrawStatusBadge(Rect rect, BuildInspectorStatus status)
        {
            if (rect.width <= 0f || rect.height <= 0f || string.IsNullOrEmpty(status.Label))
            {
                return;
            }

            EnsureStyles();
            Color color = GetToneColor(status.Tone);
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.94f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, 1f),
                new Color(1f, 1f, 1f, 0.12f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                new Color(0f, 0f, 0f, 0.20f));
            EditorGUI.LabelField(
                new Rect(
                    rect.x + BadgeHorizontalPadding,
                    rect.y,
                    Mathf.Max(0f, rect.width - BadgeHorizontalPadding * 2f),
                    rect.height),
                new GUIContent(status.Label, status.Tooltip),
                badgeStyle);
        }

        private static float GetBadgeWidth(string label, float availableWidth)
        {
            if (string.IsNullOrEmpty(label) || availableWidth < 150f)
            {
                return 0f;
            }

            return Mathf.Min(
                Mathf.Clamp(28f + label.Length * 6f, 58f, 128f),
                availableWidth * 0.42f);
        }

        private static void DrawGUILayoutActions(
            IReadOnlyList<BuildInspectorCommand> actions,
            float width,
            ref int clickedCommandId,
            ref Rect clickedCommandRect)
        {
            if (actions == null)
            {
                return;
            }

            for (int index = 0; index < actions.Count; index++)
            {
                BuildInspectorCommand action = actions[index];
                bool clicked = DrawGUILayoutAction(action, width);
                Rect actionRect = GUILayoutUtility.GetLastRect();
                if (!clicked)
                {
                    continue;
                }

                clickedCommandId = action.Id;
                clickedCommandRect = actionRect;
            }
        }

        private static void DrawActionRow(
            Rect rowRect,
            IReadOnlyList<BuildInspectorCommand> actions,
            ref int clickedCommandId,
            ref Rect clickedCommandRect)
        {
            int actionCount = actions?.Count ?? 0;
            if (actionCount == 0)
            {
                return;
            }

            float cellWidth = Mathf.Max(
                1f,
                (rowRect.width - GridGap * (actionCount - 1)) / actionCount);
            for (int index = 0; index < actionCount; index++)
            {
                Rect actionRect = new Rect(
                    rowRect.x + index * (cellWidth + GridGap),
                    rowRect.y,
                    cellWidth,
                    rowRect.height);
                if (!DrawActionButton(actionRect, actions[index]))
                {
                    continue;
                }

                clickedCommandId = actions[index].Id;
                clickedCommandRect = actionRect;
            }
        }

        private static bool DrawActionButton(
            Rect rect,
            BuildInspectorCommand command)
        {
            EnsureStyles();
            using (new EditorGUI.DisabledScope(!command.Enabled))
            using (new GuiBackgroundColorScope(GetActionColor(command.Role)))
            {
                return command.Role == BuildInspectorActionRole.Primary
                    ? GUI.Button(rect, command.Content, primaryActionStyle)
                    : GUI.Button(rect, command.Content);
            }
        }

        private static bool DrawGUILayoutAction(
            BuildInspectorCommand command,
            float width)
        {
            EnsureStyles();
            using (new EditorGUI.DisabledScope(!command.Enabled))
            using (new GuiBackgroundColorScope(GetActionColor(command.Role)))
            {
                GUILayoutOption[] options =
                {
                    GUILayout.Width(Mathf.Max(1f, width)),
                    GUILayout.Height(GetActionHeight(command.Role))
                };
                return command.Role == BuildInspectorActionRole.Primary
                    ? GUILayout.Button(command.Content, primaryActionStyle, options)
                    : GUILayout.Button(command.Content, options);
            }
        }

        private static float GetEstimatedContentWidth()
        {
            return Mathf.Max(
                1f,
                EditorGUIUtility.currentViewWidth - ResponsiveHorizontalInset);
        }

        private static float GetActionHeight(BuildInspectorActionRole role)
        {
            switch (role)
            {
                case BuildInspectorActionRole.Primary:
                    return 32f;
                case BuildInspectorActionRole.Accessory:
                    return 22f;
                default:
                    return 26f;
            }
        }

        private static Color GetActionColor(BuildInspectorActionRole role)
        {
            switch (role)
            {
                case BuildInspectorActionRole.Selected:
                    return SetupColor;
                case BuildInspectorActionRole.Primary:
                    return GetPrimaryActionTint(EditorGUIUtility.isProSkin);
                case BuildInspectorActionRole.Destructive:
                    return GetToneColor(BuildInspectorTone.Error);
                default:
                    return Color.white;
            }
        }

        private static void DrawColoredLabel(
            Rect rect,
            string value,
            Color color,
            GUIStyle style,
            string tooltip)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            EditorGUI.LabelField(rect, new GUIContent(value, tooltip), style);
            GUI.color = previousColor;
        }

        private static bool DrawNestedFoldoutHeader(
            Rect headerRect,
            bool expanded,
            GUIContent title,
            BuildInspectorStatus status,
            string summary)
        {
            EnsureStyles();

            Rect rect = NormalizeRect(headerRect);
            GUIContent safeTitle = title ?? GUIContent.none;
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.11f, 0.12f, 0.14f, expanded ? 0.72f : 0.52f)
                : new Color(0.76f, 0.78f, 0.81f, expanded ? 0.82f : 0.62f);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, Mathf.Min(1f, rect.height)),
                new Color(1f, 1f, 1f, 0.08f));
            EditorGUI.DrawRect(
                new Rect(
                    rect.x,
                    Mathf.Max(rect.y, rect.yMax - 1f),
                    rect.width,
                    Mathf.Min(1f, rect.height)),
                new Color(0f, 0f, 0f, 0.20f));

            Rect arrowRect = GetNestedFoldoutArrowRect(rect);
            DrawFoldoutTriangle(arrowRect, expanded);

            float textLeft = Mathf.Min(rect.xMax, arrowRect.xMax + 4f);
            float textRight = Mathf.Max(
                textLeft,
                rect.xMax - NestedHeaderHorizontalPadding);
            float desiredTitleWidth = nestedFoldoutLabelStyle.CalcSize(safeTitle).x;
            float badgeWidth = GetBadgeWidth(status.Label, rect.width);
            if (badgeWidth > 0f
                && textRight - badgeWidth - NestedTextGap - textLeft
                < Mathf.Max(NestedMinimumTitleWidth, desiredTitleWidth))
            {
                badgeWidth = 0f;
            }

            if (badgeWidth > 0f)
            {
                Rect badgeRect = new Rect(
                    textRight - badgeWidth,
                    rect.y + Mathf.Min(3f, rect.height * 0.5f),
                    badgeWidth,
                    Mathf.Max(0f, Mathf.Min(16f, rect.height - 6f)));
                DrawStatusBadge(badgeRect, status);
                textRight = Mathf.Max(textLeft, badgeRect.xMin - NestedTextGap);
            }

            float availableTextWidth = Mathf.Max(0f, textRight - textLeft);
            float desiredSummaryWidth = string.IsNullOrWhiteSpace(summary)
                ? 0f
                : nestedSummaryStyle.CalcSize(new GUIContent(summary)).x;
            bool showSummary = !string.IsNullOrWhiteSpace(summary)
                && availableTextWidth
                >= desiredTitleWidth
                    + NestedTextGap
                    + Mathf.Max(NestedMinimumSummaryWidth, desiredSummaryWidth);
            float titleWidth = showSummary
                ? desiredTitleWidth
                : availableTextWidth;

            string tooltip = safeTitle.tooltip ?? string.Empty;
            if (!showSummary && !string.IsNullOrWhiteSpace(summary))
            {
                tooltip = string.IsNullOrEmpty(tooltip)
                    ? summary
                    : tooltip + "\n" + summary;
            }

            if (badgeWidth <= 0f && !string.IsNullOrWhiteSpace(status.Label))
            {
                string hiddenStatus = string.IsNullOrWhiteSpace(status.Tooltip)
                    ? status.Label
                    : status.Label + ": " + status.Tooltip;
                tooltip = string.IsNullOrEmpty(tooltip)
                    ? hiddenStatus
                    : tooltip + "\n" + hiddenStatus;
            }

            EditorGUI.LabelField(
                new Rect(textLeft, rect.y, titleWidth, rect.height),
                new GUIContent(safeTitle.text, safeTitle.image, tooltip),
                nestedFoldoutLabelStyle);
            if (showSummary)
            {
                float summaryLeft = textLeft + titleWidth + NestedTextGap;
                EditorGUI.LabelField(
                    new Rect(
                        summaryLeft,
                        rect.y,
                        Mathf.Max(0f, textRight - summaryLeft),
                        rect.height),
                    new GUIContent(summary, summary),
                    nestedSummaryStyle);
            }

            Event current = Event.current;
            if (current != null
                && current.type == EventType.MouseDown
                && current.button == 0
                && rect.Contains(current.mousePosition))
            {
                expanded = !expanded;
                current.Use();
            }

            return expanded;
        }

        private static Rect NormalizeRect(Rect rect)
        {
            float xMin = Mathf.Min(rect.xMin, rect.xMax);
            float xMax = Mathf.Max(rect.xMin, rect.xMax);
            float yMin = Mathf.Min(rect.yMin, rect.yMax);
            float yMax = Mathf.Max(rect.yMin, rect.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static void EnsureStyles()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            if (titleStyle != null && stylesUseProSkin == proSkin)
            {
                return;
            }

            stylesUseProSkin = proSkin;
            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };
            statusLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            statusValueStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip
            };
            mutedWrappedStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            noticeStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0)
            };
            nestedFoldoutLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            nestedSummaryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip,
                normal =
                {
                    textColor = proSkin
                        ? new Color(0.70f, 0.72f, 0.76f)
                        : new Color(0.34f, 0.36f, 0.40f)
                }
            };
            primaryActionStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };
            primaryActionStyle.normal.textColor = Color.white;
            primaryActionStyle.hover.textColor = Color.white;
            primaryActionStyle.active.textColor = Color.white;
            primaryActionStyle.focused.textColor = Color.white;
        }

        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Vector2 center = rect.center;
            if (expanded)
            {
                float halfWidth = Mathf.Min(4f, rect.width * 0.5f);
                float topOffset = Mathf.Min(2f, rect.height * 0.5f);
                float bottomOffset = Mathf.Min(3f, rect.height * 0.5f);
                TrianglePoints[0] = new Vector3(center.x - halfWidth, center.y - topOffset);
                TrianglePoints[1] = new Vector3(center.x + halfWidth, center.y - topOffset);
                TrianglePoints[2] = new Vector3(center.x, center.y + bottomOffset);
            }
            else
            {
                float leftOffset = Mathf.Min(2f, rect.width * 0.5f);
                float rightOffset = Mathf.Min(3f, rect.width * 0.5f);
                float halfHeight = Mathf.Min(4f, rect.height * 0.5f);
                TrianglePoints[0] = new Vector3(center.x - leftOffset, center.y - halfHeight);
                TrianglePoints[1] = new Vector3(center.x - leftOffset, center.y + halfHeight);
                TrianglePoints[2] = new Vector3(center.x + rightOffset, center.y);
            }

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(0.92f, 0.92f, 0.92f, 0.96f);
            Handles.DrawAAConvexPolygon(TrianglePoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }

        private readonly struct GuiBackgroundColorScope : IDisposable
        {
            private readonly Color previousColor;

            internal GuiBackgroundColorScope(Color color)
            {
                previousColor = GUI.backgroundColor;
                GUI.backgroundColor = color;
            }

            public void Dispose()
            {
                GUI.backgroundColor = previousColor;
            }
        }
    }
}
