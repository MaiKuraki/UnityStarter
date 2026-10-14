using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Editor
{
    /// <summary>
    /// Live diagnostics window for a context registered through <see cref="EventBusEditorDiagnostics"/>.
    ///
    /// Performance model: a retained model rebuilt only on a snapshot tick (0.5 s cadence or a
    /// manual refresh), never per repaint. Every displayed string is formatted during the rebuild,
    /// so OnGUI repaints (mouse move, scroll, focus) perform no managed allocation. Subscriber rows
    /// are capped per bus; the counter aggregates stay exact while the row list is a bounded sample.
    /// Read-only by design.
    /// </summary>
    public sealed class EventBusDebuggerWindow : EditorWindow
    {
        private const double RefreshIntervalSeconds = 0.5d;
        private const float RowHeight = 20f;
        private const float HeaderHeight = 22f;
        private const int MaxSubscriberRowsPerBus = 512;

        private static readonly string[] Tabs = { "Buses", "Summary" };

        private static readonly Color PillBuses = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color PillSubs = new Color(0.2f, 0.7f, 0.3f);
        private static readonly Color PillRate = new Color(0.2f, 0.5f, 1.0f);
        private static readonly Color PillWarning = new Color(0.9f, 0.6f, 0.1f);
        private static readonly Color PillError = new Color(0.9f, 0.25f, 0.2f);

        private static readonly Color RowEven = new Color(0.5f, 0.5f, 0.5f, 0.06f);
        private static readonly Color RowOdd = new Color(0.5f, 0.5f, 0.5f, 0.02f);
        private static readonly Color RowWarning = new Color(0.9f, 0.6f, 0.1f, 0.12f);
        private static readonly Color RowError = new Color(0.9f, 0.25f, 0.2f, 0.14f);
        private static readonly Color TextDim = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color TextWarning = new Color(1f, 0.75f, 0.25f);
        private static readonly Color TextError = new Color(1f, 0.42f, 0.38f);

        // Column layout: every column is resizable by dragging its header separator; right-click
        // the header to reset. Widths are in-memory only (no EditorPrefs).
        private static readonly string[] HeaderTitles =
            { "Event Type", "Subs", "Tomb", "Publishes", "/s", "Err", "Peak", "Cap", "Depth" };
        private static readonly string[] HeaderTooltips =
        {
            "Event struct type. Click a row to expand its subscribers.",
            "Live subscribers.",
            "Dead slots awaiting compaction. A steadily climbing value means subscription churn.",
            "Dispatch entries since construction.",
            "Publishes per second, measured between refreshes.",
            "Subscriber exceptions handled by the error policy.",
            "Highest live subscriber count observed; use it to size initialCapacity.",
            "Backing array length. Grows on subscribe, never shrinks.",
            "Current dispatch nesting depth; zero when idle.",
        };
        private static readonly float[] DefaultColumnWidths =
            { 300f, 46f, 50f, 92f, 58f, 40f, 48f, 48f, 52f };
        private static readonly float[] MinColumnWidths =
            { 120f, 36f, 40f, 60f, 44f, 32f, 40f, 40f, 44f };
        private const float ResizeHandleWidth = 7f;

        private readonly float[] _columnWidths = (float[])DefaultColumnWidths.Clone();

        // Column-drag state: -1 when idle, otherwise the index being resized.
        private int _dragColumn = -1;
        private float _dragStartX;
        private float _dragStartWidth;

        private readonly List<BusEntry> _buses = new List<BusEntry>();
        private readonly List<EventSubscriberInfo> _subscriberScratch = new List<EventSubscriberInfo>(64);
        private readonly Dictionary<string, long> _previousPublishes = new Dictionary<string, long>();
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly StringBuilder _copyBuilder = new StringBuilder(2048);
        private readonly GUIContent _cellContent = new GUIContent();

        private Vector2 _scrollPos;
        private string _searchFilter = string.Empty;
        private int _selectedTab;
        private bool _autoRefresh = true;
        private bool _isVisible;
        private bool _stylesBuilt;
        private double _nextRefresh;
        private double _lastSnapshotTime;
        private EventBusContext _lastContext;

        // Summary strings, preformatted per snapshot.
        private string _pillBusesText;
        private string _pillSubsText;
        private string _pillRateText;
        private string _pillTombText;
        private string _pillErrText;
        private string _pillDropText;
        private string _pillDeadText;
        private int _deadSubscriberCount;
        private EventBusDiagnosticsSnapshot _aggregate;

        private GUIStyle _rowStyle;
        private GUIStyle _numericStyle;
        private GUIStyle _headerCellStyle;
        private GUIStyle _subStyle;
        private GUIStyle _pillStyle;
        private GUIStyle _summaryLabelStyle;
        private GUIStyle _summaryValueStyle;
        private GUILayoutOption[] _pillOptions;

        // Class (not struct) so the subscriber list survives across snapshots without reallocation.
        private sealed class BusEntry
        {
            public string TypeName;
            // Short form (after the last '.') for the row; the full name stays in the tooltip
            // and in TSV/JSON copies.
            public string DisplayName;
            public string SubsText;
            public string TombText;
            public string PublishText;
            public string RateText;
            public string ErrorText;
            public string PeakText;
            public string CapText;
            public string DepthText;
            public string DroppedText;
            public bool HasTombstones;
            public bool HasErrors;
            public bool HasDropped;
            public bool HasDeadSubscribers;
            public int OverflowSubscriberCount;
            public string OverflowText;
            public readonly List<SubRow> Subs = new List<SubRow>();
        }

        private struct SubRow
        {
            public string MethodText;
            public string TargetText;
            public bool IsDeadTarget;
        }

        [MenuItem("Tools/CycloneGames/EventBus/Debugger")]
        private static void Open()
        {
            var window = GetWindow<EventBusDebuggerWindow>("EventBus Debugger");
            window.minSize = new Vector2(720, 420);
            window.Show();
        }

        private void OnEnable()
        {
            _isVisible = true;
            _nextRefresh = 0d;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            _isVisible = false;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnBecameVisible()
        {
            _isVisible = true;
            _nextRefresh = 0d;
        }

        private void OnBecameInvisible()
        {
            _isVisible = false;
        }

        private void OnEditorUpdate()
        {
            if (!_isVisible || !_autoRefresh || !Application.isPlaying)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup >= _nextRefresh)
            {
                _nextRefresh = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;
                RefreshSnapshot();
                Repaint();
            }
        }

        private void BuildStyles()
        {
            if (_stylesBuilt)
            {
                return;
            }

            _stylesBuilt = true;

            _rowStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
            };

            _numericStyle = new GUIStyle(_rowStyle)
            {
                alignment = TextAnchor.MiddleRight,
            };

            _headerCellStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
            };

            _subStyle = new GUIStyle(EditorStyles.label)
            {
                font = EditorStyles.miniFont,
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
            };

            _pillStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 1, 1),
            };
            _pillOptions = new[] { GUILayout.ExpandWidth(false) };

            _summaryLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
            };

            _summaryValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
            };
        }

        private void OnGUI()
        {
            BuildStyles();

            EventBusContext context = EventBusEditorDiagnostics.Current;
            if (context == null)
            {
                _lastContext = null;
                DrawPlaceholder(
                    "No context registered. Call EventBusEditorDiagnostics.Register(context) from "
                    + "editor-only code to observe a context.",
                    MessageType.Info);
                return;
            }

            if (context.IsDisposed)
            {
                // Drop the borrowed reference: keeping it would pin the context (and its buses)
                // for the Editor process lifetime.
                EventBusEditorDiagnostics.Clear();
                _lastContext = null;
                DrawPlaceholder(
                    "The observed EventBusContext was disposed. The reference has been released; "
                    + "register a new context to observe it.",
                    MessageType.Warning);
                return;
            }

            if (context != _lastContext)
            {
                _lastContext = context;
                _previousPublishes.Clear();
                RefreshSnapshot();
            }

            if (_lastSnapshotTime == 0d)
            {
                RefreshSnapshot();
            }

            DrawTopBar();

            GUILayout.Space(4f);
            int newTab = GUILayout.Toolbar(_selectedTab, Tabs, EditorStyles.toolbarButton, GUILayout.Height(22f));
            if (newTab != _selectedTab)
            {
                _selectedTab = newTab;
                _scrollPos = Vector2.zero;
            }

            GUILayout.Space(2f);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, true, true);
            if (_selectedTab == 0)
            {
                DrawWarnings();
                DrawHeader();
                DrawBusRows();
            }
            else
            {
                DrawSummaryTab();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPlaceholder(string message, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
            if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
            {
                _nextRefresh = 0d;
                Repaint();
            }
        }

        // ------------------------------------------------------------------ top bar

        private void DrawTopBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    RefreshSnapshot();
                }

                bool auto = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(48f));
                if (auto != _autoRefresh)
                {
                    _autoRefresh = auto;
                    _nextRefresh = 0d;
                }

                GUILayout.Space(10f);

                DrawStatPill(_pillBusesText, PillBuses);
                GUILayout.Space(4f);
                DrawStatPill(_pillSubsText, PillSubs);
                GUILayout.Space(4f);
                DrawStatPill(_pillRateText, PillRate);
                if (_aggregate.TombstoneCount > 0)
                {
                    GUILayout.Space(4f);
                    DrawStatPill(_pillTombText, PillWarning);
                }

                if (_aggregate.SubscriberErrorCount > 0)
                {
                    GUILayout.Space(4f);
                    DrawStatPill(_pillErrText, PillError);
                }

                if (_aggregate.DroppedReentrantCount > 0)
                {
                    GUILayout.Space(4f);
                    DrawStatPill(_pillDropText, PillError);
                }

                if (_deadSubscriberCount > 0)
                {
                    GUILayout.Space(4f);
                    DrawStatPill(_pillDeadText, PillError);
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Copy TSV", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildTsv();
                }

                if (GUILayout.Button("Copy JSON", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildJson();
                }

                GUILayout.Space(8f);
                GUILayout.Label("Filter:", GUILayout.Width(38f));
                string newFilter = EditorGUILayout.TextField(
                    _searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(180f));
                if (newFilter != _searchFilter)
                {
                    _searchFilter = newFilter;
                }

                if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                {
                    _searchFilter = string.Empty;
                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawStatPill(string text, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, _pillStyle, _pillOptions);
            GUI.color = previous;
        }

        // ------------------------------------------------------------------ warnings

        private void DrawWarnings()
        {
            if (_deadSubscriberCount > 0)
            {
                EditorGUILayout.HelpBox(
                    _deadSubscriberCount
                    + " subscriber target(s) are destroyed but still subscribed. This is a leak: "
                    + "release those subscriptions through a scope or OnDestroy.",
                    MessageType.Error);
            }

            if (_aggregate.DroppedReentrantCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "Publishes were dropped at the re-entrancy depth ceiling. That is a recursive "
                    + "publish chain: find the handler that publishes its own event type, directly or "
                    + "through a cycle.",
                    MessageType.Warning);
            }

            if (_aggregate.SubscriberErrorCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "At least one subscriber threw. Every fault is counted and logged through the "
                    + "configured sink; check the console for the original throw sites.",
                    MessageType.Warning);
            }

            if (_aggregate.TombstoneCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "Tombstones are dead slots left by unsubscribe. A value that keeps climbing means "
                    + "subscription churn: gate the subscription with an activity flag instead of "
                    + "resubscribing.",
                    MessageType.Info);
            }
        }

        // ------------------------------------------------------------------ buses tab

        private void DrawHeader()
        {
            float tableWidth = GetTableWidth();
            Rect headerRect = GUILayoutUtility.GetRect(tableWidth, tableWidth, HeaderHeight, HeaderHeight, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.toolbar.Draw(headerRect, GUIContent.none, false, false, false, false);
            }

            if (Event.current.type == EventType.ContextClick && headerRect.Contains(Event.current.mousePosition))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Reset Columns"), false, () =>
                {
                    Array.Copy(DefaultColumnWidths, _columnWidths, DefaultColumnWidths.Length);
                    Repaint();
                });
                menu.ShowAsContext();
                Event.current.Use();
            }

            float x = headerRect.x;
            for (int column = 0; column < HeaderTitles.Length; column++)
            {
                Rect cellRect = new Rect(x, headerRect.y, _columnWidths[column], headerRect.height);
                _cellContent.text = HeaderTitles[column];
                _cellContent.tooltip = HeaderTooltips[column];
                GUI.Label(cellRect, _cellContent, _headerCellStyle);
                DrawColumnSeparator(cellRect);
                HandleColumnResize(column, cellRect);
                x += _columnWidths[column];
            }
        }

        private void DrawColumnSeparator(Rect cellRect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            EditorGUI.DrawRect(
                new Rect(cellRect.xMax - 1f, cellRect.y + 3f, 1f, cellRect.height - 6f),
                new Color(0.5f, 0.5f, 0.5f, 0.35f));
        }

        private void HandleColumnResize(int column, Rect cellRect)
        {
            Rect handle = new Rect(
                cellRect.xMax - ResizeHandleWidth * 0.5f, cellRect.y, ResizeHandleWidth, cellRect.height);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && handle.Contains(current.mousePosition))
            {
                _dragColumn = column;
                _dragStartX = current.mousePosition.x;
                _dragStartWidth = _columnWidths[column];
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && _dragColumn == column)
            {
                float width = _dragStartWidth + (current.mousePosition.x - _dragStartX);
                _columnWidths[column] = Mathf.Max(MinColumnWidths[column], width);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && _dragColumn == column)
            {
                _dragColumn = -1;
                current.Use();
            }
        }

        private float GetTableWidth()
        {
            float total = 0f;
            for (int index = 0; index < _columnWidths.Length; index++)
            {
                total += _columnWidths[index];
            }

            // Wider than the view enables the horizontal scrollbar instead of clipping.
            return Mathf.Max(EditorGUIUtility.currentViewWidth - 30f, total);
        }

        private void DrawBusRows()
        {
            float tableWidth = GetTableWidth();
            int rowIndex = 0;

            for (int busIndex = 0; busIndex < _buses.Count; busIndex++)
            {
                BusEntry entry = _buses[busIndex];
                if (!MatchesFilter(entry))
                {
                    continue;
                }

                DrawBusRow(entry, tableWidth, rowIndex);
                rowIndex++;

                if (!_expanded.Contains(entry.TypeName))
                {
                    continue;
                }

                for (int subIndex = 0; subIndex < entry.Subs.Count; subIndex++)
                {
                    DrawSubscriberRow(entry.Subs[subIndex], tableWidth, rowIndex);
                    rowIndex++;
                }

                if (entry.OverflowSubscriberCount > 0)
                {
                    Rect rect = RowRect(tableWidth);
                    DrawColoredLabel(rect, entry.OverflowText, _subStyle, TextDim);
                    rowIndex++;
                }
            }

            if (rowIndex == 0)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(_searchFilter)
                        ? "No buses registered on this context yet."
                        : "No bus matches the current filter.",
                    MessageType.Info);
            }
        }

        private void DrawBusRow(BusEntry entry, float tableWidth, int rowIndex)
        {
            Rect rect = RowRect(tableWidth);

            Color background = entry.HasErrors || entry.HasDropped
                ? RowError
                : entry.HasTombstones
                    ? RowWarning
                    : (rowIndex & 1) == 0 ? RowEven : RowOdd;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, background);
            }

            if (HandleRowClick(rect))
            {
                if (!_expanded.Add(entry.TypeName))
                {
                    _expanded.Remove(entry.TypeName);
                }

                Repaint();
            }

            float x = rect.x;
            float nameWidth = _columnWidths[0];

            bool expanded = _expanded.Contains(entry.TypeName);
            _cellContent.text = (expanded ? "▾ " : "▸ ") + entry.DisplayName;
            _cellContent.tooltip = entry.TypeName + "\nClick to "
                + (expanded ? "collapse" : "expand") + " the subscriber list.";
            GUI.Label(new Rect(x, rect.y, nameWidth, rect.height), _cellContent, _rowStyle);
            x += nameWidth;

            DrawNumericCell(rect, ref x, 1, entry.SubsText, Color.white, "Live subscribers.");
            DrawNumericCell(rect, ref x, 2, entry.TombText,
                entry.HasTombstones ? TextWarning : Color.white, HeaderTooltips[2]);
            DrawNumericCell(rect, ref x, 3, entry.PublishText, Color.white, HeaderTooltips[3]);
            DrawNumericCell(rect, ref x, 4, entry.RateText, Color.white, HeaderTooltips[4]);
            DrawNumericCell(rect, ref x, 5, entry.ErrorText,
                entry.HasErrors ? TextError : Color.white, HeaderTooltips[5]);
            DrawNumericCell(rect, ref x, 6, entry.PeakText, Color.white, HeaderTooltips[6]);
            DrawNumericCell(rect, ref x, 7, entry.CapText, Color.white, HeaderTooltips[7]);
            DrawNumericCell(rect, ref x, 8, entry.DepthText, Color.white, HeaderTooltips[8]);
        }

        private void DrawSubscriberRow(in SubRow sub, float tableWidth, int rowIndex)
        {
            Rect rect = RowRect(tableWidth);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, (rowIndex & 1) == 0 ? RowEven : RowOdd);
            }

            float x = rect.x + 18f;
            float nameWidth = _columnWidths[0] - 18f;

            Color color = sub.IsDeadTarget ? TextError : TextDim;
            DrawColoredLabel(new Rect(x, rect.y, nameWidth, rect.height), sub.MethodText, _subStyle, color);
            x += nameWidth;

            DrawColoredLabel(
                new Rect(x, rect.y, _columnWidths[3] + _columnWidths[4], rect.height),
                sub.TargetText, _subStyle, color);
        }

        private static Rect RowRect(float tableWidth)
        {
            return GUILayoutUtility.GetRect(tableWidth, tableWidth, RowHeight, RowHeight, GUIStyle.none);
        }

        private void DrawNumericCell(Rect rowRect, ref float x, int column, string text, Color color, string tooltip)
        {
            _cellContent.text = text;
            _cellContent.tooltip = tooltip;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(x, rowRect.y, _columnWidths[column], rowRect.height), _cellContent, _numericStyle);
            GUI.color = previous;
            x += _columnWidths[column];
        }

        private void DrawColoredLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            _cellContent.text = text;
            _cellContent.tooltip = null;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(rect, _cellContent, style);
            GUI.color = previous;
        }

        private static bool HandleRowClick(Rect rect)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                current.Use();
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ summary tab

        private void DrawSummaryTab()
        {
            EventBusDiagnosticsSnapshot s = _aggregate;

            EditorGUILayout.LabelField("Aggregate", EditorStyles.boldLabel);
            DrawSummaryRow("Active buses", s.ActiveBusCount.ToString());
            DrawSummaryRow("Live subscriptions", s.SubscriptionCount.ToString());
            DrawSummaryRow("Peak subscriptions (max across buses)", s.PeakSubscriptionCount.ToString());
            DrawSummaryRow("Publishes (total)", s.PublishCount.ToString());
            DrawSummaryRow("Publish rate", _pillRateText);
            DrawSummaryRow("Scopes", s.ScopeCount.ToString());
            GUILayout.Space(6f);

            EditorGUILayout.LabelField("Health", EditorStyles.boldLabel);
            DrawSummaryRow("Tombstones", s.TombstoneCount.ToString(),
                s.TombstoneCount > 0 ? (Color?)TextWarning : null);
            DrawSummaryRow("Subscriber errors", s.SubscriberErrorCount.ToString(),
                s.SubscriberErrorCount > 0 ? (Color?)TextError : null);
            DrawSummaryRow("Dropped (re-entrancy ceiling)", s.DroppedReentrantCount.ToString(),
                s.DroppedReentrantCount > 0 ? (Color?)TextError : null);
            DrawSummaryRow("Destroyed subscriber targets", _deadSubscriberCount.ToString(),
                _deadSubscriberCount > 0 ? (Color?)TextError : null);
            GUILayout.Space(6f);

            EditorGUILayout.LabelField("Reading the numbers", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Peak subscriptions is the high-water mark across all buses; use it to size "
                + "initialCapacity so the subscribe path never grows an array. Tombstones are dead "
                + "slots awaiting automatic compaction; a steadily climbing value means subscribers "
                + "are churning and should be gated with an activity flag instead.",
                MessageType.None);
            EditorGUILayout.HelpBox(
                "Dropped publishes are a recursive publish chain hitting the depth ceiling, not a "
                + "capacity problem. Subscriber errors are faults routed by the configured error "
                + "policy; destroyed targets are subscriptions that were never released — the leak "
                + "this window exists to catch.",
                MessageType.None);
        }

        private void DrawSummaryRow(string label, string value, Color? valueColor = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, _summaryLabelStyle, GUILayout.Width(260f));
                Color previous = GUI.color;
                if (valueColor.HasValue)
                {
                    GUI.color = valueColor.Value;
                }

                GUILayout.Label(value, _summaryValueStyle, GUILayout.ExpandWidth(false));
                GUI.color = previous;
                GUILayout.FlexibleSpace();
            }
        }

        // ------------------------------------------------------------------ snapshot

        private void RefreshSnapshot()
        {
            EventBusContext context = EventBusEditorDiagnostics.Current;
            if (context == null || context.IsDisposed)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double elapsed = _lastSnapshotTime > 0d ? now - _lastSnapshotTime : 0d;
            _lastSnapshotTime = now;

            _aggregate = context.GetDiagnosticsSnapshot();
            _pillBusesText = "Buses " + _aggregate.ActiveBusCount;
            _pillSubsText = "Subs " + _aggregate.SubscriptionCount;
            _pillTombText = "Tomb " + _aggregate.TombstoneCount;
            _pillErrText = "Errors " + _aggregate.SubscriberErrorCount;
            _pillDropText = "Dropped " + _aggregate.DroppedReentrantCount;

            IReadOnlyList<IEventBusDiagnostics> buses = context.GetRegisteredBuses();

            _deadSubscriberCount = 0;
            long totalRateCount = 0;

            for (int index = 0; index < buses.Count; index++)
            {
                IEventBusDiagnostics bus = buses[index];
                EventBusSnapshot snapshot = bus.GetSnapshot();
                BusEntry entry = GetOrCreateEntry(index, bus.EventTypeName);

                // Per-bus publish rate from the delta between snapshots.
                _previousPublishes.TryGetValue(entry.TypeName, out long previousPublish);
                _previousPublishes[entry.TypeName] = snapshot.PublishCount;
                long delta = snapshot.PublishCount - previousPublish;
                totalRateCount += delta;

                entry.RateText = elapsed > 0d ? FormatRate(delta / elapsed) : "-";
                entry.SubsText = snapshot.SubscriptionCount.ToString();
                entry.TombText = snapshot.TombstoneCount.ToString();
                entry.PublishText = snapshot.PublishCount.ToString();
                entry.ErrorText = snapshot.SubscriberErrorCount.ToString();
                entry.PeakText = snapshot.PeakSubscriptionCount.ToString();
                entry.CapText = snapshot.Capacity.ToString();
                entry.DepthText = snapshot.DispatchDepth.ToString();
                entry.DroppedText = snapshot.DroppedReentrantCount.ToString();
                entry.HasTombstones = snapshot.TombstoneCount > 0;
                entry.HasErrors = snapshot.SubscriberErrorCount > 0;
                entry.HasDropped = snapshot.DroppedReentrantCount > 0;

                RefreshSubscribers(bus, entry);
            }

            _pillRateText = elapsed > 0d ? FormatRate(totalRateCount / elapsed) + "/s" : "-";
            _pillDeadText = "Dead " + _deadSubscriberCount;
        }

        private BusEntry GetOrCreateEntry(int index, string typeName)
        {
            if (index < _buses.Count && _buses[index].TypeName == typeName)
            {
                return _buses[index];
            }

            var entry = new BusEntry { TypeName = typeName };
            int lastDot = typeName.LastIndexOf('.');
            entry.DisplayName = lastDot >= 0 && lastDot < typeName.Length - 1
                ? typeName.Substring(lastDot + 1)
                : typeName;
            if (index < _buses.Count)
            {
                _buses[index] = entry;
            }
            else
            {
                _buses.Add(entry);
            }

            return entry;
        }

        private void RefreshSubscribers(IEventBusDiagnostics bus, BusEntry entry)
        {
            entry.Subs.Clear();
            entry.HasDeadSubscribers = false;
            entry.OverflowSubscriberCount = 0;

            if (!(bus is IEventBusSubscriberInspection inspection))
            {
                return;
            }

            _subscriberScratch.Clear();
            inspection.CopySubscribersTo(_subscriberScratch);

            int rowCount = Math.Min(_subscriberScratch.Count, MaxSubscriberRowsPerBus);
            for (int index = 0; index < rowCount; index++)
            {
                EventSubscriberInfo info = _subscriberScratch[index];

                string targetText;
                bool dead = false;
                if (info.Target == null)
                {
                    targetText = "(static)";
                }
                else if (info.Target is UnityEngine.Object unityTarget && !unityTarget)
                {
                    // Unity fake-null: a destroyed object still compares non-null against null.
                    targetText = info.Target.GetType().Name + "  DESTROYED";
                    dead = true;
                    _deadSubscriberCount++;
                    entry.HasDeadSubscribers = true;
                }
                else
                {
                    targetText = info.Target.GetType().Name;
                }

                entry.Subs.Add(new SubRow
                {
                    MethodText = "↳ " + info.MethodName + "  (" + info.DeclaringTypeName + ")",
                    TargetText = targetText,
                    IsDeadTarget = dead,
                });
            }

            entry.OverflowSubscriberCount = _subscriberScratch.Count - rowCount;
            if (entry.OverflowSubscriberCount > 0)
            {
                entry.OverflowText = "    + " + entry.OverflowSubscriberCount
                    + " more subscribers (row cap " + MaxSubscriberRowsPerBus + "; counts stay exact)";
            }
        }

        private bool MatchesFilter(BusEntry entry)
        {
            return string.IsNullOrEmpty(_searchFilter)
                || entry.DisplayName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.TypeName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ copy

        private string BuildTsv()
        {
            StringBuilder builder = _copyBuilder.Clear();
            builder.AppendLine("EventType\tSubs\tTombstones\tPublishes\tRate/s\tErrors\tDropped\tPeak\tCapacity\tDepth");

            for (int index = 0; index < _buses.Count; index++)
            {
                BusEntry entry = _buses[index];
                if (!MatchesFilter(entry))
                {
                    continue;
                }

                builder.Append(entry.TypeName).Append('\t')
                    .Append(entry.SubsText).Append('\t')
                    .Append(entry.TombText).Append('\t')
                    .Append(entry.PublishText).Append('\t')
                    .Append(entry.RateText).Append('\t')
                    .Append(entry.ErrorText).Append('\t')
                    .Append(entry.DroppedText).Append('\t')
                    .Append(entry.PeakText).Append('\t')
                    .Append(entry.CapText).Append('\t')
                    .Append(entry.DepthText).Append('\n');
            }

            return builder.ToString();
        }

        private string BuildJson()
        {
            StringBuilder builder = _copyBuilder.Clear();
            builder.Append('[');

            bool first = true;
            for (int index = 0; index < _buses.Count; index++)
            {
                BusEntry entry = _buses[index];
                if (!MatchesFilter(entry))
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                builder.Append("{\"eventType\":\"").Append(EscapeJson(entry.TypeName))
                    .Append("\",\"subs\":").Append(entry.SubsText)
                    .Append(",\"tombstones\":").Append(entry.TombText)
                    .Append(",\"publishes\":").Append(entry.PublishText)
                    .Append(",\"errors\":").Append(entry.ErrorText)
                    .Append(",\"dropped\":").Append(entry.DroppedText)
                    .Append(",\"peak\":").Append(entry.PeakText)
                    .Append(",\"capacity\":").Append(entry.CapText)
                    .Append(",\"depth\":").Append(entry.DepthText)
                    .Append('}');
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatRate(double ratePerSecond)
        {
            if (ratePerSecond >= 1000d)
            {
                return (ratePerSecond / 1000d).ToString("F1") + "k";
            }

            return ratePerSecond >= 100d
                ? ratePerSecond.ToString("F0")
                : ratePerSecond.ToString("F1");
        }
    }
}
