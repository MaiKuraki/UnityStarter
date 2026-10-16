using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace CycloneGames.UIFramework.Editor.DynamicAtlas
{
    /// <summary>
    /// Always-on visibility for SpriteAtlas compatibility: lists every project atlas with its
    /// dynamic-atlas copy classification, refreshed automatically when atlas imports change.
    /// </summary>
    public sealed class SpriteAtlasCompatibilityDashboard : EditorWindow
    {
        private const int MaximumVisibleEntries = 256;

        private static readonly GUIContent RescanContent = new GUIContent("Rescan", "Runs an explicit project-wide AssetDatabase scan.");
        private static readonly GUIContent AutoScanContent = new GUIContent("Auto Scan On Import", "Rescan throttled when SpriteAtlas assets are imported, moved, or deleted.");

        private readonly List<SpriteAtlasCompatibilityCache.Entry> _entries =
            new List<SpriteAtlasCompatibilityCache.Entry>(64);
        private readonly List<SpriteAtlasCompatibilityCache.Entry> _visibleEntries =
            new List<SpriteAtlasCompatibilityCache.Entry>(MaximumVisibleEntries);
        private readonly List<string> _visibleReasons = new List<string>(MaximumVisibleEntries);

        private Vector2 _scrollPosition;
        private int _overflowCount;
        private int _fastPathCount;
        private int _cpuFallbackCount;
        private int _readbackCount;
        private int _unsupportedCount;

        [MenuItem("Tools/CycloneGames/UI Framework/Atlas Compatibility Dashboard")]
        public static void ShowWindow()
        {
            SpriteAtlasCompatibilityDashboard window = GetWindow<SpriteAtlasCompatibilityDashboard>();
            window.titleContent = new GUIContent("Atlas Dashboard");
            window.minSize = new Vector2(620f, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            SpriteAtlasCompatibilityCache.ScanCompleted += OnScanCompleted;
            if (ShouldRescanOnEnable(
                    SpriteAtlasCompatibilityCache.AutoScanEnabled,
                    SpriteAtlasCompatibilityCache.Count))
            {
                SpriteAtlasCompatibilityCache.ScanProjectNow();
            }
            else
            {
                // Reuse the cached results instead of forcing a full project scan on every open.
                RefreshFromCache();
            }
        }

        // A rescan is only needed when auto-scan is on or nothing has been scanned yet.
        internal static bool ShouldRescanOnEnable(bool autoScanEnabled, int cachedCount)
        {
            return autoScanEnabled || cachedCount == 0;
        }

        private void OnDisable()
        {
            SpriteAtlasCompatibilityCache.ScanCompleted -= OnScanCompleted;
        }

        private void OnScanCompleted()
        {
            RefreshFromCache();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Continuous view of how each SpriteAtlas fits the dynamic UI atlas copy paths. Entries update automatically after atlas imports; conflicts stay visible without a manual scan.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(RescanContent, GUILayout.Height(26f)))
            {
                SpriteAtlasCompatibilityCache.ScanProjectNow();
            }

            bool autoScan = EditorGUILayout.ToggleLeft(
                AutoScanContent,
                SpriteAtlasCompatibilityCache.AutoScanEnabled,
                GUILayout.Width(160f));
            if (autoScan != SpriteAtlasCompatibilityCache.AutoScanEnabled)
            {
                SpriteAtlasCompatibilityCache.AutoScanEnabled = autoScan;
            }

            GUILayout.FlexibleSpace();
            if (SpriteAtlasCompatibilityCache.LastScanTime >= 0d)
            {
                EditorGUILayout.LabelField(
                    $"Scans: {SpriteAtlasCompatibilityCache.CompletedScanCount}",
                    EditorStyles.miniLabel,
                    GUILayout.Width(84f));
            }

            EditorGUILayout.EndHorizontal();

            DrawSummary();
            DrawResults();
        }

        private void DrawSummary()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Total: {_entries.Count}", EditorStyles.boldLabel, GUILayout.Width(82f));
            EditorGUILayout.LabelField($"GPU only: {_fastPathCount}", GUILayout.Width(104f));
            EditorGUILayout.LabelField($"GPU + CPU: {_cpuFallbackCount}", GUILayout.Width(112f));
            EditorGUILayout.LabelField($"Readback: {_readbackCount}", GUILayout.Width(100f));
            EditorGUILayout.LabelField($"Unsupported: {_unsupportedCount}", GUILayout.Width(112f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawResults()
        {
            if (_entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    SpriteAtlasCompatibilityCache.LastScanTime < 0d
                        ? "No scan has completed yet."
                        : "No SpriteAtlas assets found in this project.",
                    MessageType.None);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            for (int i = 0; i < _visibleEntries.Count; i++)
            {
                SpriteAtlasCompatibilityCache.Entry entry = _visibleEntries[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.Name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                DrawCompatibilityLabel(entry.Compatibility);
                if (GUILayout.Button("Select", GUILayout.Width(56f)))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(entry.Path);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(entry.Path, EditorStyles.miniLabel);

                string reasons = _visibleReasons[i];
                if (reasons.Length > 0)
                {
                    EditorGUILayout.LabelField(reasons, EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
            }

            if (_overflowCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"+{_overflowCount} more entries not shown. The dashboard displays at most {MaximumVisibleEntries} entries, sorted by severity.",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void RefreshFromCache()
        {
            SpriteAtlasCompatibilityCache.CopyEntries(_entries);
            _overflowCount = SpriteAtlasCompatibilityCache.CopyVisible(
                _entries,
                _visibleEntries,
                MaximumVisibleEntries);

            _visibleReasons.Clear();
            _fastPathCount = 0;
            _cpuFallbackCount = 0;
            _readbackCount = 0;
            _unsupportedCount = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                switch (_entries[i].Compatibility)
                {
                    case SpriteAtlasFormatValidator.Compatibility.FastPathCandidate:
                        _fastPathCount++;
                        break;
                    case SpriteAtlasFormatValidator.Compatibility.FastPathWithCpuFallback:
                        _cpuFallbackCount++;
                        break;
                    case SpriteAtlasFormatValidator.Compatibility.ReadbackRequired:
                        _readbackCount++;
                        break;
                    default:
                        _unsupportedCount++;
                        break;
                }
            }

            for (int i = 0; i < _visibleEntries.Count; i++)
            {
                _visibleReasons.Add(SpriteAtlasCompatibilityCache.BuildFailureReasons(_visibleEntries[i]));
            }
        }

        private static void DrawCompatibilityLabel(SpriteAtlasFormatValidator.Compatibility compatibility)
        {
            Color previous = GUI.contentColor;
            switch (compatibility)
            {
                case SpriteAtlasFormatValidator.Compatibility.FastPathCandidate:
                    GUI.contentColor = new Color(0.35f, 0.85f, 0.45f);
                    EditorGUILayout.LabelField("GPU Candidate", EditorStyles.boldLabel, GUILayout.Width(104f));
                    break;
                case SpriteAtlasFormatValidator.Compatibility.FastPathWithCpuFallback:
                    GUI.contentColor = new Color(0.3f, 0.78f, 0.9f);
                    EditorGUILayout.LabelField("GPU + CPU", EditorStyles.boldLabel, GUILayout.Width(88f));
                    break;
                case SpriteAtlasFormatValidator.Compatibility.ReadbackRequired:
                    GUI.contentColor = new Color(1f, 0.72f, 0.24f);
                    EditorGUILayout.LabelField("Readback", EditorStyles.boldLabel, GUILayout.Width(76f));
                    break;
                default:
                    GUI.contentColor = new Color(1f, 0.35f, 0.35f);
                    EditorGUILayout.LabelField("Unsupported", EditorStyles.boldLabel, GUILayout.Width(92f));
                    break;
            }

            GUI.contentColor = previous;
        }
    }
}
