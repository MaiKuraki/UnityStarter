#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Editor
{
    public sealed class UIPerformanceAuditWindow : EditorWindow
    {
        private readonly List<Entry> _entries = new List<Entry>(64);
        private Vector2 _scroll;
        private bool _showOnlyIssues = true;
        private string _searchText = string.Empty;
        private SeverityFilter _severityFilter = SeverityFilter.InfoAndAbove;
        private SortMode _sortMode = SortMode.WarningCount;
        private GUIStyle _toolbarSearchFieldStyle;
        private GUIStyle _chipValueStyle;
        private GUIStyle _chipTitleStyle;
        private GUIStyle _metricLabelStyle;
        private GUIStyle _metricValueStyle;
        private GUIStyle _entryTitleStyle;
        private GUIStyle _badgeStyle;

        private enum SeverityFilter
        {
            InfoAndAbove = 0,
            WarningAndAbove = 1,
            ErrorsOnly = 2
        }

        private enum SortMode
        {
            WarningCount = 0,
            Name = 1,
            GraphicsCount = 2,
            MaterialVariants = 3
        }

        private sealed class Entry
        {
            public UIWindowConfiguration Config;
            public GameObject Prefab;
            public UIPerformanceAuditUtility.AuditReport Report;
        }

        [MenuItem("Tools/CycloneGames/UI Framework/Performance Auditor")]
        public static void ShowWindow()
        {
            UIPerformanceAuditWindow window = GetWindow<UIPerformanceAuditWindow>("UI Perf Auditor");
            window.minSize = new Vector2(860f, 480f);
            window.Refresh();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnGUI()
        {
            EnsureStyles();
            SortEntries();
            DrawSummaryBar();
            DrawToolbar();
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (!ShouldDisplay(entry))
                {
                    continue;
                }

                DrawEntry(entry);
                EditorGUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSummaryBar()
        {
            int visibleEntries = 0;
            int warnings = 0;
            int errors = 0;
            int infos = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!ShouldDisplay(_entries[i])) continue;
                visibleEntries++;
                if (_entries[i].Report == null) continue;
                warnings += _entries[i].Report.WarningCount;
                errors += _entries[i].Report.ErrorCount;
                infos += _entries[i].Report.InfoCount;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Title row
            EditorGUILayout.LabelField("Audit Summary", EditorStyles.boldLabel);

            // Responsive chip row using Rect-based layout
            const float chipHeight = 42f;
            const float chipGap = 4f;
            const int chipCount = 4;

            Rect chipRow = EditorGUILayout.GetControlRect(false, chipHeight);
            float totalGap = chipGap * (chipCount - 1);
            float chipWidth = (chipRow.width - totalGap) / chipCount;

            Rect chipRect = new Rect(chipRow.x, chipRow.y, chipWidth, chipHeight);
            DrawSummaryChipRect(chipRect, "Visible", visibleEntries.ToString(), new Color(0.25f, 0.55f, 0.85f));
            chipRect.x += chipWidth + chipGap;
            DrawSummaryChipRect(chipRect, "Warnings", warnings.ToString(), new Color(0.85f, 0.6f, 0.2f));
            chipRect.x += chipWidth + chipGap;
            DrawSummaryChipRect(chipRect, "Errors", errors.ToString(), new Color(0.85f, 0.3f, 0.3f));
            chipRect.x += chipWidth + chipGap;
            chipRect.width = chipRow.xMax - chipRect.x; // absorb rounding remainder
            DrawSummaryChipRect(chipRect, "Infos", infos.ToString(), new Color(0.45f, 0.45f, 0.45f));

            EditorGUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.toolbar);
            try
            {
                DrawToolbarResponsive();
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawEntry(Entry entry)
        {
            if (entry == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header row
            Rect headerRow = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4f);
            headerRow.y += 1f;

            // Severity badge (left-side color strip)
            UIPerformanceAuditUtility.AuditReport report = entry.Report;
            Color badgeColor = GetSeverityColor(report);
            Rect strip = new Rect(headerRow.x, headerRow.y - 1f, 3f, headerRow.height + 2f);
            EditorGUI.DrawRect(strip, badgeColor);

            // Title
            Rect titleRect = new Rect(headerRow.x + 8f, headerRow.y, headerRow.width * 0.45f, headerRow.height);
            GUI.Label(titleRect, entry.Config != null ? entry.Config.name : "<missing config>", _entryTitleStyle ?? EditorStyles.boldLabel);

            // Buttons (right-aligned)
            const float btnWidth = 86f;
            const float btnGap = 4f;
            const float badgeWidth = 88f;
            float rightEdge = headerRow.xMax;

            if (entry.Prefab != null)
            {
                rightEdge -= btnWidth;
                if (GUI.Button(new Rect(rightEdge, headerRow.y, btnWidth, headerRow.height), "Ping Prefab", EditorStyles.miniButton))
                {
                    Selection.activeObject = entry.Prefab;
                    EditorGUIUtility.PingObject(entry.Prefab);
                }
                rightEdge -= btnGap;
            }

            if (entry.Config != null)
            {
                rightEdge -= btnWidth;
                if (GUI.Button(new Rect(rightEdge, headerRow.y, btnWidth, headerRow.height), "Ping Config", EditorStyles.miniButton))
                {
                    Selection.activeObject = entry.Config;
                    EditorGUIUtility.PingObject(entry.Config);
                }
                rightEdge -= btnGap;
            }

            // Inline severity badge
            rightEdge -= badgeWidth;
            DrawSeverityBadgeRect(new Rect(rightEdge, headerRow.y, badgeWidth, headerRow.height), report);

            if (entry.Prefab == null)
            {
                EditorGUILayout.HelpBox("No inspection prefab could be resolved for this window configuration.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space(2);
            DrawMetricsGrid(report);

            if (!report.HasIssues)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox("No major audit issues detected.", MessageType.None);
            }
            else
            {
                EditorGUILayout.Space(2);
                for (int i = 0; i < report.Issues.Count; i++)
                {
                    MessageType type = report.Issues[i].Severity == UIPerformanceAuditUtility.AuditSeverity.Warning
                        ? MessageType.Warning
                        : report.Issues[i].Severity == UIPerformanceAuditUtility.AuditSeverity.Error
                            ? MessageType.Error
                            : MessageType.None;
                    EditorGUILayout.HelpBox(report.Issues[i].Message, type);
                }
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }

        private void Refresh()
        {
            _entries.Clear();

            string[] guids = AssetDatabase.FindAssets("t:UIWindowConfiguration");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                UIWindowConfiguration config = AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(path);
                if (config == null) continue;

                GameObject prefab = UIPerformanceAuditUtility.ResolveInspectionPrefab(config);
                _entries.Add(new Entry
                {
                    Config = config,
                    Prefab = prefab,
                    Report = UIPerformanceAuditUtility.AuditWindowConfiguration(config)
                });
            }
            SortEntries();
        }

        private void RefreshSelectionOnly()
        {
            _entries.Clear();

            for (int i = 0; i < Selection.objects.Length; i++)
            {
                if (Selection.objects[i] is UIWindowConfiguration config)
                {
                    GameObject prefab = UIPerformanceAuditUtility.ResolveInspectionPrefab(config);
                    _entries.Add(new Entry
                    {
                        Config = config,
                        Prefab = prefab,
                    Report = UIPerformanceAuditUtility.AuditWindowConfiguration(config)
                    });
                }
            }
            SortEntries();
        }

        private bool ShouldDisplay(Entry entry)
        {
            if (entry == null) return false;
            if (_showOnlyIssues && (entry.Report == null || !entry.Report.HasIssues)) return false;

            if (!string.IsNullOrEmpty(_searchText))
            {
                string target = $"{entry.Config?.name} {entry.Prefab?.name}".ToLowerInvariant();
                if (!target.Contains(_searchText.ToLowerInvariant()))
                {
                    return false;
                }
            }

            if (entry.Report == null) return true;
            return _severityFilter switch
            {
                SeverityFilter.ErrorsOnly => entry.Report.ErrorCount > 0,
                SeverityFilter.WarningAndAbove => entry.Report.WarningCount > 0 || entry.Report.ErrorCount > 0,
                _ => true
            };
        }

        private void SortEntries()
        {
            _entries.Sort((a, b) =>
            {
                switch (_sortMode)
                {
                    case SortMode.Name:
                        return string.Compare(a.Config?.name, b.Config?.name, System.StringComparison.OrdinalIgnoreCase);
                    case SortMode.GraphicsCount:
                        return (b.Report?.GraphicsCount ?? 0).CompareTo(a.Report?.GraphicsCount ?? 0);
                    case SortMode.MaterialVariants:
                        return (b.Report?.MaterialVariantCount ?? 0).CompareTo(a.Report?.MaterialVariantCount ?? 0);
                    default:
                        int severityCompare = (b.Report?.WarningCount + b.Report?.ErrorCount * 10 ?? 0)
                            .CompareTo(a.Report?.WarningCount + a.Report?.ErrorCount * 10 ?? 0);
                        return severityCompare != 0
                            ? severityCompare
                            : string.Compare(a.Config?.name, b.Config?.name, System.StringComparison.OrdinalIgnoreCase);
                }
            });
        }

        private void DrawToolbarResponsive()
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            rowRect.x += 2f;
            rowRect.width -= 4f;

            const float gap = 4f;
            const float minPopupWidth = 88f;
            const float maxPopupWidth = 132f;
            const float minSearchWidth = 110f;
            const float maxSearchWidth = 260f;
            const float refreshWidth = 76f;
            const float issuesWidth = 92f;
            const float scanWidth = 102f;
            const float severityLabelWidth = 42f;
            const float sortLabelWidth = 26f;
            const float searchLabelWidth = 42f;
            const float entriesWidth = 58f;

            float popupWidth = Mathf.Clamp((rowRect.width - 520f) * 0.2f + 96f, minPopupWidth, maxPopupWidth);
            float remaining = rowRect.width
                              - refreshWidth
                              - issuesWidth
                              - scanWidth
                              - severityLabelWidth
                              - sortLabelWidth
                              - searchLabelWidth
                              - entriesWidth
                              - popupWidth * 2f
                              - gap * 9f;
            float searchWidth = Mathf.Clamp(remaining, minSearchWidth, maxSearchWidth);

            Rect rect = rowRect;
            rect.width = refreshWidth;
            if (GUI.Button(rect, "Refresh", EditorStyles.toolbarButton))
            {
                Refresh();
            }

            rect.x += rect.width + gap;
            rect.width = issuesWidth;
            _showOnlyIssues = GUI.Toggle(rect, _showOnlyIssues, "Issues Only", EditorStyles.toolbarButton);

            rect.x += rect.width + gap;
            rect.width = severityLabelWidth;
            GUI.Label(rect, "Level", EditorStyles.miniLabel);

            rect.x += rect.width + gap;
            rect.width = popupWidth;
            _severityFilter = (SeverityFilter)EditorGUI.EnumPopup(rect, GUIContent.none, _severityFilter, EditorStyles.toolbarPopup);

            rect.x += rect.width + gap;
            rect.width = sortLabelWidth;
            GUI.Label(rect, "Sort", EditorStyles.miniLabel);

            rect.x += rect.width + gap;
            rect.width = popupWidth;
            _sortMode = (SortMode)EditorGUI.EnumPopup(rect, GUIContent.none, _sortMode, EditorStyles.toolbarPopup);

            rect.x += rect.width + gap;
            rect.width = searchLabelWidth;
            GUI.Label(rect, "Search", EditorStyles.miniLabel);

            rect.x += rect.width + gap;
            rect.width = searchWidth;
            _searchText = GUI.TextField(rect, _searchText ?? string.Empty, _toolbarSearchFieldStyle);

            rect.x += rect.width + gap;
            rect.width = scanWidth;
            if (GUI.Button(rect, "Scan Selection", EditorStyles.toolbarButton))
            {
                RefreshSelectionOnly();
            }

            rect.x += rect.width + gap;
            rect.width = Mathf.Max(0f, rowRect.xMax - rect.x);
            GUI.Label(rect, $"Entries {_entries.Count}", EditorStyles.miniLabel);
        }

        private void DrawSummaryChipRect(Rect rect, string title, string value, Color color)
        {
            // Background
            Color dimmed = new Color(color.r, color.g, color.b, 0.15f);
            EditorGUI.DrawRect(rect, dimmed);

            // Left accent strip
            Rect accent = new Rect(rect.x, rect.y, 3f, rect.height);
            EditorGUI.DrawRect(accent, color);

            // Title (top half)
            Rect titleRect = new Rect(rect.x + 8f, rect.y + 4f, rect.width - 12f, 14f);
            GUI.Label(titleRect, title, _chipTitleStyle ?? EditorStyles.centeredGreyMiniLabel);

            // Value (bottom half, larger)
            Rect valueRect = new Rect(rect.x + 8f, rect.y + 18f, rect.width - 12f, 20f);
            GUI.Label(valueRect, value, _chipValueStyle ?? EditorStyles.boldLabel);
        }

        private static Color GetSeverityColor(UIPerformanceAuditUtility.AuditReport report)
        {
            if (report == null) return new Color(0.4f, 0.4f, 0.4f);
            if (report.ErrorCount > 0) return new Color(0.8f, 0.25f, 0.25f);
            if (report.WarningCount > 0) return new Color(0.85f, 0.6f, 0.2f);
            if (report.InfoCount > 0) return new Color(0.35f, 0.55f, 0.85f);
            return new Color(0.25f, 0.65f, 0.4f);
        }

        private void DrawSeverityBadgeRect(Rect rect, UIPerformanceAuditUtility.AuditReport report)
        {
            if (report == null) return;

            string label = report.ErrorCount > 0
                ? $"Errors: {report.ErrorCount}"
                : report.WarningCount > 0
                    ? $"Warn: {report.WarningCount}"
                    : report.InfoCount > 0
                        ? $"Info: {report.InfoCount}"
                        : "Clean";

            Color color = GetSeverityColor(report);
            Color bg = new Color(color.r, color.g, color.b, 0.2f);
            EditorGUI.DrawRect(rect, bg);

            // Left accent
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), color);

            GUI.Label(rect, label, _badgeStyle ?? EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawMetricsGrid(UIPerformanceAuditUtility.AuditReport report)
        {
            // Use Rect-based grid: 3 columns, 4 rows — all cells equal width
            const int cols = 3;
            const int rows = 4;
            const float cellHeight = 30f;
            const float cellGap = 2f;
            float totalHeight = rows * cellHeight + (rows - 1) * cellGap;

            Rect gridArea = EditorGUILayout.GetControlRect(false, totalHeight);
            float colWidth = (gridArea.width - cellGap * (cols - 1)) / cols;

            // Row data: label, value triples
            string[,] cells = new string[rows, cols * 2];
            cells[0, 0] = "Graphics"; cells[0, 1] = report.GraphicsCount.ToString();
            cells[0, 2] = "Raycasts"; cells[0, 3] = report.RaycastTargets.ToString();
            cells[0, 4] = "NonInteractive"; cells[0, 5] = report.NonInteractiveRaycastTargets.ToString();
            cells[1, 0] = "Layout"; cells[1, 1] = $"{report.LayoutGroupCount} / {report.ContentSizeFitterCount}";
            cells[1, 2] = "Masks"; cells[1, 3] = $"{report.MaskCount} / {report.RectMaskCount}";
            cells[1, 4] = "ScrollRect"; cells[1, 5] = report.ScrollRectCount.ToString();
            cells[2, 0] = "Animators"; cells[2, 1] = $"{report.AnimatorCount + report.AnimationCount}";
            cells[2, 2] = "Materials"; cells[2, 3] = report.MaterialVariantCount.ToString();
            cells[2, 4] = "Textures"; cells[2, 5] = report.TextureVariantCount.ToString();
            cells[3, 0] = "Canvas"; cells[3, 1] = $"{report.CanvasCount} ({report.NestedCanvasCount} nested)";
            cells[3, 2] = "SubCanvas"; cells[3, 3] = report.SuggestedSubCanvasPolicy.ToString();
            cells[3, 4] = "TMP"; cells[3, 5] = report.HasTextMeshPro ? "Yes" : "No";

            Color cellBg = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 0.5f)
                : new Color(0.82f, 0.82f, 0.82f, 0.5f);

            for (int row = 0; row < rows; row++)
            {
                float y = gridArea.y + row * (cellHeight + cellGap);
                for (int col = 0; col < cols; col++)
                {
                    float x = gridArea.x + col * (colWidth + cellGap);
                    Rect cellRect = new Rect(x, y, colWidth, cellHeight);

                    EditorGUI.DrawRect(cellRect, cellBg);

                    string lbl = cells[row, col * 2];
                    string val = cells[row, col * 2 + 1];

                    Rect labelRect = new Rect(cellRect.x + 6f, cellRect.y + 2f, cellRect.width - 12f, 13f);
                    Rect valueRect = new Rect(cellRect.x + 6f, cellRect.y + 15f, cellRect.width - 12f, 13f);

                    GUI.Label(labelRect, lbl, _metricLabelStyle ?? EditorStyles.miniLabel);
                    GUI.Label(valueRect, val, _metricValueStyle ?? EditorStyles.miniBoldLabel);
                }
            }
        }

        private void EnsureStyles()
        {
            if (_chipValueStyle != null) return;

            _toolbarSearchFieldStyle =
                GUI.skin?.FindStyle("ToolbarSearchTextField") ??
                GUI.skin?.FindStyle("ToolbarSeachTextField") ??
                EditorStyles.toolbarSearchField ??
                EditorStyles.textField;

            _chipTitleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 10
            };

            _chipValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14
            };

            _metricLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.6f, 0.6f, 0.6f)
                    : new Color(0.35f, 0.35f, 0.35f) }
            };

            _metricValueStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.85f, 0.85f, 0.85f)
                    : new Color(0.15f, 0.15f, 0.15f) }
            };

            _entryTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };

            _badgeStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.8f, 0.8f, 0.8f)
                    : new Color(0.2f, 0.2f, 0.2f) }
            };
        }
    }
}
#endif
