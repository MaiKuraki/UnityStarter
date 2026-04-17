#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Editor
{
    public sealed class UIRuntimeMonitorWindow : EditorWindow
    {
        private readonly List<UILayerRuntimeStats> _layerStats = new List<UILayerRuntimeStats>(16);
        private Vector2 _scroll;

        private UIManager _cachedManager;
        private double _nextRepaintTime;
        private const double RepaintInterval = 0.25;

        private GUIStyle _sectionTitleStyle;
        private GUIStyle _chipTitleStyle;
        private GUIStyle _chipValueStyle;
        private GUIStyle _layerHeaderStyle;
        private GUIStyle _layerCellStyle;

        private static readonly string[] IntCache = InitIntCache(512);

        [MenuItem("Tools/CycloneGames/UI Framework/Runtime Monitor")]
        public static void ShowWindow()
        {
            UIRuntimeMonitorWindow window = GetWindow<UIRuntimeMonitorWindow>("UI Runtime Monitor");
            window.minSize = new Vector2(420f, 280f);
        }

        private void OnEnable()
        {
            _cachedManager = null;
            EditorApplication.update += ThrottledRepaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= ThrottledRepaint;
            _cachedManager = null;
        }

        private void ThrottledRepaint()
        {
            if (!Application.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextRepaintTime) return;
            _nextRepaintTime = now + RepaintInterval;
            Repaint();
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (!Application.isPlaying)
            {
                _cachedManager = null;
                EditorGUILayout.HelpBox("Enter Play Mode with an active UIManager to monitor runtime UI stats.", MessageType.Info);
                return;
            }

            if (_cachedManager == null)
                _cachedManager = Object.FindFirstObjectByType<UIManager>();

            if (_cachedManager == null)
            {
                EditorGUILayout.HelpBox("No UIManager found in scene.", MessageType.Warning);
                return;
            }

            UIPerformanceStats stats = _cachedManager.GetPerformanceStats();
            _cachedManager.CopyLayerRuntimeStats(_layerStats);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(4);
            DrawSectionHeader("Runtime Snapshot");
            EditorGUILayout.Space(2);
            DrawStatGrid(stats);

            EditorGUILayout.Space(8);
            DrawSectionHeader("Layer Breakdown");
            EditorGUILayout.Space(2);
            DrawLayerTable();

            EditorGUILayout.Space(4);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSectionHeader(string title)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 22f);
            Color bg = EditorGUIUtility.isProSkin
                ? new Color(0.2f, 0.2f, 0.2f, 0.5f)
                : new Color(0.78f, 0.78f, 0.78f, 0.5f);
            Color accent = EditorGUIUtility.isProSkin
                ? new Color(0.35f, 0.55f, 0.85f, 0.6f)
                : new Color(0.25f, 0.45f, 0.75f, 0.4f);

            EditorGUI.DrawRect(rect, bg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);
            GUI.Label(new Rect(rect.x + 10f, rect.y, rect.width - 14f, rect.height), title, _sectionTitleStyle);
        }

        private void DrawStatGrid(UIPerformanceStats stats)
        {
            const int cols = 2;
            const int rows = 5;
            const float chipHeight = 38f;
            const float chipGap = 2f;
            float totalHeight = rows * chipHeight + (rows - 1) * chipGap;

            Rect grid = EditorGUILayout.GetControlRect(false, totalHeight);
            float colWidth = (grid.width - chipGap * (cols - 1)) / cols;

            DrawStatChipInt(ChipRect(grid, 0, 0, colWidth, chipHeight, chipGap), "Active Windows", stats.ActiveWindowCount);
            DrawStatChipInt(ChipRect(grid, 0, 1, colWidth, chipHeight, chipGap), "Scene-Bound", stats.SceneBoundWindowCount);
            DrawStatChipInt(ChipRect(grid, 1, 0, colWidth, chipHeight, chipGap), "In-Flight Opens", stats.InFlightOpenCount);
            DrawStatChipBool(ChipRect(grid, 1, 1, colWidth, chipHeight, chipGap), "Pending Sweep", stats.HasPendingSceneSweep);
            DrawStatChipInt(ChipRect(grid, 2, 0, colWidth, chipHeight, chipGap), "Config Handles", stats.CachedConfigHandleCount);
            DrawStatChipInt(ChipRect(grid, 2, 1, colWidth, chipHeight, chipGap), "Prefab Handles", stats.CachedPrefabHandleCount);
            DrawStatChipInt(ChipRect(grid, 3, 0, colWidth, chipHeight, chipGap), "Layer Count", stats.LayerCount);
            DrawStatChipInt(ChipRect(grid, 3, 1, colWidth, chipHeight, chipGap), "Layer Windows", stats.TotalLayerWindowCount);
            DrawStatChipInt(ChipRect(grid, 4, 0, colWidth, chipHeight, chipGap), "Isolated Canvases", stats.IsolatedWindowCanvasCount);
        }

        private static Rect ChipRect(Rect grid, int row, int col, float colWidth, float chipHeight, float gap)
        {
            return new Rect(
                grid.x + col * (colWidth + gap),
                grid.y + row * (chipHeight + gap),
                colWidth,
                chipHeight);
        }

        private void DrawStatChipInt(Rect rect, string label, int value)
        {
            DrawChipBackground(rect, new Color(0.35f, 0.55f, 0.85f, 0.8f));
            GUI.Label(new Rect(rect.x + 10f, rect.y + 3f, rect.width - 14f, 14f), label, _chipTitleStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 18f, rect.width - 14f, 18f), IntToString(value), _chipValueStyle);
        }

        private void DrawStatChipBool(Rect rect, string label, bool value)
        {
            Color accent = value
                ? new Color(0.85f, 0.6f, 0.2f, 0.8f)
                : new Color(0.25f, 0.65f, 0.4f, 0.8f);
            DrawChipBackground(rect, accent);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 3f, rect.width - 14f, 14f), label, _chipTitleStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 18f, rect.width - 14f, 18f), value ? "Yes" : "No", _chipValueStyle);
        }

        private static void DrawChipBackground(Rect rect, Color accent)
        {
            Color bg = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 0.5f)
                : new Color(0.82f, 0.82f, 0.82f, 0.5f);
            EditorGUI.DrawRect(rect, bg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);
        }

        private void DrawLayerTable()
        {
            if (_layerStats.Count == 0)
            {
                EditorGUILayout.HelpBox("No layers registered.", MessageType.None);
                return;
            }

            const float headerHeight = 20f;
            const float rowHeight = 20f;
            const float rowGap = 1f;

            Rect header = EditorGUILayout.GetControlRect(false, headerHeight);
            Color headerBg = EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.18f, 0.18f, 0.7f)
                : new Color(0.72f, 0.72f, 0.72f, 0.7f);
            EditorGUI.DrawRect(header, headerBg);

            float c0 = header.width * 0.50f;
            float c1 = header.width * 0.25f;
            float c2 = header.width - c0 - c1;

            GUI.Label(new Rect(header.x + 6f, header.y, c0 - 6f, headerHeight), "Layer", _layerHeaderStyle);
            GUI.Label(new Rect(header.x + c0 + 6f, header.y, c1 - 6f, headerHeight), "Sort", _layerHeaderStyle);
            GUI.Label(new Rect(header.x + c0 + c1 + 6f, header.y, c2 - 6f, headerHeight), "Windows", _layerHeaderStyle);

            float totalRows = _layerStats.Count * (rowHeight + rowGap);
            Rect rowsArea = EditorGUILayout.GetControlRect(false, totalRows);

            Color rowEven = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 0.3f)
                : new Color(0.85f, 0.85f, 0.85f, 0.3f);
            Color rowOdd = EditorGUIUtility.isProSkin
                ? new Color(0.25f, 0.25f, 0.25f, 0.3f)
                : new Color(0.82f, 0.82f, 0.82f, 0.3f);
            Color rowAccent = new Color(0.35f, 0.55f, 0.85f, 0.6f);

            for (int i = 0; i < _layerStats.Count; i++)
            {
                float y = rowsArea.y + i * (rowHeight + rowGap);
                Rect row = new Rect(rowsArea.x, y, rowsArea.width, rowHeight);
                EditorGUI.DrawRect(row, i % 2 == 0 ? rowEven : rowOdd);

                if (_layerStats[i].WindowCount > 0)
                    EditorGUI.DrawRect(new Rect(row.x, row.y, 2f, row.height), rowAccent);

                GUI.Label(new Rect(row.x + 6f, row.y, c0 - 6f, rowHeight), _layerStats[i].LayerName, _layerCellStyle);
                GUI.Label(new Rect(row.x + c0 + 6f, row.y, c1 - 6f, rowHeight), IntToString(_layerStats[i].SortingOrder), _layerCellStyle);
                GUI.Label(new Rect(row.x + c0 + c1 + 6f, row.y, c2 - 6f, rowHeight), IntToString(_layerStats[i].WindowCount), _layerCellStyle);
            }
        }

        private static string[] InitIntCache(int size)
        {
            string[] cache = new string[size];
            for (int i = 0; i < size; i++) cache[i] = i.ToString();
            return cache;
        }

        private static string IntToString(int value)
        {
            return (uint)value < (uint)IntCache.Length ? IntCache[value] : value.ToString();
        }

        private void EnsureStyles()
        {
            if (_sectionTitleStyle != null) return;

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
            _chipTitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 10,
                normal = { textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.6f, 0.6f, 0.6f)
                    : new Color(0.35f, 0.35f, 0.35f) }
            };
            _chipValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14
            };
            _layerHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
            _layerCellStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11
            };
        }
    }
}
#endif
