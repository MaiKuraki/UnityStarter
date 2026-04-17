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
        private readonly List<SceneTracker.SceneInfo> _filtered = new List<SceneTracker.SceneInfo>(16);

        private string _search = string.Empty;
        private Vector2 _scroll;
        private int _stateFilter;
        private double _nextRepaint;
        private bool _hasSnapshot;

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
            Filter();
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
                GUILayout.Label($"Tracked: {_snapshot.Count}", EditorStyles.miniLabel);
            }
        }

        private void RefreshSnapshot()
        {
            _snapshot.Clear();
            var scenes = SceneTracker.GetTrackedScenes();
            for (int i = 0; i < scenes.Count; i++) _snapshot.Add(scenes[i]);
            _hasSnapshot = true;
        }

        private void Filter()
        {
            _filtered.Clear();
            for (int i = 0; i < _snapshot.Count; i++)
            {
                var info = _snapshot[i];
                if (!MatchesState(info) || !MatchesSearch(info)) continue;
                _filtered.Add(info);
            }
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
            int loading = 0, waiting = 0, activated = 0, unloadPending = 0, manual = 0;
            for (int i = 0; i < _filtered.Count; i++)
            {
                var info = _filtered[i];
                if (info.UnloadRequested) unloadPending++;
                if (info.ActivationMode == SceneActivationMode.Manual) manual++;

                switch (info.ActivationState)
                {
                    case SceneActivationState.Loading: loading++; break;
                    case SceneActivationState.WaitingForActivation: waiting++; break;
                    case SceneActivationState.Activated: activated++; break;
                }
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                DrawMetric("Visible", _filtered.Count);
                DrawMetric("Loading", loading);
                DrawMetric("Waiting", waiting);
                DrawMetric("Activated", activated);
                DrawMetric("Unload Pending", unloadPending);
                DrawMetric("Manual", manual);
            }
        }

        private static void DrawMetric(string label, int value)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(110f)))
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                GUILayout.Label(value.ToString(), EditorStyles.boldLabel);
            }
        }

        private void DrawTable()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Scene", EditorStyles.boldLabel, GUILayout.Width(SceneColumnWidth));
                GUILayout.Label("Provider", EditorStyles.boldLabel, GUILayout.Width(PROVIDER_W));
                GUILayout.Label("Package", EditorStyles.boldLabel, GUILayout.Width(PACKAGE_W));
                GUILayout.Label("Bucket", EditorStyles.boldLabel, GUILayout.Width(BUCKET_W));
                GUILayout.Label("State", EditorStyles.boldLabel, GUILayout.Width(STATE_W));
                GUILayout.Label("Progress", EditorStyles.boldLabel, GUILayout.Width(PROGRESS_W));
                GUILayout.Label("Refs", EditorStyles.boldLabel, GUILayout.Width(REFS_W));
                GUILayout.Label("Age", EditorStyles.boldLabel, GUILayout.Width(AGE_W));
                GUILayout.FlexibleSpace();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _filtered.Count; i++)
            {
                DrawRow(_filtered[i], i);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(SceneTracker.SceneInfo info, int rowIndex)
        {
            Color bg = rowIndex % 2 == 0
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.19f, 0.19f, 0.19f, 1f);

            if (info.UnloadRequested) bg = new Color(0.28f, 0.20f, 0.12f, 1f);
            else if (info.ActivationState == SceneActivationState.WaitingForActivation) bg = new Color(0.16f, 0.22f, 0.30f, 1f);

            var rect = EditorGUILayout.BeginHorizontal();
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, bg);

            string sceneLabel = !string.IsNullOrEmpty(info.RuntimeSceneName) ? info.RuntimeSceneName : info.SceneLocation;
            GUILayout.Label(new GUIContent(sceneLabel, info.ScenePath ?? info.SceneLocation), EditorStyles.label, GUILayout.Width(SceneColumnWidth));
            GUILayout.Label(info.ProviderType ?? "-", EditorStyles.label, GUILayout.Width(PROVIDER_W));
            GUILayout.Label(info.PackageName ?? "-", EditorStyles.label, GUILayout.Width(PACKAGE_W));
            GUILayout.Label(string.IsNullOrEmpty(info.Bucket) ? "-" : info.Bucket, EditorStyles.label, GUILayout.Width(BUCKET_W));
            GUILayout.Label(GetStateLabel(info), EditorStyles.label, GUILayout.Width(STATE_W));
            GUILayout.Label($"{info.Progress * 100f:F0}%", EditorStyles.label, GUILayout.Width(PROGRESS_W));
            GUILayout.Label(info.RefCount.ToString(), EditorStyles.label, GUILayout.Width(REFS_W));
            GUILayout.Label(FormatAge(info.RegistrationTimeUtc), EditorStyles.label, GUILayout.Width(AGE_W));
            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
        }

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

        private static string FormatAge(DateTime registeredUtc)
        {
            double seconds = (DateTime.UtcNow - registeredUtc).TotalSeconds;
            if (seconds < 60) return $"{seconds:F0}s";
            if (seconds < 3600) return $"{seconds / 60d:F1}m";
            return $"{seconds / 3600d:F1}h";
        }
    }
}
#endif
