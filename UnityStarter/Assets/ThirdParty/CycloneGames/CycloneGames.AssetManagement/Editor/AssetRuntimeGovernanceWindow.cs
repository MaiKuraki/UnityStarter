#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Editor
{
    public sealed class AssetRuntimeGovernanceWindow : EditorWindow
    {
        private readonly List<HandleTracker.HandleInfo> _handles = new List<HandleTracker.HandleInfo>(256);
        private readonly List<SceneTracker.SceneInfo> _scenes = new List<SceneTracker.SceneInfo>(32);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _active = new List<AssetCacheService.CacheDiagnosticEntry>(512);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _trial = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _main = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _activeScratch = new List<AssetCacheService.CacheDiagnosticEntry>(512);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _trialScratch = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _mainScratch = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly Dictionary<string, int> _bucketCounts = new Dictionary<string, int>(64, StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, int>> _bucketPairs = new List<KeyValuePair<string, int>>(64);

        private Vector2 _scroll;
        private double _nextRepaint;
        private bool _hasSnapshot;

        [MenuItem("Tools/CycloneGames/AssetManagement/Runtime Governance")]
        public static void ShowWindow()
        {
            var w = GetWindow<AssetRuntimeGovernanceWindow>("Asset Runtime Governance");
            w.minSize = new Vector2(820f, 420f);
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
                _nextRepaint = EditorApplication.timeSinceStartup + 0.2;
                RefreshSnapshot();
                Repaint();
            }
        }

        private void OnGUI()
        {
            float toolbarWidth = Mathf.Max(320f, position.width - 12f);
            float refreshWidth = 64f;
            float navWidth = Mathf.Max(120f, (toolbarWidth - refreshWidth) / 3f);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(refreshWidth)))
                {
                    RefreshSnapshot();
                }
                if (GUILayout.Button("Open Cache Debugger", EditorStyles.toolbarButton, GUILayout.Width(navWidth)))
                {
                    AssetCacheDebuggerWindow.ShowWindow();
                }
                if (GUILayout.Button("Open Handle Tracker", EditorStyles.toolbarButton, GUILayout.Width(navWidth)))
                {
                    HandleTrackerWindow.ShowWindow();
                }
                if (GUILayout.Button("Open Scene Tracker", EditorStyles.toolbarButton, GUILayout.Width(navWidth)))
                {
                    SceneTrackerWindow.ShowWindow();
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Run the game to inspect live runtime governance metrics.", MessageType.Info);
                return;
            }

            if (!_hasSnapshot) RefreshSnapshot();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawOverview();
            DrawBucketSummary();
            DrawLongLivedHandles();
            DrawSceneSummary();
            EditorGUILayout.EndScrollView();
        }

        private void RefreshSnapshot()
        {
            _handles.Clear();
            _scenes.Clear();
            _active.Clear();
            _trial.Clear();
            _main.Clear();
            _bucketCounts.Clear();
            _bucketPairs.Clear();

            var handles = HandleTracker.GetActiveHandles();
            for (int i = 0; i < handles.Count; i++) _handles.Add(handles[i]);

            var scenes = SceneTracker.GetTrackedScenes();
            for (int i = 0; i < scenes.Count; i++) _scenes.Add(scenes[i]);

            var caches = AssetCacheService.GlobalInstances;
            for (int i = 0; i < caches.Count; i++)
            {
                caches[i].GetDiagnostics(_activeScratch, _trialScratch, _mainScratch);
                for (int j = 0; j < _activeScratch.Count; j++) _active.Add(_activeScratch[j]);
                for (int j = 0; j < _trialScratch.Count; j++) _trial.Add(_trialScratch[j]);
                for (int j = 0; j < _mainScratch.Count; j++) _main.Add(_mainScratch[j]);
            }

            CountBuckets(_active);
            CountBuckets(_trial);
            CountBuckets(_main);

            foreach (var kvp in _bucketCounts)
                _bucketPairs.Add(kvp);

            _bucketPairs.Sort((a, b) => b.Value.CompareTo(a.Value));
            _handles.Sort((a, b) => a.RegistrationTime.CompareTo(b.RegistrationTime));
            _scenes.Sort((a, b) => a.RegistrationTimeUtc.CompareTo(b.RegistrationTimeUtc));
            _hasSnapshot = true;
        }

        private void CountBuckets(List<AssetCacheService.CacheDiagnosticEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                string key = string.IsNullOrEmpty(entries[i].Bucket) ? "<none>" : entries[i].Bucket;
                _bucketCounts.TryGetValue(key, out int count);
                _bucketCounts[key] = count + 1;
            }
        }

        private void DrawOverview()
        {
            int waitingScenes = 0;
            int unloadingScenes = 0;
            for (int i = 0; i < _scenes.Count; i++)
            {
                if (_scenes[i].UnloadRequested) unloadingScenes++;
                if (_scenes[i].ActivationState == SceneActivationState.WaitingForActivation) waitingScenes++;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Overview", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawMetric("Active Handles", _handles.Count);
                    DrawMetric("Tracked Scenes", _scenes.Count);
                    DrawMetric("Waiting Scenes", waitingScenes);
                    DrawMetric("Unloading Scenes", unloadingScenes);
                    DrawMetric("Active Cache", _active.Count);
                    DrawMetric("Idle Trial", _trial.Count);
                    DrawMetric("Idle Main", _main.Count);
                    DrawMetric("Buckets", _bucketCounts.Count);
                }
            }
        }

        private static void DrawMetric(string label, int value)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(100f)))
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                GUILayout.Label(value.ToString(), EditorStyles.boldLabel);
            }
        }

        private void DrawBucketSummary()
        {
            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Top Buckets", EditorStyles.boldLabel);
                int count = Mathf.Min(10, _bucketPairs.Count);
                if (count == 0)
                {
                    EditorGUILayout.LabelField("No bucketed cache entries.", EditorStyles.miniLabel);
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(_bucketPairs[i].Key, GUILayout.Width(320f));
                        GUILayout.Label(_bucketPairs[i].Value.ToString(), EditorStyles.boldLabel, GUILayout.Width(40f));
                    }
                }
            }
        }

        private void DrawLongLivedHandles()
        {
            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Longest-Lived Active Handles", EditorStyles.boldLabel);
                if (_handles.Count == 0)
                {
                    EditorGUILayout.LabelField("No tracked active handles.", EditorStyles.miniLabel);
                    return;
                }

                int count = Mathf.Min(10, _handles.Count);
                for (int i = 0; i < count; i++)
                {
                    var handle = _handles[i];
                    TimeSpan age = DateTime.UtcNow - handle.RegistrationTime;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(handle.PackageName ?? "-", GUILayout.Width(100f));
                        GUILayout.Label(handle.Description ?? "-", GUILayout.Width(520f));
                        GUILayout.Label(FormatAge(age), GUILayout.Width(60f));
                    }
                }
            }
        }

        private void DrawSceneSummary()
        {
            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scene Lifecycle Snapshot", EditorStyles.boldLabel);
                if (_scenes.Count == 0)
                {
                    EditorGUILayout.LabelField("No tracked scenes.", EditorStyles.miniLabel);
                    return;
                }

                int count = Mathf.Min(10, _scenes.Count);
                for (int i = 0; i < count; i++)
                {
                    var scene = _scenes[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(scene.PackageName ?? "-", GUILayout.Width(100f));
                        GUILayout.Label(scene.SceneLocation ?? "-", GUILayout.Width(320f));
                        GUILayout.Label(scene.ProviderType ?? "-", GUILayout.Width(90f));
                        GUILayout.Label(scene.UnloadRequested ? "Unloading" : scene.ActivationState.ToString(), GUILayout.Width(90f));
                        GUILayout.Label($"{scene.Progress * 100f:F0}%", GUILayout.Width(50f));
                        GUILayout.Label(FormatAge(DateTime.UtcNow - scene.RegistrationTimeUtc), GUILayout.Width(60f));
                    }
                }
            }
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 60) return $"{age.TotalSeconds:F0}s";
            if (age.TotalMinutes < 60) return $"{age.TotalMinutes:F1}m";
            return $"{age.TotalHours:F1}h";
        }
    }
}
#endif
