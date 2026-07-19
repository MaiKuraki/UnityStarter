using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    public sealed class UIPerformanceAuditWindow : EditorWindow
    {
        private enum ScanStage
        {
            None,
            Discover,
            Audit,
        }

        private sealed class Entry
        {
            public UIWindowConfiguration Configuration;
            public UIPerformanceAuditUtility.AuditReport Report;
            public string Error;
            public string SearchText;
            public string GraphicsAndRaycasts;
            public string LayoutsAndFitters;
            public string MaterialsAndTextures;
            public string CanvasesAndMasks;
        }

        private const int MaximumRenderedEntries = 250;
        private const int MaximumStoredEntries = 4096;
        private const int MaximumAssetsPerUpdate = 4;
        private const double ScanSliceBudgetSeconds = 0.004d;
        private static readonly string[] IntCache = BuildIntCache(1024);

        private readonly UIPerformanceAuditUtility _auditor = new UIPerformanceAuditUtility();
        private readonly List<Entry> _entries = new List<Entry>(64);
        private Vector2 _scrollPosition;
        private string _search = string.Empty;
        private bool _issuesOnly = true;
        private int _warningCount;
        private int _errorCount;
        private int _omittedEntryCount;
        private string _lastScanDurationText = "Not run";
        private string _scanProgressText = "Idle";
        private string _scanError = string.Empty;
        private string _omittedEntryMessage = string.Empty;
        private string[] _scanGuids = Array.Empty<string>();
        private int _scanIndex;
        private double _scanStarted;
        private bool _cancelScanRequested;
        private ScanStage _scanStage;

        private bool IsScanning => _scanStage != ScanStage.None;

        [MenuItem("Tools/CycloneGames/UI Framework/Performance Auditor")]
        public static void ShowWindow()
        {
            UIPerformanceAuditWindow window = GetWindow<UIPerformanceAuditWindow>("UI Performance Auditor");
            window.minSize = new Vector2(720f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("UI Performance Auditor");
        }

        private void OnDisable()
        {
            StopScanLoop();
        }

        private void OnGUI()
        {
            InspectorUiUtility.DrawInspectorTitle(
                "UI Performance Auditor",
                "Explicit prefab diagnostics; target-device profiling remains authoritative",
                InspectorUiUtility.RuntimeColor);

            DrawToolbar();
            DrawSummary();
            DrawResults();
        }

        private void DrawToolbar()
        {
            InspectorUiUtility.BeginPanel();
            EditorGUILayout.BeginHorizontal();
            string nextSearch = EditorGUILayout.TextField("Search", _search);
            if (!string.Equals(nextSearch, _search, StringComparison.Ordinal))
            {
                _search = nextSearch;
            }

            _issuesOnly = GUILayout.Toggle(_issuesOnly, "Issues only", EditorStyles.miniButton, GUILayout.Width(92f));
            if (GUILayout.Button(IsScanning ? "Cancel Scan" : "Scan Project", GUILayout.Width(110f)))
            {
                if (IsScanning)
                {
                    _cancelScanRequested = true;
                }
                else
                {
                    BeginProjectScan();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Scanning runs only when requested. Findings are bounded heuristics for review; they do not prove DrawCall, Canvas rebuild, overdraw, memory, or device performance.",
                MessageType.Info);
            InspectorUiUtility.EndPanel();
        }

        private void DrawSummary()
        {
            InspectorUiUtility.BeginPanel();
            InspectorUiUtility.DrawStatusRow("Configurations", IntToString(_entries.Count), InspectorUiUtility.AssetColor);
            InspectorUiUtility.DrawStatusRow("Warnings", IntToString(_warningCount), _warningCount > 0 ? InspectorUiUtility.WarningColor : InspectorUiUtility.SuccessColor);
            InspectorUiUtility.DrawStatusRow("Errors", IntToString(_errorCount), _errorCount > 0 ? Color.red : InspectorUiUtility.SuccessColor);
            InspectorUiUtility.DrawStatusRow("Last scan", _lastScanDurationText, InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow(
                "Scan progress",
                _scanProgressText,
                IsScanning ? InspectorUiUtility.RuntimeColor : InspectorUiUtility.NeutralColor);
            if (IsScanning && _scanStage == ScanStage.Audit && _scanGuids.Length > 0)
            {
                Rect progressRect = EditorGUILayout.GetControlRect(false, 18f);
                EditorGUI.ProgressBar(
                    progressRect,
                    Mathf.Clamp01((float)_scanIndex / _scanGuids.Length),
                    _scanProgressText);
            }
            if (!string.IsNullOrEmpty(_scanError))
            {
                EditorGUILayout.HelpBox(_scanError, MessageType.Error);
            }
            if (!string.IsNullOrEmpty(_omittedEntryMessage))
            {
                EditorGUILayout.HelpBox(_omittedEntryMessage, MessageType.Info);
            }
            InspectorUiUtility.EndPanel();
        }

        private void DrawResults()
        {
            if (_entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Select Scan Project to inspect UIWindowConfiguration assets. No asset is modified by this tool.",
                    MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            string normalizedSearch = _search.Trim();
            int rendered = 0;
            int matched = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (!Matches(entry, normalizedSearch))
                {
                    continue;
                }

                matched++;
                if (rendered >= MaximumRenderedEntries)
                {
                    continue;
                }

                DrawEntry(entry);
                rendered++;
            }
            EditorGUILayout.EndScrollView();

            if (matched > rendered)
            {
                EditorGUILayout.HelpBox(
                    $"Showing {rendered} of {matched} matching entries. Narrow the search to keep Editor GUI work bounded.",
                    MessageType.Info);
            }
        }

        private void DrawEntry(Entry entry)
        {
            UIPerformanceAuditUtility.AuditReport report = entry.Report;
            bool unresolved = report == null;
            Color color = unresolved
                ? InspectorUiUtility.WarningColor
                : report.HighestSeverity == UIPerformanceAuditUtility.AuditSeverity.Error
                    ? Color.red
                    : report.WarningCount > 0
                        ? InspectorUiUtility.WarningColor
                        : InspectorUiUtility.SuccessColor;

            InspectorUiUtility.BeginPanel();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(entry.Configuration.name, EditorStyles.boldLabel);
            InspectorUiUtility.DrawStatusBadge(
                unresolved ? "Unresolved" : report.Issues.Count == 0 ? "Review clean" : "Review",
                color,
                88f);
            if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                Selection.activeObject = entry.Configuration;
                EditorGUIUtility.PingObject(entry.Configuration);
            }
            if (!unresolved && GUILayout.Button("Prefab", EditorStyles.miniButton, GUILayout.Width(54f)))
            {
                Selection.activeObject = report.Prefab;
                EditorGUIUtility.PingObject(report.Prefab);
            }
            EditorGUILayout.EndHorizontal();

            if (unresolved)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(entry.Error)
                        ? "The prefab cannot be resolved from the direct reference, Editor GUID, or an Assets/ authoring path."
                        : entry.Error,
                    string.IsNullOrEmpty(entry.Error) ? MessageType.Warning : MessageType.Error);
                InspectorUiUtility.EndPanel();
                return;
            }

            InspectorUiUtility.DrawStatusRow("Graphics / Raycast targets", entry.GraphicsAndRaycasts, InspectorUiUtility.AssetColor);
            InspectorUiUtility.DrawStatusRow("Layouts / Fitters", entry.LayoutsAndFitters, InspectorUiUtility.SetupColor);
            InspectorUiUtility.DrawStatusRow("Materials / Textures", entry.MaterialsAndTextures, InspectorUiUtility.RuntimeColor);
            InspectorUiUtility.DrawStatusRow("Canvases / Masks", entry.CanvasesAndMasks, InspectorUiUtility.NeutralColor);

            for (int i = 0; i < report.Issues.Count; i++)
            {
                UIPerformanceAuditUtility.AuditIssue issue = report.Issues[i];
                MessageType type = issue.Severity == UIPerformanceAuditUtility.AuditSeverity.Error
                    ? MessageType.Error
                    : issue.Severity == UIPerformanceAuditUtility.AuditSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Message, type);
            }
            InspectorUiUtility.EndPanel();
        }

        private bool Matches(Entry entry, string normalizedSearch)
        {
            if (_issuesOnly && entry.Report != null && entry.Report.Issues.Count == 0)
            {
                return false;
            }

            return normalizedSearch.Length == 0 ||
                   entry.SearchText.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void BeginProjectScan()
        {
            StopScanLoop();
            _entries.Clear();
            _warningCount = 0;
            _errorCount = 0;
            _omittedEntryCount = 0;
            _omittedEntryMessage = string.Empty;
            _scanError = string.Empty;
            _scanProgressText = "Preparing project index";
            _lastScanDurationText = "Running";
            _scanGuids = Array.Empty<string>();
            _scanIndex = 0;
            _cancelScanRequested = false;
            _scanStarted = EditorApplication.timeSinceStartup;
            _scanStage = ScanStage.Discover;
            EditorApplication.update += ContinueProjectScan;
            Repaint();
        }

        private void ContinueProjectScan()
        {
            if (!IsScanning)
            {
                StopScanLoop();
                return;
            }

            if (_cancelScanRequested)
            {
                FinishProjectScan(cancelled: true, null);
                return;
            }

            if (_scanStage == ScanStage.Discover)
            {
                try
                {
                    _scanGuids = AssetDatabase.FindAssets("t:UIWindowConfiguration");
                    Array.Sort(_scanGuids, StringComparer.Ordinal);
                    _scanStage = ScanStage.Audit;
                    UpdateScanProgressText();
                    if (_scanGuids.Length == 0)
                    {
                        FinishProjectScan(cancelled: false, null);
                    }
                }
                catch (Exception exception)
                {
                    FinishProjectScan(cancelled: false, exception.Message);
                }

                Repaint();
                return;
            }

            double sliceStarted = EditorApplication.timeSinceStartup;
            int processedThisUpdate = 0;
            try
            {
                while (_scanIndex < _scanGuids.Length &&
                       processedThisUpdate < MaximumAssetsPerUpdate)
                {
                    AuditConfiguration(_scanGuids[_scanIndex]);
                    _scanIndex++;
                    processedThisUpdate++;

                    if (EditorApplication.timeSinceStartup - sliceStarted >= ScanSliceBudgetSeconds)
                    {
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                FinishProjectScan(cancelled: false, exception.Message);
                Repaint();
                return;
            }

            UpdateScanProgressText();
            if (_scanIndex >= _scanGuids.Length)
            {
                FinishProjectScan(cancelled: false, null);
            }

            Repaint();
        }

        private void AuditConfiguration(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UIWindowConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(path);
            if (configuration == null)
            {
                return;
            }

            UIPerformanceAuditUtility.AuditReport report;
            try
            {
                report = _auditor.Audit(configuration);
            }
            catch (Exception exception)
            {
                _errorCount++;
                if (_entries.Count >= MaximumStoredEntries)
                {
                    _omittedEntryCount++;
                    return;
                }

                _entries.Add(new Entry
                {
                    Configuration = configuration,
                    Error = $"Audit failed for '{path}': {exception.Message}",
                    SearchText = configuration.name + "\n" + path + "\n" + configuration.WindowId,
                    GraphicsAndRaycasts = "0 / 0",
                    LayoutsAndFitters = "0 / 0",
                    MaterialsAndTextures = "0 / 0",
                    CanvasesAndMasks = "0 / 0",
                });
                return;
            }

            if (report == null)
            {
                _warningCount++;
            }
            else
            {
                _warningCount += report.WarningCount;
                _errorCount += report.ErrorCount;
            }

            if (_entries.Count >= MaximumStoredEntries)
            {
                _omittedEntryCount++;
                return;
            }

            _entries.Add(new Entry
            {
                Configuration = configuration,
                Report = report,
                SearchText = configuration.name + "\n" + path + "\n" + configuration.WindowId,
                GraphicsAndRaycasts = FormatPair(
                    report?.GraphicsCount ?? 0,
                    report?.RaycastTargetCount ?? 0),
                LayoutsAndFitters = FormatPair(
                    report?.LayoutGroupCount ?? 0,
                    report?.ContentSizeFitterCount ?? 0),
                MaterialsAndTextures = FormatPair(
                    report?.MaterialCount ?? 0,
                    report?.TextureCount ?? 0),
                CanvasesAndMasks = FormatPair(
                    report?.CanvasCount ?? 0,
                    report == null ? 0 : report.MaskCount + report.RectMaskCount),
            });
        }

        private void UpdateScanProgressText()
        {
            _scanProgressText = _scanGuids.Length == 0
                ? "No configurations"
                : $"{_scanIndex} / {_scanGuids.Length}";
        }

        private void FinishProjectScan(bool cancelled, string error)
        {
            double duration = Math.Max(0d, EditorApplication.timeSinceStartup - _scanStarted);
            _lastScanDurationText = cancelled
                ? $"Cancelled after {duration:F3} s"
                : error == null
                    ? $"{duration:F3} s"
                    : $"Failed after {duration:F3} s";
            _scanProgressText = cancelled
                ? $"Cancelled at {_scanIndex} / {_scanGuids.Length}"
                : error == null
                    ? $"Complete: {_scanIndex} / {_scanGuids.Length}"
                    : $"Stopped: {_scanIndex} / {_scanGuids.Length}";
            _scanError = error ?? string.Empty;
            _omittedEntryMessage = _omittedEntryCount > 0
                ? $"Detailed results are retained for the first {MaximumStoredEntries} configurations. " +
                  $"Aggregate counts include {_omittedEntryCount} additional audited configuration(s)."
                : string.Empty;
            _scanStage = ScanStage.None;
            _cancelScanRequested = false;
            _scanGuids = Array.Empty<string>();
            EditorApplication.update -= ContinueProjectScan;
        }

        private void StopScanLoop()
        {
            EditorApplication.update -= ContinueProjectScan;
            _scanStage = ScanStage.None;
            _cancelScanRequested = false;
            _scanGuids = Array.Empty<string>();
        }

        private static string FormatPair(int first, int second)
        {
            return IntToString(first) + " / " + IntToString(second);
        }

        private static string[] BuildIntCache(int length)
        {
            var values = new string[length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = i.ToString();
            }

            return values;
        }

        private static string IntToString(int value)
        {
            return (uint)value < (uint)IntCache.Length ? IntCache[value] : value.ToString();
        }
    }
}
