#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Editor
{
    public class HandleTrackerWindow : EditorWindow
    {
        // ── Pre-allocated snapshots ───────────────────────────────────────────────
        private readonly List<HandleTracker.HandleInfo> _snapshot = new List<HandleTracker.HandleInfo>(256);
        // Pre-formatted, repaint-stable view models (rebuilt only on refresh).
        private readonly List<HandleRowView> _views = new List<HandleRowView>(256);
        // Indices into _views passing the current filter (rebuilt only when display is dirty).
        private readonly List<int> _filteredIndices = new List<int>(256);

        // Set of locations currently resident in AssetCacheService's idle pools
        // (Trial + Main). Rebuilt each repaint from GlobalInstances diagnostics.
        private readonly HashSet<string> _idleCacheLocations = new HashSet<string>();
        // Maps location -> (Tag, Owner) for all currently-known cache entries.
        private readonly Dictionary<string, (string Tag, string Owner)> _tagOwnerMap
            = new Dictionary<string, (string, string)>(256, StringComparer.Ordinal);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _diagActive
            = new List<AssetCacheService.CacheDiagnosticEntry>(512);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _diagTrial
            = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _diagMain
            = new List<AssetCacheService.CacheDiagnosticEntry>(256);

        // ── State ────────────────────────────────────────────────────────────────
        private string _searchFilter = string.Empty;
        private string _lastSearchFilter = "\0";
        private bool _displayDirty = true;
        private Vector2 _scrollPos;
        private double _nextRepaint;
        private bool _hasSnapshot;

        // Cached stat text + reusable content (rebuilt only when display changes).
        private string _totalText = "  Total: 0";
        private string _filteredText = string.Empty;
        private string _normalPill = "Normal: 0";
        private string _cachedPill = "Cached (idle pool): 0";
        private string _persistentPill = string.Empty;
        private string _leakPill = string.Empty;
        private int _persistentCount;
        private int _leakCount;
        private readonly GUIContent _cell = new GUIContent();

        // Cached GUILayoutOption arrays — avoids per-call params[] allocations.
        private GUILayoutOption[] _wId, _wPkg, _wDesc, _wTag, _wOwner, _wStatus, _wTime, _wDur, _pillOpts;
        private float _lastDescWidth = -1f;
        private bool _widthsBuilt;

        private const string LeakTooltip =
            "Handle alive >5 min and NOT found in any AssetCacheService idle pool.\nThis may indicate a missing Dispose() call.\nIf this is intentional (DontDestroyOnLoad / bootstrap / main scene), right-click → Mark Persistent.";
        private const string CachedTooltip =
            "Handle is long-lived because AssetCacheService is intentionally keeping this\nasset in its Trial/Main idle pool for fast re-use. Not a leak.";
        private const string PersistentTooltip =
            "Marked persistent (intentionally long-lived, e.g. DontDestroyOnLoad / bootstrap / main-scene assets).\nExcluded from leak heuristics. Right-click to unmark.";

        // ── Lazy styles ──────────────────────────────────────────────────────────
        private GUIStyle _monoStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _boxStyle;
        private GUIStyle _pillStyle;
        private bool _stylesBuilt;

        private static bool IsPro => EditorGUIUtility.isProSkin;
        private static Color RowEven => IsPro ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.86f, 0.86f, 0.86f);
        private static Color RowOdd => IsPro ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.80f, 0.80f, 0.80f);
        // True leak: handle has RefCount > 0 but is long-lived and NOT explained by cache.
        private static Color LeakRowBg => IsPro ? new Color(0.38f, 0.12f, 0.10f) : new Color(0.98f, 0.80f, 0.78f);
        private static readonly Color _leakTextColor = new Color(1.0f, 0.35f, 0.25f);
        // Cached idle: long-lived but AssetCacheService is intentionally holding it.
        private static Color CachedRowBg => IsPro ? new Color(0.16f, 0.22f, 0.30f) : new Color(0.80f, 0.88f, 0.98f);
        private static readonly Color _cachedTextColor = new Color(0.4f, 0.75f, 1.0f);
        // Persistent: developer-declared long-lived (DontDestroyOnLoad / bootstrap / main scene).
        private static Color PersistentRowBg => IsPro ? new Color(0.12f, 0.26f, 0.24f) : new Color(0.80f, 0.94f, 0.90f);
        private static readonly Color _persistentTextColor = new Color(0.35f, 0.85f, 0.70f);
        private static Color DimColor => IsPro ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color _normalPillColor = new Color(0.3f, 0.75f, 0.3f);

        // Column widths
        private const float ID_W = 44f;
        private const float PKG_W = 100f;
        private const float STATUS_W = 72f;
        private const float TIME_W = 72f;
        private const float DUR_W = 56f;
        private const float TAG_W = 68f;
        private const float OWNER_W = 84f;
        private float DescWidth => Mathf.Max(60f,
            position.width - ID_W - PKG_W - STATUS_W - TIME_W - DUR_W - TAG_W - OWNER_W - 30f);

        [MenuItem("Tools/CycloneGames/AssetManagement/Asset Handle Tracker")]
        public static void ShowWindow()
        {
            var w = GetWindow<HandleTrackerWindow>("Handle Tracker");
            w.minSize = new Vector2(680, 360);
            w.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            RefreshSnapshot();
        }
        private void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        private void OnEditorUpdate()
        {
            if (!HandleTracker.Enabled) return;
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

            _monoStyle = new GUIStyle(EditorStyles.label)
            {
                font = EditorStyles.miniFont,
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip
            };

            _rowStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip
            };

            _boxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(6, 6, 4, 4) };

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

            // ── Top controls ─────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Scenes", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                    SceneTrackerWindow.ShowWindow();
                if (GUILayout.Button("Governance", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                    AssetRuntimeGovernanceWindow.ShowWindow();
                GUILayout.Space(8f);

                bool wasEnabled = HandleTracker.Enabled;
                HandleTracker.Enabled = GUILayout.Toggle(wasEnabled, "  Enable Tracking",
                    EditorStyles.toolbarButton, GUILayout.Width(130f));
                HandleTracker.EnableStackTrace = GUILayout.Toggle(HandleTracker.EnableStackTrace,
                    "  Stack Traces (slow)", EditorStyles.toolbarButton, GUILayout.Width(130f));

                GUILayout.Space(8f);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                    RefreshSnapshot();

                GUILayout.FlexibleSpace();
                GUILayout.Label("Filter:", GUILayout.Width(36f));
                _searchFilter = EditorGUILayout.TextField(_searchFilter,
                    EditorStyles.toolbarSearchField, GUILayout.Width(200f));
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                    _searchFilter = string.Empty;
            }

            if (!HandleTracker.Enabled)
            {
                DrawPlaceholder("Tracking is disabled. Enable it to see active handles.", MessageType.Info);
                return;
            }

            if (!_hasSnapshot) RefreshSnapshot();
            if (_searchFilter != _lastSearchFilter)
            {
                _lastSearchFilter = _searchFilter;
                _displayDirty = true;
            }
            if (_displayDirty) RebuildDisplay();

            EnsureWidths();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(_totalText, EditorStyles.boldLabel);
                if (_filteredText.Length > 0)
                    GUILayout.Label(_filteredText, EditorStyles.miniLabel);
                GUILayout.Space(8f);
                DrawPill(_normalPill, _normalPillColor);
                GUILayout.Space(4f);
                DrawPill(_cachedPill, _cachedTextColor);
                GUILayout.Space(4f);
                if (_persistentCount > 0)
                {
                    DrawPill(_persistentPill, _persistentTextColor);
                    GUILayout.Space(4f);
                }
                if (_leakCount > 0)
                    DrawPill(_leakPill, _leakTextColor);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Right-click a row for options  ", EditorStyles.miniLabel);
            }

            if (_filteredIndices.Count == 0)
            {
                GUILayout.Space(16f);
                EditorGUILayout.LabelField(
                    _snapshot.Count == 0 ? "No active handles." : "No handles match the current filter.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            DrawTableHeader();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            for (int i = 0; i < _filteredIndices.Count; i++)
                DrawRow(_views[_filteredIndices[i]], i);
            EditorGUILayout.EndScrollView();
        }

        // ── Classification ───────────────────────────────────────────────────────
        private enum HandleStatus { Normal, Cached, Leaked, Persistent }

        private HandleStatus ClassifyHandle(HandleTracker.HandleInfo info, DateTime nowUtc)
        {
            double lifeSeconds = (nowUtc - info.RegistrationTime).TotalSeconds;
            if (lifeSeconds < 300.0) return HandleStatus.Normal;

            if (!string.IsNullOrEmpty(info.Description) &&
                (info.Description.StartsWith("SceneAsync", StringComparison.Ordinal) ||
                 info.Description.StartsWith("SceneSync", StringComparison.Ordinal)))
            {
                return HandleStatus.Normal;
            }

            // Long-lived — check if it is explained by the cache or an explicit persistent marking.
            // Extract the asset location from the description (format: "TypeName : location")
            string location = ExtractLocation(info.Description);
            if (location != null && _idleCacheLocations.Contains(location))
                return HandleStatus.Cached;        // in idle pool — expected
            if (location != null && HandleTracker.IsPersistent(location))
                return HandleStatus.Persistent;    // developer-declared long-lived — not a leak

            return HandleStatus.Leaked;            // long-lived and NOT explained
        }

        // Description format: "AssetAsync TypeName : path/to/asset"
        private static string ExtractLocation(string description)
        {
            if (string.IsNullOrEmpty(description)) return null;
            int colon = description.IndexOf(" : ", StringComparison.Ordinal);
            return colon >= 0 ? description.Substring(colon + 3).Trim() : null;
        }

        // ── Table header ─────────────────────────────────────────────────────────
        private void DrawTableHeader()
        {
            EnsureWidths();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("ID", EditorStyles.boldLabel, _wId);
                GUILayout.Label("Package", EditorStyles.boldLabel, _wPkg);
                GUILayout.Label("Description", EditorStyles.boldLabel, _wDesc);
                GUILayout.Label("Tag", EditorStyles.boldLabel, _wTag);
                GUILayout.Label("Owner", EditorStyles.boldLabel, _wOwner);
                GUILayout.Label("Status", EditorStyles.boldLabel, _wStatus);
                GUILayout.Label("Registered", EditorStyles.boldLabel, _wTime);
                GUILayout.Label("Duration", EditorStyles.boldLabel, _wDur);
            }
        }

        // ── Row drawing ──────────────────────────────────────────────────────────
        private void DrawRow(in HandleRowView v, int rowIndex)
        {
            Color rowBg = v.Status == 2 ? LeakRowBg
                : v.Status == 3 ? PersistentRowBg
                : v.Status == 1 ? CachedRowBg
                : (rowIndex % 2 == 0 ? RowEven : RowOdd);

            var rect = EditorGUILayout.BeginHorizontal();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, rowBg);

            GUILayout.Label(v.IdText, _rowStyle, _wId);
            GUILayout.Label(v.Package, _rowStyle, _wPkg);

            _cell.text = v.Description; _cell.tooltip = v.Description;
            GUILayout.Label(_cell, _monoStyle, _wDesc);

            // Tag
            if (!v.HasTag)
            {
                GUI.contentColor = DimColor;
                GUILayout.Label("—", _rowStyle, _wTag);
            }
            else
            {
                GUI.contentColor = new Color(0.6f, 0.9f, 0.6f);
                GUILayout.Label(v.Tag, _rowStyle, _wTag);
            }
            GUI.contentColor = Color.white;

            // Owner
            if (!v.HasOwner)
            {
                GUI.contentColor = DimColor;
                GUILayout.Label("—", _rowStyle, _wOwner);
            }
            else
            {
                GUI.contentColor = new Color(0.85f, 0.75f, 1.0f);
                GUILayout.Label(v.Owner, _rowStyle, _wOwner);
            }
            GUI.contentColor = Color.white;

            // Status badge
            switch (v.Status)
            {
                case 2:
                    GUI.contentColor = _leakTextColor;
                    _cell.text = "⚠ Leak?"; _cell.tooltip = LeakTooltip;
                    GUILayout.Label(_cell, _rowStyle, _wStatus);
                    break;
                case 1:
                    GUI.contentColor = _cachedTextColor;
                    _cell.text = "● Cached"; _cell.tooltip = CachedTooltip;
                    GUILayout.Label(_cell, _rowStyle, _wStatus);
                    break;
                case 3:
                    GUI.contentColor = _persistentTextColor;
                    _cell.text = "◆ Persistent"; _cell.tooltip = PersistentTooltip;
                    GUILayout.Label(_cell, _rowStyle, _wStatus);
                    break;
                default:
                    GUI.contentColor = DimColor;
                    GUILayout.Label("—", _rowStyle, _wStatus);
                    break;
            }
            GUI.contentColor = Color.white;

            GUILayout.Label(v.RegisteredText, _rowStyle, _wTime);

            GUI.contentColor = v.Status == 2 ? _leakTextColor : Color.white;
            GUILayout.Label(v.DurationText, _rowStyle, _wDur);
            GUI.contentColor = Color.white;

            EditorGUILayout.EndHorizontal();

            // Right-click context menu
            if (rect.Contains(Event.current.mousePosition)
                && Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                var menu = new GenericMenu();
                string desc = v.Description;
                string stack = v.StackTrace;
                menu.AddItem(new GUIContent("Copy Description"), false,
                    () => EditorGUIUtility.systemCopyBuffer = desc);

                string menuLoc = ExtractLocation(v.Description);
                if (!string.IsNullOrEmpty(menuLoc))
                {
                    string capturedLoc = menuLoc;
                    if (HandleTracker.IsPersistent(capturedLoc))
                        menu.AddItem(new GUIContent("Unmark Persistent"), false,
                            () => { HandleTracker.UnmarkPersistent(capturedLoc); RefreshSnapshot(); Repaint(); });
                    else
                        menu.AddItem(new GUIContent("Mark Persistent (ignore leak)"), false,
                            () => { HandleTracker.MarkPersistent(capturedLoc); RefreshSnapshot(); Repaint(); });
                }

                if (!string.IsNullOrEmpty(stack))
                {
                    menu.AddItem(new GUIContent("Copy Stack Trace"), false,
                        () => EditorGUIUtility.systemCopyBuffer = stack);
                    bool expanded = _expandedIds.Contains(v.Id);
                    int capturedId = v.Id;
                    menu.AddItem(new GUIContent(expanded ? "Collapse Stack Trace" : "Expand Stack Trace"),
                        false, () =>
                        {
                            if (_expandedIds.Contains(capturedId)) _expandedIds.Remove(capturedId);
                            else _expandedIds.Add(capturedId);
                            Repaint();
                        });
                }
                menu.ShowAsContext();
                Event.current.Use();
            }

            // Expanded stack trace
            if (!string.IsNullOrEmpty(v.StackTrace) && _expandedIds.Contains(v.Id))
            {
                using (new EditorGUI.IndentLevelScope(1))
                using (new EditorGUILayout.VerticalScope(_boxStyle))
                {
                    EditorGUILayout.LabelField("Stack Trace", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(v.StackTrace, _monoStyle,
                        GUILayout.ExpandHeight(true));
                }
            }
        }

        // ── Expanded ids ─────────────────────────────────────────────────────────
        private readonly HashSet<int> _expandedIds = new HashSet<int>();

        // ── Idle-location set ────────────────────────────────────────────────────
        private void RebuildIdleLocationSet()
        {
            _idleCacheLocations.Clear();
            _tagOwnerMap.Clear();
            var instances = AssetCacheService.GlobalInstances;
            if (instances == null) return;

            for (int i = 0; i < instances.Count; i++)
            {
                _diagActive.Clear(); _diagTrial.Clear(); _diagMain.Clear();
                instances[i].GetDiagnostics(_diagActive, _diagTrial, _diagMain);

                // Populate tag+owner map from all tiers.
                for (int j = 0; j < _diagActive.Count; j++) AddToTagOwnerMap(_diagActive[j]);
                for (int j = 0; j < _diagTrial.Count; j++) AddToTagOwnerMap(_diagTrial[j]);
                for (int j = 0; j < _diagMain.Count; j++) AddToTagOwnerMap(_diagMain[j]);

                // Idle location set is Trial + Main only.
                for (int j = 0; j < _diagTrial.Count; j++) _idleCacheLocations.Add(_diagTrial[j].Location);
                for (int j = 0; j < _diagMain.Count; j++) _idleCacheLocations.Add(_diagMain[j].Location);
            }
        }

        private void AddToTagOwnerMap(AssetCacheService.CacheDiagnosticEntry e)
        {
            if (string.IsNullOrEmpty(e.Location)) return;
            // Only store if at least one field is set; don't overwrite a richer entry.
            if (!_tagOwnerMap.TryGetValue(e.Location, out var existing)
                || (string.IsNullOrEmpty(existing.Tag) && !string.IsNullOrEmpty(e.Tag))
                || (string.IsNullOrEmpty(existing.Owner) && !string.IsNullOrEmpty(e.Owner)))
            {
                _tagOwnerMap[e.Location] = (e.Tag, e.Owner);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
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
            _displayDirty = true;
            _hasSnapshot = true;
        }

        private HandleRowView BuildView(HandleTracker.HandleInfo info, DateTime nowUtc)
        {
            byte status = (byte)ClassifyHandle(info, nowUtc);
            double lifeSec = (nowUtc - info.RegistrationTime).TotalSeconds;
            string loc = ExtractLocation(info.Description);
            _tagOwnerMap.TryGetValue(loc ?? string.Empty, out var to);
            return new HandleRowView
            {
                Id = info.Id,
                IdText = info.Id.ToString(),
                Package = info.PackageName ?? "—",
                Description = info.Description ?? string.Empty,
                Tag = to.Tag,
                Owner = to.Owner,
                HasTag = !string.IsNullOrEmpty(to.Tag),
                HasOwner = !string.IsNullOrEmpty(to.Owner),
                Status = status,
                RegisteredText = info.RegistrationTime.ToLocalTime().ToString("HH:mm:ss"),
                DurationText = FormatDuration(lifeSec),
                StackTrace = info.StackTrace
            };
        }

        private void RebuildDisplay()
        {
            _filteredIndices.Clear();
            bool hasFilter = _searchFilter.Length > 0;
            int normal = 0, cached = 0, leaked = 0, persistent = 0;
            for (int i = 0; i < _views.Count; i++)
            {
                if (hasFilter && !MatchesFilter(_views[i])) continue;
                _filteredIndices.Add(i);
                byte s = _views[i].Status;
                if (s == 2) leaked++;
                else if (s == 1) cached++;
                else if (s == 3) persistent++;
                else normal++;
            }
            _leakCount = leaked;
            _persistentCount = persistent;
            _totalText = "  Total: " + _snapshot.Count;
            _filteredText = hasFilter ? "  Filtered: " + _filteredIndices.Count : string.Empty;
            _normalPill = "Normal: " + normal;
            _cachedPill = "Cached (idle pool): " + cached;
            _persistentPill = persistent > 0 ? "◆ Persistent: " + persistent : string.Empty;
            _leakPill = leaked > 0 ? "⚠ Leak suspect: " + leaked : string.Empty;
            _displayDirty = false;
        }

        private bool MatchesFilter(in HandleRowView v)
        {
            return (v.Description != null && v.Description.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (v.Package != null && v.Package.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void EnsureWidths()
        {
            if (!_widthsBuilt)
            {
                _wId = new[] { GUILayout.Width(ID_W) };
                _wPkg = new[] { GUILayout.Width(PKG_W) };
                _wTag = new[] { GUILayout.Width(TAG_W) };
                _wOwner = new[] { GUILayout.Width(OWNER_W) };
                _wStatus = new[] { GUILayout.Width(STATUS_W) };
                _wTime = new[] { GUILayout.Width(TIME_W) };
                _wDur = new[] { GUILayout.Width(DUR_W) };
                _pillOpts = new[] { GUILayout.ExpandWidth(false) };
                _widthsBuilt = true;
            }
            float dw = DescWidth;
            if (_wDesc == null || Mathf.Abs(dw - _lastDescWidth) > 0.5f)
            {
                _wDesc = new[] { GUILayout.Width(dw) };
                _lastDescWidth = dw;
            }
        }

        private struct HandleRowView
        {
            public int Id;
            public string IdText;
            public string Package;
            public string Description;
            public string Tag;
            public string Owner;
            public byte Status;          // 0 = normal, 1 = cached, 2 = leaked
            public string RegisteredText;
            public string DurationText;
            public string StackTrace;
            public bool HasTag;
            public bool HasOwner;
        }

        private void DrawPill(string text, Color color)
        {
            var prev = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, _pillStyle, _pillOpts);
            GUI.contentColor = prev;
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 60.0) return $"{seconds:F1}s";
            if (seconds < 3600.0) return $"{seconds / 60.0:F1}m";
            return $"{seconds / 3600.0:F1}h";
        }

        private static void DrawPlaceholder(string msg, MessageType type)
        {
            GUILayout.Space(40f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(400f)))
                    EditorGUILayout.HelpBox(msg, type);
                GUILayout.FlexibleSpace();
            }
        }
    }
}
#endif
