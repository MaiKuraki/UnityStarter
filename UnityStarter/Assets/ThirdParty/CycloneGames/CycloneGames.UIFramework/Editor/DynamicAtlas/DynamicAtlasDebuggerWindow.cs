using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.DynamicAtlas;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor.DynamicAtlas
{
    public sealed class DynamicAtlasDebuggerWindow : EditorWindow
    {
        private readonly struct EntryPresentation
        {
            internal readonly string Size;
            internal readonly string CopyPath;
            internal readonly string ReferenceState;

            internal EntryPresentation(string size, string copyPath, string referenceState)
            {
                Size = size;
                CopyPath = copyPath;
                ReferenceState = referenceState;
            }
        }

        private const int MaximumSnapshotEntries = 4096;
        private const int MaximumVisibleEntries = 500;

        private static readonly GUIContent RefreshContent = new GUIContent("Refresh", "Refresh diagnostics without scanning scenes or assets.");
        private static readonly GUIContent AutoRefreshContent = new GUIContent("Auto Refresh", "Refresh at a throttled interval while the Editor is playing.");
        private static readonly GUIContent TrimContent = new GUIContent("Trim Unused", "Evict every zero-reference cache entry.");
        private static readonly GUIContent ClearContent = new GUIContent("Clear", "Destroy every generated sprite and page owned by the selected service.");

        private readonly List<DynamicAtlasService> _services = new List<DynamicAtlasService>(8);
        private readonly List<DynamicAtlasPageSnapshot> _pages = new List<DynamicAtlasPageSnapshot>(8);
        private readonly List<DynamicAtlasEntrySnapshot> _entries = new List<DynamicAtlasEntrySnapshot>(256);
        private readonly List<DynamicAtlasEntrySnapshot> _visibleEntries = new List<DynamicAtlasEntrySnapshot>(MaximumVisibleEntries);
        private readonly List<GUIContent> _pageLabels = new List<GUIContent>(8);
        private readonly List<EntryPresentation> _entryPresentations = new List<EntryPresentation>(MaximumVisibleEntries);

        private string[] _serviceNames = Array.Empty<string>();
        private int _selectedServiceIndex = -1;
        private int _selectedPageId = -1;
        private DynamicAtlasStats _stats;
        private Vector2 _pageScroll;
        private Vector2 _entryScroll;
        private Vector2 _previewScroll;
        private float _previewZoom = 1f;
        private bool _autoRefresh;
        private double _refreshInterval = 0.5d;
        private double _nextRefreshTime;
        private string _keyFilter = string.Empty;
        private string _activeRetainedText = "0 / 0";
        private string _reservedText = "0.0%";
        private string _estimatedMemoryText = "0 B";
        private string _pendingDestructionText = "0 B";
        private string _budgetText = "0 B";
        private string _gpuReadbackText = "0 / 0";
        private bool _entrySnapshotSuppressed;
        private int _matchingEntryCount;

        [MenuItem("Tools/CycloneGames/UI Framework/Dynamic Atlas Debugger")]
        public static void ShowWindow()
        {
            DynamicAtlasDebuggerWindow window = GetWindow<DynamicAtlasDebuggerWindow>();
            window.titleContent = new GUIContent("Dynamic Atlas");
            window.minSize = new Vector2(860f, 560f);
            window.Show();
        }

        private DynamicAtlasService SelectedService =>
            _selectedServiceIndex >= 0 && _selectedServiceIndex < _services.Count
                ? _services[_selectedServiceIndex]
                : null;

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            RefreshAll();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            ClearDiagnosticBuffers();
        }

        private void OnEditorUpdate()
        {
            if (!_autoRefresh || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = EditorApplication.timeSinceStartup + _refreshInterval;
            RefreshAll();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Runtime atlas services are available in Play Mode. Enter Play Mode, then refresh this window.", MessageType.Info);
            }

            DynamicAtlasService service = SelectedService;
            if (service == null)
            {
                EditorGUILayout.HelpBox("No active DynamicAtlasService is registered. Services are discovered through the editor-only diagnostics registry; no scene scan is performed.", MessageType.Warning);
                return;
            }

            DrawSummary();
            EditorGUILayout.BeginHorizontal();
            DrawPageSidebar();
            DrawSelectedPage();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            int nextIndex = EditorGUILayout.Popup(
                Mathf.Max(0, _selectedServiceIndex),
                _serviceNames,
                EditorStyles.toolbarPopup,
                GUILayout.MinWidth(220f));
            if (_services.Count > 0 && nextIndex != _selectedServiceIndex)
            {
                _selectedServiceIndex = Mathf.Clamp(nextIndex, 0, _services.Count - 1);
                _selectedPageId = -1;
                RefreshSelectedService();
            }

            if (GUILayout.Button(RefreshContent, EditorStyles.toolbarButton, GUILayout.Width(62f)))
            {
                RefreshAll();
            }

            GUILayout.Space(8f);
            _autoRefresh = GUILayout.Toggle(_autoRefresh, AutoRefreshContent, EditorStyles.toolbarButton, GUILayout.Width(92f));
            using (new EditorGUI.DisabledScope(!_autoRefresh))
            {
                GUILayout.Label("Interval", GUILayout.Width(44f));
                _refreshInterval = EditorGUILayout.DoubleField(_refreshInterval, GUILayout.Width(48f));
                _refreshInterval = Math.Max(0.25d, Math.Min(5d, _refreshInterval));
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(SelectedService == null))
            {
                if (GUILayout.Button(TrimContent, EditorStyles.toolbarButton, GUILayout.Width(84f)))
                {
                    SelectedService.TrimUnused();
                    RefreshSelectedService();
                }

                if (GUILayout.Button(ClearContent, EditorStyles.toolbarButton, GUILayout.Width(46f)) &&
                    EditorUtility.DisplayDialog(
                        "Clear Dynamic Atlas",
                        "Destroy every generated sprite and page in the selected service? Active UI references will become invalid.",
                        "Clear",
                        "Cancel"))
                {
                    SelectedService.Clear();
                    RefreshSelectedService();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            DrawMetric("Pages", _stats.PageCount.ToString());
            DrawMetric("Entries", _stats.EntryCount.ToString());
            DrawMetric("Active / Retained", _activeRetainedText);
            DrawMetric("References", _stats.ActiveReferenceCount.ToString());
            DrawMetric("Reserved", _reservedText);
            DrawMetric("Estimated Memory", _estimatedMemoryText);
            DrawMetric("Pending Destroy", _pendingDestructionText);
            DrawMetric("Budget", _budgetText);
            DrawMetric("GPU / Readback", _gpuReadbackText);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawMetric(string label, string value)
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(78f));
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawPageSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(235f));
            EditorGUILayout.LabelField("Pages", EditorStyles.boldLabel);
            _pageScroll = EditorGUILayout.BeginScrollView(_pageScroll);

            if (_pages.Count == 0)
            {
                EditorGUILayout.HelpBox("No page has been allocated.", MessageType.None);
            }

            for (int i = 0; i < _pages.Count; i++)
            {
                DynamicAtlasPageSnapshot page = _pages[i];
                bool selected = page.PageId == _selectedPageId;
                if (GUILayout.Toggle(
                        selected,
                        _pageLabels[i],
                        EditorStyles.helpBox,
                        GUILayout.Height(43f)) && !selected)
                {
                    _selectedPageId = page.PageId;
                    RebuildVisibleEntries();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedPage()
        {
            EditorGUILayout.BeginVertical();
            if (!TryGetSelectedPage(out DynamicAtlasPageSnapshot page))
            {
                EditorGUILayout.HelpBox("Select a page to inspect its texture and entries.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Page {page.PageId} Preview", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Zoom", GUILayout.Width(34f));
            _previewZoom = GUILayout.HorizontalSlider(_previewZoom, 0.25f, 4f, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            float previewHeight = Mathf.Max(180f, position.height * 0.42f);
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(previewHeight));
            DrawTexturePreview(page, previewHeight - 20f);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Entries", EditorStyles.boldLabel, GUILayout.Width(54f));
            EditorGUI.BeginChangeCheck();
            string nextFilter = EditorGUILayout.TextField(_keyFilter, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                _keyFilter = nextFilter;
                RebuildVisibleEntries();
            }

            EditorGUILayout.EndHorizontal();
            DrawEntryList();
            EditorGUILayout.EndVertical();
        }

        private void DrawTexturePreview(DynamicAtlasPageSnapshot page, float availableHeight)
        {
            if (page.Texture == null)
            {
                return;
            }

            float baseSize = Mathf.Max(128f, availableHeight);
            float width = baseSize * _previewZoom;
            float height = baseSize * _previewZoom;
            Rect textureRect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(textureRect, new Color(0.12f, 0.12f, 0.12f));
            GUI.DrawTexture(textureRect, page.Texture, ScaleMode.StretchToFill, alphaBlend: true);

            float scaleX = textureRect.width / page.Width;
            float scaleY = textureRect.height / page.Height;
            Handles.color = new Color(0.2f, 1f, 0.45f, 0.8f);
            for (int i = 0; i < _visibleEntries.Count; i++)
            {
                DynamicAtlasEntrySnapshot entry = _visibleEntries[i];

                RectInt pixelRect = entry.PixelRect;
                Rect outline = new Rect(
                    textureRect.x + (pixelRect.x * scaleX),
                    textureRect.yMax - (pixelRect.yMax * scaleY),
                    pixelRect.width * scaleX,
                    pixelRect.height * scaleY);
                Handles.DrawWireCube(outline.center, outline.size);
            }

            Handles.color = Color.white;
        }

        private void DrawEntryList()
        {
            _entryScroll = EditorGUILayout.BeginScrollView(_entryScroll);
            if (_entrySnapshotSuppressed)
            {
                EditorGUILayout.HelpBox(
                    $"Entry details are not captured because this service has more than {MaximumSnapshotEntries} entries. Summary and page diagnostics remain available.",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            for (int i = 0; i < _visibleEntries.Count; i++)
            {
                DynamicAtlasEntrySnapshot entry = _visibleEntries[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.SelectableLabel(entry.Key, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.MinWidth(220f));
                EntryPresentation presentation = _entryPresentations[i];
                GUILayout.Label(presentation.Size, GUILayout.Width(72f));
                GUILayout.Label(presentation.CopyPath, GUILayout.Width(110f));
                GUILayout.Label(presentation.ReferenceState, GUILayout.Width(72f));
                EditorGUILayout.EndHorizontal();
            }

            if (_matchingEntryCount > MaximumVisibleEntries)
            {
                EditorGUILayout.HelpBox(
                    $"Showing {MaximumVisibleEntries} of {_matchingEntryCount} matching entries. Narrow the key filter to inspect a smaller bounded set. Preview outlines follow this visible set.",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private bool TryGetSelectedPage(out DynamicAtlasPageSnapshot selectedPage)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                if (_pages[i].PageId == _selectedPageId)
                {
                    selectedPage = _pages[i];
                    return true;
                }
            }

            selectedPage = default;
            return false;
        }

        private void RefreshAll()
        {
            DynamicAtlasService previous = SelectedService;
            DynamicAtlasService.CopyActiveEditorServices(_services);
            RebuildServiceNames();

            _selectedServiceIndex = previous != null ? _services.IndexOf(previous) : -1;
            if (_selectedServiceIndex < 0 && _services.Count > 0)
            {
                _selectedServiceIndex = 0;
            }

            RefreshSelectedService();
        }

        private void RebuildServiceNames()
        {
            if (_services.Count == 0)
            {
                _serviceNames = Array.Empty<string>();
                return;
            }

            _serviceNames = new string[_services.Count];
            for (int i = 0; i < _services.Count; i++)
            {
                DynamicAtlasStats stats = _services[i].GetStats();
                _serviceNames[i] = $"Service {i + 1}  ({stats.PageCount} pages, {stats.EntryCount} entries)";
            }
        }

        private void RefreshSelectedService()
        {
            DynamicAtlasService service = SelectedService;
            _pages.Clear();
            _entries.Clear();
            _visibleEntries.Clear();
            _pageLabels.Clear();
            _entryPresentations.Clear();
            _stats = default;
            _entrySnapshotSuppressed = false;
            _matchingEntryCount = 0;

            if (service == null || service.IsDisposed)
            {
                _selectedPageId = -1;
                return;
            }

            _stats = service.GetStats();
            service.CopyPageSnapshots(_pages);
            if (_stats.EntryCount <= MaximumSnapshotEntries)
            {
                service.CopyEntrySnapshots(_entries);
            }
            else
            {
                _entrySnapshotSuppressed = true;
            }

            _activeRetainedText = $"{_stats.ActiveEntryCount} / {_stats.RetainedEntryCount}";
            _reservedText = $"{_stats.ReservedUsageRatio * 100f:F1}%";
            _estimatedMemoryText = EditorUtility.FormatBytes(_stats.EstimatedTextureBytes);
            _pendingDestructionText = EditorUtility.FormatBytes(_stats.PendingDestructionBytes);
            _budgetText = EditorUtility.FormatBytes(_stats.MemoryBudgetBytes);
            _gpuReadbackText = $"{_stats.GpuCopyCount} / {_stats.SynchronousReadbackCount}";

            for (int i = 0; i < _pages.Count; i++)
            {
                DynamicAtlasPageSnapshot page = _pages[i];
                _pageLabels.Add(new GUIContent(
                    $"Page {page.PageId}  |  {page.Mode}\n{page.EntryCount} entries, {page.ReleasedSlotCount} reusable slots"));
            }

            if (!TryGetSelectedPage(out _) && _pages.Count > 0)
            {
                _selectedPageId = _pages[0].PageId;
            }

            RebuildVisibleEntries();
        }

        private void RebuildVisibleEntries()
        {
            _visibleEntries.Clear();
            _entryPresentations.Clear();
            _matchingEntryCount = 0;

            if (_entrySnapshotSuppressed || _selectedPageId < 0)
            {
                return;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                DynamicAtlasEntrySnapshot entry = _entries[i];
                if (entry.PageId != _selectedPageId ||
                    (!string.IsNullOrEmpty(_keyFilter) && entry.Key.IndexOf(_keyFilter, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                _matchingEntryCount++;
                if (_visibleEntries.Count >= MaximumVisibleEntries)
                {
                    continue;
                }

                _visibleEntries.Add(entry);
                _entryPresentations.Add(new EntryPresentation(
                    $"{entry.PixelRect.width}x{entry.PixelRect.height}",
                    entry.CopyPath.ToString(),
                    entry.ReferenceCount == 0 ? "Retained" : $"Refs {entry.ReferenceCount}"));
            }
        }

        private void ClearDiagnosticBuffers()
        {
            _services.Clear();
            _pages.Clear();
            _entries.Clear();
            _visibleEntries.Clear();
            _pageLabels.Clear();
            _entryPresentations.Clear();
            _serviceNames = Array.Empty<string>();
        }
    }
}
