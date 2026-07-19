using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    public sealed class UIRuntimeMonitorWindow : EditorWindow
    {
        private const double DefaultRefreshInterval = 0.5d;
        private static readonly string[] IntCache = BuildIntCache(1024);

        private readonly List<UILayerRuntimeStats> _layerStats =
            new List<UILayerRuntimeStats>(16);
        private readonly List<string> _layerValueText = new List<string>(16);
        private UIManager _target;
        private UIPerformanceStats _stats;
        private Vector2 _scrollPosition;
        private double _nextRefreshTime;
        private double _refreshInterval = DefaultRefreshInterval;
        private bool _hasSnapshot;
        private bool _showLifecycle = true;
        private bool _showLayers = true;
        private string _sessionUsageText = "0 / 0";

        [MenuItem("Tools/CycloneGames/UI Framework/Runtime Monitor")]
        public static void ShowWindow()
        {
            UIRuntimeMonitorWindow window = GetWindow<UIRuntimeMonitorWindow>("UI Runtime Monitor");
            window.minSize = new Vector2(500f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("UI Runtime Monitor");
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            ClearSnapshot();
        }

        private void OnGUI()
        {
            InspectorUiUtility.DrawInspectorTitle(
                "UI Runtime Monitor",
                "Bounded snapshots from an explicitly selected UIManager",
                InspectorUiUtility.RuntimeColor);

            DrawTargetPanel();
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to read runtime UI state.", MessageType.Info);
                return;
            }

            if (_target == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a UIManager or use Find in Open Scenes. Scene scanning runs only when the button is pressed.",
                    MessageType.Info);
                return;
            }

            if (!_target.IsInitialized)
            {
                EditorGUILayout.HelpBox("The selected UIManager is not initialized.", MessageType.Warning);
                return;
            }

            if (!_hasSnapshot)
            {
                RefreshSnapshot();
            }

            DrawSnapshot();
        }

        private void DrawTargetPanel()
        {
            InspectorUiUtility.BeginPanel();
            UIManager nextTarget = (UIManager)EditorGUILayout.ObjectField(
                "UIManager",
                _target,
                typeof(UIManager),
                true);
            if (nextTarget != _target)
            {
                _target = nextTarget;
                ClearSnapshot();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Selection"))
            {
                _target = Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponentInParent<UIManager>()
                    : null;
                ClearSnapshot();
            }

            if (GUILayout.Button("Find in Open Scenes"))
            {
                _target = UnityEngine.Object.FindFirstObjectByType<UIManager>(
                    FindObjectsInactive.Include);
                ClearSnapshot();
            }

            using (new EditorGUI.DisabledScope(
                       !Application.isPlaying || _target == null || !_target.IsInitialized))
            {
                if (GUILayout.Button("Refresh Now"))
                {
                    RefreshSnapshot();
                }
            }
            EditorGUILayout.EndHorizontal();

            _refreshInterval = EditorGUILayout.Slider(
                "Refresh interval (s)",
                (float)_refreshInterval,
                0.1f,
                2f);
            InspectorUiUtility.EndPanel();
        }

        private void DrawSnapshot()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            _showLifecycle = InspectorUiUtility.DrawFoldoutHeader(
                "Lifecycle",
                _showLifecycle,
                InspectorUiUtility.RuntimeColor,
                IntToString(_stats.SessionCount),
                _stats.SessionCount >= _stats.MaxWindowCapacity
                    ? InspectorUiUtility.WarningColor
                    : InspectorUiUtility.SuccessColor);
            if (_showLifecycle)
            {
                InspectorUiUtility.BeginPanel();
                InspectorUiUtility.DrawStatusRow("Sessions", _sessionUsageText, _stats.SessionCount >= _stats.MaxWindowCapacity ? InspectorUiUtility.WarningColor : InspectorUiUtility.SuccessColor);
                InspectorUiUtility.DrawStatusRow("Opening", IntToString(_stats.OpeningWindowCount), InspectorUiUtility.SetupColor);
                InspectorUiUtility.DrawStatusRow("Open", IntToString(_stats.OpenWindowCount), InspectorUiUtility.SuccessColor);
                InspectorUiUtility.DrawStatusRow("Closing", IntToString(_stats.ClosingWindowCount), InspectorUiUtility.WarningColor);
                InspectorUiUtility.DrawStatusRow("Scene-bound", IntToString(_stats.SceneBoundWindowCount), InspectorUiUtility.AssetColor);
                InspectorUiUtility.DrawStatusRow("Bindings", IntToString(_stats.BinderCount), InspectorUiUtility.NeutralColor);
                InspectorUiUtility.DrawStatusRow("Isolated canvases", IntToString(_stats.IsolatedWindowCanvasCount), InspectorUiUtility.RuntimeColor);
                InspectorUiUtility.EndPanel();
            }

            _showLayers = InspectorUiUtility.DrawFoldoutHeader(
                "Layers",
                _showLayers,
                InspectorUiUtility.AssetColor,
                IntToString(_layerStats.Count),
                InspectorUiUtility.AssetColor);
            if (_showLayers)
            {
                InspectorUiUtility.BeginPanel();
                if (_layerStats.Count == 0)
                {
                    EditorGUILayout.HelpBox("No layer runtime data is available.", MessageType.Info);
                }
                else
                {
                    for (int i = 0; i < _layerStats.Count; i++)
                    {
                        UILayerRuntimeStats layer = _layerStats[i];
                        InspectorUiUtility.DrawStatusRow(
                            string.IsNullOrEmpty(layer.LayerName) ? "<unnamed>" : layer.LayerName,
                            _layerValueText[i],
                            layer.WindowCount > 0 ? InspectorUiUtility.SuccessColor : InspectorUiUtility.NeutralColor);
                    }
                }
                InspectorUiUtility.EndPanel();
            }

            EditorGUILayout.HelpBox(
                "Snapshots are diagnostic counts, not a frame profiler. Use Unity Profiler, Frame Debugger, retained-object inspection, and target-device captures for performance conclusions.",
                MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying ||
                _target == null ||
                !_target.IsInitialized ||
                EditorApplication.timeSinceStartup < _nextRefreshTime)
            {
                return;
            }

            RefreshSnapshot();
            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode ||
                state == PlayModeStateChange.EnteredEditMode)
            {
                ClearSnapshot();
            }
        }

        private void RefreshSnapshot()
        {
            if (_target == null || !_target.IsInitialized)
            {
                ClearSnapshot();
                return;
            }

            IUIService service = _target.Service;
            _stats = service.GetPerformanceStats();
            service.CopyLayerRuntimeStats(_layerStats);
            _sessionUsageText =
                IntToString(_stats.SessionCount) + " / " + IntToString(_stats.MaxWindowCapacity);
            _layerValueText.Clear();
            if (_layerValueText.Capacity < _layerStats.Count)
            {
                _layerValueText.Capacity = _layerStats.Count;
            }
            for (int i = 0; i < _layerStats.Count; i++)
            {
                UILayerRuntimeStats layer = _layerStats[i];
                _layerValueText.Add(
                    "Sort " + IntToString(layer.SortingOrder) +
                    " · Windows " + IntToString(layer.WindowCount));
            }
            _hasSnapshot = true;
            _nextRefreshTime = EditorApplication.timeSinceStartup + _refreshInterval;
        }

        private void ClearSnapshot()
        {
            _stats = default;
            _layerStats.Clear();
            _layerValueText.Clear();
            _sessionUsageText = "0 / 0";
            _hasSnapshot = false;
            _nextRefreshTime = 0d;
        }

        private static string[] BuildIntCache(int length)
        {
            var result = new string[length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = i.ToString();
            }

            return result;
        }

        private static string IntToString(int value)
        {
            return value >= 0 && value < IntCache.Length
                ? IntCache[value]
                : value.ToString();
        }
    }
}
