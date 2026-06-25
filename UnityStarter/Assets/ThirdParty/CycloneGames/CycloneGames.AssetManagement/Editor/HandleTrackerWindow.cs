#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Editor
{
    public class HandleTrackerWindow : EditorWindow
    {
        private const float ROW_HEIGHT = 20f;
        private const float HEADER_HEIGHT = 22f;
        private const float STACK_TRACE_HEIGHT = 132f;
        private const float RESIZE_HANDLE_WIDTH = 7f;
        private const float MAX_COLUMN_WIDTH = 1600f;
        private const string MISSING_TEXT = "-";

        private const int COL_ID = 0;
        private const int COL_PACKAGE = 1;
        private const int COL_DESCRIPTION = 2;
        private const int COL_LOCATION = 3;
        private const int COL_TAG = 4;
        private const int COL_OWNER = 5;
        private const int COL_STATUS = 6;
        private const int COL_REGISTERED = 7;
        private const int COL_DURATION = 8;

        private readonly List<HandleTracker.HandleInfo> _snapshot = new List<HandleTracker.HandleInfo>(256);
        private readonly List<HandleRowView> _views = new List<HandleRowView>(256);
        private readonly List<int> _filteredIndices = new List<int>(256);

        private readonly HashSet<string> _idleCacheLocations = new HashSet<string>();
        private readonly Dictionary<string, (string Tag, string Owner)> _tagOwnerMap
            = new Dictionary<string, (string, string)>(256, StringComparer.Ordinal);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _diagActive
            = new List<AssetCacheService.CacheDiagnosticEntry>(512);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _diagTrial
            = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _diagMain
            = new List<AssetCacheService.CacheDiagnosticEntry>(256);

        private string _searchFilter = string.Empty;
        private string _lastSearchFilter = "\0";
        private bool _displayDirty = true;
        private Vector2 _scrollPos;
        private double _nextRepaint;
        private bool _hasSnapshot;

        private string _totalText = "  Total: 0";
        private string _filteredText = string.Empty;
        private string _normalPill = "Normal: 0";
        private string _cachedPill = "Cached (idle pool): 0";
        private string _persistentPill = string.Empty;
        private string _leakPill = string.Empty;
        private int _persistentCount;
        private int _leakCount;
        private readonly GUIContent _cell = new GUIContent();
        private readonly StringBuilder _copyBuilder = new StringBuilder(4096);

        private TableColumn[] _columns;
        private int _resizingColumnIndex = -1;
        private float _resizeStartMouseX;
        private float _resizeStartWidth;
        private readonly HashSet<int> _selectedHandleIds = new HashSet<int>();
        private readonly List<int> _handleSelectionPruneList = new List<int>(32);
        private int _lastSelectedHandleVisibleIndex = -1;
        private string _selectedHandleText = string.Empty;

        private GUILayoutOption[] _pillOpts;
        private bool _layoutOptionsBuilt;

        private const string LeakTooltip =
            "Handle alive >5 min and not found in any AssetCacheService idle pool.\n" +
            "This may indicate a missing Dispose() call.\nIf this is intentional, right-click and choose Mark Persistent.";

        private const string CachedTooltip =
            "Handle is long-lived because AssetCacheService is intentionally keeping this\n" +
            "asset in its Trial/Main idle pool for fast re-use. Not a leak.";

        private const string PersistentTooltip =
            "Marked persistent (intentionally long-lived, e.g. DontDestroyOnLoad, bootstrap, or main-scene assets).\n" +
            "Excluded from leak heuristics. Right-click to unmark.";

        private GUIStyle _monoStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _numericStyle;
        private GUIStyle _headerCellStyle;
        private GUIStyle _boxStyle;
        private GUIStyle _pillStyle;
        private bool _stylesBuilt;

        private static bool IsPro => EditorGUIUtility.isProSkin;
        private static Color RowEven => IsPro ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.86f, 0.86f, 0.86f);
        private static Color RowOdd => IsPro ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.80f, 0.80f, 0.80f);
        private static Color RowSelected => IsPro ? new Color(0.22f, 0.34f, 0.48f, 1f) : new Color(0.68f, 0.82f, 1.0f, 1f);
        private static Color LeakRowBg => IsPro ? new Color(0.38f, 0.12f, 0.10f) : new Color(0.98f, 0.80f, 0.78f);
        private static readonly Color LeakTextColor = new Color(1.0f, 0.35f, 0.25f);
        private static Color CachedRowBg => IsPro ? new Color(0.16f, 0.22f, 0.30f) : new Color(0.80f, 0.88f, 0.98f);
        private static readonly Color CachedTextColor = new Color(0.4f, 0.75f, 1.0f);
        private static Color PersistentRowBg => IsPro ? new Color(0.12f, 0.26f, 0.24f) : new Color(0.80f, 0.94f, 0.90f);
        private static readonly Color PersistentTextColor = new Color(0.35f, 0.85f, 0.70f);
        private static Color DimColor => IsPro ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color NormalPillColor = new Color(0.3f, 0.75f, 0.3f);
        private static Color SeparatorColor => IsPro ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(0.62f, 0.62f, 0.62f, 1f);

        [MenuItem("Tools/CycloneGames/AssetManagement/Asset Handle Tracker")]
        public static void ShowWindow()
        {
            var window = GetWindow<HandleTrackerWindow>("Handle Tracker");
            window.minSize = new Vector2(760, 360);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            RefreshSnapshot();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!HandleTracker.Enabled)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup >= _nextRepaint)
            {
                _nextRepaint = EditorApplication.timeSinceStartup + 0.1;
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

            _monoStyle = new GUIStyle(EditorStyles.label)
            {
                font = EditorStyles.miniFont,
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft
            };

            _rowStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft
            };

            _numericStyle = new GUIStyle(_rowStyle)
            {
                alignment = TextAnchor.MiddleRight
            };

            _headerCellStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft
            };

            _boxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 6, 4, 4)
            };

            _pillStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 1, 1)
            };
        }

        private void OnGUI()
        {
            BuildStyles();
            DrawTopControls();

            if (!HandleTracker.Enabled)
            {
                DrawPlaceholder("Tracking is disabled. Enable it to see active handles.", MessageType.Info);
                return;
            }

            if (!_hasSnapshot)
            {
                RefreshSnapshot();
            }

            if (_searchFilter != _lastSearchFilter)
            {
                _lastSearchFilter = _searchFilter;
                _displayDirty = true;
            }

            if (_displayDirty)
            {
                RebuildDisplay();
            }

            DrawStatsBar();

            if (_filteredIndices.Count == 0)
            {
                GUILayout.Space(16f);
                EditorGUILayout.LabelField(
                    _snapshot.Count == 0 ? "No active handles." : "No handles match the current filter.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, true, true);
            DrawTableHeader();
            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                DrawRow(_views[_filteredIndices[i]], i);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTopControls()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Scenes", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                {
                    SceneTrackerWindow.ShowWindow();
                }

                if (GUILayout.Button("Governance", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                {
                    AssetRuntimeGovernanceWindow.ShowWindow();
                }

                GUILayout.Space(8f);

                bool wasEnabled = HandleTracker.Enabled;
                HandleTracker.Enabled = GUILayout.Toggle(wasEnabled, "  Enable Tracking", EditorStyles.toolbarButton, GUILayout.Width(130f));
                HandleTracker.EnableStackTrace = GUILayout.Toggle(HandleTracker.EnableStackTrace, "  Stack Traces (slow)", EditorStyles.toolbarButton, GUILayout.Width(130f));

                GUILayout.Space(8f);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    RefreshSnapshot();
                }

                if (HandleTracker.HasPersistentEntries && GUILayout.Button("Clear Persistent", EditorStyles.toolbarButton, GUILayout.Width(104f)))
                {
                    HandleTracker.ClearPersistent();
                    RefreshSnapshot();
                    Repaint();
                }

                GUILayout.FlexibleSpace();

                if (_selectedHandleIds.Count > 0)
                {
                    GUILayout.Label(_selectedHandleText, EditorStyles.miniLabel);
                    if (GUILayout.Button("Copy Selected", EditorStyles.toolbarButton, GUILayout.Width(94f)))
                    {
                        CopyToClipboard(BuildSelectedHandleRowsTsv());
                    }
                }

                if (GUILayout.Button("Copy Visible", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                {
                    CopyToClipboard(BuildVisibleHandleRowsTsv());
                }

                if (GUILayout.Button("Reset Columns", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                {
                    ResetColumns();
                }

                GUILayout.Space(8f);
                GUILayout.Label("Filter:", GUILayout.Width(36f));
                _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200f));
                if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                {
                    _searchFilter = string.Empty;
                }
            }
        }

        private void DrawStatsBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(_totalText, EditorStyles.boldLabel);
                if (_filteredText.Length > 0)
                {
                    GUILayout.Label(_filteredText, EditorStyles.miniLabel);
                }

                GUILayout.Space(8f);
                DrawPill(_normalPill, NormalPillColor);
                GUILayout.Space(4f);
                DrawPill(_cachedPill, CachedTextColor);
                GUILayout.Space(4f);

                if (_persistentCount > 0)
                {
                    DrawPill(_persistentPill, PersistentTextColor);
                    GUILayout.Space(4f);
                }

                if (_leakCount > 0)
                {
                    DrawPill(_leakPill, LeakTextColor);
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label("Right-click rows or headers for copy and table options  ", EditorStyles.miniLabel);
            }
        }

        private enum HandleStatus
        {
            Normal,
            Cached,
            Leaked,
            Persistent
        }

        private HandleStatus ClassifyHandle(HandleTracker.HandleInfo info, DateTime nowUtc)
        {
            double lifeSeconds = (nowUtc - info.RegistrationTime).TotalSeconds;
            if (lifeSeconds < 300.0)
            {
                return HandleStatus.Normal;
            }

            if (!string.IsNullOrEmpty(info.Description) &&
                (info.Description.StartsWith("SceneAsync", StringComparison.Ordinal) ||
                 info.Description.StartsWith("SceneSync", StringComparison.Ordinal)))
            {
                return HandleStatus.Normal;
            }

            string location = ExtractLocation(info.Description);
            if (location != null && _idleCacheLocations.Contains(location))
            {
                return HandleStatus.Cached;
            }

            if (location != null && HandleTracker.IsPersistent(location))
            {
                return HandleStatus.Persistent;
            }

            return HandleStatus.Leaked;
        }

        private static string ExtractLocation(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return null;
            }

            int colon = description.IndexOf(" : ", StringComparison.Ordinal);
            return colon >= 0 ? description.Substring(colon + 3).Trim() : null;
        }

        private void DrawTableHeader()
        {
            EnsureColumns();

            float tableWidth = GetTableWidth();
            Rect headerRect = GUILayoutUtility.GetRect(tableWidth, tableWidth, HEADER_HEIGHT, HEADER_HEIGHT, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.toolbar.Draw(headerRect, GUIContent.none, false, false, false, false);
            }

            HandleHeaderContextMenu(headerRect);

            float x = headerRect.x;
            for (int i = 0; i < _columns.Length; i++)
            {
                Rect cellRect = new Rect(x, headerRect.y, _columns[i].Width, headerRect.height);
                _cell.text = _columns[i].Header;
                _cell.tooltip = _columns[i].Tooltip;
                GUI.Label(cellRect, _cell, _headerCellStyle);
                DrawColumnSeparator(cellRect);
                HandleColumnResize(i, cellRect);
                x += _columns[i].Width;
            }
        }

        private void DrawRow(in HandleRowView view, int rowIndex)
        {
            EnsureColumns();

            Color rowBg = _selectedHandleIds.Contains(view.Id)
                ? RowSelected
                : view.Status == (byte)HandleStatus.Leaked
                    ? LeakRowBg
                    : view.Status == (byte)HandleStatus.Persistent
                        ? PersistentRowBg
                        : view.Status == (byte)HandleStatus.Cached
                            ? CachedRowBg
                            : rowIndex % 2 == 0 ? RowEven : RowOdd;

            float tableWidth = GetTableWidth();
            Rect rowRect = GUILayoutUtility.GetRect(tableWidth, tableWidth, ROW_HEIGHT, ROW_HEIGHT, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rowRect, rowBg);
            }

            float x = rowRect.x;
            DrawTextCell(NextCell(ref x, rowRect, COL_ID), view.IdText, _numericStyle, Color.white, "Handle id");
            DrawTextCell(NextCell(ref x, rowRect, COL_PACKAGE), view.Package, _rowStyle, Color.white, view.Package);
            DrawTextCell(NextCell(ref x, rowRect, COL_DESCRIPTION), view.Description, _monoStyle, Color.white, view.Description);
            DrawTextCell(NextCell(ref x, rowRect, COL_LOCATION), string.IsNullOrEmpty(view.Location) ? MISSING_TEXT : view.Location, _monoStyle, string.IsNullOrEmpty(view.Location) ? DimColor : Color.white, view.Location);
            DrawTextCell(NextCell(ref x, rowRect, COL_TAG), view.HasTag ? view.Tag : MISSING_TEXT, _rowStyle, view.HasTag ? new Color(0.6f, 0.9f, 0.6f) : DimColor, view.Tag);
            DrawTextCell(NextCell(ref x, rowRect, COL_OWNER), view.HasOwner ? view.Owner : MISSING_TEXT, _rowStyle, view.HasOwner ? new Color(0.85f, 0.75f, 1.0f) : DimColor, view.Owner);
            DrawTextCell(NextCell(ref x, rowRect, COL_STATUS), view.StatusText, _rowStyle, GetStatusColor(view.Status), view.StatusTooltip);
            DrawTextCell(NextCell(ref x, rowRect, COL_REGISTERED), view.RegisteredText, _rowStyle, Color.white, null);
            DrawTextCell(NextCell(ref x, rowRect, COL_DURATION), view.DurationText, _numericStyle, view.Status == (byte)HandleStatus.Leaked ? LeakTextColor : Color.white, null);

            HandleRowInput(rowRect, view, rowIndex);

            if (!string.IsNullOrEmpty(view.StackTrace) && _expandedIds.Contains(view.Id))
            {
                DrawExpandedStackTrace(tableWidth, view.StackTrace);
            }
        }

        private void DrawTextCell(Rect rect, string text, GUIStyle style, Color color, string tooltip)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            _cell.text = string.IsNullOrEmpty(text) ? MISSING_TEXT : text;
            _cell.tooltip = tooltip;
            GUI.Label(rect, _cell, style);
            GUI.contentColor = previous;
        }

        private Rect NextCell(ref float x, Rect rowRect, int columnIndex)
        {
            Rect rect = new Rect(x, rowRect.y, _columns[columnIndex].Width, rowRect.height);
            x += _columns[columnIndex].Width;
            return rect;
        }

        private void DrawExpandedStackTrace(float tableWidth, string stackTrace)
        {
            Rect boxRect = GUILayoutUtility.GetRect(tableWidth, tableWidth, STACK_TRACE_HEIGHT, STACK_TRACE_HEIGHT, GUIStyle.none);
            GUI.Box(boxRect, GUIContent.none, _boxStyle);

            Rect titleRect = new Rect(boxRect.x + 8f, boxRect.y + 4f, boxRect.width - 16f, 18f);
            EditorGUI.LabelField(titleRect, "Stack Trace", EditorStyles.boldLabel);

            Rect traceRect = new Rect(boxRect.x + 8f, boxRect.y + 24f, boxRect.width - 16f, boxRect.height - 30f);
            EditorGUI.SelectableLabel(traceRect, stackTrace, _monoStyle);
        }

        private readonly HashSet<int> _expandedIds = new HashSet<int>();

        private void RebuildIdleLocationSet()
        {
            _idleCacheLocations.Clear();
            _tagOwnerMap.Clear();

            var instances = AssetCacheService.GlobalInstances;
            if (instances == null)
            {
                return;
            }

            for (int i = 0; i < instances.Count; i++)
            {
                _diagActive.Clear();
                _diagTrial.Clear();
                _diagMain.Clear();
                instances[i].GetDiagnostics(_diagActive, _diagTrial, _diagMain);

                for (int j = 0; j < _diagActive.Count; j++)
                {
                    AddToTagOwnerMap(_diagActive[j]);
                }

                for (int j = 0; j < _diagTrial.Count; j++)
                {
                    AddToTagOwnerMap(_diagTrial[j]);
                }

                for (int j = 0; j < _diagMain.Count; j++)
                {
                    AddToTagOwnerMap(_diagMain[j]);
                }

                for (int j = 0; j < _diagTrial.Count; j++)
                {
                    _idleCacheLocations.Add(_diagTrial[j].Location);
                }

                for (int j = 0; j < _diagMain.Count; j++)
                {
                    _idleCacheLocations.Add(_diagMain[j].Location);
                }
            }
        }

        private void AddToTagOwnerMap(AssetCacheService.CacheDiagnosticEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Location))
            {
                return;
            }

            if (!_tagOwnerMap.TryGetValue(entry.Location, out var existing)
                || (string.IsNullOrEmpty(existing.Tag) && !string.IsNullOrEmpty(entry.Tag))
                || (string.IsNullOrEmpty(existing.Owner) && !string.IsNullOrEmpty(entry.Owner)))
            {
                _tagOwnerMap[entry.Location] = (entry.Tag, entry.Owner);
            }
        }

        private void RefreshSnapshot()
        {
            RebuildIdleLocationSet();
            _snapshot.Clear();
            _views.Clear();

            var active = HandleTracker.GetActiveHandles();
            DateTime nowUtc = DateTime.UtcNow;
            if (active != null)
            {
                for (int i = 0; i < active.Count; i++)
                {
                    _snapshot.Add(active[i]);
                    _views.Add(BuildView(active[i], nowUtc));
                }
            }

            PruneHandleSelection();
            _displayDirty = true;
            _hasSnapshot = true;
        }

        private HandleRowView BuildView(HandleTracker.HandleInfo info, DateTime nowUtc)
        {
            byte status = (byte)ClassifyHandle(info, nowUtc);
            double lifeSeconds = (nowUtc - info.RegistrationTime).TotalSeconds;
            string location = ExtractLocation(info.Description);
            _tagOwnerMap.TryGetValue(location ?? string.Empty, out var tagOwner);

            return new HandleRowView
            {
                Id = info.Id,
                IdText = info.Id.ToString(),
                Package = info.PackageName ?? MISSING_TEXT,
                Description = info.Description ?? string.Empty,
                Location = location,
                Tag = tagOwner.Tag,
                Owner = tagOwner.Owner,
                HasTag = !string.IsNullOrEmpty(tagOwner.Tag),
                HasOwner = !string.IsNullOrEmpty(tagOwner.Owner),
                Status = status,
                StatusText = GetStatusText(status),
                StatusTooltip = GetStatusTooltip(status),
                RegisteredText = info.RegistrationTime.ToLocalTime().ToString("HH:mm:ss"),
                DurationText = FormatDuration(lifeSeconds),
                StackTrace = info.StackTrace
            };
        }

        private void RebuildDisplay()
        {
            _filteredIndices.Clear();
            bool hasFilter = _searchFilter.Length > 0;
            int normal = 0;
            int cached = 0;
            int leaked = 0;
            int persistent = 0;

            for (int i = 0; i < _views.Count; i++)
            {
                if (hasFilter && !MatchesFilter(_views[i]))
                {
                    continue;
                }

                _filteredIndices.Add(i);
                byte status = _views[i].Status;
                if (status == (byte)HandleStatus.Leaked)
                {
                    leaked++;
                }
                else if (status == (byte)HandleStatus.Cached)
                {
                    cached++;
                }
                else if (status == (byte)HandleStatus.Persistent)
                {
                    persistent++;
                }
                else
                {
                    normal++;
                }
            }

            _leakCount = leaked;
            _persistentCount = persistent;
            _totalText = "  Total: " + _snapshot.Count;
            _filteredText = hasFilter ? "  Filtered: " + _filteredIndices.Count : string.Empty;
            _normalPill = "Normal: " + normal;
            _cachedPill = "Cached (idle pool): " + cached;
            _persistentPill = persistent > 0 ? "Persistent: " + persistent : string.Empty;
            _leakPill = leaked > 0 ? "Leak suspect: " + leaked : string.Empty;
            _displayDirty = false;
        }

        private bool MatchesFilter(in HandleRowView view)
        {
            return (view.Description != null && view.Description.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (view.Location != null && view.Location.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (view.Package != null && view.Package.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (view.Tag != null && view.Tag.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (view.Owner != null && view.Owner.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (view.StatusText != null && view.StatusText.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void EnsureLayoutOptions()
        {
            if (_layoutOptionsBuilt)
            {
                return;
            }

            _pillOpts = new[] { GUILayout.ExpandWidth(false) };
            _layoutOptionsBuilt = true;
        }

        private void EnsureColumns()
        {
            if (_columns != null)
            {
                return;
            }

            float availableWidth = Mathf.Max(760f, position.width - 24f);
            float fixedWidth = 54f + 112f + 260f + 96f + 120f + 96f + 84f + 78f;
            float descriptionWidth = Mathf.Max(260f, availableWidth - fixedWidth);

            _columns = new[]
            {
                new TableColumn("ID", 54f, 44f, "Handle id."),
                new TableColumn("Package", 112f, 82f, "Asset package name."),
                new TableColumn("Description", descriptionWidth, 180f, "Original registration description."),
                new TableColumn("Location", 260f, 160f, "Extracted asset location when available."),
                new TableColumn("Tag", 96f, 68f, "Tag resolved from AssetCacheService diagnostics."),
                new TableColumn("Owner", 120f, 86f, "Owner resolved from AssetCacheService diagnostics."),
                new TableColumn("Status", 96f, 72f, "Leak/cache/persistent classification."),
                new TableColumn("Registered", 84f, 72f, "Local registration time."),
                new TableColumn("Duration", 78f, 64f, "Current handle lifetime.")
            };
        }

        private void ResetColumns()
        {
            _columns = null;
            _resizingColumnIndex = -1;
            Repaint();
        }

        private float GetTableWidth()
        {
            EnsureColumns();

            float width = 0f;
            for (int i = 0; i < _columns.Length; i++)
            {
                width += _columns[i].Width;
            }

            return width;
        }

        private void DrawColumnSeparator(Rect cellRect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            EditorGUI.DrawRect(new Rect(cellRect.xMax - 1f, cellRect.y + 3f, 1f, cellRect.height - 6f), SeparatorColor);
        }

        private void HandleColumnResize(int columnIndex, Rect cellRect)
        {
            Rect handleRect = new Rect(cellRect.xMax - RESIZE_HANDLE_WIDTH * 0.5f, cellRect.y, RESIZE_HANDLE_WIDTH, cellRect.height);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event evt = Event.current;

            switch (evt.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && handleRect.Contains(evt.mousePosition))
                    {
                        _resizingColumnIndex = columnIndex;
                        _resizeStartMouseX = evt.mousePosition.x;
                        _resizeStartWidth = _columns[columnIndex].Width;
                        GUIUtility.hotControl = controlId;
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId && _resizingColumnIndex == columnIndex)
                    {
                        float delta = evt.mousePosition.x - _resizeStartMouseX;
                        _columns[columnIndex].Width = Mathf.Clamp(_resizeStartWidth + delta, _columns[columnIndex].MinWidth, MAX_COLUMN_WIDTH);
                        Repaint();
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && _resizingColumnIndex == columnIndex)
                    {
                        GUIUtility.hotControl = 0;
                        _resizingColumnIndex = -1;
                        evt.Use();
                    }
                    break;
            }
        }

        private void HandleHeaderContextMenu(Rect headerRect)
        {
            Event evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 1 || !headerRect.Contains(evt.mousePosition))
            {
                return;
            }

            var menu = new GenericMenu();
            if (_selectedHandleIds.Count > 0)
            {
                menu.AddItem(new GUIContent("Copy Selected Rows/As TSV"), false, () => CopyToClipboard(BuildSelectedHandleRowsTsv()));
                menu.AddItem(new GUIContent("Copy Selected Rows/As JSON"), false, () => CopyToClipboard(BuildSelectedHandleRowsJson()));
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent("Copy Visible Rows/As TSV"), false, () => CopyToClipboard(BuildVisibleHandleRowsTsv()));
            menu.AddItem(new GUIContent("Copy Visible Rows/As JSON"), false, () => CopyToClipboard(BuildVisibleHandleRowsJson()));
            menu.AddSeparator(string.Empty);
            if (_selectedHandleIds.Count > 0)
            {
                menu.AddItem(new GUIContent("Clear Selection"), false, () =>
                {
                    ClearHandleSelection();
                    Repaint();
                });
            }

            menu.AddItem(new GUIContent("Reset Column Widths"), false, ResetColumns);
            menu.ShowAsContext();
            evt.Use();
        }

        private void HandleRowInput(Rect rowRect, in HandleRowView view, int visibleIndex)
        {
            Event evt = Event.current;
            if (!rowRect.Contains(evt.mousePosition) || evt.type != EventType.MouseDown)
            {
                return;
            }

            if (evt.button == 0)
            {
                SelectHandleRow(view.Id, visibleIndex, evt);
                Repaint();
                evt.Use();
                return;
            }

            if (evt.button != 1)
            {
                return;
            }

            if (!_selectedHandleIds.Contains(view.Id))
            {
                SelectSingleHandleRow(view.Id, visibleIndex);
            }

            ShowRowContextMenu(view);
            evt.Use();
        }

        private void SelectHandleRow(int id, int visibleIndex, Event evt)
        {
            bool additive = evt.control || evt.command;
            if (evt.shift && _lastSelectedHandleVisibleIndex >= 0)
            {
                if (!additive)
                {
                    _selectedHandleIds.Clear();
                }

                SelectVisibleHandleRange(_lastSelectedHandleVisibleIndex, visibleIndex);
                UpdateHandleSelectionText();
                return;
            }

            if (additive)
            {
                if (!_selectedHandleIds.Add(id))
                {
                    _selectedHandleIds.Remove(id);
                }
            }
            else
            {
                _selectedHandleIds.Clear();
                _selectedHandleIds.Add(id);
            }

            _lastSelectedHandleVisibleIndex = visibleIndex;
            UpdateHandleSelectionText();
        }

        private void SelectSingleHandleRow(int id, int visibleIndex)
        {
            _selectedHandleIds.Clear();
            _selectedHandleIds.Add(id);
            _lastSelectedHandleVisibleIndex = visibleIndex;
            UpdateHandleSelectionText();
        }

        private void SelectVisibleHandleRange(int from, int to)
        {
            int min = Mathf.Min(from, to);
            int max = Mathf.Max(from, to);
            min = Mathf.Clamp(min, 0, _filteredIndices.Count - 1);
            max = Mathf.Clamp(max, 0, _filteredIndices.Count - 1);

            for (int i = min; i <= max; i++)
            {
                _selectedHandleIds.Add(_views[_filteredIndices[i]].Id);
            }
        }

        private void ClearHandleSelection()
        {
            _selectedHandleIds.Clear();
            _lastSelectedHandleVisibleIndex = -1;
            UpdateHandleSelectionText();
        }

        private void PruneHandleSelection()
        {
            if (_selectedHandleIds.Count == 0)
            {
                return;
            }

            _handleSelectionPruneList.Clear();
            foreach (int id in _selectedHandleIds)
            {
                if (!ContainsHandleId(id))
                {
                    _handleSelectionPruneList.Add(id);
                }
            }

            for (int i = 0; i < _handleSelectionPruneList.Count; i++)
            {
                _selectedHandleIds.Remove(_handleSelectionPruneList[i]);
            }

            if (_selectedHandleIds.Count == 0)
            {
                _lastSelectedHandleVisibleIndex = -1;
            }

            UpdateHandleSelectionText();
        }

        private bool ContainsHandleId(int id)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                if (_views[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateHandleSelectionText()
        {
            _selectedHandleText = _selectedHandleIds.Count > 0 ? "Selected: " + _selectedHandleIds.Count : string.Empty;
        }

        private void ShowRowContextMenu(HandleRowView view)
        {
            var menu = new GenericMenu();

            string fullRow = BuildHandleRowDetails(view);
            string rowTsv = BuildHandleRowTsv(view);
            string rowJson = BuildHandleRowJson(view);

            menu.AddItem(new GUIContent("Copy/Full Row"), false, () => CopyToClipboard(fullRow));
            menu.AddItem(new GUIContent("Copy/Row as TSV"), false, () => CopyToClipboard(rowTsv));
            menu.AddItem(new GUIContent("Copy/Row as JSON"), false, () => CopyToClipboard(rowJson));
            menu.AddSeparator("Copy/");
            AddCopyValue(menu, "Copy/ID", view.IdText);
            AddCopyValue(menu, "Copy/Package", view.Package);
            AddCopyValue(menu, "Copy/Description", view.Description);
            AddCopyValue(menu, "Copy/Location", view.Location);
            AddCopyValue(menu, "Copy/Tag", view.Tag);
            AddCopyValue(menu, "Copy/Owner", view.Owner);
            AddCopyValue(menu, "Copy/Status", view.StatusText);
            AddCopyValue(menu, "Copy/Registered Time", view.RegisteredText);
            AddCopyValue(menu, "Copy/Duration", view.DurationText);
            AddCopyValue(menu, "Copy/Stack Trace", view.StackTrace);

            menu.AddSeparator(string.Empty);
            if (_selectedHandleIds.Count > 0)
            {
                menu.AddItem(new GUIContent("Copy Selected Rows/As TSV"), false, () => CopyToClipboard(BuildSelectedHandleRowsTsv()));
                menu.AddItem(new GUIContent("Copy Selected Rows/As JSON"), false, () => CopyToClipboard(BuildSelectedHandleRowsJson()));
                menu.AddItem(new GUIContent("Selection/Clear Selection"), false, () =>
                {
                    ClearHandleSelection();
                    Repaint();
                });
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent("Copy Visible Rows/As TSV"), false, () => CopyToClipboard(BuildVisibleHandleRowsTsv()));
            menu.AddItem(new GUIContent("Copy Visible Rows/As JSON"), false, () => CopyToClipboard(BuildVisibleHandleRowsJson()));

            if (!string.IsNullOrEmpty(view.Location))
            {
                string capturedLocation = view.Location;
                menu.AddSeparator(string.Empty);
                if (HandleTracker.IsPersistent(capturedLocation))
                {
                    menu.AddItem(new GUIContent("Actions/Unmark Persistent"), false, () =>
                    {
                        HandleTracker.UnmarkPersistent(capturedLocation);
                        RefreshSnapshot();
                        Repaint();
                    });
                }
                else
                {
                    menu.AddItem(new GUIContent("Actions/Mark Persistent (ignore leak)"), false, () =>
                    {
                        HandleTracker.MarkPersistent(capturedLocation);
                        RefreshSnapshot();
                        Repaint();
                    });
                }

                if (IsProjectAssetPath(capturedLocation))
                {
                    menu.AddItem(new GUIContent("Actions/Ping Asset"), false, () => PingAsset(capturedLocation));
                }
            }

            if (!string.IsNullOrEmpty(view.StackTrace))
            {
                bool expanded = _expandedIds.Contains(view.Id);
                int capturedId = view.Id;
                menu.AddItem(new GUIContent(expanded ? "Actions/Collapse Stack Trace" : "Actions/Expand Stack Trace"), false, () =>
                {
                    if (_expandedIds.Contains(capturedId))
                    {
                        _expandedIds.Remove(capturedId);
                    }
                    else
                    {
                        _expandedIds.Add(capturedId);
                    }

                    Repaint();
                });
            }

            menu.ShowAsContext();
        }

        private static void AddCopyValue(GenericMenu menu, string path, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                menu.AddDisabledItem(new GUIContent(path));
                return;
            }

            string captured = value;
            menu.AddItem(new GUIContent(path), false, () => CopyToClipboard(captured));
        }

        private string BuildHandleRowDetails(HandleRowView view)
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("Asset Handle Row");
            _copyBuilder.AppendLine("ID: " + view.Id);
            _copyBuilder.AppendLine("Package: " + SafeText(view.Package));
            _copyBuilder.AppendLine("Description: " + SafeText(view.Description));
            _copyBuilder.AppendLine("Location: " + SafeText(view.Location));
            _copyBuilder.AppendLine("Tag: " + SafeText(view.Tag));
            _copyBuilder.AppendLine("Owner: " + SafeText(view.Owner));
            _copyBuilder.AppendLine("Status: " + SafeText(view.StatusText));
            _copyBuilder.AppendLine("Registered: " + SafeText(view.RegisteredText));
            _copyBuilder.AppendLine("Duration: " + SafeText(view.DurationText));
            if (!string.IsNullOrEmpty(view.StackTrace))
            {
                _copyBuilder.AppendLine("Stack Trace:");
                _copyBuilder.AppendLine(view.StackTrace);
            }

            return _copyBuilder.ToString();
        }

        private static string BuildHandleRowTsv(HandleRowView view)
        {
            return view.Id + "\t" +
                SanitizeTsv(view.Package) + "\t" +
                SanitizeTsv(view.Description) + "\t" +
                SanitizeTsv(view.Location) + "\t" +
                SanitizeTsv(view.Tag) + "\t" +
                SanitizeTsv(view.Owner) + "\t" +
                SanitizeTsv(view.StatusText) + "\t" +
                SanitizeTsv(view.RegisteredText) + "\t" +
                SanitizeTsv(view.DurationText);
        }

        private static string BuildHandleRowJson(HandleRowView view)
        {
            var builder = new StringBuilder(256);
            AppendHandleRowJson(builder, view);
            return builder.ToString();
        }

        private string BuildVisibleHandleRowsTsv()
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("ID\tPackage\tDescription\tLocation\tTag\tOwner\tStatus\tRegistered\tDuration");
            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                _copyBuilder.AppendLine(BuildHandleRowTsv(_views[_filteredIndices[i]]));
            }

            return _copyBuilder.ToString();
        }

        private string BuildVisibleHandleRowsJson()
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("[");
            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                if (i > 0)
                {
                    _copyBuilder.AppendLine(",");
                }

                _copyBuilder.Append("  ");
                AppendHandleRowJson(_copyBuilder, _views[_filteredIndices[i]]);
            }

            _copyBuilder.AppendLine();
            _copyBuilder.Append("]");
            return _copyBuilder.ToString();
        }

        private string BuildSelectedHandleRowsTsv()
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("ID\tPackage\tDescription\tLocation\tTag\tOwner\tStatus\tRegistered\tDuration");
            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                var view = _views[_filteredIndices[i]];
                if (_selectedHandleIds.Contains(view.Id))
                {
                    _copyBuilder.AppendLine(BuildHandleRowTsv(view));
                }
            }

            return _copyBuilder.ToString();
        }

        private string BuildSelectedHandleRowsJson()
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("[");
            bool first = true;
            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                var view = _views[_filteredIndices[i]];
                if (!_selectedHandleIds.Contains(view.Id))
                {
                    continue;
                }

                if (!first)
                {
                    _copyBuilder.AppendLine(",");
                }

                _copyBuilder.Append("  ");
                AppendHandleRowJson(_copyBuilder, view);
                first = false;
            }

            _copyBuilder.AppendLine();
            _copyBuilder.Append("]");
            return _copyBuilder.ToString();
        }

        private static void AppendHandleRowJson(StringBuilder builder, HandleRowView view)
        {
            builder.Append('{');
            AppendJsonProperty(builder, "id", view.Id, false);
            AppendJsonProperty(builder, "package", view.Package, true);
            AppendJsonProperty(builder, "description", view.Description, true);
            AppendJsonProperty(builder, "location", view.Location, true);
            AppendJsonProperty(builder, "tag", view.Tag, true);
            AppendJsonProperty(builder, "owner", view.Owner, true);
            AppendJsonProperty(builder, "status", view.StatusText, true);
            AppendJsonProperty(builder, "registered", view.RegisteredText, true);
            AppendJsonProperty(builder, "duration", view.DurationText, true);
            builder.Append('}');
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, string value, bool prependComma)
        {
            if (prependComma)
            {
                builder.Append(',');
            }

            builder.Append('"');
            builder.Append(name);
            builder.Append("\":");
            AppendJsonString(builder, value ?? string.Empty);
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, int value, bool prependComma)
        {
            if (prependComma)
            {
                builder.Append(',');
            }

            builder.Append('"');
            builder.Append(name);
            builder.Append("\":");
            builder.Append(value);
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            builder.Append('"');
        }

        private void DrawPill(string text, Color color)
        {
            EnsureLayoutOptions();
            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, _pillStyle, _pillOpts);
            GUI.contentColor = previous;
        }

        private static string GetStatusText(byte status)
        {
            switch ((HandleStatus)status)
            {
                case HandleStatus.Cached:
                    return "Cached";
                case HandleStatus.Leaked:
                    return "Leak?";
                case HandleStatus.Persistent:
                    return "Persistent";
                default:
                    return "Normal";
            }
        }

        private static string GetStatusTooltip(byte status)
        {
            switch ((HandleStatus)status)
            {
                case HandleStatus.Cached:
                    return CachedTooltip;
                case HandleStatus.Leaked:
                    return LeakTooltip;
                case HandleStatus.Persistent:
                    return PersistentTooltip;
                default:
                    return "Handle is active and below the long-lived threshold.";
            }
        }

        private static Color GetStatusColor(byte status)
        {
            switch ((HandleStatus)status)
            {
                case HandleStatus.Cached:
                    return CachedTextColor;
                case HandleStatus.Leaked:
                    return LeakTextColor;
                case HandleStatus.Persistent:
                    return PersistentTextColor;
                default:
                    return DimColor;
            }
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 60.0)
            {
                return seconds.ToString("F1") + "s";
            }

            if (seconds < 3600.0)
            {
                return (seconds / 60.0).ToString("F1") + "m";
            }

            return (seconds / 3600.0).ToString("F1") + "h";
        }

        private static string SafeText(string value)
        {
            return string.IsNullOrEmpty(value) ? MISSING_TEXT : value;
        }

        private static string SanitizeTsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static void CopyToClipboard(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text ?? string.Empty;
        }

        private static bool IsProjectAssetPath(string location)
        {
            return !string.IsNullOrEmpty(location)
                && (location.StartsWith("Assets/", StringComparison.Ordinal) || location.StartsWith("Packages/", StringComparison.Ordinal));
        }

        private static void PingAsset(string location)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(location);
            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static void DrawPlaceholder(string message, MessageType type)
        {
            GUILayout.Space(40f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(400f)))
                {
                    EditorGUILayout.HelpBox(message, type);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private sealed class TableColumn
        {
            public readonly string Header;
            public readonly float MinWidth;
            public readonly string Tooltip;
            public float Width;

            public TableColumn(string header, float width, float minWidth, string tooltip)
            {
                Header = header;
                Width = width;
                MinWidth = minWidth;
                Tooltip = tooltip;
            }
        }

        private struct HandleRowView
        {
            public int Id;
            public string IdText;
            public string Package;
            public string Description;
            public string Location;
            public string Tag;
            public string Owner;
            public byte Status;
            public string StatusText;
            public string StatusTooltip;
            public string RegisteredText;
            public string DurationText;
            public string StackTrace;
            public bool HasTag;
            public bool HasOwner;
        }
    }
}
#endif
