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
        private const int MaxVisibleRows = 10;
        private const int MaxAutomaticHandles = 4_096;
        private const int MaxAutomaticCacheRowsPerTier = 4_096;
        private const int MetricCount = 8;
        private const double AutoRefreshIntervalSeconds = 0.5d;
        private const float SectionSpacing = 8f;
        private const float HeaderHeight = 20f;
        private const float RowHeight = 20f;
        private const float ColumnSpacing = 8f;
        private const float MetricCardHeight = 40f;
        private const float MetricCardMinWidth = 120f;

        private static readonly string[] MetricLabels =
        {
            "Tracked Handles",
            "Tracked Scenes",
            "Waiting Scenes",
            "Unloading Scenes",
            "Active Cache",
            "Idle Probation",
            "Idle Protected",
            "Sampled Buckets"
        };

        private readonly List<HandleTracker.HandleInfo> _handles = new List<HandleTracker.HandleInfo>(256);
        private readonly List<SceneTracker.SceneInfo> _scenes = new List<SceneTracker.SceneInfo>(32);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _active = new List<AssetCacheService.CacheDiagnosticEntry>(512);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _trial = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _main = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _activeScratch = new List<AssetCacheService.CacheDiagnosticEntry>(512);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _trialScratch = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService.CacheDiagnosticEntry> _mainScratch = new List<AssetCacheService.CacheDiagnosticEntry>(256);
        private readonly List<AssetCacheService> _cacheInstances = new List<AssetCacheService>(4);
        private readonly Dictionary<string, int> _bucketCounts = new Dictionary<string, int>(64, StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, int>> _bucketPairs = new List<KeyValuePair<string, int>>(64);
        private readonly List<BucketRow> _bucketRows = new List<BucketRow>(64);
        private readonly List<HandleRow> _handleRows = new List<HandleRow>(256);
        private readonly List<SceneRow> _sceneRows = new List<SceneRow>(64);
        private readonly List<HandleTracker.HandleInfo> _oldestHandles = new List<HandleTracker.HandleInfo>(MaxVisibleRows);
        private readonly List<SceneTracker.SceneInfo> _oldestScenes = new List<SceneTracker.SceneInfo>(MaxVisibleRows);

        private readonly float[] _bucketWeights = { 0.84f, 0.16f };
        private readonly float[] _handleWeights = { 0.17f, 0.67f, 0.16f };
        private readonly float[] _sceneWeights = { 0.14f, 0.41f, 0.12f, 0.14f, 0.09f, 0.10f };
        private readonly Rect[] _bucketColumns = new Rect[2];
        private readonly Rect[] _handleColumns = new Rect[3];
        private readonly Rect[] _sceneColumns = new Rect[6];
        private readonly string[] _metricValues = new string[MetricCount];
        private readonly GUIContent _cellContent = new GUIContent();

        private Vector2 _scroll;
        private double _nextRepaint;
        private bool _hasSnapshot;
        private bool _stylesInitialized;
        private bool _isVisible = true;
        private bool _handlesTruncated;
        private bool _cacheDiagnosticsTruncated;
        private int _handleTotal;
        private int _activeTotal;
        private int _trialTotal;
        private int _mainTotal;

        private GUIStyle _sectionTitleStyle;
        private GUIStyle _tableHeaderStyle;
        private GUIStyle _tableCellStyle;
        private GUIStyle _tableCellRightStyle;
        private GUIStyle _metricLabelStyle;
        private GUIStyle _metricValueStyle;

        [MenuItem("Tools/CycloneGames/AssetManagement/Runtime Governance")]
        public static void ShowWindow()
        {
            var w = GetWindow<AssetRuntimeGovernanceWindow>("Asset Runtime Governance");
            w.minSize = new Vector2(900f, 460f);
            w.Show();
        }

        private void OnEnable()
        {
            _isVisible = true;
            _nextRepaint = 0d;
            EditorApplication.update += OnEditorUpdate;
            if (Application.isPlaying)
                RefreshSnapshot();
            else
                _hasSnapshot = false;
        }

        private void OnDisable()
        {
            _isVisible = false;
            EditorApplication.update -= OnEditorUpdate;
            _cacheInstances.Clear();
        }

        private void OnBecameVisible()
        {
            _isVisible = true;
            _nextRepaint = 0d;
            _hasSnapshot = false;
        }

        private void OnBecameInvisible()
        {
            _isVisible = false;
            _cacheInstances.Clear();
        }

        private void OnEditorUpdate()
        {
            if (!_isVisible || !Application.isPlaying) return;
            if (EditorApplication.timeSinceStartup < _nextRepaint) return;

            _nextRepaint = EditorApplication.timeSinceStartup + AutoRefreshIntervalSeconds;
            RefreshSnapshot();
            Repaint();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Run the game to inspect live runtime governance metrics.", MessageType.Info);
                return;
            }

            if (!_hasSnapshot) RefreshSnapshot();

            if (_handlesTruncated)
            {
                EditorGUILayout.HelpBox(
                    $"Handle rows are capped at {MaxAutomaticHandles:N0}. The tracked-registry total is exact for the current observation epoch; " +
                    "the longest-lived table is calculated from the captured sample.",
                    MessageType.Warning);
            }

            if (HandleTracker.ObservationIncomplete)
            {
                EditorGUILayout.HelpBox(
                    "Handle observation began after one or more handles may have been created, or the enabled registry was cleared. " +
                    "Counts are exact for the current observation epoch but may not cover every live provider handle. Restart Play Mode with tracking enabled for a complete epoch.",
                    MessageType.Warning);
            }

            long droppedHandles = HandleTracker.DroppedRegistrationCount;
            if (droppedHandles > 0L)
            {
                EditorGUILayout.HelpBox(
                    $"Handle tracking dropped {droppedHandles:N0} registration(s) after reaching its configured capacity. " +
                    "The tracked-registry total is not a complete live-handle count for this observation epoch.",
                    MessageType.Warning);
            }

            long droppedScenes = SceneTracker.DroppedRegistrationCount;
            if (droppedScenes > 0L)
            {
                EditorGUILayout.HelpBox(
                    $"Scene tracking dropped {droppedScenes:N0} registration(s) after reaching its configured capacity. " +
                    "The scene summary is incomplete for this Play Mode session.",
                    MessageType.Warning);
            }

            if (SceneTracker.ObservationIncomplete)
            {
                EditorGUILayout.HelpBox(
                    "Scene observation is incomplete because tracking was disabled or cleared, or a previous Play Mode scene owner survived subsystem registration. " +
                    "The scene summary covers only the current registry.",
                    MessageType.Warning);
            }

            if (_cacheDiagnosticsTruncated)
            {
                EditorGUILayout.HelpBox(
                    $"Cache detail is capped at {MaxAutomaticCacheRowsPerTier:N0} rows per tier across all cache instances. " +
                    "Tier totals are exact; bucket counts are a bounded sample.",
                    MessageType.Warning);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawOverview();
            DrawBucketSummary();
            DrawLongLivedHandles();
            DrawSceneSummary();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            float toolbarWidth = Mathf.Max(360f, position.width - 12f);
            float refreshWidth = 64f;
            float navWidth = Mathf.Max(120f, (toolbarWidth - refreshWidth) / 3f);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(refreshWidth)))
                    RefreshSnapshot();

                if (GUILayout.Button("Open Cache Debugger", EditorStyles.toolbarButton, GUILayout.Width(navWidth)))
                    AssetCacheDebuggerWindow.ShowWindow();

                if (GUILayout.Button("Open Handle Tracker", EditorStyles.toolbarButton, GUILayout.Width(navWidth)))
                    HandleTrackerWindow.ShowWindow();

                if (GUILayout.Button("Open Scene Tracker", EditorStyles.toolbarButton, GUILayout.Width(navWidth)))
                    SceneTrackerWindow.ShowWindow();
            }
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
            _bucketRows.Clear();
            _handleRows.Clear();
            _sceneRows.Clear();
            _oldestHandles.Clear();
            _oldestScenes.Clear();
            _handleTotal = 0;
            _activeTotal = 0;
            _trialTotal = 0;
            _mainTotal = 0;
            _handlesTruncated = false;
            _cacheDiagnosticsTruncated = false;

            _handleTotal = HandleTracker.CopyActiveHandlesTo(_handles, MaxAutomaticHandles);
            _handlesTruncated = _handles.Count < _handleTotal;

            SceneTracker.CopyTrackedScenesTo(_scenes, SceneTracker.Capacity);

            AssetCacheService.CopyGlobalInstancesTo(_cacheInstances);
            for (int i = 0; i < _cacheInstances.Count; i++)
            {
                _activeScratch.Clear();
                _trialScratch.Clear();
                _mainScratch.Clear();

                int activeRemaining = Math.Max(0, MaxAutomaticCacheRowsPerTier - _active.Count);
                int trialRemaining = Math.Max(0, MaxAutomaticCacheRowsPerTier - _trial.Count);
                int mainRemaining = Math.Max(0, MaxAutomaticCacheRowsPerTier - _main.Count);
                AssetCacheService.CacheDiagnosticCapture capture = _cacheInstances[i].GetDiagnostics(
                    _activeScratch,
                    _trialScratch,
                    _mainScratch,
                    activeRemaining,
                    trialRemaining,
                    mainRemaining);
                _activeTotal += capture.ActiveTotal;
                _trialTotal += capture.ProbationTotal;
                _mainTotal += capture.ProtectedTotal;
                _cacheDiagnosticsTruncated |= capture.IsTruncated;
                for (int j = 0; j < _activeScratch.Count; j++) _active.Add(_activeScratch[j]);
                for (int j = 0; j < _trialScratch.Count; j++) _trial.Add(_trialScratch[j]);
                for (int j = 0; j < _mainScratch.Count; j++) _main.Add(_mainScratch[j]);
            }

            CountBuckets(_active);
            CountBuckets(_trial);
            CountBuckets(_main);

            foreach (var kvp in _bucketCounts)
                ConsiderBucket(kvp);

            BuildCachedRows();
            _hasSnapshot = true;
        }

        private void BuildCachedRows()
        {
            long nowTimestamp = HandleTracker.GetMonotonicTimestamp();
            long sceneNowTimestamp = SceneTracker.GetMonotonicTimestamp();
            int waitingScenes = 0;
            int unloadingScenes = 0;

            for (int i = 0; i < _bucketPairs.Count; i++)
            {
                var pair = _bucketPairs[i];
                _bucketRows.Add(new BucketRow(pair.Key, pair.Value.ToString()));
            }

            for (int i = 0; i < _handles.Count; i++)
                ConsiderOldestHandle(_handles[i]);

            for (int i = 0; i < _oldestHandles.Count; i++)
            {
                var handle = _oldestHandles[i];
                _handleRows.Add(new HandleRow(
                    handle.PackageName ?? "-",
                    handle.Description ?? "-",
                    FormatAge(TimeSpan.FromSeconds(HandleTracker.GetActiveDurationSeconds(in handle, nowTimestamp)))));
            }

            for (int i = 0; i < _scenes.Count; i++)
            {
                var scene = _scenes[i];
                if (scene.UnloadRequested) unloadingScenes++;
                if (scene.ActivationState == SceneActivationState.WaitingForActivation) waitingScenes++;

                ConsiderOldestScene(scene);
            }

            for (int i = 0; i < _oldestScenes.Count; i++)
            {
                var scene = _oldestScenes[i];
                _sceneRows.Add(new SceneRow(
                    scene.PackageName ?? "-",
                    scene.SceneLocation ?? "-",
                    scene.ProviderType ?? "-",
                    scene.UnloadRequested ? "Unloading" : scene.ActivationState.ToString(),
                    FormatPercent(scene.Progress),
                    FormatAge(TimeSpan.FromSeconds(SceneTracker.GetAgeSeconds(scene.RegistrationTimestamp, sceneNowTimestamp)))));
            }

            _metricValues[0] = _handleTotal.ToString();
            _metricValues[1] = _scenes.Count.ToString();
            _metricValues[2] = waitingScenes.ToString();
            _metricValues[3] = unloadingScenes.ToString();
            _metricValues[4] = _activeTotal.ToString();
            _metricValues[5] = _trialTotal.ToString();
            _metricValues[6] = _mainTotal.ToString();
            _metricValues[7] = _bucketCounts.Count.ToString();
        }

        private void ConsiderBucket(KeyValuePair<string, int> candidate)
        {
            int count = _bucketPairs.Count;
            if (count == MaxVisibleRows && !ComesBefore(candidate, _bucketPairs[count - 1])) return;

            int index;
            if (count < MaxVisibleRows)
            {
                _bucketPairs.Add(candidate);
                index = count;
            }
            else
            {
                index = count - 1;
                _bucketPairs[index] = candidate;
            }

            while (index > 0 && ComesBefore(candidate, _bucketPairs[index - 1]))
            {
                _bucketPairs[index] = _bucketPairs[index - 1];
                index--;
            }

            _bucketPairs[index] = candidate;
        }

        private void ConsiderOldestHandle(HandleTracker.HandleInfo candidate)
        {
            int count = _oldestHandles.Count;
            if (count == MaxVisibleRows && !ComesBefore(candidate, _oldestHandles[count - 1])) return;

            int index;
            if (count < MaxVisibleRows)
            {
                _oldestHandles.Add(candidate);
                index = count;
            }
            else
            {
                index = count - 1;
                _oldestHandles[index] = candidate;
            }

            while (index > 0 && ComesBefore(candidate, _oldestHandles[index - 1]))
            {
                _oldestHandles[index] = _oldestHandles[index - 1];
                index--;
            }

            _oldestHandles[index] = candidate;
        }

        private void ConsiderOldestScene(SceneTracker.SceneInfo candidate)
        {
            int count = _oldestScenes.Count;
            if (count == MaxVisibleRows && !ComesBefore(candidate, _oldestScenes[count - 1])) return;

            int index;
            if (count < MaxVisibleRows)
            {
                _oldestScenes.Add(candidate);
                index = count;
            }
            else
            {
                index = count - 1;
                _oldestScenes[index] = candidate;
            }

            while (index > 0 && ComesBefore(candidate, _oldestScenes[index - 1]))
            {
                _oldestScenes[index] = _oldestScenes[index - 1];
                index--;
            }

            _oldestScenes[index] = candidate;
        }

        private static bool ComesBefore(KeyValuePair<string, int> left, KeyValuePair<string, int> right)
        {
            int countComparison = right.Value.CompareTo(left.Value);
            return countComparison != 0
                ? countComparison < 0
                : string.CompareOrdinal(left.Key, right.Key) < 0;
        }

        private static bool ComesBefore(HandleTracker.HandleInfo left, HandleTracker.HandleInfo right)
        {
            int timeComparison = left.ActiveSinceTimestamp.CompareTo(right.ActiveSinceTimestamp);
            return timeComparison != 0 ? timeComparison < 0 : left.Id < right.Id;
        }

        private static bool ComesBefore(SceneTracker.SceneInfo left, SceneTracker.SceneInfo right)
        {
            int timeComparison = left.RegistrationTimestamp.CompareTo(right.RegistrationTimestamp);
            return timeComparison != 0 ? timeComparison < 0 : left.Id < right.Id;
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
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Overview", _sectionTitleStyle);

                float width = position.width - 28f;
                int columns = Mathf.Clamp(Mathf.FloorToInt((width + ColumnSpacing) / (MetricCardMinWidth + ColumnSpacing)), 2, MetricCount);
                int rows = (MetricCount + columns - 1) / columns;
                float cardWidth = (width - ((columns - 1) * ColumnSpacing)) / columns;
                float totalHeight = (rows * MetricCardHeight) + ((rows - 1) * 4f);
                Rect area = EditorGUILayout.GetControlRect(false, totalHeight);

                for (int i = 0; i < MetricCount; i++)
                {
                    int row = i / columns;
                    int col = i % columns;
                    Rect card = new Rect(
                        area.x + (col * (cardWidth + ColumnSpacing)),
                        area.y + (row * (MetricCardHeight + 4f)),
                        cardWidth,
                        MetricCardHeight);

                    DrawMetricCard(card, MetricLabels[i], _metricValues[i]);
                }
            }
        }

        private void DrawMetricCard(Rect rect, string label, string value)
        {
            EditorGUI.DrawRect(rect, GetCardColor());
            Rect labelRect = new Rect(rect.x + 6f, rect.y + 5f, rect.width - 12f, 14f);
            Rect valueRect = new Rect(rect.x + 6f, rect.y + 18f, rect.width - 12f, 18f);
            GUI.Label(labelRect, label, _metricLabelStyle);
            GUI.Label(valueRect, value, _metricValueStyle);
        }

        private void DrawBucketSummary()
        {
            GUILayout.Space(SectionSpacing);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Top Buckets", _sectionTitleStyle);

                if (_bucketRows.Count == 0)
                {
                    EditorGUILayout.LabelField("No bucketed cache entries.", EditorStyles.miniLabel);
                    return;
                }

                Rect header = EditorGUILayout.GetControlRect(false, HeaderHeight);
                SplitColumns(header, _bucketWeights, ColumnSpacing, _bucketColumns);
                DrawHeaderCell(_bucketColumns[0], "Bucket");
                DrawHeaderCell(_bucketColumns[1], "Count", _tableCellRightStyle);

                int count = Mathf.Min(MaxVisibleRows, _bucketRows.Count);
                for (int i = 0; i < count; i++)
                {
                    Rect row = EditorGUILayout.GetControlRect(false, RowHeight);
                    DrawRowBackground(row, i);
                    SplitColumns(row, _bucketWeights, ColumnSpacing, _bucketColumns);
                    DrawCell(_bucketColumns[0], _bucketRows[i].Name, _tableCellStyle);
                    DrawCell(_bucketColumns[1], _bucketRows[i].CountText, _tableCellRightStyle);
                }
            }
        }

        private void DrawLongLivedHandles()
        {
            GUILayout.Space(SectionSpacing);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    _handlesTruncated ? "Longest Active Epochs in Captured Sample" : "Longest Active Handle Epochs",
                    _sectionTitleStyle);

                if (_handleRows.Count == 0)
                {
                    EditorGUILayout.LabelField("No handles in the current observation epoch.", EditorStyles.miniLabel);
                    return;
                }

                Rect header = EditorGUILayout.GetControlRect(false, HeaderHeight);
                SplitColumns(header, _handleWeights, ColumnSpacing, _handleColumns);
                DrawHeaderCell(_handleColumns[0], "Package");
                DrawHeaderCell(_handleColumns[1], "Description");
                DrawHeaderCell(_handleColumns[2], "Age", _tableCellRightStyle);

                int count = Mathf.Min(MaxVisibleRows, _handleRows.Count);
                for (int i = 0; i < count; i++)
                {
                    Rect row = EditorGUILayout.GetControlRect(false, RowHeight);
                    DrawRowBackground(row, i);
                    SplitColumns(row, _handleWeights, ColumnSpacing, _handleColumns);
                    DrawCell(_handleColumns[0], _handleRows[i].PackageName, _tableCellStyle);
                    DrawCell(_handleColumns[1], _handleRows[i].Description, _tableCellStyle);
                    DrawCell(_handleColumns[2], _handleRows[i].AgeText, _tableCellRightStyle);
                }
            }
        }

        private void DrawSceneSummary()
        {
            GUILayout.Space(SectionSpacing);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scene Lifecycle Snapshot", _sectionTitleStyle);

                if (_sceneRows.Count == 0)
                {
                    EditorGUILayout.LabelField("No tracked scenes.", EditorStyles.miniLabel);
                    return;
                }

                Rect header = EditorGUILayout.GetControlRect(false, HeaderHeight);
                SplitColumns(header, _sceneWeights, ColumnSpacing, _sceneColumns);
                DrawHeaderCell(_sceneColumns[0], "Package");
                DrawHeaderCell(_sceneColumns[1], "Scene");
                DrawHeaderCell(_sceneColumns[2], "Provider");
                DrawHeaderCell(_sceneColumns[3], "State");
                DrawHeaderCell(_sceneColumns[4], "Progress", _tableCellRightStyle);
                DrawHeaderCell(_sceneColumns[5], "Age", _tableCellRightStyle);

                int count = Mathf.Min(MaxVisibleRows, _sceneRows.Count);
                for (int i = 0; i < count; i++)
                {
                    Rect row = EditorGUILayout.GetControlRect(false, RowHeight);
                    DrawRowBackground(row, i);
                    SplitColumns(row, _sceneWeights, ColumnSpacing, _sceneColumns);
                    DrawCell(_sceneColumns[0], _sceneRows[i].PackageName, _tableCellStyle);
                    DrawCell(_sceneColumns[1], _sceneRows[i].SceneLocation, _tableCellStyle);
                    DrawCell(_sceneColumns[2], _sceneRows[i].ProviderType, _tableCellStyle);
                    DrawCell(_sceneColumns[3], _sceneRows[i].StateText, _tableCellStyle);
                    DrawCell(_sceneColumns[4], _sceneRows[i].ProgressText, _tableCellRightStyle);
                    DrawCell(_sceneColumns[5], _sceneRows[i].AgeText, _tableCellRightStyle);
                }
            }
        }

        private void DrawHeaderCell(Rect rect, string text, GUIStyle styleOverride = null)
        {
            EditorGUI.DrawRect(rect, GetHeaderColor());
            DrawCell(rect, text, styleOverride ?? _tableHeaderStyle);
        }

        private void DrawCell(Rect rect, string text, GUIStyle style)
        {
            _cellContent.text = text;
            _cellContent.tooltip = text;
            GUI.Label(rect, _cellContent, style);
        }

        private void DrawRowBackground(Rect rect, int rowIndex)
        {
            EditorGUI.DrawRect(rect, rowIndex % 2 == 0 ? GetEvenRowColor() : GetOddRowColor());
        }

        private static void SplitColumns(Rect rect, float[] weights, float spacing, Rect[] output)
        {
            int count = weights.Length;
            float totalWeight = 0f;
            for (int i = 0; i < count; i++)
                totalWeight += weights[i];

            float contentWidth = Mathf.Max(0f, rect.width - ((count - 1) * spacing));
            float x = rect.x;

            for (int i = 0; i < count; i++)
            {
                float width = i == count - 1
                    ? rect.xMax - x
                    : Mathf.Floor(contentWidth * (weights[i] / totalWeight));

                output[i] = new Rect(x, rect.y, Mathf.Max(0f, width), rect.height);
                x += width + spacing;
            }
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized) return;

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };

            _tableHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 2, 2)
            };

            _tableCellStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 2, 2)
            };

            _tableCellRightStyle = new GUIStyle(_tableCellStyle)
            {
                alignment = TextAnchor.MiddleRight
            };

            _metricLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter,
                clipping = TextClipping.Clip
            };

            _metricValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                clipping = TextClipping.Clip
            };

            _stylesInitialized = true;
        }

        private static Color GetCardColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.22f, 0.28f, 0.80f)
                : new Color(0.82f, 0.90f, 0.98f, 0.85f);
        }

        private static Color GetHeaderColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.25f, 0.29f, 0.36f, 0.92f)
                : new Color(0.73f, 0.83f, 0.95f, 0.95f);
        }

        private static Color GetEvenRowColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.030f)
                : new Color(0f, 0f, 0f, 0.025f);
        }

        private static Color GetOddRowColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.010f)
                : new Color(0f, 0f, 0f, 0.010f);
        }

        private static string FormatPercent(float normalized)
        {
            return (normalized * 100f).ToString("F0") + "%";
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 60d) return age.TotalSeconds.ToString("F0") + "s";
            if (age.TotalMinutes < 60d) return age.TotalMinutes.ToString("F1") + "m";
            return age.TotalHours.ToString("F1") + "h";
        }

        private struct BucketRow
        {
            public readonly string Name;
            public readonly string CountText;

            public BucketRow(string name, string countText)
            {
                Name = name;
                CountText = countText;
            }
        }

        private struct HandleRow
        {
            public readonly string PackageName;
            public readonly string Description;
            public readonly string AgeText;

            public HandleRow(string packageName, string description, string ageText)
            {
                PackageName = packageName;
                Description = description;
                AgeText = ageText;
            }
        }

        private struct SceneRow
        {
            public readonly string PackageName;
            public readonly string SceneLocation;
            public readonly string ProviderType;
            public readonly string StateText;
            public readonly string ProgressText;
            public readonly string AgeText;

            public SceneRow(string packageName, string sceneLocation, string providerType, string stateText, string progressText, string ageText)
            {
                PackageName = packageName;
                SceneLocation = sceneLocation;
                ProviderType = providerType;
                StateText = stateText;
                ProgressText = progressText;
                AgeText = ageText;
            }
        }
    }
}
#endif
