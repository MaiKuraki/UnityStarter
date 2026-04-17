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

        // Merged view of all three tiers for the Buckets and Summary tabs.
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _allList = new List<AssetCacheService.CacheDiagnosticEntry>(1024);

        // Bucket grouping — reuses pooled lists to avoid per-frame allocations.
        private readonly Dictionary<string, List<AssetCacheService.CacheDiagnosticEntry>> _bucketMap
            = new Dictionary<string, List<AssetCacheService.CacheDiagnosticEntry>>();
        private readonly Stack<List<AssetCacheService.CacheDiagnosticEntry>> _bucketListPool
            = new Stack<List<AssetCacheService.CacheDiagnosticEntry>>();

        // ── Search ──────────────────────────────────────────────────────────────
        private string _searchFilter = string.Empty;

        // ── Multi-instance selector ──────────────────────────────────────────────
        private int _selectedInstanceIndex;

        // ── Scroll ──────────────────────────────────────────────────────────────
        private Vector2 _scrollPos;
        private bool _hasSnapshot;

        // ── Alternating row colors ───────────────────────────────────────────────
        private static readonly Color _rowEven = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color _rowOdd = new Color(0.19f, 0.19f, 0.19f, 1f);
        // Row tint for Active entries whose RefCount is suspiciously high
        private static readonly Color _rowHighRef = new Color(0.30f, 0.22f, 0.12f, 1f);

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
                var names = new string[instances.Count];
                for (int i = 0; i < instances.Count; i++) names[i] = $"Instance #{i}";
                _selectedInstanceIndex = Mathf.Clamp(_selectedInstanceIndex, 0, instances.Count - 1);
                int newIndex = EditorGUILayout.Popup("Cache Instance", _selectedInstanceIndex, names);
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
                case 0: DrawList("Active Handles (RefCount > 0)", _activeList, "Active"); break;
                case 1: DrawList("Trial Pool — new assets on probation (W-LRU)", _trialList, "Trial"); break;
                case 2: DrawList("Main Pool — promoted assets (LFU+LRU)", _mainList, "Main"); break;
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

            var instances = AssetCacheService.GlobalInstances;
            if (instances == null || instances.Count == 0)
            {
                _hasSnapshot = true;
                return;
            }

            _selectedInstanceIndex = Mathf.Clamp(_selectedInstanceIndex, 0, instances.Count - 1);
            var svc = instances[_selectedInstanceIndex];
            svc.GetDiagnostics(_activeList, _trialList, _mainList);

            for (int i = 0; i < _activeList.Count; i++) _allList.Add(_activeList[i]);
            for (int i = 0; i < _trialList.Count; i++) _allList.Add(_trialList[i]);
            for (int i = 0; i < _mainList.Count; i++) _allList.Add(_mainList[i]);

            _hasSnapshot = true;
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
                DrawStatPill("Active", _activeList.Count, new Color(0.2f, 0.7f, 0.3f));
                GUILayout.Space(4f);
                DrawStatPill("Trial", _trialList.Count, new Color(0.9f, 0.6f, 0.1f));
                GUILayout.Space(4f);
                DrawStatPill("Main", _mainList.Count, new Color(0.2f, 0.5f, 1.0f));
                GUILayout.Space(4f);
                DrawStatPill("Total", _allList.Count, new Color(0.6f, 0.6f, 0.6f));

                GUILayout.FlexibleSpace();

                // Search field
                GUILayout.Label("Filter:", GUILayout.Width(38f));
                _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200f));
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                    _searchFilter = string.Empty;
            }
        }

        private void DrawStatPill(string label, int count, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUILayout.Label($"  {label}: {count}  ", _pillStyle, GUILayout.ExpandWidth(false));
            GUI.color = prev;
        }

        // ── Table header ────────────────────────────────────────────────────────
        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(_headerStyle))
            {
                GUILayout.Label("Location", EditorStyles.boldLabel, GUILayout.Width(LocationWidth));
                GUILayout.Label("Refs", EditorStyles.boldLabel, GUILayout.Width(REFS_W));
                GUILayout.Label("Hits", EditorStyles.boldLabel, GUILayout.Width(HITS_W));
                GUILayout.Label("Provider", EditorStyles.boldLabel, GUILayout.Width(PROVIDER_W));
                GUILayout.Label("Bucket", EditorStyles.boldLabel, GUILayout.Width(BUCKET_W));
                GUILayout.Label("Tag", EditorStyles.boldLabel, GUILayout.Width(TAG_W));
                GUILayout.Label("Owner", EditorStyles.boldLabel, GUILayout.Width(OWNER_W));
                GUILayout.Label("Tier", EditorStyles.boldLabel, GUILayout.Width(TIER_W));
            }
        }

        // ── Single row ──────────────────────────────────────────────────────────
        private void DrawItem(AssetCacheService.CacheDiagnosticEntry item, string tier, int rowIndex)
        {
            bool hasFilter = _searchFilter.Length > 0;
            if (hasFilter && !MatchesFilter(item)) return;

            // RefCount anomaly: Active entry with unexpectedly high RefCount (> 8)
            bool refAnomaly = tier == "Active" && item.RefCount > 8;
            Color rowBg = refAnomaly ? _rowHighRef : (rowIndex % 2 == 0 ? _rowEven : _rowOdd);

            var rect = EditorGUILayout.BeginHorizontal();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, rowBg);

            float locW = LocationWidth;
            string tooltip = string.IsNullOrEmpty(item.AssetType)
                ? item.Location
                : $"{item.Location}\n[{item.AssetType}]";

            // ── Location cell with inline type badge ────────────────────────────
            var cellRect = GUILayoutUtility.GetRect(locW, 18f);

            if (!string.IsNullOrEmpty(item.AssetType))
            {
                // Measure how wide the type suffix " (TypeName)" needs
                string typeLabel = $" ({item.AssetType})";
                var typeContent = new GUIContent(typeLabel);
                float typeW = _typeBadgeStyle.CalcSize(typeContent).x;
                float gap = 2f;

                // Location takes the remaining space
                float locTextW = cellRect.width - typeW - gap;
                var locRect = new Rect(cellRect.x, cellRect.y, locTextW, cellRect.height);
                GUI.Label(locRect, new GUIContent(item.Location, tooltip), _monoStyle);

                // Type rendered as dim italic text right after, no background
                var typeRect = new Rect(locRect.xMax + gap, cellRect.y, typeW, cellRect.height);
                GUI.Label(typeRect, new GUIContent(typeLabel, item.AssetType), _typeBadgeStyle);
            }
            else
            {
                GUI.Label(cellRect, new GUIContent(item.Location, tooltip), _monoStyle);
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
            if (item.RefCount == 0)
            {
                // Idle pool: color-code by tier so the user instantly knows why it is 0
                string refsTooltip = tier switch
                {
                    "Trial" => "RefCount = 0 — asset is in the Trial (probation) idle pool.\n"
                             + "AssetCacheService is holding it for fast re-use.\nNot a leak.",
                    "Main" => "RefCount = 0 — asset is in the Main (hot) idle pool.\n"
                             + "Promoted due to high access frequency; kept resident for instant re-use.\nNot a leak.",
                    _ => "RefCount = 0"
                };
                GUI.contentColor = GetTierColor(tier);
                GUILayout.Label(new GUIContent("0", refsTooltip), _rowStyle, GUILayout.Width(REFS_W));
                GUI.contentColor = Color.white;
            }
            else if (refAnomaly)
            {
                GUI.contentColor = new Color(1f, 0.7f, 0.2f);
                GUILayout.Label(new GUIContent(item.RefCount.ToString(),
                    $"RefCount = {item.RefCount} is unusually high.\n"
                  + "Verify that all callers are correctly calling Dispose() / Release()."),
                    _rowStyle, GUILayout.Width(REFS_W));
                GUI.contentColor = Color.white;
            }
            else
            {
                GUILayout.Label(item.RefCount.ToString(), _rowStyle, GUILayout.Width(REFS_W));
            }

            GUILayout.Label(item.AccessCount.ToString(), _rowStyle, GUILayout.Width(HITS_W));

            GUI.contentColor = GetProviderColor(item.ProviderType);
            GUILayout.Label(item.ProviderType, _rowStyle, GUILayout.Width(PROVIDER_W));
            GUI.contentColor = Color.white;

            GUILayout.Label(string.IsNullOrEmpty(item.Bucket) ? "—" : item.Bucket,
                _rowStyle, GUILayout.Width(BUCKET_W));

            // Tag — dim grey when empty
            if (string.IsNullOrEmpty(item.Tag))
            {
                GUI.contentColor = new Color(0.45f, 0.45f, 0.45f);
                GUILayout.Label("—", _rowStyle, GUILayout.Width(TAG_W));
                GUI.contentColor = Color.white;
            }
            else
            {
                GUI.contentColor = new Color(0.6f, 0.9f, 0.6f);
                GUILayout.Label(item.Tag, _rowStyle, GUILayout.Width(TAG_W));
                GUI.contentColor = Color.white;
            }

            // Owner — dim grey when empty
            if (string.IsNullOrEmpty(item.Owner))
            {
                GUI.contentColor = new Color(0.45f, 0.45f, 0.45f);
                GUILayout.Label("—", _rowStyle, GUILayout.Width(OWNER_W));
                GUI.contentColor = Color.white;
            }
            else
            {
                GUI.contentColor = new Color(0.85f, 0.75f, 1.0f);
                GUILayout.Label(item.Owner, _rowStyle, GUILayout.Width(OWNER_W));
                GUI.contentColor = Color.white;
            }

            GUI.contentColor = GetTierColor(tier);
            GUILayout.Label(new GUIContent(tier, GetTierTooltip(tier)), _rowStyle, GUILayout.Width(TIER_W));
            GUI.contentColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        // ── List tab ────────────────────────────────────────────────────────────
        private void DrawList(string title, List<AssetCacheService.CacheDiagnosticEntry> list, string tier)
        {
            EditorGUILayout.LabelField($"{title}  [{list.Count}]", EditorStyles.boldLabel);
            GUILayout.Space(2f);
            DrawHeader();

            int rowIndex = 0;
            for (int i = 0; i < list.Count; i++)
                DrawItem(list[i], tier, rowIndex++);
        }

        // ── Buckets tab ─────────────────────────────────────────────────────────
        private void DrawBuckets()
        {
            RebuildBucketMap();

            EditorGUILayout.LabelField($"Bucket View  [{_bucketMap.Count} buckets, {_allList.Count} total]", EditorStyles.boldLabel);

            foreach (var kvp in _bucketMap)
            {
                string name = string.IsNullOrEmpty(kvp.Key) ? "[No Bucket]" : kvp.Key;
                GUILayout.Space(8f);

                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label($"⬡  {name}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{kvp.Value.Count} item(s)", EditorStyles.miniLabel);
                }

                DrawHeader();
                int rowIndex = 0;
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    var e = kvp.Value[i];
                    // Determine tier from which source list it came from
                    string tier = GetTierForEntry(e);
                    DrawItem(e, tier, rowIndex++);
                }
            }
        }

        // ── Summary tab ─────────────────────────────────────────────────────────
        private void DrawSummary()
        {
            EditorGUILayout.LabelField("Cache Summary", EditorStyles.boldLabel);
            GUILayout.Space(6f);

            int yooCount = 0, addrCount = 0, resCount = 0, otherCount = 0;
            int totalRefs = 0, maxHits = 0;
            int refAnomalies = 0;
            var ownerCounts = new Dictionary<string, int>();
            var tagCounts = new Dictionary<string, int>();

            for (int i = 0; i < _allList.Count; i++)
            {
                var e = _allList[i];
                totalRefs += e.RefCount;
                if (e.AccessCount > maxHits) maxHits = e.AccessCount;
                if (e.RefCount > 8) refAnomalies++;
                switch (e.ProviderType)
                {
                    case "YooAsset": yooCount++; break;
                    case "Addressables": addrCount++; break;
                    case "Resources": resCount++; break;
                    default: otherCount++; break;
                }
                if (!string.IsNullOrEmpty(e.Owner))
                {
                    ownerCounts.TryGetValue(e.Owner, out int oc);
                    ownerCounts[e.Owner] = oc + 1;
                }
                if (!string.IsNullOrEmpty(e.Tag))
                {
                    tagCounts.TryGetValue(e.Tag, out int tc);
                    tagCounts[e.Tag] = tc + 1;
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Total cached assets", _allList.Count.ToString());
                DrawSummaryRow("Active (in-use, RefCount > 0)", _activeList.Count.ToString());
                DrawSummaryRow("Trial pool (idle probation)", _trialList.Count.ToString());
                DrawSummaryRow("Main pool (idle hot cache)", _mainList.Count.ToString());
                DrawSummaryRow("Total live ref-count sum", totalRefs.ToString());
                DrawSummaryRow("Highest AccessCount (hottest asset)", maxHits.ToString());
            }

            // Leak suspect section
            if (refAnomalies > 0)
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("⚠ Ref-Count Anomalies (> 8)", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int i = 0; i < _activeList.Count; i++)
                    {
                        var e = _activeList[i];
                        if (e.RefCount <= 8) continue;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUI.contentColor = new Color(1f, 0.7f, 0.2f);
                            GUILayout.Label($"Refs={e.RefCount}", EditorStyles.boldLabel, GUILayout.Width(70f));
                            GUI.contentColor = Color.white;
                            GUILayout.Label(e.Location, _monoStyle);
                        }
                    }
                    EditorGUILayout.HelpBox(
                        "RefCount > 8 may indicate callers are loading the same asset repeatedly without releasing.\n" +
                        "Verify each LoadAssetAsync() has a corresponding Dispose() / Release().",
                        MessageType.Warning);
                }
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Assets by Provider", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("YooAsset", yooCount.ToString(), new Color(1f, 0.6f, 0f));
                DrawSummaryRow("Addressables", addrCount.ToString(), new Color(0.2f, 0.6f, 1f));
                DrawSummaryRow("Resources", resCount.ToString(), Color.gray);
                if (otherCount > 0) DrawSummaryRow("Other", otherCount.ToString());
            }

            if (ownerCounts.Count > 0)
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Assets by Owner", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    foreach (var kv in ownerCounts)
                        DrawSummaryRow(kv.Key, kv.Value.ToString(), new Color(0.85f, 0.75f, 1.0f));
                }
            }

            if (tagCounts.Count > 0)
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Assets by Tag", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    foreach (var kv in tagCounts)
                        DrawSummaryRow(kv.Key, kv.Value.ToString(), new Color(0.6f, 0.9f, 0.6f));
                }
            }

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Distribution", EditorStyles.boldLabel);
            int total = _allList.Count;
            if (total > 0)
            {
                DrawProgressBar("Active", _activeList.Count, total, new Color(0.2f, 0.7f, 0.3f));
                DrawProgressBar("Trial", _trialList.Count, total, new Color(0.9f, 0.6f, 0.1f));
                DrawProgressBar("Main", _mainList.Count, total, new Color(0.2f, 0.5f, 1.0f));
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
            float t = total > 0 ? (float)count / total : 0f;
            var rect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
            rect = new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4);
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * t, rect.height), fill);
            EditorGUI.LabelField(rect, $"  {label}: {count} ({t * 100f:F1}%)", EditorStyles.whiteLabel);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private void RebuildBucketMap()
        {
            // Return existing lists to pool before clearing.
            foreach (var v in _bucketMap.Values)
            {
                v.Clear();
                _bucketListPool.Push(v);
            }
            _bucketMap.Clear();

            AddToBuckets(_activeList);
            AddToBuckets(_trialList);
            AddToBuckets(_mainList);
        }

        private void AddToBuckets(List<AssetCacheService.CacheDiagnosticEntry> src)
        {
            for (int i = 0; i < src.Count; i++)
            {
                var e = src[i];
                string key = e.Bucket ?? string.Empty;
                if (!_bucketMap.TryGetValue(key, out var lst))
                {
                    lst = _bucketListPool.Count > 0
                        ? _bucketListPool.Pop()
                        : new List<AssetCacheService.CacheDiagnosticEntry>(16);
                    _bucketMap[key] = lst;
                }
                lst.Add(e);
            }
        }

        private string GetTierForEntry(AssetCacheService.CacheDiagnosticEntry e)
        {
            for (int i = 0; i < _activeList.Count; i++)
                if (_activeList[i].CacheKey == e.CacheKey) return "Active";
            for (int i = 0; i < _trialList.Count; i++)
                if (_trialList[i].CacheKey == e.CacheKey) return "Trial";
            return "Main";
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


    }
}
#endif
