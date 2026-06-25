#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Editor
{
    public class AssetCacheDebuggerWindow : EditorWindow
    {
        private const float ROW_HEIGHT = 20f;
        private const float HEADER_HEIGHT = 22f;
        private const float RESIZE_HANDLE_WIDTH = 7f;
        private const float MAX_COLUMN_WIDTH = 1600f;
        private const string MISSING_TEXT = "-";

        private const int COL_LOCATION = 0;
        private const int COL_REFS = 1;
        private const int COL_HITS = 2;
        private const int COL_PROVIDER = 3;
        private const int COL_BUCKET = 4;
        private const int COL_TAG = 5;
        private const int COL_OWNER = 6;
        private const int COL_TIER = 7;
        private const int COL_MEMORY = 8;

        private static readonly string[] _tabs = { "Active (ARC)", "Trial (LRU)", "Main (LFU/LRU)", "Buckets", "Summary" };
        private int _selectedTab;

        private readonly List<AssetCacheService.CacheDiagnosticEntry> _activeList = new List<AssetCacheService.CacheDiagnosticEntry>(512);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _trialList = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _mainList = new List<AssetCacheService.CacheDiagnosticEntry>(256);

        private readonly List<AssetCacheService.CacheDiagnosticEntry> _allList = new List<AssetCacheService.CacheDiagnosticEntry>(1024);
        private readonly List<CacheRowView> _allViews = new List<CacheRowView>(1024);
        private readonly List<int> _visibleCacheIndices = new List<int>(1024);
        private int _activeCount;
        private int _trialCount;
        private int _mainCount;

        private long _idleBytesApprox;
        private long _maxIdleBytesBudget;

        private readonly Dictionary<string, List<int>> _bucketGroups = new Dictionary<string, List<int>>();
        private readonly Stack<List<int>> _bucketListPool = new Stack<List<int>>();
        private readonly List<string> _bucketNames = new List<string>();
        private readonly List<string> _bucketCounts = new List<string>();
        private readonly List<List<int>> _bucketIndexLists = new List<List<int>>();
        private string _bucketTitle = "Bucket View";

        private string _activeTitle;
        private string _trialTitle;
        private string _mainTitle;
        private string _pillActive = "  Active: 0  ";
        private string _pillTrial = "  Trial: 0  ";
        private string _pillMain = "  Main: 0  ";
        private string _pillTotal = "  Total: 0  ";

        private string _sumTotal;
        private string _sumActive;
        private string _sumTrial;
        private string _sumMain;
        private string _sumRefs;
        private string _sumMaxHits;
        private string _sumActiveBytes;
        private string _sumIdleBytes;
        private string _sumBudget;
        private string _sumIdleBarLabel;
        private float _sumIdlePct;
        private string _sumYoo;
        private string _sumAddr;
        private string _sumRes;
        private string _sumOther;
        private int _sumOtherCount;
        private bool _sumHasAnomalies;
        private readonly List<string> _anomalyRefs = new List<string>();
        private readonly List<string> _anomalyLocs = new List<string>();
        private readonly Dictionary<string, int> _ownerCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _tagCounts = new Dictionary<string, int>();
        private readonly List<string> _ownerLabels = new List<string>();
        private readonly List<string> _ownerValues = new List<string>();
        private readonly List<string> _tagLabels = new List<string>();
        private readonly List<string> _tagValues = new List<string>();

        private readonly GUIContent _cellA = new GUIContent();
        private readonly GUIContent _cellB = new GUIContent();
        private readonly StringBuilder _copyBuilder = new StringBuilder(4096);
        private string[] _instanceNames = Array.Empty<string>();

        private string _searchFilter = string.Empty;
        private int _selectedInstanceIndex;
        private Vector2 _scrollPos;
        private readonly Vector2[] _tabScrollPositions = new Vector2[_tabs.Length];
        private bool _hasSnapshot;

        private TableColumn[] _columns;
        private int _resizingColumnIndex = -1;
        private float _resizeStartMouseX;
        private float _resizeStartWidth;
        private readonly HashSet<string> _selectedCacheKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _cacheSelectionPruneList = new List<string>(32);
        private int _lastSelectedCacheVisibleIndex = -1;
        private string _selectedCacheText = string.Empty;

        private GUILayoutOption[] _pillOpts;
        private GUILayoutOption[] _expandWidth;
        private bool _layoutOptionsBuilt;

        private const string TrialZeroTooltip =
            "RefCount = 0 - asset is in the Trial (probation) idle pool.\n" +
            "AssetCacheService is holding it for fast re-use.\nNot a leak.";

        private const string MainZeroTooltip =
            "RefCount = 0 - asset is in the Main (hot) idle pool.\n" +
            "Promoted due to high access frequency; kept resident for instant re-use.\nNot a leak.";

        private static bool IsPro => EditorGUIUtility.isProSkin;
        private static Color RowEven => IsPro ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f);
        private static Color RowOdd => IsPro ? new Color(0.19f, 0.19f, 0.19f, 1f) : new Color(0.80f, 0.80f, 0.80f, 1f);
        private static Color RowHighRef => IsPro ? new Color(0.30f, 0.22f, 0.12f, 1f) : new Color(0.98f, 0.88f, 0.74f, 1f);
        private static Color RowSelected => IsPro ? new Color(0.22f, 0.34f, 0.48f, 1f) : new Color(0.68f, 0.82f, 1.0f, 1f);
        private static Color DimColor => IsPro ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.5f, 0.5f, 0.5f);
        private static Color SeparatorColor => IsPro ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(0.62f, 0.62f, 0.62f, 1f);

        private GUIStyle _rowStyle;
        private GUIStyle _numericStyle;
        private GUIStyle _headerCellStyle;
        private GUIStyle _pillStyle;
        private GUIStyle _monoStyle;
        private GUIStyle _typeBadgeStyle;
        private bool _stylesBuilt;

        [MenuItem("Tools/CycloneGames/AssetManagement/Asset Cache Debugger")]
        public static void ShowWindow()
        {
            var window = GetWindow<AssetCacheDebuggerWindow>("Asset Cache Debugger");
            window.minSize = new Vector2(720, 420);
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

        private double _nextRepaint;

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying)
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

            _pillStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(4, 4, 1, 1)
            };

            _monoStyle = new GUIStyle(EditorStyles.label)
            {
                font = EditorStyles.miniFont,
                clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 2, 2),
                alignment = TextAnchor.MiddleLeft
            };

            _typeBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Italic,
                padding = new RectOffset(2, 4, 2, 2),
                clipping = TextClipping.Clip
            };
        }

        private void OnGUI()
        {
            BuildStyles();

            if (!Application.isPlaying)
            {
                DrawPlaceholder("Run the game to view live asset cache diagnostics.", MessageType.Info);
                return;
            }

            var instances = AssetCacheService.GlobalInstances;
            if (instances == null || instances.Count == 0)
            {
                DrawPlaceholder("No active AssetCacheService instances found.", MessageType.Warning);
                return;
            }

            DrawInstanceSelector(instances);

            if (!_hasSnapshot)
            {
                RefreshSnapshot();
            }

            DrawTopBar();

            GUILayout.Space(4f);
            int newSelectedTab = GUILayout.Toolbar(_selectedTab, _tabs, EditorStyles.toolbarButton, GUILayout.Height(22f));
            if (newSelectedTab != _selectedTab)
            {
                _tabScrollPositions[_selectedTab] = _scrollPos;
                _selectedTab = newSelectedTab;
                _scrollPos = _tabScrollPositions[_selectedTab];
                _lastSelectedCacheVisibleIndex = -1;
            }

            GUILayout.Space(2f);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, true, true);
            BuildVisibleCacheIndices(_visibleCacheIndices);

            switch (_selectedTab)
            {
                case 0:
                    DrawTier(0, _activeCount, _activeTitle);
                    break;
                case 1:
                    DrawTier(_activeCount, _trialCount, _trialTitle);
                    break;
                case 2:
                    DrawTier(_activeCount + _trialCount, _mainCount, _mainTitle);
                    break;
                case 3:
                    DrawBuckets();
                    break;
                case 4:
                    DrawSummary();
                    break;
            }

            EditorGUILayout.EndScrollView();
            _tabScrollPositions[_selectedTab] = _scrollPos;
        }

        private void DrawInstanceSelector(IReadOnlyList<AssetCacheService> instances)
        {
            if (instances.Count > 1)
            {
                if (_instanceNames.Length != instances.Count)
                {
                    _instanceNames = new string[instances.Count];
                    for (int i = 0; i < instances.Count; i++)
                    {
                        _instanceNames[i] = "Instance #" + i;
                    }
                }

                _selectedInstanceIndex = Mathf.Clamp(_selectedInstanceIndex, 0, instances.Count - 1);
                int newIndex = EditorGUILayout.Popup("Cache Instance", _selectedInstanceIndex, _instanceNames);
                if (newIndex != _selectedInstanceIndex)
                {
                    _selectedInstanceIndex = newIndex;
                    ClearCacheSelection();
                    RefreshSnapshot();
                }
            }
            else
            {
                _selectedInstanceIndex = 0;
            }
        }

        private void RefreshSnapshot()
        {
            _activeList.Clear();
            _trialList.Clear();
            _mainList.Clear();
            _allList.Clear();
            _allViews.Clear();
            _activeCount = 0;
            _trialCount = 0;
            _mainCount = 0;

            var instances = AssetCacheService.GlobalInstances;
            if (instances == null || instances.Count == 0)
            {
                BuildBuckets();
                BuildSummary();
                PruneCacheSelection();
                _hasSnapshot = true;
                return;
            }

            _selectedInstanceIndex = Mathf.Clamp(_selectedInstanceIndex, 0, instances.Count - 1);
            var service = instances[_selectedInstanceIndex];
            service.GetDiagnostics(_activeList, _trialList, _mainList);
            _idleBytesApprox = service.IdleBytesApprox;
            _maxIdleBytesBudget = service.MaxIdleBytesBudget;

            for (int i = 0; i < _activeList.Count; i++)
            {
                _allList.Add(_activeList[i]);
                _allViews.Add(BuildView(_activeList[i], 0));
            }

            for (int i = 0; i < _trialList.Count; i++)
            {
                _allList.Add(_trialList[i]);
                _allViews.Add(BuildView(_trialList[i], 1));
            }

            for (int i = 0; i < _mainList.Count; i++)
            {
                _allList.Add(_mainList[i]);
                _allViews.Add(BuildView(_mainList[i], 2));
            }

            _activeCount = _activeList.Count;
            _trialCount = _trialList.Count;
            _mainCount = _mainList.Count;

            BuildBuckets();
            BuildSummary();
            PruneCacheSelection();
            _hasSnapshot = true;
        }

        private CacheRowView BuildView(in AssetCacheService.CacheDiagnosticEntry entry, byte tierKind)
        {
            bool refAnomaly = tierKind == 0 && entry.RefCount > 8;
            byte refKind = entry.RefCount == 0 ? (byte)1 : refAnomaly ? (byte)2 : (byte)0;
            string refsTooltip = null;

            if (refKind == 1)
            {
                refsTooltip = tierKind == 1 ? TrialZeroTooltip : tierKind == 2 ? MainZeroTooltip : "RefCount = 0";
            }
            else if (refKind == 2)
            {
                refsTooltip = "RefCount = " + entry.RefCount + " is unusually high.\nVerify that all callers are correctly calling Dispose().";
            }

            string tier = tierKind == 0 ? "Active" : tierKind == 1 ? "Trial" : "Main";
            string memoryText = FormatBytes(entry.EstimatedBytes);

            return new CacheRowView
            {
                LocationTooltip = string.IsNullOrEmpty(entry.AssetType) ? entry.Location : entry.Location + "\n[" + entry.AssetType + "]",
                TypeSuffix = string.IsNullOrEmpty(entry.AssetType) ? null : " (" + entry.AssetType + ")",
                TypeName = entry.AssetType,
                HasType = !string.IsNullOrEmpty(entry.AssetType),
                RefsText = entry.RefCount.ToString(),
                RefsTooltip = refsTooltip,
                RefKind = refKind,
                HitsText = entry.AccessCount.ToString(),
                Tier = tier,
                TierTooltip = GetTierTooltip(tier),
                TierKind = tierKind,
                MemoryText = memoryText,
                MemoryTooltip = entry.EstimatedBytes > 0 ? "Estimated runtime footprint: " + memoryText : "No runtime footprint estimate is available."
            };
        }

        private void BuildBuckets()
        {
            foreach (var value in _bucketGroups.Values)
            {
                value.Clear();
                _bucketListPool.Push(value);
            }

            _bucketGroups.Clear();
            _bucketNames.Clear();
            _bucketCounts.Clear();
            _bucketIndexLists.Clear();

            for (int i = 0; i < _allList.Count; i++)
            {
                string key = _allList[i].Bucket ?? string.Empty;
                if (!_bucketGroups.TryGetValue(key, out var list))
                {
                    list = _bucketListPool.Count > 0 ? _bucketListPool.Pop() : new List<int>(16);
                    _bucketGroups[key] = list;
                }

                list.Add(i);
            }

            foreach (var pair in _bucketGroups)
            {
                _bucketNames.Add(string.IsNullOrEmpty(pair.Key) ? "[No Bucket]" : pair.Key);
                _bucketCounts.Add(pair.Value.Count + " item(s)");
                _bucketIndexLists.Add(pair.Value);
            }

            _bucketTitle = "Bucket View  [" + _bucketGroups.Count + " buckets, " + _allList.Count + " total]";
        }

        private void BuildSummary()
        {
            _activeTitle = "Active Handles (RefCount > 0)  [" + _activeCount + "]";
            _trialTitle = "Trial Pool - new assets on probation (W-LRU)  [" + _trialCount + "]";
            _mainTitle = "Main Pool - promoted assets (LFU+LRU)  [" + _mainCount + "]";
            _pillActive = "  Active: " + _activeCount + "  ";
            _pillTrial = "  Trial: " + _trialCount + "  ";
            _pillMain = "  Main: " + _mainCount + "  ";
            _pillTotal = "  Total: " + _allList.Count + "  ";

            int yoo = 0;
            int addressables = 0;
            int resources = 0;
            int other = 0;
            int totalRefs = 0;
            int maxHits = 0;
            long activeBytes = 0;

            _ownerCounts.Clear();
            _tagCounts.Clear();
            _anomalyRefs.Clear();
            _anomalyLocs.Clear();

            for (int i = 0; i < _allList.Count; i++)
            {
                var entry = _allList[i];
                totalRefs += entry.RefCount;
                if (entry.AccessCount > maxHits)
                {
                    maxHits = entry.AccessCount;
                }

                switch (entry.ProviderType)
                {
                    case "YooAsset":
                        yoo++;
                        break;
                    case "Addressables":
                        addressables++;
                        break;
                    case "Resources":
                        resources++;
                        break;
                    default:
                        other++;
                        break;
                }

                if (!string.IsNullOrEmpty(entry.Owner))
                {
                    _ownerCounts.TryGetValue(entry.Owner, out int ownerCount);
                    _ownerCounts[entry.Owner] = ownerCount + 1;
                }

                if (!string.IsNullOrEmpty(entry.Tag))
                {
                    _tagCounts.TryGetValue(entry.Tag, out int tagCount);
                    _tagCounts[entry.Tag] = tagCount + 1;
                }
            }

            for (int i = 0; i < _activeCount; i++)
            {
                var entry = _allList[i];
                activeBytes += entry.EstimatedBytes;
                if (entry.RefCount > 8)
                {
                    _anomalyRefs.Add("Refs=" + entry.RefCount);
                    _anomalyLocs.Add(entry.Location);
                }
            }

            _sumHasAnomalies = _anomalyRefs.Count > 0;
            _sumTotal = _allList.Count.ToString();
            _sumActive = _activeCount.ToString();
            _sumTrial = _trialCount.ToString();
            _sumMain = _mainCount.ToString();
            _sumRefs = totalRefs.ToString();
            _sumMaxHits = maxHits.ToString();
            _sumActiveBytes = FormatBytes(activeBytes);
            _sumIdleBytes = FormatBytes(_idleBytesApprox);
            _sumBudget = FormatBytes(_maxIdleBytesBudget);
            _sumIdlePct = (float)_idleBytesApprox / Mathf.Max(1, _maxIdleBytesBudget);
            _sumIdleBarLabel = "  Idle budget used: " + (_sumIdlePct * 100f).ToString("F1") + "%";
            _sumYoo = yoo.ToString();
            _sumAddr = addressables.ToString();
            _sumRes = resources.ToString();
            _sumOther = other.ToString();
            _sumOtherCount = other;

            _ownerLabels.Clear();
            _ownerValues.Clear();
            foreach (var pair in _ownerCounts)
            {
                _ownerLabels.Add(pair.Key);
                _ownerValues.Add(pair.Value.ToString());
            }

            _tagLabels.Clear();
            _tagValues.Clear();
            foreach (var pair in _tagCounts)
            {
                _tagLabels.Add(pair.Key);
                _tagValues.Add(pair.Value.ToString());
            }
        }

        private void DrawTopBar()
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

                DrawStatPill(_pillActive, new Color(0.2f, 0.7f, 0.3f));
                GUILayout.Space(4f);
                DrawStatPill(_pillTrial, new Color(0.9f, 0.6f, 0.1f));
                GUILayout.Space(4f);
                DrawStatPill(_pillMain, new Color(0.2f, 0.5f, 1.0f));
                GUILayout.Space(4f);
                DrawStatPill(_pillTotal, new Color(0.6f, 0.6f, 0.6f));

                GUILayout.FlexibleSpace();

                if (_selectedCacheKeys.Count > 0)
                {
                    GUILayout.Label(_selectedCacheText, EditorStyles.miniLabel);
                    if (GUILayout.Button("Copy Selected", EditorStyles.toolbarButton, GUILayout.Width(94f)))
                    {
                        CopyToClipboard(BuildSelectedCacheRowsTsv());
                    }
                }

                if (GUILayout.Button("Copy Visible", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                {
                    CopyToClipboard(BuildVisibleCacheRowsTsv());
                }

                if (GUILayout.Button("Reset Columns", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                {
                    ResetColumns();
                }

                GUILayout.Space(8f);
                GUILayout.Label("Filter:", GUILayout.Width(38f));
                _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200f));
                if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                {
                    _searchFilter = string.Empty;
                }
            }
        }

        private void DrawStatPill(string text, Color color)
        {
            EnsureLayoutOptions();
            Color previous = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, _pillStyle, _pillOpts);
            GUI.color = previous;
        }

        private void DrawHeader()
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
                _cellA.text = _columns[i].Header;
                _cellA.tooltip = _columns[i].Tooltip;
                GUI.Label(cellRect, _cellA, _headerCellStyle);
                DrawColumnSeparator(cellRect);
                HandleColumnResize(i, cellRect);
                x += _columns[i].Width;
            }
        }

        private bool DrawItem(int index, int rowIndex, int visibleIndex)
        {
            var item = _allList[index];
            var view = _allViews[index];

            EnsureColumns();

            Color rowBg = IsCacheSelected(item.CacheKey)
                ? RowSelected
                : view.RefKind == 2
                    ? RowHighRef
                    : rowIndex % 2 == 0 ? RowEven : RowOdd;

            float tableWidth = GetTableWidth();
            Rect rowRect = GUILayoutUtility.GetRect(tableWidth, tableWidth, ROW_HEIGHT, ROW_HEIGHT, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rowRect, rowBg);
            }

            float x = rowRect.x;
            DrawLocationCell(NextCell(ref x, rowRect, COL_LOCATION), item, view);

            Color refColor = view.RefKind == 1 ? GetTierColor(view.Tier) : view.RefKind == 2 ? new Color(1f, 0.7f, 0.2f) : Color.white;
            DrawTextCell(NextCell(ref x, rowRect, COL_REFS), view.RefsText, _numericStyle, refColor, view.RefsTooltip);
            DrawTextCell(NextCell(ref x, rowRect, COL_HITS), view.HitsText, _numericStyle, Color.white, null);
            DrawTextCell(NextCell(ref x, rowRect, COL_PROVIDER), item.ProviderType, _rowStyle, GetProviderColor(item.ProviderType), item.ProviderType);
            DrawTextCell(NextCell(ref x, rowRect, COL_BUCKET), string.IsNullOrEmpty(item.Bucket) ? MISSING_TEXT : item.Bucket, _rowStyle, string.IsNullOrEmpty(item.Bucket) ? DimColor : Color.white, item.Bucket);
            DrawTextCell(NextCell(ref x, rowRect, COL_TAG), string.IsNullOrEmpty(item.Tag) ? MISSING_TEXT : item.Tag, _rowStyle, string.IsNullOrEmpty(item.Tag) ? DimColor : new Color(0.6f, 0.9f, 0.6f), item.Tag);
            DrawTextCell(NextCell(ref x, rowRect, COL_OWNER), string.IsNullOrEmpty(item.Owner) ? MISSING_TEXT : item.Owner, _rowStyle, string.IsNullOrEmpty(item.Owner) ? DimColor : new Color(0.85f, 0.75f, 1.0f), item.Owner);
            DrawTextCell(NextCell(ref x, rowRect, COL_TIER), view.Tier, _rowStyle, GetTierColor(view.Tier), view.TierTooltip);
            DrawTextCell(NextCell(ref x, rowRect, COL_MEMORY), view.MemoryText, _numericStyle, item.EstimatedBytes > 0 ? Color.white : DimColor, view.MemoryTooltip);

            HandleRowInput(rowRect, item, view, visibleIndex);
            return true;
        }

        private void DrawLocationCell(Rect cellRect, in AssetCacheService.CacheDiagnosticEntry item, in CacheRowView view)
        {
            if (view.HasType)
            {
                _cellA.text = view.TypeSuffix;
                _cellA.tooltip = null;
                float typeWidth = Mathf.Min(_typeBadgeStyle.CalcSize(_cellA).x, Mathf.Max(0f, cellRect.width * 0.45f));
                float gap = 2f;
                float locationWidth = Mathf.Max(0f, cellRect.width - typeWidth - gap);

                Rect locationRect = new Rect(cellRect.x, cellRect.y, locationWidth, cellRect.height);
                _cellB.text = item.Location;
                _cellB.tooltip = view.LocationTooltip;
                GUI.Label(locationRect, _cellB, _monoStyle);

                Rect typeRect = new Rect(locationRect.xMax + gap, cellRect.y, typeWidth, cellRect.height);
                _cellA.tooltip = view.TypeName;
                GUI.Label(typeRect, _cellA, _typeBadgeStyle);
            }
            else
            {
                _cellB.text = item.Location;
                _cellB.tooltip = view.LocationTooltip;
                GUI.Label(cellRect, _cellB, _monoStyle);
            }
        }

        private void DrawTextCell(Rect rect, string text, GUIStyle style, Color color, string tooltip)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            _cellA.text = string.IsNullOrEmpty(text) ? MISSING_TEXT : text;
            _cellA.tooltip = tooltip;
            GUI.Label(rect, _cellA, style);
            GUI.contentColor = previous;
        }

        private Rect NextCell(ref float x, Rect rowRect, int columnIndex)
        {
            Rect rect = new Rect(x, rowRect.y, _columns[columnIndex].Width, rowRect.height);
            x += _columns[columnIndex].Width;
            return rect;
        }

        private void DrawTier(int start, int count, string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.Space(2f);
            DrawHeader();

            int rowIndex = 0;
            for (int i = 0; i < _visibleCacheIndices.Count; i++)
            {
                DrawItem(_visibleCacheIndices[i], rowIndex, i);
                rowIndex++;
            }

            if (rowIndex == 0)
            {
                DrawEmptyTableMessage(count == 0 ? "No rows in this tier." : "No rows match the current filter.");
            }
        }

        private void DrawBuckets()
        {
            EditorGUILayout.LabelField(_bucketTitle, EditorStyles.boldLabel);

            bool drewAnyBucket = false;
            int visibleIndex = 0;
            for (int bucketIndex = 0; bucketIndex < _bucketIndexLists.Count; bucketIndex++)
            {
                var indices = _bucketIndexLists[bucketIndex];
                if (!BucketHasVisibleRows(indices))
                {
                    continue;
                }

                drewAnyBucket = true;
                GUILayout.Space(8f);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label(_bucketNames[bucketIndex], EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(_bucketCounts[bucketIndex], EditorStyles.miniLabel);
                }

                DrawHeader();

                int rowIndex = 0;
                for (int i = 0; i < indices.Count; i++)
                {
                    int index = indices[i];
                    if (_searchFilter.Length > 0 && !MatchesFilter(_allList[index]))
                    {
                        continue;
                    }

                    DrawItem(index, rowIndex, visibleIndex);
                    rowIndex++;
                    visibleIndex++;
                }
            }

            if (!drewAnyBucket)
            {
                DrawEmptyTableMessage(_bucketIndexLists.Count == 0 ? "No bucket data." : "No bucket rows match the current filter.");
            }
        }

        private void BuildVisibleCacheIndices(List<int> target)
        {
            target.Clear();

            switch (_selectedTab)
            {
                case 0:
                    AppendVisibleCacheRange(0, _activeCount, target);
                    break;
                case 1:
                    AppendVisibleCacheRange(_activeCount, _trialCount, target);
                    break;
                case 2:
                    AppendVisibleCacheRange(_activeCount + _trialCount, _mainCount, target);
                    break;
                case 3:
                    for (int bucketIndex = 0; bucketIndex < _bucketIndexLists.Count; bucketIndex++)
                    {
                        var indices = _bucketIndexLists[bucketIndex];
                        for (int i = 0; i < indices.Count; i++)
                        {
                            int index = indices[i];
                            if (_searchFilter.Length == 0 || MatchesFilter(_allList[index]))
                            {
                                target.Add(index);
                            }
                        }
                    }
                    break;
            }
        }

        private void AppendVisibleCacheRange(int start, int count, List<int> target)
        {
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                if (_searchFilter.Length == 0 || MatchesFilter(_allList[i]))
                {
                    target.Add(i);
                }
            }
        }

        private bool BucketHasVisibleRows(List<int> indices)
        {
            if (_searchFilter.Length == 0)
            {
                return indices.Count > 0;
            }

            for (int i = 0; i < indices.Count; i++)
            {
                if (MatchesFilter(_allList[indices[i]]))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawSummary()
        {
            EditorGUILayout.LabelField("Cache Summary", EditorStyles.boldLabel);
            GUILayout.Space(6f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Total cached assets", _sumTotal);
                DrawSummaryRow("Active (in-use, RefCount > 0)", _sumActive);
                DrawSummaryRow("Trial pool (idle probation)", _sumTrial);
                DrawSummaryRow("Main pool (idle hot cache)", _sumMain);
                DrawSummaryRow("Total live ref-count sum", _sumRefs);
                DrawSummaryRow("Highest AccessCount (hottest asset)", _sumMaxHits);
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Memory Footprint (approx.)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Active (in-use) assets", _sumActiveBytes);
                DrawSummaryRow("Idle pool (evictable)", _sumIdleBytes);
                DrawSummaryRow("Idle memory budget", _sumBudget);
                DrawPercentBar(_sumIdleBarLabel, _sumIdlePct, _sumIdlePct > 0.9f ? new Color(0.9f, 0.3f, 0.2f) : new Color(0.2f, 0.6f, 1.0f));
            }

            if (_sumHasAnomalies)
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Ref-Count Anomalies (> 8)", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int i = 0; i < _anomalyRefs.Count; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUI.contentColor = new Color(1f, 0.7f, 0.2f);
                            GUILayout.Label(_anomalyRefs[i], EditorStyles.boldLabel, GUILayout.Width(70f));
                            GUI.contentColor = Color.white;
                            GUILayout.Label(_anomalyLocs[i], _monoStyle);
                        }
                    }

                    EditorGUILayout.HelpBox(
                        "RefCount > 8 may indicate callers are loading the same asset repeatedly without releasing.\n" +
                        "Verify each LoadAssetAsync() has a corresponding Dispose().",
                        MessageType.Warning);
                }
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Assets by Provider", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("YooAsset", _sumYoo, new Color(1f, 0.6f, 0f));
                DrawSummaryRow("Addressables", _sumAddr, new Color(0.2f, 0.6f, 1f));
                DrawSummaryRow("Resources", _sumRes, Color.gray);
                if (_sumOtherCount > 0)
                {
                    DrawSummaryRow("Other", _sumOther);
                }
            }

            if (_ownerLabels.Count > 0)
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Assets by Owner", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int i = 0; i < _ownerLabels.Count; i++)
                    {
                        DrawSummaryRow(_ownerLabels[i], _ownerValues[i], new Color(0.85f, 0.75f, 1.0f));
                    }
                }
            }

            if (_tagLabels.Count > 0)
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Assets by Tag", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int i = 0; i < _tagLabels.Count; i++)
                    {
                        DrawSummaryRow(_tagLabels[i], _tagValues[i], new Color(0.6f, 0.9f, 0.6f));
                    }
                }
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Distribution", EditorStyles.boldLabel);
            int total = _allList.Count;
            if (total > 0)
            {
                DrawProgressBar("Active", _activeCount, total, new Color(0.2f, 0.7f, 0.3f));
                DrawProgressBar("Trial", _trialCount, total, new Color(0.9f, 0.6f, 0.1f));
                DrawProgressBar("Main", _mainCount, total, new Color(0.2f, 0.5f, 1.0f));
            }
        }

        private void DrawSummaryRow(string label, string value, Color? valueColor = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(240f));
                if (valueColor.HasValue)
                {
                    GUI.contentColor = valueColor.Value;
                }

                GUILayout.Label(value, EditorStyles.boldLabel);
                GUI.contentColor = Color.white;
            }
        }

        private void DrawProgressBar(string label, int count, int total, Color fill)
        {
            EnsureLayoutOptions();
            float value = total > 0 ? (float)count / total : 0f;
            Rect rect = GUILayoutUtility.GetRect(18f, 18f, _expandWidth);
            rect = new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * value, rect.height), fill);
            EditorGUI.LabelField(rect, "  " + label + ": " + count + " (" + (value * 100f).ToString("F1") + "%)", EditorStyles.whiteLabel);
        }

        private void DrawPercentBar(string label, float value, Color fill)
        {
            EnsureLayoutOptions();
            value = Mathf.Clamp01(value);
            Rect rect = GUILayoutUtility.GetRect(18f, 18f, _expandWidth);
            rect = new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * value, rect.height), fill);
            EditorGUI.LabelField(rect, label, EditorStyles.whiteLabel);
        }

        private void EnsureLayoutOptions()
        {
            if (_layoutOptionsBuilt)
            {
                return;
            }

            _pillOpts = new[] { GUILayout.ExpandWidth(false) };
            _expandWidth = new[] { GUILayout.ExpandWidth(true) };
            _layoutOptionsBuilt = true;
        }

        private void EnsureColumns()
        {
            if (_columns != null)
            {
                return;
            }

            float availableWidth = Mathf.Max(720f, position.width - 24f);
            float fixedWidth = 46f + 46f + 112f + 130f + 96f + 120f + 70f + 84f;
            float locationWidth = Mathf.Max(260f, availableWidth - fixedWidth);

            _columns = new[]
            {
                new TableColumn("Location", locationWidth, 180f, "Clean asset location. The type badge is shown inline when available."),
                new TableColumn("Refs", 46f, 42f, "Current reference count."),
                new TableColumn("Hits", 46f, 42f, "Access count used by the cache policy."),
                new TableColumn("Provider", 112f, 88f, "Provider or handle backend."),
                new TableColumn("Bucket", 130f, 86f, "Logical lifetime bucket."),
                new TableColumn("Tag", 96f, 68f, "Optional diagnostic tag."),
                new TableColumn("Owner", 120f, 86f, "Optional diagnostic owner."),
                new TableColumn("Tier", 70f, 58f, "Cache tier."),
                new TableColumn("Memory", 84f, 76f, "Estimated runtime footprint.")
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
            if (_selectedCacheKeys.Count > 0)
            {
                menu.AddItem(new GUIContent("Copy Selected Rows/As TSV"), false, () => CopyToClipboard(BuildSelectedCacheRowsTsv()));
                menu.AddItem(new GUIContent("Copy Selected Rows/As JSON"), false, () => CopyToClipboard(BuildSelectedCacheRowsJson()));
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent("Copy Visible Rows/As TSV"), false, () => CopyToClipboard(BuildVisibleCacheRowsTsv()));
            menu.AddItem(new GUIContent("Copy Visible Rows/As JSON"), false, () => CopyToClipboard(BuildVisibleCacheRowsJson()));
            menu.AddSeparator(string.Empty);
            if (_selectedCacheKeys.Count > 0)
            {
                menu.AddItem(new GUIContent("Clear Selection"), false, () =>
                {
                    ClearCacheSelection();
                    Repaint();
                });
            }

            menu.AddItem(new GUIContent("Reset Column Widths"), false, ResetColumns);
            menu.ShowAsContext();
            evt.Use();
        }

        private void HandleRowInput(Rect rowRect, in AssetCacheService.CacheDiagnosticEntry item, in CacheRowView view, int visibleIndex)
        {
            Event evt = Event.current;
            if (!rowRect.Contains(evt.mousePosition) || evt.type != EventType.MouseDown)
            {
                return;
            }

            if (evt.button == 0)
            {
                SelectCacheRow(item.CacheKey, visibleIndex, evt);
                Repaint();
                evt.Use();
                return;
            }

            if (evt.button != 1)
            {
                return;
            }

            if (!IsCacheSelected(item.CacheKey))
            {
                SelectSingleCacheRow(item.CacheKey, visibleIndex);
            }

            ShowRowContextMenu(item, view);
            evt.Use();
        }

        private bool IsCacheSelected(string cacheKey)
        {
            return !string.IsNullOrEmpty(cacheKey) && _selectedCacheKeys.Contains(cacheKey);
        }

        private void SelectCacheRow(string cacheKey, int visibleIndex, Event evt)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return;
            }

            bool additive = evt.control || evt.command;
            if (evt.shift && _lastSelectedCacheVisibleIndex >= 0)
            {
                if (!additive)
                {
                    _selectedCacheKeys.Clear();
                }

                SelectVisibleCacheRange(_lastSelectedCacheVisibleIndex, visibleIndex);
                UpdateCacheSelectionText();
                return;
            }

            if (additive)
            {
                if (!_selectedCacheKeys.Add(cacheKey))
                {
                    _selectedCacheKeys.Remove(cacheKey);
                }
            }
            else
            {
                _selectedCacheKeys.Clear();
                _selectedCacheKeys.Add(cacheKey);
            }

            _lastSelectedCacheVisibleIndex = visibleIndex;
            UpdateCacheSelectionText();
        }

        private void SelectSingleCacheRow(string cacheKey, int visibleIndex)
        {
            _selectedCacheKeys.Clear();
            if (!string.IsNullOrEmpty(cacheKey))
            {
                _selectedCacheKeys.Add(cacheKey);
            }

            _lastSelectedCacheVisibleIndex = visibleIndex;
            UpdateCacheSelectionText();
        }

        private void SelectVisibleCacheRange(int from, int to)
        {
            int min = Mathf.Min(from, to);
            int max = Mathf.Max(from, to);
            min = Mathf.Clamp(min, 0, _visibleCacheIndices.Count - 1);
            max = Mathf.Clamp(max, 0, _visibleCacheIndices.Count - 1);

            for (int i = min; i <= max; i++)
            {
                string cacheKey = _allList[_visibleCacheIndices[i]].CacheKey;
                if (!string.IsNullOrEmpty(cacheKey))
                {
                    _selectedCacheKeys.Add(cacheKey);
                }
            }
        }

        private void ClearCacheSelection()
        {
            _selectedCacheKeys.Clear();
            _lastSelectedCacheVisibleIndex = -1;
            UpdateCacheSelectionText();
        }

        private void PruneCacheSelection()
        {
            if (_selectedCacheKeys.Count == 0)
            {
                return;
            }

            _cacheSelectionPruneList.Clear();
            foreach (string key in _selectedCacheKeys)
            {
                if (!ContainsCacheKey(key))
                {
                    _cacheSelectionPruneList.Add(key);
                }
            }

            for (int i = 0; i < _cacheSelectionPruneList.Count; i++)
            {
                _selectedCacheKeys.Remove(_cacheSelectionPruneList[i]);
            }

            if (_selectedCacheKeys.Count == 0)
            {
                _lastSelectedCacheVisibleIndex = -1;
            }

            UpdateCacheSelectionText();
        }

        private bool ContainsCacheKey(string cacheKey)
        {
            for (int i = 0; i < _allList.Count; i++)
            {
                if (string.Equals(_allList[i].CacheKey, cacheKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateCacheSelectionText()
        {
            _selectedCacheText = _selectedCacheKeys.Count > 0 ? "Selected: " + _selectedCacheKeys.Count : string.Empty;
        }

        private void ShowRowContextMenu(AssetCacheService.CacheDiagnosticEntry item, CacheRowView view)
        {
            var menu = new GenericMenu();

            string fullRow = BuildCacheRowDetails(item, view);
            string rowTsv = BuildCacheRowTsv(item, view);
            string rowJson = BuildCacheRowJson(item, view);

            menu.AddItem(new GUIContent("Copy/Full Row"), false, () => CopyToClipboard(fullRow));
            menu.AddItem(new GUIContent("Copy/Row as TSV"), false, () => CopyToClipboard(rowTsv));
            menu.AddItem(new GUIContent("Copy/Row as JSON"), false, () => CopyToClipboard(rowJson));
            menu.AddSeparator("Copy/");
            AddCopyValue(menu, "Copy/Location", item.Location);
            AddCopyValue(menu, "Copy/Cache Key", item.CacheKey);
            AddCopyValue(menu, "Copy/Asset Type", item.AssetType);
            AddCopyValue(menu, "Copy/Provider", item.ProviderType);
            AddCopyValue(menu, "Copy/Bucket", item.Bucket);
            AddCopyValue(menu, "Copy/Tag", item.Tag);
            AddCopyValue(menu, "Copy/Owner", item.Owner);
            AddCopyValue(menu, "Copy/Tier", view.Tier);
            AddCopyValue(menu, "Copy/Memory", view.MemoryText);

            menu.AddSeparator(string.Empty);
            if (_selectedCacheKeys.Count > 0)
            {
                menu.AddItem(new GUIContent("Copy Selected Rows/As TSV"), false, () => CopyToClipboard(BuildSelectedCacheRowsTsv()));
                menu.AddItem(new GUIContent("Copy Selected Rows/As JSON"), false, () => CopyToClipboard(BuildSelectedCacheRowsJson()));
                menu.AddItem(new GUIContent("Selection/Clear Selection"), false, () =>
                {
                    ClearCacheSelection();
                    Repaint();
                });
                menu.AddSeparator(string.Empty);
            }

            menu.AddItem(new GUIContent("Copy Visible Rows/As TSV"), false, () => CopyToClipboard(BuildVisibleCacheRowsTsv()));
            menu.AddItem(new GUIContent("Copy Visible Rows/As JSON"), false, () => CopyToClipboard(BuildVisibleCacheRowsJson()));

            if (IsProjectAssetPath(item.Location))
            {
                string location = item.Location;
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Actions/Ping Asset"), false, () => PingAsset(location));
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

        private string BuildCacheRowDetails(AssetCacheService.CacheDiagnosticEntry item, CacheRowView view)
        {
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("Asset Cache Row");
            _copyBuilder.AppendLine("Location: " + SafeText(item.Location));
            _copyBuilder.AppendLine("Cache Key: " + SafeText(item.CacheKey));
            _copyBuilder.AppendLine("Asset Type: " + SafeText(item.AssetType));
            _copyBuilder.AppendLine("Refs: " + item.RefCount);
            _copyBuilder.AppendLine("Hits: " + item.AccessCount);
            _copyBuilder.AppendLine("Provider: " + SafeText(item.ProviderType));
            _copyBuilder.AppendLine("Bucket: " + SafeText(item.Bucket));
            _copyBuilder.AppendLine("Tag: " + SafeText(item.Tag));
            _copyBuilder.AppendLine("Owner: " + SafeText(item.Owner));
            _copyBuilder.AppendLine("Tier: " + SafeText(view.Tier));
            _copyBuilder.AppendLine("Memory: " + SafeText(view.MemoryText));
            return _copyBuilder.ToString();
        }

        private static string BuildCacheRowTsv(AssetCacheService.CacheDiagnosticEntry item, CacheRowView view)
        {
            return SanitizeTsv(item.Location) + "\t" +
                SanitizeTsv(item.AssetType) + "\t" +
                item.RefCount + "\t" +
                item.AccessCount + "\t" +
                SanitizeTsv(item.ProviderType) + "\t" +
                SanitizeTsv(item.Bucket) + "\t" +
                SanitizeTsv(item.Tag) + "\t" +
                SanitizeTsv(item.Owner) + "\t" +
                SanitizeTsv(view.Tier) + "\t" +
                SanitizeTsv(view.MemoryText) + "\t" +
                SanitizeTsv(item.CacheKey);
        }

        private static string BuildCacheRowJson(AssetCacheService.CacheDiagnosticEntry item, CacheRowView view)
        {
            var builder = new StringBuilder(256);
            AppendCacheRowJson(builder, item, view);
            return builder.ToString();
        }

        private string BuildVisibleCacheRowsTsv()
        {
            BuildVisibleCacheIndices(_visibleCacheIndices);
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("Location\tAssetType\tRefs\tHits\tProvider\tBucket\tTag\tOwner\tTier\tMemory\tCacheKey");
            AppendVisibleCacheRows(false, _copyBuilder);
            return _copyBuilder.ToString();
        }

        private string BuildVisibleCacheRowsJson()
        {
            BuildVisibleCacheIndices(_visibleCacheIndices);
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("[");
            AppendVisibleCacheRows(true, _copyBuilder);
            _copyBuilder.AppendLine();
            _copyBuilder.Append("]");
            return _copyBuilder.ToString();
        }

        private void AppendVisibleCacheRows(bool json, StringBuilder builder)
        {
            bool first = true;
            for (int i = 0; i < _visibleCacheIndices.Count; i++)
            {
                AppendCacheRow(_visibleCacheIndices[i], json, builder, ref first);
            }
        }

        private string BuildSelectedCacheRowsTsv()
        {
            BuildVisibleCacheIndices(_visibleCacheIndices);
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("Location\tAssetType\tRefs\tHits\tProvider\tBucket\tTag\tOwner\tTier\tMemory\tCacheKey");
            AppendSelectedCacheRows(false, _copyBuilder);
            return _copyBuilder.ToString();
        }

        private string BuildSelectedCacheRowsJson()
        {
            BuildVisibleCacheIndices(_visibleCacheIndices);
            _copyBuilder.Length = 0;
            _copyBuilder.AppendLine("[");
            AppendSelectedCacheRows(true, _copyBuilder);
            _copyBuilder.AppendLine();
            _copyBuilder.Append("]");
            return _copyBuilder.ToString();
        }

        private void AppendSelectedCacheRows(bool json, StringBuilder builder)
        {
            bool first = true;
            for (int i = 0; i < _visibleCacheIndices.Count; i++)
            {
                int index = _visibleCacheIndices[i];
                if (_selectedCacheKeys.Contains(_allList[index].CacheKey))
                {
                    AppendCacheRow(index, json, builder, ref first);
                }
            }
        }

        private void AppendCacheRow(int index, bool json, StringBuilder builder, ref bool first)
        {
            var item = _allList[index];
            if (_searchFilter.Length > 0 && !MatchesFilter(item))
            {
                return;
            }

            if (json)
            {
                if (!first)
                {
                    builder.AppendLine(",");
                }

                builder.Append("  ");
                AppendCacheRowJson(builder, item, _allViews[index]);
            }
            else
            {
                builder.AppendLine(BuildCacheRowTsv(item, _allViews[index]));
            }

            first = false;
        }

        private static void AppendCacheRowJson(StringBuilder builder, AssetCacheService.CacheDiagnosticEntry item, CacheRowView view)
        {
            builder.Append('{');
            AppendJsonProperty(builder, "location", item.Location, false);
            AppendJsonProperty(builder, "assetType", item.AssetType, true);
            AppendJsonProperty(builder, "refs", item.RefCount, true);
            AppendJsonProperty(builder, "hits", item.AccessCount, true);
            AppendJsonProperty(builder, "provider", item.ProviderType, true);
            AppendJsonProperty(builder, "bucket", item.Bucket, true);
            AppendJsonProperty(builder, "tag", item.Tag, true);
            AppendJsonProperty(builder, "owner", item.Owner, true);
            AppendJsonProperty(builder, "tier", view.Tier, true);
            AppendJsonProperty(builder, "memory", view.MemoryText, true);
            AppendJsonProperty(builder, "estimatedBytes", item.EstimatedBytes, true);
            AppendJsonProperty(builder, "cacheKey", item.CacheKey, true);
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

        private bool MatchesFilter(AssetCacheService.CacheDiagnosticEntry entry)
        {
            return (entry.Location != null && entry.Location.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (entry.CacheKey != null && entry.CacheKey.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (entry.AssetType != null && entry.AssetType.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (entry.Bucket != null && entry.Bucket.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (entry.Tag != null && entry.Tag.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (entry.Owner != null && entry.Owner.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (entry.ProviderType != null && entry.ProviderType.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void DrawEmptyTableMessage(string message)
        {
            GUILayout.Space(10f);
            EditorGUILayout.LabelField(message, EditorStyles.centeredGreyMiniLabel);
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

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            if (bytes < 1024L)
            {
                return bytes + " B";
            }

            if (bytes < 1024L * 1024L)
            {
                return (bytes / 1024.0).ToString("F1") + " KB";
            }

            if (bytes < 1024L * 1024L * 1024L)
            {
                return (bytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
            }

            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
        }

        private static Color GetProviderColor(string provider)
        {
            switch (provider)
            {
                case "YooAsset":
                    return new Color(1.0f, 0.62f, 0.0f);
                case "Addressables":
                    return new Color(0.3f, 0.65f, 1.0f);
                case "Resources":
                    return new Color(0.6f, 0.6f, 0.6f);
                default:
                    return Color.white;
            }
        }

        private static Color GetTierColor(string tier)
        {
            switch (tier)
            {
                case "Active":
                    return new Color(0.2f, 0.85f, 0.4f);
                case "Trial":
                    return new Color(1.0f, 0.75f, 0.1f);
                case "Main":
                    return new Color(0.3f, 0.6f, 1.0f);
                default:
                    return Color.white;
            }
        }

        private static string GetTierTooltip(string tier)
        {
            switch (tier)
            {
                case "Active":
                    return "Asset is actively in use (RefCount > 0). A handle caller is holding a reference.";
                case "Trial":
                    return "Asset is in the Trial (W-LRU) idle pool.\nRefCount = 0 - no caller holds it, but AssetCacheService keeps it loaded for fast re-use.\nEvicted if the pool is full and a new asset arrives.";
                case "Main":
                    return "Asset is in the Main (LFU+LRU) hot cache.\nRefCount = 0 - promoted here due to high access frequency.\nKept resident for instant re-use. Not a leak.";
                default:
                    return string.Empty;
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

        private struct CacheRowView
        {
            public string LocationTooltip;
            public string TypeSuffix;
            public string TypeName;
            public bool HasType;
            public string RefsText;
            public string RefsTooltip;
            public byte RefKind;
            public string HitsText;
            public string Tier;
            public string TierTooltip;
            public byte TierKind;
            public string MemoryText;
            public string MemoryTooltip;
        }
    }
}
#endif
