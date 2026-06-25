#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Editor
{
    public sealed class SceneTrackerWindow : EditorWindow
    {
        private const float ROW_HEIGHT = 20f;
        private const float HEADER_HEIGHT = 22f;
        private const float RESIZE_HANDLE_WIDTH = 7f;
        private const float MAX_COLUMN_WIDTH = 1600f;
        private const string MISSING_TEXT = "-";

        private const int COL_SCENE = 0;
        private const int COL_PROVIDER = 1;
        private const int COL_PACKAGE = 2;
        private const int COL_BUCKET = 3;
        private const int COL_STATE = 4;
        private const int COL_MODE = 5;
        private const int COL_ACTIVATION = 6;
        private const int COL_PROGRESS = 7;
        private const int COL_REFS = 8;
        private const int COL_AGE = 9;
        private const int COL_ERROR = 10;

        private static readonly string[] StateOptions =
        {
            "All",
            "Loading",
            "Waiting",
            "Activated",
            "Unload Pending",
            "Error"
        };

        private readonly List<SceneTracker.SceneInfo> _snapshot = new List<SceneTracker.SceneInfo>(16);
        private readonly List<SceneRowView> _views = new List<SceneRowView>(16);
        private readonly List<int> _filteredIndices = new List<int>(16);
        private readonly HashSet<int> _selectedSceneIds = new HashSet<int>();
        private readonly List<int> _sceneSelectionPruneList = new List<int>(16);
        private readonly string[] _metricValues = new string[6];
        private readonly GUIContent _cell = new GUIContent();
        private readonly StringBuilder _copyBuilder = new StringBuilder(4096);

        private string _search = string.Empty;
        private string _lastSearch = string.Empty;
        private int _stateFilter;
        private int _lastStateFilter = -1;
        private bool _displayDirty = true;
        private Vector2 _scroll;
        private double _nextRepaint;
        private bool _hasSnapshot;
        private string _trackedText = "Tracked: 0";
        private string _selectedSceneText = string.Empty;
        private int _lastSelectedSceneVisibleIndex = -1;

        private TableColumn[] _columns;
        private int _resizingColumnIndex = -1;
        private float _resizeStartMouseX;
        private float _resizeStartWidth;

        private GUILayoutOption[] _metricWidth;
        private bool _layoutOptionsBuilt;

        private GUIStyle _rowStyle;
        private GUIStyle _monoStyle;
        private GUIStyle _numericStyle;
        private GUIStyle _headerCellStyle;
        private GUIStyle _metricLabelStyle;
        private GUIStyle _metricValueStyle;
        private bool _stylesBuilt;

        private static bool IsPro => EditorGUIUtility.isProSkin;
        private static Color RowEven => IsPro ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f);
        private static Color RowOdd => IsPro ? new Color(0.19f, 0.19f, 0.19f, 1f) : new Color(0.80f, 0.80f, 0.80f, 1f);
        private static Color RowSelected => IsPro ? new Color(0.22f, 0.34f, 0.48f, 1f) : new Color(0.68f, 0.82f, 1.0f, 1f);
        private static Color RowUnloading => IsPro ? new Color(0.28f, 0.20f, 0.12f, 1f) : new Color(0.98f, 0.90f, 0.76f, 1f);
        private static Color RowWaiting => IsPro ? new Color(0.16f, 0.22f, 0.30f, 1f) : new Color(0.79f, 0.88f, 0.98f, 1f);
        private static Color RowError => IsPro ? new Color(0.36f, 0.12f, 0.12f, 1f) : new Color(0.98f, 0.78f, 0.76f, 1f);
        private static Color DimColor => IsPro ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.5f, 0.5f, 0.5f);
        private static Color SeparatorColor => IsPro ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Color WaitingTextColor = new Color(0.45f, 0.75f, 1.0f);
        private static readonly Color UnloadingTextColor = new Color(1.0f, 0.68f, 0.25f);
        private static readonly Color ErrorTextColor = new Color(1.0f, 0.36f, 0.30f);

        [MenuItem("Tools/CycloneGames/AssetManagement/Scene Tracker")]
        public static void ShowWindow()
        {
            var window = GetWindow<SceneTrackerWindow>("Scene Tracker");
            window.minSize = new Vector2(760f, 340f);
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
            if (!Application.isPlaying)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup >= _nextRepaint)
            {
                _nextRepaint = EditorApplication.timeSinceStartup + 0.15d;
                RefreshSnapshot();
                Repaint();
            }
        }

        private void OnGUI()
        {
            BuildStyles();
            DrawToolbar();

            if (!Application.isPlaying)
            {
                DrawPlaceholder("Run the game to inspect live scene lifecycle diagnostics.", MessageType.Info);
                return;
            }

            if (!_hasSnapshot)
            {
                RefreshSnapshot();
            }

            if (_search != _lastSearch || _stateFilter != _lastStateFilter)
            {
                _lastSearch = _search;
                _lastStateFilter = _stateFilter;
                _displayDirty = true;
                _lastSelectedSceneVisibleIndex = -1;
            }

            if (_displayDirty)
            {
                RebuildDisplay();
            }

            DrawSummary();

            _scroll = EditorGUILayout.BeginScrollView(_scroll, true, true);
            DrawTableHeader();

            if (_filteredIndices.Count == 0)
            {
                DrawEmptyTableMessage(_snapshot.Count == 0 ? "No tracked scenes." : "No scenes match the current filter.");
            }
            else
            {
                for (int i = 0; i < _filteredIndices.Count; i++)
                {
                    DrawRow(_views[_filteredIndices[i]], i);
                }
            }

            EditorGUILayout.EndScrollView();
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
                alignment = TextAnchor.MiddleLeft
            };

            _monoStyle = new GUIStyle(_rowStyle)
            {
                font = EditorStyles.miniFont
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

            _metricLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };

            _metricValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Cache", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                {
                    AssetCacheDebuggerWindow.ShowWindow();
                }

                if (GUILayout.Button("Handles", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    HandleTrackerWindow.ShowWindow();
                }

                if (GUILayout.Button("Governance", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                {
                    AssetRuntimeGovernanceWindow.ShowWindow();
                }

                GUILayout.Space(8f);

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    RefreshSnapshot();
                }

                GUILayout.Space(6f);
                GUILayout.Label("State", GUILayout.Width(34f));
                _stateFilter = EditorGUILayout.Popup(_stateFilter, StateOptions, EditorStyles.toolbarPopup, GUILayout.Width(118f));

                GUILayout.Space(6f);
                GUILayout.Label("Search", GUILayout.Width(42f));
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(200f));
                if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                {
                    _search = string.Empty;
                }

                GUILayout.FlexibleSpace();

                if (_selectedSceneIds.Count > 0)
                {
                    GUILayout.Label(_selectedSceneText, EditorStyles.miniLabel);
                    if (GUILayout.Button("Copy Selected", EditorStyles.toolbarButton, GUILayout.Width(94f)))
                    {
                        CopyToClipboard(BuildSelectedSceneRowsTsv());
                    }
                }

                if (GUILayout.Button("Copy Visible", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                {
                    CopyToClipboard(BuildVisibleSceneRowsTsv());
                }

                if (GUILayout.Button("Reset Columns", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                {
                    ResetColumns();
                }

                GUILayout.Space(6f);
                GUILayout.Label(_trackedText, EditorStyles.miniLabel);
            }
        }

        private void RefreshSnapshot()
        {
            _snapshot.Clear();
            _views.Clear();

            var scenes = SceneTracker.GetTrackedScenes();
            DateTime nowUtc = DateTime.UtcNow;
            for (int i = 0; i < scenes.Count; i++)
            {
                var info = scenes[i];
                _snapshot.Add(info);
                _views.Add(BuildView(info, nowUtc));
            }

            _trackedText = "Tracked: " + _snapshot.Count;
            PruneSceneSelection();
            _displayDirty = true;
            _hasSnapshot = true;
        }

        private static SceneRowView BuildView(SceneTracker.SceneInfo info, DateTime nowUtc)
        {
            bool hasError = !string.IsNullOrEmpty(info.Error);
            byte kind = hasError ? (byte)3
                : info.UnloadRequested ? (byte)1
                : info.ActivationState == SceneActivationState.WaitingForActivation ? (byte)2 : (byte)0;

            string label = !string.IsNullOrEmpty(info.RuntimeSceneName) ? info.RuntimeSceneName : info.SceneLocation;
            string sceneTooltip = !string.IsNullOrEmpty(info.ScenePath) ? info.ScenePath : info.SceneLocation;
            string loadMode = info.LoadMode.ToString();
            string activationMode = info.ActivationMode.ToString();

            return new SceneRowView
            {
                Id = info.Id,
                IdText = info.Id.ToString(),
                SceneLabel = string.IsNullOrEmpty(label) ? MISSING_TEXT : label,
                SceneLocation = info.SceneLocation,
                ScenePath = info.ScenePath,
                RuntimeSceneName = info.RuntimeSceneName,
                SceneTooltip = sceneTooltip ?? string.Empty,
                Provider = string.IsNullOrEmpty(info.ProviderType) ? MISSING_TEXT : info.ProviderType,
                Package = string.IsNullOrEmpty(info.PackageName) ? MISSING_TEXT : info.PackageName,
                Bucket = string.IsNullOrEmpty(info.Bucket) ? MISSING_TEXT : info.Bucket,
                State = GetStateLabel(info),
                LoadMode = loadMode,
                ActivationMode = activationMode,
                SupportsManualActivation = info.SupportsManualActivation ? "Yes" : "No",
                RuntimeSceneLoaded = info.RuntimeSceneLoaded ? "Yes" : "No",
                IsDone = info.IsDone ? "Yes" : "No",
                Progress = FormatPercent(info.Progress),
                Refs = info.RefCount.ToString(),
                Age = FormatAge(info.RegistrationTimeUtc, nowUtc),
                Error = info.Error,
                HasError = hasError,
                StateKind = kind
            };
        }

        private void RebuildDisplay()
        {
            _filteredIndices.Clear();
            int loading = 0;
            int waiting = 0;
            int activated = 0;
            int unloadPending = 0;
            int manual = 0;

            for (int i = 0; i < _snapshot.Count; i++)
            {
                var info = _snapshot[i];
                var view = _views[i];
                if (!MatchesState(info) || !MatchesSearch(view))
                {
                    continue;
                }

                _filteredIndices.Add(i);
                if (info.UnloadRequested)
                {
                    unloadPending++;
                }

                if (info.ActivationMode == SceneActivationMode.Manual)
                {
                    manual++;
                }

                switch (info.ActivationState)
                {
                    case SceneActivationState.Loading:
                        loading++;
                        break;
                    case SceneActivationState.WaitingForActivation:
                        waiting++;
                        break;
                    case SceneActivationState.Activated:
                        activated++;
                        break;
                }
            }

            _metricValues[0] = _filteredIndices.Count.ToString();
            _metricValues[1] = loading.ToString();
            _metricValues[2] = waiting.ToString();
            _metricValues[3] = activated.ToString();
            _metricValues[4] = unloadPending.ToString();
            _metricValues[5] = manual.ToString();
            _displayDirty = false;
        }

        private bool MatchesState(SceneTracker.SceneInfo info)
        {
            switch (_stateFilter)
            {
                case 1:
                    return info.ActivationState == SceneActivationState.Loading && !info.UnloadRequested;
                case 2:
                    return info.ActivationState == SceneActivationState.WaitingForActivation && !info.UnloadRequested;
                case 3:
                    return info.ActivationState == SceneActivationState.Activated && !info.UnloadRequested;
                case 4:
                    return info.UnloadRequested;
                case 5:
                    return !string.IsNullOrEmpty(info.Error);
                default:
                    return true;
            }
        }

        private bool MatchesSearch(in SceneRowView view)
        {
            if (string.IsNullOrEmpty(_search))
            {
                return true;
            }

            return Contains(view.SceneLabel, _search)
                || Contains(view.SceneLocation, _search)
                || Contains(view.ScenePath, _search)
                || Contains(view.RuntimeSceneName, _search)
                || Contains(view.Package, _search)
                || Contains(view.Provider, _search)
                || Contains(view.Bucket, _search)
                || Contains(view.State, _search)
                || Contains(view.LoadMode, _search)
                || Contains(view.ActivationMode, _search)
                || Contains(view.Error, _search);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawSummary()
        {
            EnsureLayoutOptions();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                DrawMetric("Visible", _metricValues[0]);
                DrawMetric("Loading", _metricValues[1]);
                DrawMetric("Waiting", _metricValues[2]);
                DrawMetric("Activated", _metricValues[3]);
                DrawMetric("Unload Pending", _metricValues[4]);
                DrawMetric("Manual", _metricValues[5]);
            }
        }

        private void DrawMetric(string label, string value)
        {
            using (new EditorGUILayout.VerticalScope(_metricWidth))
            {
                GUILayout.Label(label, _metricLabelStyle);
                GUILayout.Label(value, _metricValueStyle);
            }
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

        private void DrawRow(in SceneRowView view, int rowIndex)
        {
            EnsureColumns();

            Color rowBg = _selectedSceneIds.Contains(view.Id)
                ? RowSelected
                : view.StateKind == 3
                    ? RowError
                    : view.StateKind == 1
                        ? RowUnloading
                        : view.StateKind == 2
                            ? RowWaiting
                            : rowIndex % 2 == 0 ? RowEven : RowOdd;

            float tableWidth = GetTableWidth();
            Rect rowRect = GUILayoutUtility.GetRect(tableWidth, tableWidth, ROW_HEIGHT, ROW_HEIGHT, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rowRect, rowBg);
            }

            float x = rowRect.x;
            DrawTextCell(NextCell(ref x, rowRect, COL_SCENE), view.SceneLabel, _monoStyle, Color.white, view.SceneTooltip);
            DrawTextCell(NextCell(ref x, rowRect, COL_PROVIDER), view.Provider, _rowStyle, Color.white, view.Provider);
            DrawTextCell(NextCell(ref x, rowRect, COL_PACKAGE), view.Package, _rowStyle, Color.white, view.Package);
            DrawTextCell(NextCell(ref x, rowRect, COL_BUCKET), view.Bucket, _rowStyle, view.Bucket == MISSING_TEXT ? DimColor : Color.white, view.Bucket);
            DrawTextCell(NextCell(ref x, rowRect, COL_STATE), view.State, _rowStyle, GetStateColor(view.StateKind), GetStateTooltip(view));
            DrawTextCell(NextCell(ref x, rowRect, COL_MODE), view.LoadMode, _rowStyle, Color.white, view.LoadMode);
            DrawTextCell(NextCell(ref x, rowRect, COL_ACTIVATION), view.ActivationMode, _rowStyle, Color.white, GetActivationTooltip(view));
            DrawTextCell(NextCell(ref x, rowRect, COL_PROGRESS), view.Progress, _numericStyle, Color.white, null);
            DrawTextCell(NextCell(ref x, rowRect, COL_REFS), view.Refs, _numericStyle, Color.white, null);
            DrawTextCell(NextCell(ref x, rowRect, COL_AGE), view.Age, _numericStyle, Color.white, null);
            DrawTextCell(NextCell(ref x, rowRect, COL_ERROR), string.IsNullOrEmpty(view.Error) ? MISSING_TEXT : view.Error, _rowStyle, view.HasError ? ErrorTextColor : DimColor, view.Error);

            HandleRowInput(rowRect, view, rowIndex);
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

        private void EnsureLayoutOptions()
        {
            if (_layoutOptionsBuilt)
            {
                return;
            }

            _metricWidth = new[] { GUILayout.Width(112f) };
            _layoutOptionsBuilt = true;
        }

        private void EnsureColumns()
        {
            if (_columns != null)
            {
                return;
            }

            float availableWidth = Mathf.Max(760f, position.width - 24f);
            float fixedWidth = 100f + 120f + 130f + 110f + 82f + 104f + 74f + 52f + 70f + 160f;
            float sceneWidth = Mathf.Max(220f, availableWidth - fixedWidth);

            _columns = new[]
            {
                new TableColumn("Scene", sceneWidth, 180f, "Runtime scene name, or scene location when the runtime scene is not loaded yet."),
                new TableColumn("Provider", 100f, 82f, "Asset provider backend."),
                new TableColumn("Package", 120f, 88f, "Asset package name."),
                new TableColumn("Bucket", 130f, 88f, "Scene lifetime bucket."),
                new TableColumn("State", 110f, 82f, "Scene loading or activation state."),
                new TableColumn("Mode", 82f, 66f, "Unity scene load mode."),
                new TableColumn("Activation", 104f, 86f, "Scene activation mode."),
                new TableColumn("Progress", 74f, 66f, "Normalized loading progress."),
                new TableColumn("Refs", 52f, 44f, "Scene handle reference count."),
                new TableColumn("Age", 70f, 58f, "Time since registration."),
                new TableColumn("Error", 160f, 96f, "Latest scene handle error, when available.")
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
            if (_selectedSceneIds.Count > 0)
            {
                menu.AddItem(new GUIContent("Copy Selected Rows/As TSV"), false, () => CopyToClipboard(BuildSelectedSceneRowsTsv()));
                menu.AddItem(new GUIContent("Copy Selected Rows/As JSON"), false, () => CopyToClipboard(BuildSelectedSceneRowsJson()));
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent("Copy Visible Rows/As TSV"), false, () => CopyToClipboard(BuildVisibleSceneRowsTsv()));
            menu.AddItem(new GUIContent("Copy Visible Rows/As JSON"), false, () => CopyToClipboard(BuildVisibleSceneRowsJson()));
            menu.AddSeparator(string.Empty);
            if (_selectedSceneIds.Count > 0)
            {
                menu.AddItem(new GUIContent("Clear Selection"), false, () =>
                {
                    ClearSceneSelection();
                    Repaint();
                });
            }

            menu.AddItem(new GUIContent("Reset Column Widths"), false, ResetColumns);
            menu.ShowAsContext();
            evt.Use();
        }

        private void HandleRowInput(Rect rowRect, in SceneRowView view, int visibleIndex)
        {
            Event evt = Event.current;
            if (!rowRect.Contains(evt.mousePosition) || evt.type != EventType.MouseDown)
            {
                return;
            }

            if (evt.button == 0)
            {
                SelectSceneRow(view.Id, visibleIndex, evt);
                Repaint();
                evt.Use();
                return;
            }

            if (evt.button != 1)
            {
                return;
            }

            if (!_selectedSceneIds.Contains(view.Id))
            {
                SelectSingleSceneRow(view.Id, visibleIndex);
            }

            ShowRowContextMenu(view);
            evt.Use();
        }

        private void SelectSceneRow(int id, int visibleIndex, Event evt)
        {
            bool additive = evt.control || evt.command;
            if (evt.shift && _lastSelectedSceneVisibleIndex >= 0)
            {
                if (!additive)
                {
                    _selectedSceneIds.Clear();
                }

                SelectVisibleSceneRange(_lastSelectedSceneVisibleIndex, visibleIndex);
                UpdateSceneSelectionText();
                return;
            }

            if (additive)
            {
                if (!_selectedSceneIds.Add(id))
                {
                    _selectedSceneIds.Remove(id);
                }
            }
            else
            {
                _selectedSceneIds.Clear();
                _selectedSceneIds.Add(id);
            }

            _lastSelectedSceneVisibleIndex = visibleIndex;
            UpdateSceneSelectionText();
        }

        private void SelectSingleSceneRow(int id, int visibleIndex)
        {
            _selectedSceneIds.Clear();
            _selectedSceneIds.Add(id);
            _lastSelectedSceneVisibleIndex = visibleIndex;
            UpdateSceneSelectionText();
        }

        private void SelectVisibleSceneRange(int from, int to)
        {
            int min = Mathf.Min(from, to);
            int max = Mathf.Max(from, to);
            min = Mathf.Clamp(min, 0, _filteredIndices.Count - 1);
            max = Mathf.Clamp(max, 0, _filteredIndices.Count - 1);

            for (int i = min; i <= max; i++)
            {
                _selectedSceneIds.Add(_views[_filteredIndices[i]].Id);
            }
        }

        private void ClearSceneSelection()
        {
            _selectedSceneIds.Clear();
            _lastSelectedSceneVisibleIndex = -1;
            UpdateSceneSelectionText();
        }

        private void PruneSceneSelection()
        {
            if (_selectedSceneIds.Count == 0)
            {
                return;
            }

            _sceneSelectionPruneList.Clear();
            foreach (int id in _selectedSceneIds)
            {
                if (!ContainsSceneId(id))
                {
                    _sceneSelectionPruneList.Add(id);
                }
            }

            for (int i = 0; i < _sceneSelectionPruneList.Count; i++)
            {
                _selectedSceneIds.Remove(_sceneSelectionPruneList[i]);
            }

            if (_selectedSceneIds.Count == 0)
            {
                _lastSelectedSceneVisibleIndex = -1;
            }

            UpdateSceneSelectionText();
        }

        private bool ContainsSceneId(int id)
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

        private void UpdateSceneSelectionText()
        {
            _selectedSceneText = _selectedSceneIds.Count > 0 ? "Selected: " + _selectedSceneIds.Count : string.Empty;
        }

        private void ShowRowContextMenu(SceneRowView view)
        {
            var menu = new GenericMenu();

            string fullRow = BuildSceneRowDetails(view);
            string rowTsv = BuildSceneRowTsv(view);
            string rowJson = BuildSceneRowJson(view);

            menu.AddItem(new GUIContent("Copy/Full Row"), false, () => CopyToClipboard(fullRow));
            menu.AddItem(new GUIContent("Copy/Row as TSV"), false, () => CopyToClipboard(rowTsv));
            menu.AddItem(new GUIContent("Copy/Row as JSON"), false, () => CopyToClipboard(rowJson));
            menu.AddSeparator("Copy/");
            AddCopyValue(menu, "Copy/ID", view.IdText);
            AddCopyValue(menu, "Copy/Scene", view.SceneLabel);
            AddCopyValue(menu, "Copy/Location", view.SceneLocation);
            AddCopyValue(menu, "Copy/Scene Path", view.ScenePath);
            AddCopyValue(menu, "Copy/Runtime Scene Name", view.RuntimeSceneName);
            AddCopyValue(menu, "Copy/Provider", view.Provider);
            AddCopyValue(menu, "Copy/Package", view.Package);
            AddCopyValue(menu, "Copy/Bucket", view.Bucket);
            AddCopyValue(menu, "Copy/State", view.State);
            AddCopyValue(menu, "Copy/Load Mode", view.LoadMode);
            AddCopyValue(menu, "Copy/Activation Mode", view.ActivationMode);
            AddCopyValue(menu, "Copy/Progress", view.Progress);
            AddCopyValue(menu, "Copy/Refs", view.Refs);
            AddCopyValue(menu, "Copy/Age", view.Age);
            AddCopyValue(menu, "Copy/Error", view.Error);

            menu.AddSeparator(string.Empty);
            if (_selectedSceneIds.Count > 0)
            {
                menu.AddItem(new GUIContent("Copy Selected Rows/As TSV"), false, () => CopyToClipboard(BuildSelectedSceneRowsTsv()));
                menu.AddItem(new GUIContent("Copy Selected Rows/As JSON"), false, () => CopyToClipboard(BuildSelectedSceneRowsJson()));
                menu.AddItem(new GUIContent("Selection/Clear Selection"), false, () =>
                {
                    ClearSceneSelection();
                    Repaint();
                });
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent("Copy Visible Rows/As TSV"), false, () => CopyToClipboard(BuildVisibleSceneRowsTsv()));
            menu.AddItem(new GUIContent("Copy Visible Rows/As JSON"), false, () => CopyToClipboard(BuildVisibleSceneRowsJson()));

            string assetPath = GetProjectAssetPath(view);
            if (!string.IsNullOrEmpty(assetPath))
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Actions/Ping Scene Asset"), false, () => PingAsset(assetPath));
            }

            menu.ShowAsContext();
        }

        private static void AddCopyValue(GenericMenu menu, string path, string value)
        {
            if (string.IsNullOrEmpty(value) || value == MISSING_TEXT)
            {
                menu.AddDisabledItem(new GUIContent(path));
                return;
            }

            string captured = value;
            menu.AddItem(new GUIContent(path), false, () => CopyToClipboard(captured));
        }

        private string BuildSceneRowDetails(SceneRowView view)
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("Scene Tracker Row");
            _copyBuilder.AppendLine("ID: " + view.Id);
            _copyBuilder.AppendLine("Scene: " + SafeText(view.SceneLabel));
            _copyBuilder.AppendLine("Location: " + SafeText(view.SceneLocation));
            _copyBuilder.AppendLine("Scene Path: " + SafeText(view.ScenePath));
            _copyBuilder.AppendLine("Runtime Scene Name: " + SafeText(view.RuntimeSceneName));
            _copyBuilder.AppendLine("Provider: " + SafeText(view.Provider));
            _copyBuilder.AppendLine("Package: " + SafeText(view.Package));
            _copyBuilder.AppendLine("Bucket: " + SafeText(view.Bucket));
            _copyBuilder.AppendLine("State: " + SafeText(view.State));
            _copyBuilder.AppendLine("Load Mode: " + SafeText(view.LoadMode));
            _copyBuilder.AppendLine("Activation Mode: " + SafeText(view.ActivationMode));
            _copyBuilder.AppendLine("Supports Manual Activation: " + SafeText(view.SupportsManualActivation));
            _copyBuilder.AppendLine("Runtime Scene Loaded: " + SafeText(view.RuntimeSceneLoaded));
            _copyBuilder.AppendLine("Is Done: " + SafeText(view.IsDone));
            _copyBuilder.AppendLine("Progress: " + SafeText(view.Progress));
            _copyBuilder.AppendLine("Refs: " + SafeText(view.Refs));
            _copyBuilder.AppendLine("Age: " + SafeText(view.Age));
            _copyBuilder.AppendLine("Error: " + SafeText(view.Error));
            return _copyBuilder.ToString();
        }

        private static string BuildSceneRowTsv(SceneRowView view)
        {
            return view.Id + "\t" +
                SanitizeTsv(view.SceneLabel) + "\t" +
                SanitizeTsv(view.SceneLocation) + "\t" +
                SanitizeTsv(view.ScenePath) + "\t" +
                SanitizeTsv(view.RuntimeSceneName) + "\t" +
                SanitizeTsv(view.Provider) + "\t" +
                SanitizeTsv(view.Package) + "\t" +
                SanitizeTsv(view.Bucket) + "\t" +
                SanitizeTsv(view.State) + "\t" +
                SanitizeTsv(view.LoadMode) + "\t" +
                SanitizeTsv(view.ActivationMode) + "\t" +
                SanitizeTsv(view.SupportsManualActivation) + "\t" +
                SanitizeTsv(view.RuntimeSceneLoaded) + "\t" +
                SanitizeTsv(view.IsDone) + "\t" +
                SanitizeTsv(view.Progress) + "\t" +
                SanitizeTsv(view.Refs) + "\t" +
                SanitizeTsv(view.Age) + "\t" +
                SanitizeTsv(view.Error);
        }

        private static string BuildSceneRowJson(SceneRowView view)
        {
            var builder = new StringBuilder(256);
            AppendSceneRowJson(builder, view);
            return builder.ToString();
        }

        private string BuildVisibleSceneRowsTsv()
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine(GetSceneTsvHeader());
            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                _copyBuilder.AppendLine(BuildSceneRowTsv(_views[_filteredIndices[i]]));
            }

            return _copyBuilder.ToString();
        }

        private string BuildVisibleSceneRowsJson()
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
                AppendSceneRowJson(_copyBuilder, _views[_filteredIndices[i]]);
            }

            _copyBuilder.AppendLine();
            _copyBuilder.Append("]");
            return _copyBuilder.ToString();
        }

        private string BuildSelectedSceneRowsTsv()
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine(GetSceneTsvHeader());
            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                var view = _views[_filteredIndices[i]];
                if (_selectedSceneIds.Contains(view.Id))
                {
                    _copyBuilder.AppendLine(BuildSceneRowTsv(view));
                }
            }

            return _copyBuilder.ToString();
        }

        private string BuildSelectedSceneRowsJson()
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("[");
            bool first = true;
            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                var view = _views[_filteredIndices[i]];
                if (!_selectedSceneIds.Contains(view.Id))
                {
                    continue;
                }

                if (!first)
                {
                    _copyBuilder.AppendLine(",");
                }

                _copyBuilder.Append("  ");
                AppendSceneRowJson(_copyBuilder, view);
                first = false;
            }

            _copyBuilder.AppendLine();
            _copyBuilder.Append("]");
            return _copyBuilder.ToString();
        }

        private static string GetSceneTsvHeader()
        {
            return "ID\tScene\tLocation\tScenePath\tRuntimeSceneName\tProvider\tPackage\tBucket\tState\tLoadMode\tActivationMode\tSupportsManualActivation\tRuntimeSceneLoaded\tIsDone\tProgress\tRefs\tAge\tError";
        }

        private static void AppendSceneRowJson(StringBuilder builder, SceneRowView view)
        {
            builder.Append('{');
            AppendJsonProperty(builder, "id", view.Id, false);
            AppendJsonProperty(builder, "scene", view.SceneLabel, true);
            AppendJsonProperty(builder, "location", view.SceneLocation, true);
            AppendJsonProperty(builder, "scenePath", view.ScenePath, true);
            AppendJsonProperty(builder, "runtimeSceneName", view.RuntimeSceneName, true);
            AppendJsonProperty(builder, "provider", view.Provider, true);
            AppendJsonProperty(builder, "package", view.Package, true);
            AppendJsonProperty(builder, "bucket", view.Bucket, true);
            AppendJsonProperty(builder, "state", view.State, true);
            AppendJsonProperty(builder, "loadMode", view.LoadMode, true);
            AppendJsonProperty(builder, "activationMode", view.ActivationMode, true);
            AppendJsonProperty(builder, "supportsManualActivation", view.SupportsManualActivation, true);
            AppendJsonProperty(builder, "runtimeSceneLoaded", view.RuntimeSceneLoaded, true);
            AppendJsonProperty(builder, "isDone", view.IsDone, true);
            AppendJsonProperty(builder, "progress", view.Progress, true);
            AppendJsonProperty(builder, "refs", view.Refs, true);
            AppendJsonProperty(builder, "age", view.Age, true);
            AppendJsonProperty(builder, "error", view.Error, true);
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

        private static Color GetStateColor(byte stateKind)
        {
            switch (stateKind)
            {
                case 1:
                    return UnloadingTextColor;
                case 2:
                    return WaitingTextColor;
                case 3:
                    return ErrorTextColor;
                default:
                    return DimColor;
            }
        }

        private static string GetStateTooltip(in SceneRowView view)
        {
            if (view.HasError)
            {
                return view.Error;
            }

            if (view.StateKind == 1)
            {
                return "Unload has been requested for this scene handle.";
            }

            if (view.StateKind == 2)
            {
                return "The scene is loaded enough to wait for manual activation.";
            }

            return null;
        }

        private static string GetActivationTooltip(in SceneRowView view)
        {
            return "Supports manual activation: " + view.SupportsManualActivation + "\n" +
                "Runtime scene loaded: " + view.RuntimeSceneLoaded + "\n" +
                "Is done: " + view.IsDone;
        }

        private static string GetStateLabel(SceneTracker.SceneInfo info)
        {
            if (!string.IsNullOrEmpty(info.Error))
            {
                return "Error";
            }

            if (info.UnloadRequested)
            {
                return "Unloading";
            }

            switch (info.ActivationState)
            {
                case SceneActivationState.Loading:
                    return "Loading";
                case SceneActivationState.WaitingForActivation:
                    return "Waiting";
                case SceneActivationState.Activated:
                    return "Activated";
                default:
                    return info.ActivationState.ToString();
            }
        }

        private static string FormatPercent(float normalized)
        {
            return (normalized * 100f).ToString("F0") + "%";
        }

        private static string FormatAge(DateTime registeredUtc, DateTime nowUtc)
        {
            double seconds = (nowUtc - registeredUtc).TotalSeconds;
            if (seconds < 60d)
            {
                return seconds.ToString("F0") + "s";
            }

            if (seconds < 3600d)
            {
                return (seconds / 60d).ToString("F1") + "m";
            }

            return (seconds / 3600d).ToString("F1") + "h";
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

        private static string GetProjectAssetPath(in SceneRowView view)
        {
            if (IsProjectAssetPath(view.ScenePath))
            {
                return view.ScenePath;
            }

            return IsProjectAssetPath(view.SceneLocation) ? view.SceneLocation : null;
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
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(420f)))
                {
                    EditorGUILayout.HelpBox(message, type);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawEmptyTableMessage(string text)
        {
            Rect rect = GUILayoutUtility.GetRect(100f, 40f, GUILayout.ExpandWidth(true));
            GUI.Label(rect, text, EditorStyles.centeredGreyMiniLabel);
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

        private struct SceneRowView
        {
            public int Id;
            public string IdText;
            public string SceneLabel;
            public string SceneLocation;
            public string ScenePath;
            public string RuntimeSceneName;
            public string SceneTooltip;
            public string Provider;
            public string Package;
            public string Bucket;
            public string State;
            public string LoadMode;
            public string ActivationMode;
            public string SupportsManualActivation;
            public string RuntimeSceneLoaded;
            public string IsDone;
            public string Progress;
            public string Refs;
            public string Age;
            public string Error;
            public bool HasError;
            public byte StateKind;
        }
    }
}
#endif
