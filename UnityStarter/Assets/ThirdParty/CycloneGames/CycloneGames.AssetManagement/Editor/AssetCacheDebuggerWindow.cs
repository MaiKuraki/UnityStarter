#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Editor
{
    public class AssetCacheDebuggerWindow : EditorWindow
    {
        // ── Layout constants ────────────────────────────────────────────────────
        private const float REFS_W = 38f;
        private const float HITS_W = 38f;
        private const float PROVIDER_W = 90f;
        private const float BUCKET_W = 100f;
        private const float TAG_W = 74f;
        private const float OWNER_W = 90f;
        private const float TIER_W = 54f;
        private const float MIN_LOC_W = 80f;
        private float LocationWidth => Mathf.Max(MIN_LOC_W,
            position.width - REFS_W - HITS_W - PROVIDER_W - BUCKET_W - TAG_W - OWNER_W - TIER_W - 34f);

        // ── Tabs ────────────────────────────────────────────────────────────────
        private static readonly string[] _tabs = { "Active (ARC)", "Trial (LRU)", "Main (LFU/LRU)", "Buckets", "Summary" };
        private int _selectedTab;

        // ── 0-GC pre-allocated diagnostic lists ─────────────────────────────────
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _activeList = new List<AssetCacheService.CacheDiagnosticEntry>(512);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _trialList = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _mainList = new List<AssetCacheService.CacheDiagnosticEntry>(256);

        // Merged entries + parallel, pre-formatted view models for all three tiers.
        // Index layout: [0, _activeCount) active, then trial, then main.
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _allList = new List<AssetCacheService.CacheDiagnosticEntry>(1024);
        private readonly List<CacheRowView> _allViews = new List<CacheRowView>(1024);
        private int _activeCount, _trialCount, _mainCount;

        // Memory-budget snapshot for the selected cache instance.
        private long _idleBytesApprox;
        private long _maxIdleBytesBudget;

        // Bucket grouping — pooled index lists into _allList/_allViews, materialised at refresh.
        private readonly Dictionary<string, List<int>> _bucketGroups = new Dictionary<string, List<int>>();
        private readonly Stack<List<int>> _bucketListPool = new Stack<List<int>>();
        private readonly List<string> _bucketNames = new List<string>();
        private readonly List<string> _bucketCounts = new List<string>();
        private readonly List<List<int>> _bucketIndexLists = new List<List<int>>();
        private string _bucketTitle = "Bucket View";

        // Cached titles + stat pills (rebuilt only on refresh).
        private string _activeTitle, _trialTitle, _mainTitle;
        private string _pillActive = "  Active: 0  ", _pillTrial = "  Trial: 0  ", _pillMain = "  Main: 0  ", _pillTotal = "  Total: 0  ";

        // Summary cache (rebuilt only on refresh).
        private string _sumTotal, _sumActive, _sumTrial, _sumMain, _sumRefs, _sumMaxHits;
        private string _sumActiveBytes, _sumIdleBytes, _sumBudget, _sumIdleBarLabel;
        private float _sumIdlePct;
        private string _sumYoo, _sumAddr, _sumRes, _sumOther;
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

        // Reusable content + cached instance names.
        private readonly GUIContent _cellA = new GUIContent();
        private readonly GUIContent _cellB = new GUIContent();
        private string[] _instanceNames = System.Array.Empty<string>();

        // ── Search ──────────────────────────────────────────────────────────────
        private string _searchFilter = string.Empty;

        // ── Multi-instance selector ──────────────────────────────────────────────
        private int _selectedInstanceIndex;

        // ── Scroll ──────────────────────────────────────────────────────────────
        private Vector2 _scrollPos;
        private bool _hasSnapshot;

        // ── Cached GUILayoutOption arrays (alloc-free GUILayout) ──────────────────
        private GUILayoutOption[] _wLoc, _wRefs, _wHits, _wProvider, _wBucket, _wTag, _wOwner, _wTier, _pillOpts, _expandWidth;
        private float _lastLocWidth = -1f;
        private bool _widthsBuilt;

        private const string TrialZeroTooltip = "RefCount = 0 — asset is in the Trial (probation) idle pool.\nAssetCacheService is holding it for fast re-use.\nNot a leak.";
        private const string MainZeroTooltip = "RefCount = 0 — asset is in the Main (hot) idle pool.\nPromoted due to high access frequency; kept resident for instant re-use.\nNot a leak.";

        // ── Alternating row colors (skin-aware) ──────────────────────────────────
        private static bool IsPro => EditorGUIUtility.isProSkin;
        private static Color RowEven => IsPro ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f);
        private static Color RowOdd => IsPro ? new Color(0.19f, 0.19f, 0.19f, 1f) : new Color(0.80f, 0.80f, 0.80f, 1f);
        // Row tint for Active entries whose RefCount is suspiciously high
        private static Color RowHighRef => IsPro ? new Color(0.30f, 0.22f, 0.12f, 1f) : new Color(0.98f, 0.88f, 0.74f, 1f);
        private static Color DimColor => IsPro ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.5f, 0.5f, 0.5f);

        // ── Lazy GUIStyles ───────────────────────────────────────────────────────
        private GUIStyle _rowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _pillStyle;
        private GUIStyle _monoStyle;
        private GUIStyle _typeBadgeStyle;
        private bool _stylesBuilt;

        [MenuItem("Tools/CycloneGames/AssetManagement/Asset Cache Debugger")]
        public static void ShowWindow()
        {
            var w = GetWindow<AssetCacheDebuggerWindow>("Asset Cache Debugger");
            w.minSize = new Vector2(720, 420);
            w.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            RefreshSnapshot();
        }
        private void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        // Repaint at ~10 fps to keep the display live without burning CPU.
        private double _nextRepaint;
        private void OnEditorUpdate()
        {
            if (!Application.isPlaying) return;
            if (EditorApplication.timeSinceStartup >= _nextRepaint)
            {
                _nextRepaint = EditorApplication.timeSinceStartup + 0.1;
                RefreshSnapshot();
                Repaint();
            }
        }

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _rowStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip
            };

            _headerStyle = new GUIStyle(EditorStyles.toolbar)
            {
                fontStyle = FontStyle.Bold,
                fixedHeight = 20f
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
                padding = new RectOffset(4, 4, 2, 2)
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

            // ── Instance selector ────────────────────────────────────────────────
            if (instances.Count > 1)
            {
                if (_instanceNames.Length != instances.Count)
                {
                    _instanceNames = new string[instances.Count];
                    for (int i = 0; i < instances.Count; i++) _instanceNames[i] = "Instance #" + i;
                }
                _selectedInstanceIndex = Mathf.Clamp(_selectedInstanceIndex, 0, instances.Count - 1);
                int newIndex = EditorGUILayout.Popup("Cache Instance", _selectedInstanceIndex, _instanceNames);
                if (newIndex != _selectedInstanceIndex)
                {
                    _selectedInstanceIndex = newIndex;
                    RefreshSnapshot();
                }
            }
            else
            {
                _selectedInstanceIndex = 0;
            }

            if (!_hasSnapshot) RefreshSnapshot();

            // ── Top bar: stats + search ──────────────────────────────────────────
            DrawTopBar();

            // ── Tabs ─────────────────────────────────────────────────────────────
            GUILayout.Space(4f);
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabs, EditorStyles.toolbarButton, GUILayout.Height(22f));
            GUILayout.Space(2f);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case 0: DrawTier(0, _activeCount, _activeTitle); break;
                case 1: DrawTier(_activeCount, _trialCount, _trialTitle); break;
                case 2: DrawTier(_activeCount + _trialCount, _mainCount, _mainTitle); break;
                case 3: DrawBuckets(); break;
                case 4: DrawSummary(); break;
            }

            EditorGUILayout.EndScrollView();

        }

        private void RefreshSnapshot()
        {
            _activeList.Clear();
            _trialList.Clear();
            _mainList.Clear();
            _allList.Clear();
            _allViews.Clear();
            _activeCount = _trialCount = _mainCount = 0;

            var instances = AssetCacheService.GlobalInstances;
            if (instances == null || instances.Count == 0)
            {
                BuildBuckets();
                BuildSummary();
                _hasSnapshot = true;
                return;
            }

            _selectedInstanceIndex = Mathf.Clamp(_selectedInstanceIndex, 0, instances.Count - 1);
            var svc = instances[_selectedInstanceIndex];
            svc.GetDiagnostics(_activeList, _trialList, _mainList);
            _idleBytesApprox = svc.IdleBytesApprox;
            _maxIdleBytesBudget = svc.MaxIdleBytesBudget;

            for (int i = 0; i < _activeList.Count; i++) { _allList.Add(_activeList[i]); _allViews.Add(BuildView(_activeList[i], 0)); }
            for (int i = 0; i < _trialList.Count; i++) { _allList.Add(_trialList[i]); _allViews.Add(BuildView(_trialList[i], 1)); }
            for (int i = 0; i < _mainList.Count; i++) { _allList.Add(_mainList[i]); _allViews.Add(BuildView(_mainList[i], 2)); }
            _activeCount = _activeList.Count;
            _trialCount = _trialList.Count;
            _mainCount = _mainList.Count;

            BuildBuckets();
            BuildSummary();
            _hasSnapshot = true;
        }

        private CacheRowView BuildView(in AssetCacheService.CacheDiagnosticEntry e, byte tierKind)
        {
            bool refAnomaly = tierKind == 0 && e.RefCount > 8;
            byte refKind = e.RefCount == 0 ? (byte)1 : refAnomaly ? (byte)2 : (byte)0;
            string refsTooltip = null;
            if (refKind == 1)
                refsTooltip = tierKind == 1 ? TrialZeroTooltip : tierKind == 2 ? MainZeroTooltip : "RefCount = 0";
            else if (refKind == 2)
                refsTooltip = "RefCount = " + e.RefCount + " is unusually high.\nVerify that all callers are correctly calling Dispose().";
            string tier = tierKind == 0 ? "Active" : tierKind == 1 ? "Trial" : "Main";
            return new CacheRowView
            {
                LocationTooltip = string.IsNullOrEmpty(e.AssetType) ? e.Location : e.Location + "\n[" + e.AssetType + "]",
                TypeSuffix = string.IsNullOrEmpty(e.AssetType) ? null : " (" + e.AssetType + ")",
                TypeName = e.AssetType,
                HasType = !string.IsNullOrEmpty(e.AssetType),
                RefsText = e.RefCount.ToString(),
                RefsTooltip = refsTooltip,
                RefKind = refKind,
                HitsText = e.AccessCount.ToString(),
                Tier = tier,
                TierTooltip = GetTierTooltip(tier),
                TierKind = tierKind
            };
        }

        private void BuildBuckets()
        {
            foreach (var v in _bucketGroups.Values) { v.Clear(); _bucketListPool.Push(v); }
            _bucketGroups.Clear();
            _bucketNames.Clear();
            _bucketCounts.Clear();
            _bucketIndexLists.Clear();

            for (int i = 0; i < _allList.Count; i++)
            {
                string key = _allList[i].Bucket ?? string.Empty;
                if (!_bucketGroups.TryGetValue(key, out var lst))
                {
                    lst = _bucketListPool.Count > 0 ? _bucketListPool.Pop() : new List<int>(16);
                    _bucketGroups[key] = lst;
                }
                lst.Add(i);
            }

            foreach (var kvp in _bucketGroups)
            {
                _bucketNames.Add(string.IsNullOrEmpty(kvp.Key) ? "⬡  [No Bucket]" : "⬡  " + kvp.Key);
                _bucketCounts.Add(kvp.Value.Count + " item(s)");
                _bucketIndexLists.Add(kvp.Value);
            }
            _bucketTitle = "Bucket View  [" + _bucketGroups.Count + " buckets, " + _allList.Count + " total]";
        }

        private void BuildSummary()
        {
            _activeTitle = "Active Handles (RefCount > 0)  [" + _activeCount + "]";
            _trialTitle = "Trial Pool — new assets on probation (W-LRU)  [" + _trialCount + "]";
            _mainTitle = "Main Pool — promoted assets (LFU+LRU)  [" + _mainCount + "]";
            _pillActive = "  Active: " + _activeCount + "  ";
            _pillTrial = "  Trial: " + _trialCount + "  ";
            _pillMain = "  Main: " + _mainCount + "  ";
            _pillTotal = "  Total: " + _allList.Count + "  ";

            int yoo = 0, addr = 0, res = 0, other = 0;
            int totalRefs = 0, maxHits = 0;
            long activeBytes = 0;
            _ownerCounts.Clear(); _tagCounts.Clear();
            _anomalyRefs.Clear(); _anomalyLocs.Clear();

            for (int i = 0; i < _allList.Count; i++)
            {
                var e = _allList[i];
                totalRefs += e.RefCount;
                if (e.AccessCount > maxHits) maxHits = e.AccessCount;
                switch (e.ProviderType)
                {
                    case "YooAsset": yoo++; break;
                    case "Addressables": addr++; break;
                    case "Resources": res++; break;
                    default: other++; break;
                }
                if (!string.IsNullOrEmpty(e.Owner)) { _ownerCounts.TryGetValue(e.Owner, out int oc); _ownerCounts[e.Owner] = oc + 1; }
                if (!string.IsNullOrEmpty(e.Tag)) { _tagCounts.TryGetValue(e.Tag, out int tc); _tagCounts[e.Tag] = tc + 1; }
            }
            for (int i = 0; i < _activeCount; i++)
            {
                var e = _allList[i];
                activeBytes += e.EstimatedBytes;
                if (e.RefCount > 8) { _anomalyRefs.Add("Refs=" + e.RefCount); _anomalyLocs.Add(e.Location); }
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
            _sumYoo = yoo.ToString(); _sumAddr = addr.ToString(); _sumRes = res.ToString(); _sumOther = other.ToString();
            _sumOtherCount = other;

            _ownerLabels.Clear(); _ownerValues.Clear();
            foreach (var kv in _ownerCounts) { _ownerLabels.Add(kv.Key); _ownerValues.Add(kv.Value.ToString()); }
            _tagLabels.Clear(); _tagValues.Clear();
            foreach (var kv in _tagCounts) { _tagLabels.Add(kv.Key); _tagValues.Add(kv.Value.ToString()); }
        }

        // ── Top statistics + search bar ──────────────────────────────────────────
        private void DrawTopBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Scenes", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                    SceneTrackerWindow.ShowWindow();
                if (GUILayout.Button("Governance", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                    AssetRuntimeGovernanceWindow.ShowWindow();
                GUILayout.Space(8f);

                // Stats pills
                DrawStatPill(_pillActive, new Color(0.2f, 0.7f, 0.3f));
                GUILayout.Space(4f);
                DrawStatPill(_pillTrial, new Color(0.9f, 0.6f, 0.1f));
                GUILayout.Space(4f);
                DrawStatPill(_pillMain, new Color(0.2f, 0.5f, 1.0f));
                GUILayout.Space(4f);
                DrawStatPill(_pillTotal, new Color(0.6f, 0.6f, 0.6f));

                GUILayout.FlexibleSpace();

                // Search field
                GUILayout.Label("Filter:", GUILayout.Width(38f));
                _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200f));
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                    _searchFilter = string.Empty;
            }
        }

        private void DrawStatPill(string text, Color color)
        {
            EnsureWidths();
            var prev = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, _pillStyle, _pillOpts);
            GUI.color = prev;
        }

        // ── Table header ────────────────────────────────────────────────────────
        private void DrawHeader()
        {
            EnsureWidths();
            using (new EditorGUILayout.HorizontalScope(_headerStyle))
            {
                GUILayout.Label("Location", EditorStyles.boldLabel, _wLoc);
                GUILayout.Label("Refs", EditorStyles.boldLabel, _wRefs);
                GUILayout.Label("Hits", EditorStyles.boldLabel, _wHits);
                GUILayout.Label("Provider", EditorStyles.boldLabel, _wProvider);
                GUILayout.Label("Bucket", EditorStyles.boldLabel, _wBucket);
                GUILayout.Label("Tag", EditorStyles.boldLabel, _wTag);
                GUILayout.Label("Owner", EditorStyles.boldLabel, _wOwner);
                GUILayout.Label("Tier", EditorStyles.boldLabel, _wTier);
            }
        }

        // ── Single row ──────────────────────────────────────────────────────────
        private void DrawItem(in AssetCacheService.CacheDiagnosticEntry item, in CacheRowView view, int rowIndex)
        {
            if (_searchFilter.Length > 0 && !MatchesFilter(item)) return;

            Color rowBg = view.RefKind == 2 ? RowHighRef : (rowIndex % 2 == 0 ? RowEven : RowOdd);

            var rect = EditorGUILayout.BeginHorizontal();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, rowBg);

            float locW = LocationWidth;

            // ── Location cell with inline type badge ────────────────────────────
            var cellRect = GUILayoutUtility.GetRect(locW, 18f);

            if (view.HasType)
            {
                _cellA.text = view.TypeSuffix; _cellA.tooltip = null;
                float typeW = _typeBadgeStyle.CalcSize(_cellA).x;
                float gap = 2f;

                float locTextW = cellRect.width - typeW - gap;
                var locRect = new Rect(cellRect.x, cellRect.y, locTextW, cellRect.height);
                _cellB.text = item.Location; _cellB.tooltip = view.LocationTooltip;
                GUI.Label(locRect, _cellB, _monoStyle);

                var typeRect = new Rect(locRect.xMax + gap, cellRect.y, typeW, cellRect.height);
                _cellA.tooltip = view.TypeName;
                GUI.Label(typeRect, _cellA, _typeBadgeStyle);
            }
            else
            {
                _cellB.text = item.Location; _cellB.tooltip = view.LocationTooltip;
                GUI.Label(cellRect, _cellB, _monoStyle);
            }

            if (cellRect.Contains(Event.current.mousePosition)
                && Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                var menu = new GenericMenu();
                string loc = item.Location;
                string assetType = item.AssetType;
                menu.AddItem(new GUIContent("Copy Location"), false,
                    () => EditorGUIUtility.systemCopyBuffer = loc);
                if (!string.IsNullOrEmpty(assetType))
                    menu.AddItem(new GUIContent("Copy Type"), false,
                        () => EditorGUIUtility.systemCopyBuffer = assetType);
                menu.ShowAsContext();
                Event.current.Use();
            }

            // ── Refs cell ───────────────────────────────────────────────────────
            if (view.RefKind == 1)
            {
                GUI.contentColor = GetTierColor(view.Tier);
                _cellA.text = view.RefsText; _cellA.tooltip = view.RefsTooltip;
                GUILayout.Label(_cellA, _rowStyle, _wRefs);
                GUI.contentColor = Color.white;
            }
            else if (view.RefKind == 2)
            {
                GUI.contentColor = new Color(1f, 0.7f, 0.2f);
                _cellA.text = view.RefsText; _cellA.tooltip = view.RefsTooltip;
                GUILayout.Label(_cellA, _rowStyle, _wRefs);
                GUI.contentColor = Color.white;
            }
            else
            {
                GUILayout.Label(view.RefsText, _rowStyle, _wRefs);
            }

            GUILayout.Label(view.HitsText, _rowStyle, _wHits);

            GUI.contentColor = GetProviderColor(item.ProviderType);
            GUILayout.Label(item.ProviderType, _rowStyle, _wProvider);
            GUI.contentColor = Color.white;

            GUILayout.Label(string.IsNullOrEmpty(item.Bucket) ? "—" : item.Bucket, _rowStyle, _wBucket);

            // Tag — dim grey when empty
            if (string.IsNullOrEmpty(item.Tag))
            {
                GUI.contentColor = DimColor;
                GUILayout.Label("—", _rowStyle, _wTag);
                GUI.contentColor = Color.white;
            }
            else
            {
                GUI.contentColor = new Color(0.6f, 0.9f, 0.6f);
                GUILayout.Label(item.Tag, _rowStyle, _wTag);
                GUI.contentColor = Color.white;
            }

            // Owner — dim grey when empty
            if (string.IsNullOrEmpty(item.Owner))
            {
                GUI.contentColor = DimColor;
                GUILayout.Label("—", _rowStyle, _wOwner);
                GUI.contentColor = Color.white;
            }
            else
            {
                GUI.contentColor = new Color(0.85f, 0.75f, 1.0f);
                GUILayout.Label(item.Owner, _rowStyle, _wOwner);
                GUI.contentColor = Color.white;
            }

            GUI.contentColor = GetTierColor(view.Tier);
            _cellA.text = view.Tier; _cellA.tooltip = view.TierTooltip;
            GUILayout.Label(_cellA, _rowStyle, _wTier);
            GUI.contentColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        // ── Tier tab ────────────────────────────────────────────────────────────
        private void DrawTier(int start, int count, string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.Space(2f);
            DrawHeader();

            int rowIndex = 0;
            int end = start + count;
            for (int i = start; i < end; i++)
                DrawItem(_allList[i], _allViews[i], rowIndex++);
        }

        // ── Buckets tab ─────────────────────────────────────────────────────────
        private void DrawBuckets()
        {
            EditorGUILayout.LabelField(_bucketTitle, EditorStyles.boldLabel);

            for (int b = 0; b < _bucketIndexLists.Count; b++)
            {
                GUILayout.Space(8f);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label(_bucketNames[b], EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(_bucketCounts[b], EditorStyles.miniLabel);
                }

                DrawHeader();
                var idxs = _bucketIndexLists[b];
                int rowIndex = 0;
                for (int i = 0; i < idxs.Count; i++)
                {
                    int idx = idxs[i];
                    DrawItem(_allList[idx], _allViews[idx], rowIndex++);
                }
            }
        }

        // ── Summary tab ─────────────────────────────────────────────────────────
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
                DrawPercentBar(_sumIdleBarLabel, _sumIdlePct,
                    _sumIdlePct > 0.9f ? new Color(0.9f, 0.3f, 0.2f) : new Color(0.2f, 0.6f, 1.0f));
            }

            // Leak suspect section
            if (_sumHasAnomalies)
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("⚠ Ref-Count Anomalies (> 8)", EditorStyles.boldLabel);
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
                if (_sumOtherCount > 0) DrawSummaryRow("Other", _sumOther);
            }

            if (_ownerLabels.Count > 0)
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Assets by Owner", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int i = 0; i < _ownerLabels.Count; i++)
                        DrawSummaryRow(_ownerLabels[i], _ownerValues[i], new Color(0.85f, 0.75f, 1.0f));
                }
            }

            if (_tagLabels.Count > 0)
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Assets by Tag", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int i = 0; i < _tagLabels.Count; i++)
                        DrawSummaryRow(_tagLabels[i], _tagValues[i], new Color(0.6f, 0.9f, 0.6f));
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
                if (valueColor.HasValue) GUI.contentColor = valueColor.Value;
                GUILayout.Label(value, EditorStyles.boldLabel);
                GUI.contentColor = Color.white;
            }
        }

        private void DrawProgressBar(string label, int count, int total, Color fill)
        {
            EnsureWidths();
            float t = total > 0 ? (float)count / total : 0f;
            var rect = GUILayoutUtility.GetRect(18f, 18f, _expandWidth);
            rect = new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * t, rect.height), fill);
            EditorGUI.LabelField(rect, $"  {label}: {count} ({t * 100f:F1}%)", EditorStyles.whiteLabel);
        }

        // Memory budget bar: shows percentage only (label is pre-formatted, alloc-free per repaint).
        private void DrawPercentBar(string label, float t, Color fill)
        {
            EnsureWidths();
            t = Mathf.Clamp01(t);
            var rect = GUILayoutUtility.GetRect(18f, 18f, _expandWidth);
            rect = new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * t, rect.height), fill);
            EditorGUI.LabelField(rect, label, EditorStyles.whiteLabel);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024L) return $"{bytes} B";
            if (bytes < 1024L * 1024L) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private void EnsureWidths()
        {
            if (!_widthsBuilt)
            {
                _wRefs = new[] { GUILayout.Width(REFS_W) };
                _wHits = new[] { GUILayout.Width(HITS_W) };
                _wProvider = new[] { GUILayout.Width(PROVIDER_W) };
                _wBucket = new[] { GUILayout.Width(BUCKET_W) };
                _wTag = new[] { GUILayout.Width(TAG_W) };
                _wOwner = new[] { GUILayout.Width(OWNER_W) };
                _wTier = new[] { GUILayout.Width(TIER_W) };
                _pillOpts = new[] { GUILayout.ExpandWidth(false) };
                _expandWidth = new[] { GUILayout.ExpandWidth(true) };
                _widthsBuilt = true;
            }
            float lw = LocationWidth;
            if (_wLoc == null || Mathf.Abs(lw - _lastLocWidth) > 0.5f)
            {
                _wLoc = new[] { GUILayout.Width(lw) };
                _lastLocWidth = lw;
            }
        }

        private bool MatchesFilter(AssetCacheService.CacheDiagnosticEntry e)
        {
            return (e.Location != null && e.Location.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                || (e.AssetType != null && e.AssetType.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                || (e.Bucket != null && e.Bucket.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                || (e.Tag != null && e.Tag.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                || (e.Owner != null && e.Owner.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                || (e.ProviderType != null && e.ProviderType.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void DrawPlaceholder(string msg, MessageType type)
        {
            GUILayout.Space(40f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(400f)))
                {
                    EditorGUILayout.HelpBox(msg, type);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private static Color GetProviderColor(string p)
        {
            switch (p)
            {
                case "YooAsset": return new Color(1.0f, 0.62f, 0.0f);
                case "Addressables": return new Color(0.3f, 0.65f, 1.0f);
                case "Resources": return new Color(0.6f, 0.6f, 0.6f);
                default: return Color.white;
            }
        }

        private static Color GetTierColor(string tier)
        {
            switch (tier)
            {
                case "Active": return new Color(0.2f, 0.85f, 0.4f);
                case "Trial": return new Color(1.0f, 0.75f, 0.1f);
                case "Main": return new Color(0.3f, 0.6f, 1.0f);
                default: return Color.white;
            }
        }

        private static string GetTierTooltip(string tier)
        {
            switch (tier)
            {
                case "Active": return "Asset is actively in-use (RefCount > 0). A handle caller is holding a reference.";
                case "Trial": return "Asset is in the Trial (W-LRU) idle pool.\nRefCount = 0 — no caller holds it, but AssetCacheService keeps it loaded\nfor fast re-use. Evicted if the pool is full and a new asset arrives.";
                case "Main": return "Asset is in the Main (LFU+LRU) hot cache.\nRefCount = 0 — promoted here due to high access frequency.\nKept resident for instant re-use. NOT a leak.";
                default: return string.Empty;
            }
        }

        // Pre-formatted, repaint-stable view model for one cache row (built only on refresh).
        private struct CacheRowView
        {
            public string LocationTooltip;
            public string TypeSuffix;   // " (TypeName)" or null
            public string TypeName;
            public bool HasType;
            public string RefsText;
            public string RefsTooltip;
            public byte RefKind;        // 0 = normal, 1 = zero (idle), 2 = anomaly
            public string HitsText;
            public string Tier;
            public string TierTooltip;
            public byte TierKind;       // 0 = active, 1 = trial, 2 = main
        }


    }
}
#endif
