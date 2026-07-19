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
        private const int MAX_AUTOMATIC_HANDLES = 4_096;
        private const int MAX_AUTOMATIC_CACHE_ROWS_PER_TIER = 4_096;
        private const double AUTO_REFRESH_INTERVAL_SECONDS = 0.5d;

        private const int COL_ID = 0;
        private const int COL_PACKAGE = 1;
        private const int COL_DESCRIPTION = 2;
        private const int COL_LOCATION = 3;
        private const int COL_TAG = 4;
        private const int COL_OWNER = 5;
        private const int COL_STATUS = 6;
        private const int COL_ACTIVE_SINCE = 7;
        private const int COL_DURATION = 8;

        private readonly List<HandleTracker.HandleInfo> _snapshot = new List<HandleTracker.HandleInfo>(256);
        private readonly List<HandleRowView> _views = new List<HandleRowView>(256);
        private readonly List<int> _filteredIndices = new List<int>(256);

        private readonly HashSet<long> _idleCacheHandleIds = new HashSet<long>();
        private readonly Dictionary<long, (string Tag, string Owner)> _tagOwnerMap
            = new Dictionary<long, (string, string)>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _diagActive
            = new List<AssetCacheService.CacheDiagnosticEntry>(512);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _diagTrial
            = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _diagMain
            = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService> _cacheInstances = new List<AssetCacheService>(4);

        private string _searchFilter = string.Empty;
        private string _lastSearchFilter = "\0";
        private bool _displayDirty = true;
        private Vector2 _scrollPos;
        private double _nextRepaint;
        private bool _hasSnapshot;
        private bool _isVisible = true;
        private bool _handleSnapshotTruncated;
        private bool _cacheDiagnosticsTruncated;
        private bool _idleCacheCorrelationIncomplete;
        private int _activeHandleTotal;

        private string _totalText = "  Total: 0";
        private string _filteredText = string.Empty;
        private string _normalPill = "Normal: 0";
        private string _cachedPill = "Cached (idle pool): 0";
        private string _persistentPill = string.Empty;
        private string _leakPill = string.Empty;
        private string _reviewPill = string.Empty;
        private string _registryPill = "Registry: 0 / 0";
        private string _droppedPill = string.Empty;
        private int _persistentCount;
        private int _leakCount;
        private int _reviewCount;
        private long _droppedRegistrationCount;
        private readonly GUIContent _cell = new GUIContent();
        private readonly StringBuilder _copyBuilder = new StringBuilder(4096);

        private TableColumn[] _columns;
        private int _resizingColumnIndex = -1;
        private float _resizeStartMouseX;
        private float _resizeStartWidth;
        private readonly HashSet<long> _selectedHandleIds = new HashSet<long>();
        private readonly List<long> _handleSelectionPruneList = new List<long>(32);
        private int _lastSelectedHandleVisibleIndex = -1;
        private string _selectedHandleText = string.Empty;

        private GUILayoutOption[] _pillOpts;
        private bool _layoutOptionsBuilt;

        private const string LeakTooltip =
            "Handle alive >5 min and not found in any AssetCacheService idle pool.\n" +
            "This may indicate a missing Dispose() call.\nIf this is intentional, right-click and choose Mark Persistent.";

        private const string CachedTooltip =
            "Handle is long-lived because AssetCacheService is intentionally keeping this\n" +
            "asset in its Probation/Protected idle cache for fast reuse. Not a leak.";

        private const string PersistentTooltip =
            "This exact tracked handle is marked intentionally long-lived for the current runtime session.\n" +
            "It is excluded from leak heuristics until unmarked or the subsystem resets.";

        private const string IndeterminateTooltip =
            "The bounded cache snapshot could not establish this long-lived handle's exact cache identity.\n" +
            "The handle is not classified as a leak until a complete cache correlation is available.";

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
        private static readonly Color DroppedPillColor = new Color(1.0f, 0.55f, 0.2f);
        private static readonly Color ReviewTextColor = new Color(1.0f, 0.72f, 0.2f);
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
            _isVisible = true;
            _nextRepaint = 0d;
            EditorApplication.update += OnEditorUpdate;
            if (Application.isPlaying && HandleTracker.Enabled)
                RefreshSnapshot();
            else
                _hasSnapshot = false;
        }

        private void OnDisable()
        {
            _isVisible = false;
            EditorApplication.update -= OnEditorUpdate;
            _cacheInstances.Clear();
        }

        private void OnBecameVisible()
        {
            _isVisible = true;
            _nextRepaint = 0d;
            _hasSnapshot = false;
        }

        private void OnBecameInvisible()
        {
            _isVisible = false;
            _cacheInstances.Clear();
        }

        private void OnEditorUpdate()
        {
            if (!_isVisible || !Application.isPlaying || !HandleTracker.Enabled)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup >= _nextRepaint)
            {
                _nextRepaint = EditorApplication.timeSinceStartup + AUTO_REFRESH_INTERVAL_SECONDS;
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

            if (!Application.isPlaying)
            {
                DrawPlaceholder(
                    "Run the game to inspect handle lifetimes. To observe bootstrap handles, enable HandleTracker in the earliest composition root before creating packages or assets; Edit Mode window state is not runtime configuration.",
                    MessageType.Info);
                return;
            }

            if (!HandleTracker.Enabled)
            {
                DrawPlaceholder(
                    "Tracking is disabled. Enabling it here observes only handles created afterward; enable it in the earliest composition root when complete bootstrap coverage is required.",
                    MessageType.Info);
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

            if (HandleTracker.ObservationIncomplete)
            {
                EditorGUILayout.HelpBox(
                    "Handle tracking started after handles may already have existed, or the live registry was cleared. " +
                    "Counts are exact only for the current observation epoch. Complete bootstrap coverage requires the earliest " +
                    "composition root to enable tracking before package or asset creation.",
                    MessageType.Warning);
            }

            if (_handleSnapshotTruncated)
            {
                EditorGUILayout.HelpBox(
                    $"Handle rows are capped at {MAX_AUTOMATIC_HANDLES:N0}. Registry totals are exact; " +
                    "filtering and status counts apply to the captured sample.",
                    MessageType.Warning);
            }

            if (_cacheDiagnosticsTruncated)
            {
                EditorGUILayout.HelpBox(
                    $"Cache correlation is capped at {MAX_AUTOMATIC_CACHE_ROWS_PER_TIER:N0} rows per tier. " +
                    "Tag, owner, and cached-state metadata are derived from the bounded sample.",
                    MessageType.Warning);
            }

            if (_idleCacheCorrelationIncomplete)
            {
                EditorGUILayout.HelpBox(
                    "Idle-cache correlation is incomplete. Long-lived handles without an exact cache match are " +
                    "shown as Review instead of leak suspects.",
                    MessageType.Warning);
            }

            if (_filteredIndices.Count == 0)
            {
                GUILayout.Space(16f);
                EditorGUILayout.LabelField(
                    _snapshot.Count == 0 ? "No tracked handles in the current observation epoch." : "No handles match the current filter.",
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

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
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
                DrawPill(_registryPill, CachedTextColor);
                GUILayout.Space(4f);
                if (_droppedRegistrationCount > 0L)
                {
                    DrawPill(_droppedPill, DroppedPillColor);
                    GUILayout.Space(4f);
                }

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
                    GUILayout.Space(4f);
                }

                if (_reviewCount > 0)
                {
                    DrawPill(_reviewPill, ReviewTextColor);
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
            Persistent,
            Indeterminate
        }

        private HandleStatus ClassifyHandle(HandleTracker.HandleInfo info, long nowTimestamp)
        {
            double lifeSeconds = HandleTracker.GetActiveDurationSeconds(in info, nowTimestamp);
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

            if (_idleCacheHandleIds.Contains(info.Id))
            {
                return HandleStatus.Cached;
            }

            if (HandleTracker.IsPersistent(info.Id))
            {
                return HandleStatus.Persistent;
            }

            if (_idleCacheCorrelationIncomplete)
            {
                return HandleStatus.Indeterminate;
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
            DrawTextCell(NextCell(ref x, rowRect, COL_ACTIVE_SINCE), view.ActiveSinceText, _rowStyle, Color.white, null);
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

        private readonly HashSet<long> _expandedIds = new HashSet<long>();

        private void RebuildCacheCorrelation()
        {
            _idleCacheHandleIds.Clear();
            _tagOwnerMap.Clear();
            _cacheDiagnosticsTruncated = false;
            _idleCacheCorrelationIncomplete = false;

            AssetCacheService.CopyGlobalInstancesTo(_cacheInstances);

            int activeCaptured = 0;
            int trialCaptured = 0;
            int mainCaptured = 0;

            for (int i = 0; i < _cacheInstances.Count; i++)
            {
                _diagActive.Clear();
                _diagTrial.Clear();
                _diagMain.Clear();
                AssetCacheService.CacheDiagnosticCapture capture = _cacheInstances[i].GetDiagnostics(
                    _diagActive,
                    _diagTrial,
                    _diagMain,
                    Math.Max(0, MAX_AUTOMATIC_CACHE_ROWS_PER_TIER - activeCaptured),
                    Math.Max(0, MAX_AUTOMATIC_CACHE_ROWS_PER_TIER - trialCaptured),
                    Math.Max(0, MAX_AUTOMATIC_CACHE_ROWS_PER_TIER - mainCaptured));
                activeCaptured += _diagActive.Count;
                trialCaptured += _diagTrial.Count;
                mainCaptured += _diagMain.Count;
                _cacheDiagnosticsTruncated |= capture.IsTruncated;
                _idleCacheCorrelationIncomplete |=
                    capture.ProbationCaptured < capture.ProbationTotal ||
                    capture.ProtectedCaptured < capture.ProtectedTotal;

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
                    AddIdleHandleIdentity(_diagTrial[j]);
                }

                for (int j = 0; j < _diagMain.Count; j++)
                {
                    AddIdleHandleIdentity(_diagMain[j]);
                }
            }
        }

        private void AddToTagOwnerMap(AssetCacheService.CacheDiagnosticEntry entry)
        {
            if (entry.HandleId <= 0L)
            {
                return;
            }

            if (!_tagOwnerMap.TryGetValue(entry.HandleId, out var existing)
                || (string.IsNullOrEmpty(existing.Tag) && !string.IsNullOrEmpty(entry.Tag))
                || (string.IsNullOrEmpty(existing.Owner) && !string.IsNullOrEmpty(entry.Owner)))
            {
                _tagOwnerMap[entry.HandleId] = (entry.Tag, entry.Owner);
            }
        }

        private void AddIdleHandleIdentity(AssetCacheService.CacheDiagnosticEntry entry)
        {
            if (entry.HandleId <= 0L)
            {
                _idleCacheCorrelationIncomplete = true;
                return;
            }

            _idleCacheHandleIds.Add(entry.HandleId);
        }

        private void RefreshSnapshot()
        {
            RebuildCacheCorrelation();
            _snapshot.Clear();
            _views.Clear();

            _activeHandleTotal = HandleTracker.CopyActiveHandlesTo(_snapshot, MAX_AUTOMATIC_HANDLES);
            _handleSnapshotTruncated = _snapshot.Count < _activeHandleTotal;
            _droppedRegistrationCount = HandleTracker.DroppedRegistrationCount;
            long nowTimestamp = HandleTracker.GetMonotonicTimestamp();
            for (int i = 0; i < _snapshot.Count; i++)
            {
                _views.Add(BuildView(_snapshot[i], nowTimestamp));
            }

            PruneHandleSelection();
            _displayDirty = true;
            _hasSnapshot = true;
        }

        private HandleRowView BuildView(HandleTracker.HandleInfo info, long nowTimestamp)
        {
            byte status = (byte)ClassifyHandle(info, nowTimestamp);
            double lifeSeconds = HandleTracker.GetActiveDurationSeconds(in info, nowTimestamp);
            string location = ExtractLocation(info.Description);
            _tagOwnerMap.TryGetValue(info.Id, out var tagOwner);

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
                ActiveSinceText = info.ActiveSince.ToLocalTime().ToString("HH:mm:ss"),
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
            int review = 0;

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
                else if (status == (byte)HandleStatus.Indeterminate)
                {
                    review++;
                }
                else
                {
                    normal++;
                }
            }

            _leakCount = leaked;
            _persistentCount = persistent;
            _reviewCount = review;
            _totalText = _handleSnapshotTruncated
                ? "  Captured: " + _snapshot.Count + " / " + _activeHandleTotal
                : "  Total: " + _activeHandleTotal;
            _filteredText = hasFilter ? "  Filtered: " + _filteredIndices.Count : string.Empty;
            _normalPill = "Normal: " + normal;
            _cachedPill = "Cached (idle pool): " + cached;
            _persistentPill = persistent > 0 ? "Persistent: " + persistent : string.Empty;
            _leakPill = leaked > 0 ? "Leak suspect: " + leaked : string.Empty;
            _reviewPill = review > 0 ? "Review: " + review : string.Empty;
            _registryPill = (HandleTracker.ObservationIncomplete ? "Registry (incomplete): " : "Registry: ") +
                _activeHandleTotal + " / " + HandleTracker.Capacity;
            _droppedPill = _droppedRegistrationCount > 0L
                ? "Dropped registrations: " + _droppedRegistrationCount
                : string.Empty;
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
                new TableColumn("Active Since", 84f, 72f, "Local start time of the current active ownership epoch."),
                new TableColumn("Duration", 78f, 64f, "Current active ownership duration.")
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

        private void SelectHandleRow(long id, int visibleIndex, Event evt)
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

        private void SelectSingleHandleRow(long id, int visibleIndex)
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
            foreach (long id in _selectedHandleIds)
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

        private bool ContainsHandleId(long id)
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
            AddCopyValue(menu, "Copy/Active Since", view.ActiveSinceText);
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

            long capturedHandleId = view.Id;
            menu.AddSeparator(string.Empty);
            if (HandleTracker.IsPersistent(capturedHandleId))
            {
                menu.AddItem(new GUIContent("Actions/Unmark Persistent"), false, () =>
                {
                    HandleTracker.UnmarkPersistent(capturedHandleId);
                    RefreshSnapshot();
                    Repaint();
                });
            }
            else
            {
                menu.AddItem(new GUIContent("Actions/Mark Persistent (ignore leak)"), false, () =>
                {
                    HandleTracker.MarkPersistent(capturedHandleId);
                    RefreshSnapshot();
                    Repaint();
                });
            }

            if (!string.IsNullOrEmpty(view.Location))
            {
                string capturedLocation = view.Location;
                if (IsProjectAssetPath(capturedLocation))
                {
                    menu.AddItem(new GUIContent("Actions/Ping Asset"), false, () => PingAsset(capturedLocation));
                }
            }

            if (!string.IsNullOrEmpty(view.StackTrace))
            {
                bool expanded = _expandedIds.Contains(view.Id);
                long capturedId = view.Id;
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
            _copyBuilder.AppendLine("Active Since: " + SafeText(view.ActiveSinceText));
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
                SanitizeTsv(view.ActiveSinceText) + "\t" +
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
            _copyBuilder.AppendLine("ID\tPackage\tDescription\tLocation\tTag\tOwner\tStatus\tActive Since\tDuration");
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
            _copyBuilder.AppendLine("ID\tPackage\tDescription\tLocation\tTag\tOwner\tStatus\tActive Since\tDuration");
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
            AppendJsonProperty(builder, "activeSince", view.ActiveSinceText, true);
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

        private static void AppendJsonProperty(StringBuilder builder, string name, long value, bool prependComma)
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
                case HandleStatus.Indeterminate:
                    return "Review";
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
                case HandleStatus.Indeterminate:
                    return IndeterminateTooltip;
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
                case HandleStatus.Indeterminate:
                    return ReviewTextColor;
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
            public long Id;
            public string IdText;
            public string Package;
            public string Description;
            public string Location;
            public string Tag;
            public string Owner;
            public byte Status;
            public string StatusText;
            public string StatusTooltip;
            public string ActiveSinceText;
            public string DurationText;
            public string StackTrace;
            public bool HasTag;
            public bool HasOwner;
        }
    }
}
#endif
