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
        private readonly List<HandleTracker.HandleInfo> _filtered = new List<HandleTracker.HandleInfo>(256);

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
        private Vector2 _scrollPos;
        private double _nextRepaint;
        private bool _hasSnapshot;

        // ── Lazy styles ──────────────────────────────────────────────────────────
        private GUIStyle _monoStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _boxStyle;
        private GUIStyle _pillStyle;
        private bool _stylesBuilt;

        private static readonly Color _rowEven = new Color(0.22f, 0.22f, 0.22f);
        private static readonly Color _rowOdd = new Color(0.19f, 0.19f, 0.19f);
        // True leak: handle has RefCount > 0 but is long-lived and NOT explained by cache.
        private static readonly Color _leakRowBg = new Color(0.38f, 0.12f, 0.10f);
        private static readonly Color _leakTextColor = new Color(1.0f, 0.35f, 0.25f);
        // Cached idle: long-lived but AssetCacheService is intentionally holding it.
        private static readonly Color _cachedRowBg = new Color(0.16f, 0.22f, 0.30f);
        private static readonly Color _cachedTextColor = new Color(0.4f, 0.75f, 1.0f);

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
            BuildFiltered();

            // ── Stats bar ────────────────────────────────────────────────────────
            // Categorise for display
            int leaked = 0, cached = 0, normal = 0;
            for (int i = 0; i < _filtered.Count; i++)
            {
                var s = ClassifyHandle(_filtered[i]);
                if (s == HandleStatus.Leaked) leaked++;
                else if (s == HandleStatus.Cached) cached++;
                else normal++;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"  Total: {_snapshot.Count}", EditorStyles.boldLabel);
                if (_searchFilter.Length > 0)
                    GUILayout.Label($"  Filtered: {_filtered.Count}", EditorStyles.miniLabel);
                GUILayout.Space(8f);
                DrawPill("Normal", normal, new Color(0.3f, 0.75f, 0.3f));
                GUILayout.Space(4f);
                DrawPill("Cached (idle pool)", cached, _cachedTextColor);
                GUILayout.Space(4f);
                if (leaked > 0)
                    DrawPill($"⚠ Leak suspect: {leaked}", leaked, _leakTextColor);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Right-click a row for options  ", EditorStyles.miniLabel);
            }

            if (_filtered.Count == 0)
            {
                GUILayout.Space(16f);
                EditorGUILayout.LabelField(
                    _snapshot.Count == 0 ? "No active handles." : "No handles match the current filter.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            DrawTableHeader();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            for (int i = 0; i < _filtered.Count; i++)
                DrawRow(_filtered[i], i);
            EditorGUILayout.EndScrollView();
        }

        // ── Classification ───────────────────────────────────────────────────────
        private enum HandleStatus { Normal, Cached, Leaked }

        private HandleStatus ClassifyHandle(HandleTracker.HandleInfo info)
        {
            double lifeSeconds = (DateTime.UtcNow - info.RegistrationTime).TotalSeconds;
            if (lifeSeconds < 300.0) return HandleStatus.Normal;

            if (!string.IsNullOrEmpty(info.Description) &&
                (info.Description.StartsWith("SceneAsync", StringComparison.Ordinal) ||
                 info.Description.StartsWith("SceneSync", StringComparison.Ordinal)))
            {
                return HandleStatus.Normal;
            }

            // Long-lived — check if AssetCacheService is intentionally holding it.
            // Extract the asset location from the description (format: "TypeName : location")
            string location = ExtractLocation(info.Description);
            if (location != null && _idleCacheLocations.Contains(location))
                return HandleStatus.Cached;     // in idle pool — expected

            return HandleStatus.Leaked;         // long-lived and NOT in any cache pool
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
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("ID", EditorStyles.boldLabel, GUILayout.Width(ID_W));
                GUILayout.Label("Package", EditorStyles.boldLabel, GUILayout.Width(PKG_W));
                GUILayout.Label("Description", EditorStyles.boldLabel, GUILayout.Width(DescWidth));
                GUILayout.Label("Tag", EditorStyles.boldLabel, GUILayout.Width(TAG_W));
                GUILayout.Label("Owner", EditorStyles.boldLabel, GUILayout.Width(OWNER_W));
                GUILayout.Label("Status", EditorStyles.boldLabel, GUILayout.Width(STATUS_W));
                GUILayout.Label("Registered", EditorStyles.boldLabel, GUILayout.Width(TIME_W));
                GUILayout.Label("Duration", EditorStyles.boldLabel, GUILayout.Width(DUR_W));
            }
        }

        // ── Row drawing ──────────────────────────────────────────────────────────
        private void DrawRow(HandleTracker.HandleInfo info, int rowIndex)
        {
            var status = ClassifyHandle(info);
            double lifeSec = (DateTime.UtcNow - info.RegistrationTime).TotalSeconds;

            Color rowBg = status switch
            {
                HandleStatus.Leaked => _leakRowBg,
                HandleStatus.Cached => _cachedRowBg,
                _ => rowIndex % 2 == 0 ? _rowEven : _rowOdd
            };

            var rect = EditorGUILayout.BeginHorizontal();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, rowBg);

            GUILayout.Label(info.Id.ToString(), _rowStyle, GUILayout.Width(ID_W));
            GUILayout.Label(info.PackageName ?? "—", _rowStyle, GUILayout.Width(PKG_W));

            var descContent = new GUIContent(info.Description, info.Description);
            GUILayout.Label(descContent, _monoStyle, GUILayout.Width(DescWidth));

            // Tag + Owner from cache diagnostics
            string location = ExtractLocation(info.Description);
            _tagOwnerMap.TryGetValue(location ?? string.Empty, out var tagOwner);

            if (string.IsNullOrEmpty(tagOwner.Tag))
            {
                GUI.contentColor = new Color(0.4f, 0.4f, 0.4f);
                GUILayout.Label("—", _rowStyle, GUILayout.Width(TAG_W));
            }
            else
            {
                GUI.contentColor = new Color(0.6f, 0.9f, 0.6f);
                GUILayout.Label(tagOwner.Tag, _rowStyle, GUILayout.Width(TAG_W));
            }
            GUI.contentColor = Color.white;

            if (string.IsNullOrEmpty(tagOwner.Owner))
            {
                GUI.contentColor = new Color(0.4f, 0.4f, 0.4f);
                GUILayout.Label("—", _rowStyle, GUILayout.Width(OWNER_W));
            }
            else
            {
                GUI.contentColor = new Color(0.85f, 0.75f, 1.0f);
                GUILayout.Label(tagOwner.Owner, _rowStyle, GUILayout.Width(OWNER_W));
            }
            GUI.contentColor = Color.white;

            // Status badge
            switch (status)
            {
                case HandleStatus.Leaked:
                    GUI.contentColor = _leakTextColor;
                    GUILayout.Label(new GUIContent("⚠ Leak?",
                        "Handle alive >5 min and NOT found in any AssetCacheService idle pool.\nThis may indicate a missing Dispose() call."),
                        _rowStyle, GUILayout.Width(STATUS_W));
                    break;
                case HandleStatus.Cached:
                    GUI.contentColor = _cachedTextColor;
                    GUILayout.Label(new GUIContent("● Cached",
                        "Handle is long-lived because AssetCacheService is intentionally keeping this\nasset in its Trial/Main idle pool for fast re-use. Not a leak."),
                        _rowStyle, GUILayout.Width(STATUS_W));
                    break;
                default:
                    GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
                    GUILayout.Label("—", _rowStyle, GUILayout.Width(STATUS_W));
                    break;
            }
            GUI.contentColor = Color.white;

            GUILayout.Label(info.RegistrationTime.ToLocalTime().ToString("HH:mm:ss"),
                _rowStyle, GUILayout.Width(TIME_W));

            Color durColor = status == HandleStatus.Leaked ? _leakTextColor : Color.white;
            GUI.contentColor = durColor;
            GUILayout.Label(FormatDuration(lifeSec), _rowStyle, GUILayout.Width(DUR_W));
            GUI.contentColor = Color.white;

            EditorGUILayout.EndHorizontal();

            // Right-click context menu
            if (rect.Contains(Event.current.mousePosition)
                && Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                var menu = new GenericMenu();
                string desc = info.Description;
                string stack = info.StackTrace;
                menu.AddItem(new GUIContent("Copy Description"), false,
                    () => EditorGUIUtility.systemCopyBuffer = desc);
                if (!string.IsNullOrEmpty(stack))
                {
                    menu.AddItem(new GUIContent("Copy Stack Trace"), false,
                        () => EditorGUIUtility.systemCopyBuffer = stack);
                    bool expanded = _expandedIds.Contains(info.Id);
                    int capturedId = info.Id;
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
            if (!string.IsNullOrEmpty(info.StackTrace) && IsRowExpanded(info.Id))
            {
                using (new EditorGUI.IndentLevelScope(1))
                using (new EditorGUILayout.VerticalScope(_boxStyle))
                {
                    EditorGUILayout.LabelField("Stack Trace", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(info.StackTrace, _monoStyle,
                        GUILayout.ExpandHeight(true));
                }
            }
        }

        // ── Expanded ids ─────────────────────────────────────────────────────────
        private readonly HashSet<int> _expandedIds = new HashSet<int>();
        private bool IsRowExpanded(int id) => _expandedIds.Contains(id);

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
            var active = HandleTracker.GetActiveHandles();
            if (active != null)
                for (int i = 0; i < active.Count; i++) _snapshot.Add(active[i]);
            _hasSnapshot = true;
        }

        private void BuildFiltered()
        {
            _filtered.Clear();
            bool hasFilter = _searchFilter.Length > 0;
            for (int i = 0; i < _snapshot.Count; i++)
            {
                var h = _snapshot[i];
                if (!hasFilter || MatchesFilter(h))
                    _filtered.Add(h);
            }
        }

        private bool MatchesFilter(HandleTracker.HandleInfo h)
        {
            return (h.Description != null && h.Description.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || (h.PackageName != null && h.PackageName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawPill(string label, int count, Color color)
        {
            var prev = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label($"  {label}: {count}  ", _pillStyle, GUILayout.ExpandWidth(false));
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
