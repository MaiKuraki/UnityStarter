using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace CycloneGames.DataTable.Unity.Editor
{
    internal enum DataTableLubanInspectorTone
    {
        Neutral,
        Info,
        Ready,
        Warning,
        Error,
        Busy,
    }

    internal enum DataTableLubanHeroLayoutMode
    {
        Inline,
        Stacked,
        Compact,
    }

    internal readonly struct DataTableLubanHeroLayout
    {
        internal DataTableLubanHeroLayout(
            Rect bounds,
            Rect accentRect,
            Rect titleRect,
            Rect subtitleRect,
            Rect badgeRect,
            DataTableLubanHeroLayoutMode mode,
            bool hasSubtitle)
        {
            Bounds = bounds;
            AccentRect = accentRect;
            TitleRect = titleRect;
            SubtitleRect = subtitleRect;
            BadgeRect = badgeRect;
            Mode = mode;
            HasSubtitle = hasSubtitle;
        }

        internal Rect Bounds { get; }
        internal Rect AccentRect { get; }
        internal Rect TitleRect { get; }
        internal Rect SubtitleRect { get; }
        internal Rect BadgeRect { get; }
        internal DataTableLubanHeroLayoutMode Mode { get; }
        internal bool HasSubtitle { get; }
    }

    internal enum DataTableLubanSectionHeaderLayoutMode
    {
        Inline,
        Stacked,
    }

    internal readonly struct DataTableLubanSectionHeaderLayout
    {
        internal DataTableLubanSectionHeaderLayout(
            Rect headerRect,
            Rect arrowRect,
            Rect labelRect,
            Rect badgeRect,
            bool hasBadge,
            DataTableLubanSectionHeaderLayoutMode mode)
        {
            HeaderRect = headerRect;
            ArrowRect = arrowRect;
            LabelRect = labelRect;
            BadgeRect = badgeRect;
            HasBadge = hasBadge;
            Mode = mode;
        }

        internal Rect HeaderRect { get; }
        internal Rect ArrowRect { get; }
        internal Rect LabelRect { get; }
        internal Rect BadgeRect { get; }
        internal bool HasBadge { get; }
        internal DataTableLubanSectionHeaderLayoutMode Mode { get; }
    }

    internal readonly struct DataTableLubanStatusRowLayout
    {
        internal DataTableLubanStatusRowLayout(
            Rect bounds,
            Rect markerRect,
            Rect labelRect,
            Rect valueRect,
            bool isStacked)
        {
            Bounds = bounds;
            MarkerRect = markerRect;
            LabelRect = labelRect;
            ValueRect = valueRect;
            IsStacked = isStacked;
        }

        internal Rect Bounds { get; }
        internal Rect MarkerRect { get; }
        internal Rect LabelRect { get; }
        internal Rect ValueRect { get; }
        internal bool IsStacked { get; }
    }

    internal enum DataTableLubanReadOnlyPathLayoutMode
    {
        Inline,
        Stacked,
        Vertical,
    }

    internal readonly struct DataTableLubanReadOnlyPathLayout
    {
        internal DataTableLubanReadOnlyPathLayout(
            Rect rowRect,
            Rect labelRect,
            Rect valueRect,
            Rect copyRect,
            Rect revealRect,
            bool hasReveal,
            DataTableLubanReadOnlyPathLayoutMode mode)
        {
            RowRect = rowRect;
            LabelRect = labelRect;
            ValueRect = valueRect;
            CopyRect = copyRect;
            RevealRect = revealRect;
            HasReveal = hasReveal;
            Mode = mode;
        }

        internal Rect RowRect { get; }
        internal Rect LabelRect { get; }
        internal Rect ValueRect { get; }
        internal Rect CopyRect { get; }
        internal Rect RevealRect { get; }
        internal bool HasReveal { get; }
        internal DataTableLubanReadOnlyPathLayoutMode Mode { get; }
    }

    internal readonly struct DataTableLubanDualButtonLayout
    {
        internal DataTableLubanDualButtonLayout(
            Rect groupRect,
            Rect firstRect,
            Rect secondRect,
            bool isStacked)
        {
            GroupRect = groupRect;
            FirstRect = firstRect;
            SecondRect = secondRect;
            IsStacked = isStacked;
        }

        internal Rect GroupRect { get; }
        internal Rect FirstRect { get; }
        internal Rect SecondRect { get; }
        internal bool IsStacked { get; }
    }

    internal readonly struct DataTableLubanFieldActionLayout
    {
        internal DataTableLubanFieldActionLayout(
            Rect groupRect,
            Rect fieldRect,
            Rect firstActionRect,
            Rect secondActionRect,
            bool isStacked)
        {
            GroupRect = groupRect;
            FieldRect = fieldRect;
            FirstActionRect = firstActionRect;
            SecondActionRect = secondActionRect;
            IsStacked = isStacked;
        }

        internal Rect GroupRect { get; }
        internal Rect FieldRect { get; }
        internal Rect FirstActionRect { get; }
        internal Rect SecondActionRect { get; }
        internal bool IsStacked { get; }
    }

    internal static class DataTableLubanInspectorUi
    {
        private const float HeroInlineHeight = 52f;
        private const float HeroStackedHeight = 74f;
        private const float HeroCompactHeight = 52f;
        private const float HeroInlineMinimumWidth = 300f;
        private const float HeroCompactMaximumWidth = 179f;
        private const float HeroLeftPadding = 14f;
        private const float HeroRightPadding = 8f;
        private const float HeroRowGap = 2f;
        private const float HeroTitleHeight = 22f;
        private const float HeroSecondaryHeight = 18f;
        private const float SectionHeaderInlineHeight = 23f;
        private const float SectionHeaderStackedHeight = 45f;
        private const float SectionHeaderMinimumTitleWidth = 96f;
        private const float SectionHeaderRowGap = 2f;
        private const float HeaderHorizontalPadding = 6f;
        private const float HeaderArrowWidth = 13f;
        private const float StatusRowInlineHeight = 19f;
        private const float StatusRowGap = 2f;
        private const float StatusRowMinimumInlineWidth = 216f;
        private const float StatusRowMarkerOffset = 2f;
        private const float StatusRowMarkerSize = 8f;
        private const float StatusRowContentOffset = 17f;
        private const float StatusRowLabelWidth = 112f;
        private const float StatusRowInlineGap = 4f;
        private const float StatusRowMaximumEstimatedValueWidth = 160f;
        private const float ReadOnlyPathLabelWidth = 112f;
        private const float ReadOnlyPathCopyWidth = 58f;
        private const float ReadOnlyPathRevealWidth = 58f;
        private const float ReadOnlyPathGap = 4f;
        private const float ReadOnlyPathMinimumInlineValueWidth = 116f;
        private const float InspectorEstimatedHorizontalChrome = 64f;
        private const float ReadOnlyPathStackedLineGap = 2f;
        private const float ReadOnlyPathMinimumStackedWidth = 224f;
        private const float ReadOnlyPreviewMinimumHeight = 54f;
        private const float ReadOnlyPreviewMaximumHeight = 120f;
        private const float ResponsiveFieldActionsMinimumWidth = 316f;
        private const float ResponsiveDualButtonsMinimumWidth = 332f;
        private const float DualButtonGap = 4f;
        private const float FieldActionWidth = 64f;
        private static readonly Vector3[] TrianglePoints = new Vector3[3];
        private static readonly GUIContent ReadOnlyPathLabelContent = new GUIContent();
        private static readonly GUIContent ReadOnlyPathValueContent = new GUIContent();
        private static readonly GUIContent HeroTitleContent = new GUIContent();
        private static readonly GUIContent HeroSubtitleContent = new GUIContent();
        private static readonly GUIContent SectionTitleContent = new GUIContent();
        private static readonly GUIContent BadgeContent = new GUIContent();
        private static readonly GUIContent StatusLabelContent = new GUIContent();
        private static readonly GUIContent StatusValueContent = new GUIContent();
        private static readonly GUIContent NoticeTitleContent = new GUIContent();
        private static readonly GUIContent NoticeMessageContent = new GUIContent();
        private static readonly GUIContent NoticeDetailContent = new GUIContent();

        internal static readonly Color SetupColor = new Color(0.18f, 0.48f, 0.76f);
        internal static readonly Color ProfileColor = new Color(0.34f, 0.39f, 0.70f);
        internal static readonly Color OutputColor = new Color(0.10f, 0.56f, 0.62f);
        internal static readonly Color ToolchainColor = new Color(0.48f, 0.38f, 0.62f);
        internal static readonly Color SafetyColor = new Color(0.12f, 0.55f, 0.42f);
        internal static readonly Color ActionColor = new Color(0.18f, 0.58f, 0.34f);

        private static GUIStyle _titleStyle;
        private static GUIStyle _compactTitleStyle;
        private static GUIStyle _subtitleStyle;
        private static GUIStyle _headerStyle;
        private static GUIStyle _stackedHeaderStyle;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _valueStyle;
        private static GUIStyle _stackedValueStyle;
        private static GUIStyle _readOnlyPathStyle;
        private static GUIStyle _readOnlyOutputStyle;
        private static bool _proSkin;

        internal static void DrawHero(
            string title,
            string subtitle,
            string status,
            DataTableLubanInspectorTone tone)
        {
            EnsureStyles();
            DataTableLubanHeroLayoutMode mode = GetHeroLayoutMode(GetEstimatedContentWidth());
            Rect rect = EditorGUILayout.GetControlRect(false, GetHeroHeight(mode));
            DataTableLubanHeroLayout layout = CalculateHeroLayout(rect, status, mode);
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.14f, 0.15f, 0.17f)
                : new Color(0.84f, 0.86f, 0.89f);
            EditorGUI.DrawRect(layout.Bounds, background);
            EditorGUI.DrawRect(layout.AccentRect, GetToneColor(tone));
            HeroTitleContent.text = title;
            HeroTitleContent.tooltip = title;
            EditorGUI.LabelField(
                layout.TitleRect,
                HeroTitleContent,
                mode == DataTableLubanHeroLayoutMode.Compact
                    ? _compactTitleStyle
                    : _titleStyle);
            if (layout.HasSubtitle)
            {
                HeroSubtitleContent.text = subtitle;
                HeroSubtitleContent.tooltip = subtitle;
                EditorGUI.LabelField(
                    layout.SubtitleRect,
                    HeroSubtitleContent,
                    _subtitleStyle);
            }

            if (!string.IsNullOrEmpty(status) && layout.BadgeRect.width > 0f)
            {
                DrawBadge(layout.BadgeRect, status, GetToneColor(tone));
            }

            EditorGUILayout.Space(5f);
        }

        internal static DataTableLubanHeroLayoutMode GetHeroLayoutMode(float availableWidth)
        {
            if (availableWidth >= HeroInlineMinimumWidth)
            {
                return DataTableLubanHeroLayoutMode.Inline;
            }

            return availableWidth <= HeroCompactMaximumWidth
                ? DataTableLubanHeroLayoutMode.Compact
                : DataTableLubanHeroLayoutMode.Stacked;
        }

        internal static float GetHeroHeight(DataTableLubanHeroLayoutMode mode)
        {
            switch (mode)
            {
                case DataTableLubanHeroLayoutMode.Stacked:
                    return HeroStackedHeight;
                case DataTableLubanHeroLayoutMode.Compact:
                    return HeroCompactHeight;
                default:
                    return HeroInlineHeight;
            }
        }

        internal static DataTableLubanHeroLayout CalculateHeroLayout(
            Rect boundsRect,
            string status,
            DataTableLubanHeroLayoutMode mode)
        {
            var bounds = new Rect(
                boundsRect.x,
                boundsRect.y,
                Mathf.Max(0f, boundsRect.width),
                Mathf.Max(0f, boundsRect.height));
            float accentWidth = Mathf.Min(5f, bounds.width);
            var accent = new Rect(bounds.x, bounds.y, accentWidth, bounds.height);
            float contentLeft = Mathf.Min(bounds.xMax, bounds.x + HeroLeftPadding);
            float contentRight = Mathf.Max(
                contentLeft,
                bounds.xMax - Mathf.Min(HeroRightPadding, bounds.width));
            float contentWidth = Mathf.Max(0f, contentRight - contentLeft);
            float badgeWidth = Mathf.Min(GetHeroBadgeWidth(status), contentWidth);
            float badgeX = Mathf.Max(contentLeft, contentRight - badgeWidth);

            if (mode == DataTableLubanHeroLayoutMode.Inline)
            {
                var badge = new Rect(
                    badgeX,
                    bounds.y + 7f,
                    badgeWidth,
                    Mathf.Min(HeroSecondaryHeight, Mathf.Max(0f, bounds.height - 7f)));
                float titleRight = Mathf.Max(
                    contentLeft,
                    badge.xMin - Mathf.Min(8f, Mathf.Max(0f, badge.xMin - contentLeft)));
                var title = new Rect(
                    contentLeft,
                    bounds.y + 5f,
                    Mathf.Max(0f, titleRight - contentLeft),
                    Mathf.Min(HeroTitleHeight, Mathf.Max(0f, bounds.height - 5f)));
                var subtitle = new Rect(
                    contentLeft,
                    bounds.y + 28f,
                    contentWidth,
                    Mathf.Min(HeroSecondaryHeight, Mathf.Max(0f, bounds.height - 28f)));
                return new DataTableLubanHeroLayout(
                    bounds,
                    accent,
                    title,
                    subtitle,
                    badge,
                    mode,
                    hasSubtitle: true);
            }

            var stackedTitle = new Rect(
                contentLeft,
                bounds.y + 4f,
                contentWidth,
                Mathf.Min(HeroTitleHeight, Mathf.Max(0f, bounds.height - 4f)));
            if (mode == DataTableLubanHeroLayoutMode.Compact)
            {
                var compactBadge = new Rect(
                    badgeX,
                    stackedTitle.yMax + HeroRowGap,
                    badgeWidth,
                    Mathf.Min(
                        HeroSecondaryHeight,
                        Mathf.Max(0f, bounds.yMax - stackedTitle.yMax - HeroRowGap - 3f)));
                return new DataTableLubanHeroLayout(
                    bounds,
                    accent,
                    stackedTitle,
                    default,
                    compactBadge,
                    mode,
                    hasSubtitle: false);
            }

            var stackedSubtitle = new Rect(
                contentLeft,
                stackedTitle.yMax + HeroRowGap,
                contentWidth,
                Mathf.Min(
                    HeroSecondaryHeight,
                    Mathf.Max(0f, bounds.yMax - stackedTitle.yMax - HeroRowGap)));
            var stackedBadge = new Rect(
                badgeX,
                stackedSubtitle.yMax + HeroRowGap,
                badgeWidth,
                Mathf.Min(
                    HeroSecondaryHeight,
                    Mathf.Max(0f, bounds.yMax - stackedSubtitle.yMax - HeroRowGap - 3f)));
            return new DataTableLubanHeroLayout(
                bounds,
                accent,
                stackedTitle,
                stackedSubtitle,
                stackedBadge,
                mode,
                hasSubtitle: true);
        }

        internal static bool DrawSection(
            string title,
            bool expanded,
            Color accent,
            string status,
            DataTableLubanInspectorTone tone,
            string tooltip = null)
        {
            EnsureStyles();
            EditorGUILayout.Space(2f);
            DataTableLubanSectionHeaderLayoutMode mode = GetSectionHeaderLayoutMode(
                GetEstimatedContentWidth(),
                status);
            float height = mode == DataTableLubanSectionHeaderLayoutMode.Inline
                ? SectionHeaderInlineHeight
                : SectionHeaderStackedHeight;
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            DataTableLubanSectionHeaderLayout layout =
                CalculateSectionHeaderLayout(rect, status, mode);
            float shade = expanded ? 1f : 0.72f;
            EditorGUI.DrawRect(
                layout.HeaderRect,
                new Color(
                    accent.r * shade,
                    accent.g * shade,
                    accent.b * shade,
                    0.96f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, 1f),
                new Color(1f, 1f, 1f, 0.10f));
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                new Color(0f, 0f, 0f, 0.24f));
            DrawFoldoutTriangle(layout.ArrowRect, expanded);
            SectionTitleContent.text = title;
            SectionTitleContent.tooltip = tooltip;
            EditorGUI.LabelField(
                layout.LabelRect,
                SectionTitleContent,
                layout.Mode == DataTableLubanSectionHeaderLayoutMode.Stacked
                    ? _stackedHeaderStyle
                    : _headerStyle);
            if (layout.HasBadge)
            {
                DrawBadge(
                    layout.BadgeRect,
                    status,
                    GetToneColor(tone));
            }

            Event current = Event.current;
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                layout.HeaderRect.Contains(current.mousePosition))
            {
                expanded = !expanded;
                current.Use();
            }

            return expanded;
        }

        internal static DataTableLubanSectionHeaderLayout CalculateSectionHeaderLayout(
            Rect headerRect,
            string status)
        {
            return CalculateSectionHeaderLayout(
                headerRect,
                status,
                GetSectionHeaderLayoutMode(headerRect.width, status));
        }

        internal static DataTableLubanSectionHeaderLayout CalculateSectionHeaderLayout(
            Rect headerRect,
            string status,
            DataTableLubanSectionHeaderLayoutMode mode)
        {
            var header = new Rect(
                headerRect.x,
                headerRect.y,
                Mathf.Max(0f, headerRect.width),
                Mathf.Max(0f, headerRect.height));
            float horizontalPadding = Mathf.Min(HeaderHorizontalPadding, header.width);
            float firstRowHeight = mode == DataTableLubanSectionHeaderLayoutMode.Inline
                ? header.height
                : Mathf.Min(SectionHeaderInlineHeight, header.height);
            float arrowVerticalPadding = Mathf.Min(2f, firstRowHeight * 0.5f);
            float arrowX = Mathf.Min(header.xMax, header.x + horizontalPadding);
            float arrowWidth = Mathf.Min(
                HeaderArrowWidth,
                Mathf.Max(0f, header.xMax - arrowX));
            var arrow = new Rect(
                arrowX,
                header.y + arrowVerticalPadding,
                arrowWidth,
                Mathf.Max(0f, firstRowHeight - arrowVerticalPadding * 2f));

            bool hasBadge = !string.IsNullOrEmpty(status);
            float labelX = Mathf.Min(header.xMax, arrow.xMax + 2f);
            float desiredBadgeWidth = GetSectionBadgeWidth(status);
            float badgeRightPadding = Mathf.Min(5f, header.width);
            Rect badge;
            Rect label;
            if (mode == DataTableLubanSectionHeaderLayoutMode.Stacked)
            {
                float secondRowY = Mathf.Min(
                    header.yMax,
                    header.y + firstRowHeight + SectionHeaderRowGap);
                float secondRowHeight = Mathf.Max(0f, header.yMax - secondRowY);
                float maximumBadgeWidth = Mathf.Max(
                    0f,
                    header.width - HeaderHorizontalPadding - badgeRightPadding);
                float badgeWidth = Mathf.Min(desiredBadgeWidth, maximumBadgeWidth);
                float badgeVerticalPadding = Mathf.Min(2f, secondRowHeight * 0.5f);
                badge = new Rect(
                    Mathf.Max(header.x, header.xMax - badgeWidth - badgeRightPadding),
                    secondRowY + badgeVerticalPadding,
                    badgeWidth,
                    Mathf.Max(0f, secondRowHeight - badgeVerticalPadding * 2f));
                label = new Rect(
                    labelX,
                    header.y,
                    Mathf.Max(
                        0f,
                        header.xMax - Mathf.Min(HeaderHorizontalPadding, header.width) - labelX),
                    firstRowHeight);
            }
            else
            {
                float arrowOffset = arrow.xMax - header.x;
                float maximumBadgeWidth = Mathf.Max(
                    0f,
                    header.width - arrowOffset - HeaderHorizontalPadding - 5f);
                float badgeWidth = Mathf.Min(desiredBadgeWidth, maximumBadgeWidth);
                float badgeVerticalPadding = Mathf.Min(3f, header.height * 0.5f);
                badge = new Rect(
                    Mathf.Max(header.x, header.xMax - badgeWidth - badgeRightPadding),
                    header.y + badgeVerticalPadding,
                    badgeWidth,
                    Mathf.Max(0f, header.height - badgeVerticalPadding * 2f));
                float labelRight = hasBadge
                    ? badge.xMin - Mathf.Min(4f, Mathf.Max(0f, badge.xMin - labelX))
                    : header.xMax - Mathf.Min(HeaderHorizontalPadding, header.width);
                label = new Rect(
                    labelX,
                    header.y,
                    Mathf.Max(0f, labelRight - labelX),
                    header.height);
            }

            return new DataTableLubanSectionHeaderLayout(
                header,
                arrow,
                label,
                badge,
                hasBadge && badge.width > 0f,
                mode);
        }

        internal static DataTableLubanSectionHeaderLayoutMode GetSectionHeaderLayoutMode(
            float availableWidth,
            string status)
        {
            if (string.IsNullOrEmpty(status))
            {
                return DataTableLubanSectionHeaderLayoutMode.Inline;
            }

            float requiredWidth = HeaderHorizontalPadding +
                                  HeaderArrowWidth +
                                  2f +
                                  SectionHeaderMinimumTitleWidth +
                                  4f +
                                  GetSectionBadgeWidth(status) +
                                  5f;
            return availableWidth >= requiredWidth
                ? DataTableLubanSectionHeaderLayoutMode.Inline
                : DataTableLubanSectionHeaderLayoutMode.Stacked;
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

        internal static void DrawStatusRow(
            string label,
            string value,
            DataTableLubanInspectorTone tone,
            string tooltip = null)
        {
            EnsureStyles();
            string displayValue = string.IsNullOrEmpty(value) ? "-" : value;
            bool isStacked = ShouldStackStatusRow(
                GetEstimatedContentWidth(),
                label,
                displayValue);
            float height = isStacked
                ? StatusRowInlineHeight * 2f + StatusRowGap
                : StatusRowInlineHeight;
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            DataTableLubanStatusRowLayout layout =
                CalculateStatusRowLayout(rect, isStacked);
            EditorGUI.DrawRect(
                layout.MarkerRect,
                GetToneColor(tone));
            StatusLabelContent.text = label;
            StatusLabelContent.tooltip = string.IsNullOrEmpty(tooltip) ? label : tooltip;
            EditorGUI.LabelField(
                layout.LabelRect,
                StatusLabelContent,
                EditorStyles.miniBoldLabel);
            StatusValueContent.text = displayValue;
            StatusValueContent.tooltip = string.IsNullOrEmpty(tooltip)
                ? displayValue
                : tooltip;
            EditorGUI.LabelField(
                layout.ValueRect,
                StatusValueContent,
                isStacked ? _stackedValueStyle : _valueStyle);
        }

        internal static bool ShouldStackStatusRow(
            float availableWidth,
            string label,
            string value)
        {
            float estimatedValueWidth = Mathf.Clamp(
                16f + (string.IsNullOrEmpty(value) ? 0f : value.Length * 6f),
                64f,
                StatusRowMaximumEstimatedValueWidth);
            float requiredWidth = StatusRowContentOffset +
                                  StatusRowLabelWidth +
                                  StatusRowInlineGap +
                                  estimatedValueWidth +
                                  3f;
            return availableWidth < Mathf.Max(StatusRowMinimumInlineWidth, requiredWidth);
        }

        internal static DataTableLubanStatusRowLayout CalculateStatusRowLayout(
            Rect boundsRect,
            bool isStacked)
        {
            var bounds = new Rect(
                boundsRect.x,
                boundsRect.y,
                Mathf.Max(0f, boundsRect.width),
                Mathf.Max(0f, boundsRect.height));
            float firstRowHeight = isStacked
                ? Mathf.Max(0f, (bounds.height - StatusRowGap) * 0.5f)
                : bounds.height;
            float contentX = Mathf.Min(bounds.xMax, bounds.x + StatusRowContentOffset);
            float markerSize = Mathf.Min(
                StatusRowMarkerSize,
                Mathf.Max(0f, Mathf.Min(bounds.width, firstRowHeight)));
            var marker = new Rect(
                Mathf.Min(bounds.xMax, bounds.x + StatusRowMarkerOffset),
                bounds.y + Mathf.Max(0f, (firstRowHeight - markerSize) * 0.5f),
                markerSize,
                markerSize);
            if (isStacked)
            {
                var label = new Rect(
                    contentX,
                    bounds.y,
                    Mathf.Max(0f, bounds.xMax - contentX),
                    firstRowHeight);
                float valueY = Mathf.Min(
                    bounds.yMax,
                    bounds.y + firstRowHeight + StatusRowGap);
                var value = new Rect(
                    contentX,
                    valueY,
                    Mathf.Max(0f, bounds.xMax - contentX),
                    Mathf.Max(0f, bounds.yMax - valueY));
                return new DataTableLubanStatusRowLayout(
                    bounds,
                    marker,
                    label,
                    value,
                    true);
            }

            float labelWidth = Mathf.Min(
                StatusRowLabelWidth,
                Mathf.Max(0f, bounds.xMax - contentX));
            var inlineLabel = new Rect(
                contentX,
                bounds.y,
                labelWidth,
                bounds.height);
            float valueX = Mathf.Min(
                bounds.xMax,
                inlineLabel.xMax + Mathf.Min(
                    StatusRowInlineGap,
                    Mathf.Max(0f, bounds.xMax - inlineLabel.xMax)));
            var inlineValue = new Rect(
                valueX,
                bounds.y,
                Mathf.Max(0f, bounds.xMax - valueX),
                bounds.height);
            return new DataTableLubanStatusRowLayout(
                bounds,
                marker,
                inlineLabel,
                inlineValue,
                false);
        }

        internal static void DrawReadOnlyPath(
            string label,
            string path,
            bool showReveal,
            string tooltip = null)
        {
            EnsureStyles();
            DataTableLubanReadOnlyPathLayoutMode mode = GetReadOnlyPathLayoutMode(
                GetEstimatedContentWidth(),
                showReveal);
            int lineCount = mode == DataTableLubanReadOnlyPathLayoutMode.Inline
                ? 1
                : mode == DataTableLubanReadOnlyPathLayoutMode.Stacked
                    ? 2
                    : 3;
            float rowHeight = EditorGUIUtility.singleLineHeight * lineCount +
                              ReadOnlyPathStackedLineGap * (lineCount - 1);
            Rect row = EditorGUILayout.GetControlRect(false, rowHeight);
            DataTableLubanReadOnlyPathLayout layout =
                CalculateReadOnlyPathLayout(row, showReveal, mode);
            string displayPath = string.IsNullOrEmpty(path) ? "-" : path;
            ReadOnlyPathLabelContent.text = label;
            ReadOnlyPathLabelContent.tooltip = tooltip;
            ReadOnlyPathValueContent.text = displayPath;
            ReadOnlyPathValueContent.tooltip = path;

            EditorGUI.LabelField(
                layout.LabelRect,
                ReadOnlyPathLabelContent,
                EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(
                layout.ValueRect,
                ReadOnlyPathValueContent,
                _readOnlyPathStyle);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(path)))
            {
                if (GUI.Button(layout.CopyRect, "Copy", EditorStyles.miniButton))
                {
                    EditorGUIUtility.systemCopyBuffer = path;
                }
            }

            if (!layout.HasReveal)
            {
                return;
            }

            bool canReveal = !string.IsNullOrEmpty(path) &&
                             (File.Exists(path) || Directory.Exists(path));
            using (new EditorGUI.DisabledScope(!canReveal))
            {
                if (GUI.Button(layout.RevealRect, "Reveal", EditorStyles.miniButton))
                {
                    EditorUtility.RevealInFinder(path);
                }
            }
        }

        internal static DataTableLubanReadOnlyPathLayout CalculateReadOnlyPathLayout(
            Rect rowRect,
            bool showReveal,
            DataTableLubanReadOnlyPathLayoutMode mode)
        {
            var row = new Rect(
                rowRect.x,
                rowRect.y,
                Mathf.Max(0f, rowRect.width),
                Mathf.Max(0f, rowRect.height));
            int lineCount = mode == DataTableLubanReadOnlyPathLayoutMode.Inline
                ? 1
                : mode == DataTableLubanReadOnlyPathLayoutMode.Stacked
                    ? 2
                    : 3;
            float lineHeight = Mathf.Max(
                0f,
                (row.height - ReadOnlyPathStackedLineGap * (lineCount - 1)) / lineCount);
            float valueRowY = mode == DataTableLubanReadOnlyPathLayoutMode.Inline
                ? row.y
                : row.y + lineHeight + ReadOnlyPathStackedLineGap;
            var valueRow = new Rect(row.x, valueRowY, row.width, lineHeight);

            Rect labelRect;
            if (mode == DataTableLubanReadOnlyPathLayoutMode.Inline)
            {
                labelRect = default;
            }
            else
            {
                labelRect = new Rect(row.x, row.y, row.width, lineHeight);
            }

            if (mode == DataTableLubanReadOnlyPathLayoutMode.Vertical)
            {
                float actionRowY = valueRow.yMax + ReadOnlyPathStackedLineGap;
                var actionRow = new Rect(row.x, actionRowY, row.width, lineHeight);
                Rect copyRect;
                Rect revealRect;
                if (showReveal)
                {
                    DataTableLubanDualButtonLayout actions =
                        CalculateDualButtonLayout(actionRow, isStacked: false);
                    copyRect = actions.FirstRect;
                    revealRect = actions.SecondRect;
                }
                else
                {
                    copyRect = actionRow;
                    revealRect = default;
                }

                return new DataTableLubanReadOnlyPathLayout(
                    row,
                    labelRect,
                    valueRow,
                    copyRect,
                    revealRect,
                    showReveal,
                    mode);
            }

            float actionCursor = valueRow.xMax;
            Rect reveal = default;
            if (showReveal)
            {
                float revealWidth = Mathf.Min(
                    ReadOnlyPathRevealWidth,
                    Mathf.Max(0f, actionCursor - valueRow.x));
                reveal = new Rect(
                    actionCursor - revealWidth,
                    valueRow.y,
                    revealWidth,
                    valueRow.height);
                actionCursor = reveal.xMin;
                actionCursor -= Mathf.Min(
                    ReadOnlyPathGap,
                    Mathf.Max(0f, actionCursor - valueRow.x));
            }

            float copyWidth = Mathf.Min(
                ReadOnlyPathCopyWidth,
                Mathf.Max(0f, actionCursor - valueRow.x));
            var copy = new Rect(
                actionCursor - copyWidth,
                valueRow.y,
                copyWidth,
                valueRow.height);
            actionCursor = copy.xMin;
            actionCursor -= Mathf.Min(
                ReadOnlyPathGap,
                Mathf.Max(0f, actionCursor - valueRow.x));

            float contentWidth = Mathf.Max(0f, actionCursor - valueRow.x);
            float valueX;
            if (mode == DataTableLubanReadOnlyPathLayoutMode.Stacked)
            {
                valueX = valueRow.x;
            }
            else
            {
                float labelWidth = Mathf.Min(
                    ReadOnlyPathLabelWidth,
                    Mathf.Max(
                        0f,
                        contentWidth - ReadOnlyPathMinimumInlineValueWidth - ReadOnlyPathGap));
                labelRect = new Rect(row.x, row.y, labelWidth, lineHeight);
                valueX = labelRect.xMax;
                if (labelWidth > 0f && contentWidth > labelWidth)
                {
                    valueX += Mathf.Min(ReadOnlyPathGap, contentWidth - labelWidth);
                }
            }

            var valueRect = new Rect(
                valueX,
                valueRow.y,
                Mathf.Max(0f, actionCursor - valueX),
                valueRow.height);
            return new DataTableLubanReadOnlyPathLayout(
                row,
                labelRect,
                valueRect,
                copy,
                reveal,
                showReveal && reveal.width > 0f,
                mode);
        }

        internal static DataTableLubanReadOnlyPathLayoutMode GetReadOnlyPathLayoutMode(
            float availableWidth,
            bool showReveal)
        {
            float requiredWidth = ReadOnlyPathLabelWidth +
                                  ReadOnlyPathGap +
                                  ReadOnlyPathMinimumInlineValueWidth +
                                  ReadOnlyPathGap +
                                  ReadOnlyPathCopyWidth;
            if (showReveal)
            {
                requiredWidth += ReadOnlyPathGap + ReadOnlyPathRevealWidth;
            }

            if (availableWidth >= requiredWidth)
            {
                return DataTableLubanReadOnlyPathLayoutMode.Inline;
            }

            return availableWidth >= ReadOnlyPathMinimumStackedWidth
                ? DataTableLubanReadOnlyPathLayoutMode.Stacked
                : DataTableLubanReadOnlyPathLayoutMode.Vertical;
        }

        internal static bool ShouldStackFieldActions(float availableWidth)
        {
            return availableWidth < ResponsiveFieldActionsMinimumWidth;
        }

        internal static bool ShouldStackDualButtons(float availableWidth)
        {
            return availableWidth < ResponsiveDualButtonsMinimumWidth;
        }

        internal static float GetEstimatedContentWidth()
        {
            return Mathf.Max(
                0f,
                EditorGUIUtility.currentViewWidth - InspectorEstimatedHorizontalChrome);
        }

        internal static DataTableLubanDualButtonLayout GetDualButtonLayout(float buttonHeight)
        {
            bool isStacked = ShouldStackDualButtons(GetEstimatedContentWidth());
            float groupHeight = isStacked
                ? buttonHeight * 2f + DualButtonGap
                : buttonHeight;
            Rect groupRect = EditorGUILayout.GetControlRect(false, groupHeight);
            return CalculateDualButtonLayout(groupRect, isStacked);
        }

        internal static DataTableLubanDualButtonLayout CalculateDualButtonLayout(
            Rect groupRect,
            bool isStacked)
        {
            var group = new Rect(
                groupRect.x,
                groupRect.y,
                Mathf.Max(0f, groupRect.width),
                Mathf.Max(0f, groupRect.height));
            if (isStacked)
            {
                float buttonHeight = Mathf.Max(0f, (group.height - DualButtonGap) * 0.5f);
                return new DataTableLubanDualButtonLayout(
                    group,
                    new Rect(group.x, group.y, group.width, buttonHeight),
                    new Rect(
                        group.x,
                        group.y + buttonHeight + DualButtonGap,
                        group.width,
                        buttonHeight),
                    true);
            }

            float buttonWidth = Mathf.Max(0f, (group.width - DualButtonGap) * 0.5f);
            return new DataTableLubanDualButtonLayout(
                group,
                new Rect(group.x, group.y, buttonWidth, group.height),
                new Rect(
                    group.x + buttonWidth + DualButtonGap,
                    group.y,
                    buttonWidth,
                    group.height),
                false);
        }

        internal static DataTableLubanFieldActionLayout GetFieldActionLayout(float rowHeight)
        {
            bool isStacked = ShouldStackFieldActions(GetEstimatedContentWidth());
            float groupHeight = isStacked
                ? rowHeight * 2f + DualButtonGap
                : rowHeight;
            Rect groupRect = EditorGUILayout.GetControlRect(false, groupHeight);
            return CalculateFieldActionLayout(groupRect, isStacked);
        }

        internal static DataTableLubanFieldActionLayout CalculateFieldActionLayout(
            Rect groupRect,
            bool isStacked)
        {
            var group = new Rect(
                groupRect.x,
                groupRect.y,
                Mathf.Max(0f, groupRect.width),
                Mathf.Max(0f, groupRect.height));
            if (isStacked)
            {
                float rowHeight = Mathf.Max(0f, (group.height - DualButtonGap) * 0.5f);
                var actionRow = new Rect(
                    group.x,
                    group.y + rowHeight + DualButtonGap,
                    group.width,
                    rowHeight);
                DataTableLubanDualButtonLayout actions =
                    CalculateDualButtonLayout(actionRow, isStacked: false);
                return new DataTableLubanFieldActionLayout(
                    group,
                    new Rect(group.x, group.y, group.width, rowHeight),
                    actions.FirstRect,
                    actions.SecondRect,
                    true);
            }

            float secondActionWidth = Mathf.Min(FieldActionWidth, group.width);
            var secondAction = new Rect(
                group.xMax - secondActionWidth,
                group.y,
                secondActionWidth,
                group.height);
            float secondGap = Mathf.Min(
                DualButtonGap,
                Mathf.Max(0f, secondAction.xMin - group.x));
            float firstActionRight = secondAction.xMin - secondGap;
            float firstActionWidth = Mathf.Min(
                FieldActionWidth,
                Mathf.Max(0f, firstActionRight - group.x));
            var firstAction = new Rect(
                firstActionRight - firstActionWidth,
                group.y,
                firstActionWidth,
                group.height);
            float firstGap = Mathf.Min(
                DualButtonGap,
                Mathf.Max(0f, firstAction.xMin - group.x));
            var field = new Rect(
                group.x,
                group.y,
                Mathf.Max(0f, firstAction.xMin - firstGap - group.x),
                group.height);
            return new DataTableLubanFieldActionLayout(
                group,
                field,
                firstAction,
                secondAction,
                false);
        }

        internal static GUIStyle GetReadOnlyPathStyleForTests()
        {
            EnsureStyles();
            return _readOnlyPathStyle;
        }

        internal static GUIStyle GetReadOnlyOutputStyleForTests()
        {
            EnsureStyles();
            return _readOnlyOutputStyle;
        }

        internal static void DrawReadOnlyPreview(string label, string value, float previewHeight)
        {
            EnsureStyles();
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            float height = Mathf.Clamp(
                previewHeight,
                ReadOnlyPreviewMinimumHeight,
                ReadOnlyPreviewMaximumHeight);
            Rect previewRect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.LabelField(previewRect, value ?? string.Empty, _readOnlyOutputStyle);
            EditorGUILayout.EndVertical();
        }

        internal static void DrawNotice(
            string title,
            string message,
            string detail,
            DataTableLubanInspectorTone tone)
        {
            EnsureStyles();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect accent = EditorGUILayout.GetControlRect(false, 3f);
            EditorGUI.DrawRect(accent, GetToneColor(tone));
            if (!string.IsNullOrEmpty(title))
            {
                NoticeTitleContent.text = title;
                NoticeTitleContent.tooltip = title;
                EditorGUILayout.LabelField(NoticeTitleContent, EditorStyles.miniBoldLabel);
            }

            if (!string.IsNullOrEmpty(message))
            {
                NoticeMessageContent.text = message;
                NoticeMessageContent.tooltip = message;
                EditorGUILayout.LabelField(
                    NoticeMessageContent,
                    EditorStyles.wordWrappedMiniLabel);
            }

            if (!string.IsNullOrEmpty(detail))
            {
                NoticeDetailContent.text = detail;
                NoticeDetailContent.tooltip = detail;
                EditorGUILayout.LabelField(
                    NoticeDetailContent,
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        internal static bool DrawPrimaryButton(GUIContent content, bool enabled)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = enabled
                ? new Color(0.34f, 0.78f, 0.48f)
                : Color.white;
            bool clicked;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                clicked = GUILayout.Button(content, GUILayout.Height(34f));
            }

            GUI.backgroundColor = previous;
            return clicked;
        }

        internal static DataTableLubanInspectorTone GetStateTone(
            DataTableLubanAuthoringState state)
        {
            switch (state)
            {
                case DataTableLubanAuthoringState.Ready:
                    return DataTableLubanInspectorTone.Ready;
                case DataTableLubanAuthoringState.Inspecting:
                case DataTableLubanAuthoringState.Busy:
                    return DataTableLubanInspectorTone.Busy;
                case DataTableLubanAuthoringState.Blocked:
                    return DataTableLubanInspectorTone.Warning;
                case DataTableLubanAuthoringState.Invalid:
                case DataTableLubanAuthoringState.RecoveryRequired:
                    return DataTableLubanInspectorTone.Error;
                default:
                    return DataTableLubanInspectorTone.Neutral;
            }
        }

        internal static DataTableLubanInspectorTone GetIssueTone(
            DataTableLubanIssueSeverity severity)
        {
            switch (severity)
            {
                case DataTableLubanIssueSeverity.Error:
                    return DataTableLubanInspectorTone.Error;
                case DataTableLubanIssueSeverity.Warning:
                    return DataTableLubanInspectorTone.Warning;
                default:
                    return DataTableLubanInspectorTone.Info;
            }
        }

        private static void DrawBadge(Rect rect, string label, Color color)
        {
            EnsureStyles();
            EditorGUI.DrawRect(rect, color);
            BadgeContent.text = label;
            BadgeContent.tooltip = label;
            EditorGUI.LabelField(rect, BadgeContent, _badgeStyle);
        }

        private static float GetHeroBadgeWidth(string status)
        {
            return string.IsNullOrEmpty(status)
                ? 0f
                : Mathf.Clamp(26f + status.Length * 6f, 76f, 152f);
        }

        private static float GetSectionBadgeWidth(string status)
        {
            return string.IsNullOrEmpty(status)
                ? 0f
                : Mathf.Clamp(26f + status.Length * 6f, 52f, 112f);
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

        private static Color GetToneColor(DataTableLubanInspectorTone tone)
        {
            switch (tone)
            {
                case DataTableLubanInspectorTone.Info:
                    return new Color(0.20f, 0.58f, 0.82f);
                case DataTableLubanInspectorTone.Ready:
                    return new Color(0.18f, 0.66f, 0.40f);
                case DataTableLubanInspectorTone.Warning:
                    return new Color(0.88f, 0.55f, 0.12f);
                case DataTableLubanInspectorTone.Error:
                    return new Color(0.82f, 0.24f, 0.22f);
                case DataTableLubanInspectorTone.Busy:
                    return new Color(0.28f, 0.52f, 0.86f);
                default:
                    return new Color(0.48f, 0.51f, 0.56f);
            }
        }

        private static void EnsureStyles()
        {
            if (_titleStyle != null && _proSkin == EditorGUIUtility.isProSkin)
            {
                return;
            }

            _proSkin = EditorGUIUtility.isProSkin;
            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                clipping = TextClipping.Clip,
            };
            _compactTitleStyle = new GUIStyle(_titleStyle)
            {
                fontSize = 11,
            };
            _subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip,
            };
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };
            _stackedHeaderStyle = new GUIStyle(_headerStyle)
            {
                fontSize = 11,
            };
            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
            };
            _valueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip,
            };
            _stackedValueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };
            _readOnlyPathStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            _readOnlyOutputStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
            };
        }
    }
}
