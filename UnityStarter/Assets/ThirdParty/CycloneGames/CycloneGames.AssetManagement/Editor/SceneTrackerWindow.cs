#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Editor
{
    public sealed class SceneTrackerWindow : EditorWindow
    {
        private readonly List<SceneTracker.SceneInfo> _snapshot = new List<SceneTracker.SceneInfo>(16);
        // Pre-formatted, repaint-stable view models for the full snapshot (rebuilt only on refresh).
        private readonly List<SceneRowView> _views = new List<SceneRowView>(16);
        // Indices into _snapshot/_views that pass the current filter (rebuilt only when the display is dirty).
        private readonly List<int> _filteredIndices = new List<int>(16);
        private readonly string[] _metricValues = new string[6];
        private readonly GUIContent _cell = new GUIContent();
        private string _trackedText = "Tracked: 0";

        private string _search = string.Empty;
        private string _lastSearch = string.Empty;
        private int _stateFilter;
        private int _lastStateFilter = -1;
        private bool _displayDirty = true;
        private Vector2 _scroll;
        private double _nextRepaint;
        private bool _hasSnapshot;

        // Cached GUILayoutOption arrays — avoids the per-call params[] allocation of GUILayout.Width.
        private GUILayoutOption[] _wScene, _wProvider, _wPackage, _wBucket, _wState, _wProgress, _wRefs, _wAge, _wMetric;
        private float _lastSceneColWidth = -1f;
        private bool _widthsBuilt;

        private const float PROVIDER_W = 90f;
        private const float PACKAGE_W = 110f;
        private const float BUCKET_W = 110f;
        private const float STATE_W = 95f;
        private const float PROGRESS_W = 55f;
        private const float REFS_W = 36f;
        private const float AGE_W = 52f;
        private float SceneColumnWidth => Mathf.Max(
            180f,
            position.width - PROVIDER_W - PACKAGE_W - BUCKET_W - STATE_W - PROGRESS_W - REFS_W - AGE_W - 44f);

        private static readonly string[] _stateOptions =
        {
            "All",
            "Loading",
            "Waiting",
            "Activated",
            "Unload Pending"
        };

        [MenuItem("Tools/CycloneGames/AssetManagement/Scene Tracker")]
        public static void ShowWindow()
        {
            var w = GetWindow<SceneTrackerWindow>("Scene Tracker");
            w.minSize = new Vector2(760f, 340f);
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
            if (!Application.isPlaying) return;
            if (EditorApplication.timeSinceStartup >= _nextRepaint)
            {
                _nextRepaint = EditorApplication.timeSinceStartup + 0.15;
                RefreshSnapshot();
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Run the game to inspect live scene lifecycle diagnostics.", MessageType.Info);
                return;
            }

            if (!_hasSnapshot) RefreshSnapshot();
            if (_search != _lastSearch || _stateFilter != _lastStateFilter)
            {
                _lastSearch = _search;
                _lastStateFilter = _stateFilter;
                _displayDirty = true;
            }
            if (_displayDirty) RebuildDisplay();

            EnsureWidths();
            DrawSummary();
            DrawTable();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    RefreshSnapshot();
                }

                GUILayout.Space(6f);
                GUILayout.Label("State", GUILayout.Width(34f));
                _stateFilter = EditorGUILayout.Popup(_stateFilter, _stateOptions, EditorStyles.toolbarPopup, GUILayout.Width(110f));

                GUILayout.Space(6f);
                GUILayout.Label("Search", GUILayout.Width(40f));
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);

                GUILayout.FlexibleSpace();
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
            _displayDirty = true;
            _hasSnapshot = true;
        }

        private static SceneRowView BuildView(SceneTracker.SceneInfo info, DateTime nowUtc)
        {
            byte kind = info.UnloadRequested ? (byte)1
                : info.ActivationState == SceneActivationState.WaitingForActivation ? (byte)2 : (byte)0;
            string label = !string.IsNullOrEmpty(info.RuntimeSceneName) ? info.RuntimeSceneName : info.SceneLocation;
            return new SceneRowView
            {
                SceneLabel = label ?? "-",
                SceneTooltip = info.ScenePath ?? info.SceneLocation ?? string.Empty,
                Provider = info.ProviderType ?? "-",
                Package = info.PackageName ?? "-",
                Bucket = string.IsNullOrEmpty(info.Bucket) ? "-" : info.Bucket,
                State = GetStateLabel(info),
                Progress = (info.Progress * 100f).ToString("F0") + "%",
                Refs = info.RefCount.ToString(),
                Age = FormatAge(info.RegistrationTimeUtc, nowUtc),
                StateKind = kind
            };
        }

        private void RebuildDisplay()
        {
            _filteredIndices.Clear();
            int loading = 0, waiting = 0, activated = 0, unloadPending = 0, manual = 0;
            for (int i = 0; i < _snapshot.Count; i++)
            {
                var info = _snapshot[i];
                if (!MatchesState(info) || !MatchesSearch(info)) continue;
                _filteredIndices.Add(i);
                if (info.UnloadRequested) unloadPending++;
                if (info.ActivationMode == SceneActivationMode.Manual) manual++;
                switch (info.ActivationState)
                {
                    case SceneActivationState.Loading: loading++; break;
                    case SceneActivationState.WaitingForActivation: waiting++; break;
                    case SceneActivationState.Activated: activated++; break;
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
                case 1: return info.ActivationState == SceneActivationState.Loading && !info.UnloadRequested;
                case 2: return info.ActivationState == SceneActivationState.WaitingForActivation && !info.UnloadRequested;
                case 3: return info.ActivationState == SceneActivationState.Activated && !info.UnloadRequested;
                case 4: return info.UnloadRequested;
                default: return true;
            }
        }

        private bool MatchesSearch(SceneTracker.SceneInfo info)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return Contains(info.SceneLocation, _search)
                || Contains(info.PackageName, _search)
                || Contains(info.ProviderType, _search)
                || Contains(info.ScenePath, _search)
                || Contains(info.RuntimeSceneName, _search)
                || Contains(info.Bucket, _search);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawSummary()
        {
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
            using (new EditorGUILayout.VerticalScope(_wMetric))
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                GUILayout.Label(value, EditorStyles.boldLabel);
            }
        }

        private void DrawTable()
        {
            EnsureWidths();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Scene", EditorStyles.boldLabel, _wScene);
                GUILayout.Label("Provider", EditorStyles.boldLabel, _wProvider);
                GUILayout.Label("Package", EditorStyles.boldLabel, _wPackage);
                GUILayout.Label("Bucket", EditorStyles.boldLabel, _wBucket);
                GUILayout.Label("State", EditorStyles.boldLabel, _wState);
                GUILayout.Label("Progress", EditorStyles.boldLabel, _wProgress);
                GUILayout.Label("Refs", EditorStyles.boldLabel, _wRefs);
                GUILayout.Label("Age", EditorStyles.boldLabel, _wAge);
                GUILayout.FlexibleSpace();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _filteredIndices.Count; i++)
            {
                DrawRow(_views[_filteredIndices[i]], i);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(in SceneRowView view, int rowIndex)
        {
            Color bg = view.StateKind == 1 ? RowUnloading
                : view.StateKind == 2 ? RowWaiting
                : (rowIndex % 2 == 0 ? RowEven : RowOdd);

            var rect = EditorGUILayout.BeginHorizontal();
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, bg);

            _cell.text = view.SceneLabel; _cell.tooltip = view.SceneTooltip;
            GUILayout.Label(_cell, EditorStyles.label, _wScene);
            GUILayout.Label(view.Provider, EditorStyles.label, _wProvider);
            GUILayout.Label(view.Package, EditorStyles.label, _wPackage);
            GUILayout.Label(view.Bucket, EditorStyles.label, _wBucket);
            GUILayout.Label(view.State, EditorStyles.label, _wState);
            GUILayout.Label(view.Progress, EditorStyles.label, _wProgress);
            GUILayout.Label(view.Refs, EditorStyles.label, _wRefs);
            GUILayout.Label(view.Age, EditorStyles.label, _wAge);
            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
        }

        private void EnsureWidths()
        {
            if (!_widthsBuilt)
            {
                _wProvider = new[] { GUILayout.Width(PROVIDER_W) };
                _wPackage = new[] { GUILayout.Width(PACKAGE_W) };
                _wBucket = new[] { GUILayout.Width(BUCKET_W) };
                _wState = new[] { GUILayout.Width(STATE_W) };
                _wProgress = new[] { GUILayout.Width(PROGRESS_W) };
                _wRefs = new[] { GUILayout.Width(REFS_W) };
                _wAge = new[] { GUILayout.Width(AGE_W) };
                _wMetric = new[] { GUILayout.Width(110f) };
                _widthsBuilt = true;
            }
            float scw = SceneColumnWidth;
            if (_wScene == null || Mathf.Abs(scw - _lastSceneColWidth) > 0.5f)
            {
                _wScene = new[] { GUILayout.Width(scw) };
                _lastSceneColWidth = scw;
            }
        }

        private static Color RowEven => EditorGUIUtility.isProSkin
            ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f);
        private static Color RowOdd => EditorGUIUtility.isProSkin
            ? new Color(0.19f, 0.19f, 0.19f, 1f) : new Color(0.80f, 0.80f, 0.80f, 1f);
        private static Color RowUnloading => EditorGUIUtility.isProSkin
            ? new Color(0.28f, 0.20f, 0.12f, 1f) : new Color(0.98f, 0.90f, 0.76f, 1f);
        private static Color RowWaiting => EditorGUIUtility.isProSkin
            ? new Color(0.16f, 0.22f, 0.30f, 1f) : new Color(0.79f, 0.88f, 0.98f, 1f);

        private static string GetStateLabel(SceneTracker.SceneInfo info)
        {
            if (info.UnloadRequested) return "Unloading";
            switch (info.ActivationState)
            {
                case SceneActivationState.Loading: return "Loading";
                case SceneActivationState.WaitingForActivation: return "Waiting";
                case SceneActivationState.Activated: return "Activated";
                default: return info.ActivationState.ToString();
            }
        }

        private static string FormatAge(DateTime registeredUtc, DateTime nowUtc)
        {
            double seconds = (nowUtc - registeredUtc).TotalSeconds;
            if (seconds < 60) return seconds.ToString("F0") + "s";
            if (seconds < 3600) return (seconds / 60d).ToString("F1") + "m";
            return (seconds / 3600d).ToString("F1") + "h";
        }

        private struct SceneRowView
        {
            public string SceneLabel;
            public string SceneTooltip;
            public string Provider;
            public string Package;
            public string Bucket;
            public string State;
            public string Progress;
            public string Refs;
            public string Age;
            public byte StateKind; // 0 = normal, 1 = unloading, 2 = waiting
        }
    }
}
#endif
