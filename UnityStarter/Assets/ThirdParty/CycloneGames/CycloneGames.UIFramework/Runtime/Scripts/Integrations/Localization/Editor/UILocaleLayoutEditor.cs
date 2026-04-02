#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CycloneGames.Localization.Runtime;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization.Editor
{
    [CustomEditor(typeof(UILocaleLayout))]
    public sealed class UILocaleLayoutEditor : UnityEditor.Editor
    {
        private SerializedProperty _baseLocaleProp;
        private SerializedProperty _elementsProp;
        private SerializedProperty _snapshotsProp;

        private int _selectedLocaleIdx;
        private bool _showElements = true;
        private Vector2 _scrollPos;

        // Preview mode state
        private bool _previewing;
        private ElementSnapshot[] _previewBackup;

        // Diff cache
        private bool[] _dirtyFlags;
        private int _dirtyCount;
        private bool _diffStale = true;

        // Locale discovery cache
        private LocalizationSettings _cachedSettings;
        private bool _settingsSearched;
        private string[] _addableLocaleCodes;     // codes available to add
        private string[] _addableLocaleLabels;     // display labels for popup
        private int _addLocalePopupIdx;

        // Cached styles
        private static GUIStyle s_BoxStyle;
        private static GUIStyle s_SectionStyle;
        private static GUIStyle s_RichLabel;
        private static GUIStyle s_ToolbarBtn;
        private static readonly Color DirtyColor = new Color(0.85f, 0.55f, 0.1f, 0.25f);
        private static readonly Color CleanColor = new Color(0.2f, 0.7f, 0.3f, 0.15f);
        private static readonly Color MissingColor = new Color(0.85f, 0.2f, 0.2f, 0.25f);
        private static readonly Color PreviewBg = new Color(0.3f, 0.6f, 0.9f, 0.15f);
        private static readonly Color BaseTagColor = new Color(0.4f, 0.8f, 0.5f, 1f);

        // Element grouping
        private readonly Dictionary<string, bool> _groupFoldouts = new(16);
        private const int GroupingThreshold = 6;
        private const int ScrollThreshold = 15;

        // Reusable lists for scanning
        private static readonly List<TMP_Text> SharedTextCache = new(64);
        private static readonly List<LocalizeImage> SharedImageCache = new(32);

        private static GUIStyle BoxStyle => s_BoxStyle ??= new GUIStyle("HelpBox")
        {
            padding = new RectOffset(8, 8, 6, 6)
        };

        private static GUIStyle SectionStyle
        {
            get
            {
                if (s_SectionStyle == null)
                {
                    s_SectionStyle = new GUIStyle("HelpBox")
                    {
                        padding = new RectOffset(10, 10, 8, 8),
                        margin = new RectOffset(0, 0, 4, 4)
                    };
                }
                return s_SectionStyle;
            }
        }

        private static GUIStyle RichLabel
        {
            get
            {
                if (s_RichLabel == null)
                {
                    s_RichLabel = new GUIStyle(EditorStyles.label) { richText = true };
                }
                return s_RichLabel;
            }
        }

        private static GUIStyle ToolbarBtn
        {
            get
            {
                if (s_ToolbarBtn == null)
                {
                    s_ToolbarBtn = new GUIStyle(EditorStyles.miniButton)
                    {
                        fixedHeight = 26,
                        fontSize = 11,
                        padding = new RectOffset(10, 10, 2, 2)
                    };
                }
                return s_ToolbarBtn;
            }
        }

        private void OnEnable()
        {
            _baseLocaleProp = serializedObject.FindProperty("_baseLocale");
            _elementsProp = serializedObject.FindProperty("_elements");
            _snapshotsProp = serializedObject.FindProperty("_snapshots");
            _diffStale = true;
            _settingsSearched = false;
            _cachedSettings = null;

            PrefabStage.prefabSaving += OnPrefabSaving;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
        }

        private void OnDisable()
        {
            PrefabStage.prefabSaving -= OnPrefabSaving;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;

            // Auto-exit preview if inspector is closed
            if (_previewing) ExitPreview();
        }

        // ── Prefab Stage Hooks ──────────────────────────────────
        private void OnPrefabSaving(GameObject prefabRoot)
        {
            if (target == null || _previewing) return;
            if (_snapshotsProp.arraySize == 0) return;

            var layout = target as UILocaleLayout;
            if (layout == null || layout.gameObject != prefabRoot) return;

            // Auto-capture on Ctrl+S if there are unsaved changes
            serializedObject.Update();
            RefreshDiff();
            if (_dirtyCount > 0)
            {
                string code = GetSelectedLocaleCode();
                CaptureSnapshot();
                Debug.Log($"[UILocaleLayout] Auto-captured {_elementsProp.arraySize} elements " +
                          $"for locale '{code}' on prefab save.");
            }
        }

        private void OnPrefabStageClosing(PrefabStage stage)
        {
            if (target == null || _previewing) return;
            if (_snapshotsProp.arraySize == 0) return;

            var layout = target as UILocaleLayout;
            if (layout == null) return;

            // Check if this layout belongs to the closing prefab stage
            var prefabRoot = stage.prefabContentsRoot;
            if (prefabRoot == null) return;
            if (!layout.transform.IsChildOf(prefabRoot.transform) && layout.gameObject != prefabRoot) return;

            serializedObject.Update();
            RefreshDiff();
            if (_dirtyCount > 0)
            {
                string code = GetSelectedLocaleCode();
                int choice = EditorUtility.DisplayDialogComplex(
                    "Unsaved Layout Changes",
                    $"UILocaleLayout on '{layout.name}' has {_dirtyCount} unsaved change(s) " +
                    $"for locale '{code}'.\n\nSave before closing prefab?",
                    "Save",
                    "Discard",
                    "Cancel");

                if (choice == 0)
                {
                    CaptureSnapshot();
                    Debug.Log($"[UILocaleLayout] Saved '{code}' snapshot before closing prefab.");
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ── Preview banner ──
            if (_previewing)
            {
                var bannerRect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUI.DrawRect(bannerRect, PreviewBg);
                GUILayout.Label("\u25b6  PREVIEW MODE \u2014 Scene changes are temporary",
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Exit Preview", GUILayout.Width(110), GUILayout.Height(22)))
                    ExitPreview();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(4);
            }

            DrawBaseLocale();
            EditorGUILayout.Space(6);
            DrawLocaleSelector();
            EditorGUILayout.Space(6);
            DrawToolbar();
            EditorGUILayout.Space(4);

            // Diff check
            if (_diffStale) RefreshDiff();

            if (_dirtyCount > 0 && !_previewing)
            {
                string code = GetSelectedLocaleCode();
                EditorGUILayout.HelpBox(
                    $"{_dirtyCount} element(s) differ from the saved '{code}' snapshot.\n" +
                    "Click Capture to save, or Revert to discard scene changes.",
                    MessageType.Warning);
            }

            DrawElements();

            serializedObject.ApplyModifiedProperties();
        }

        // ── Base Locale ─────────────────────────────────────────
        private void DrawBaseLocale()
        {
            EditorGUILayout.BeginVertical(SectionStyle);
            EditorGUILayout.LabelField("Base Locale", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();

            // Show as dropdown from available locales if possible
            var settings = FindLocalizationSettings();
            if (settings != null && settings.AvailableLocales.Count > 0)
            {
                var locales = settings.AvailableLocales;
                string current = _baseLocaleProp.stringValue;
                int selectedIdx = -1;
                string[] labels = new string[locales.Count];
                for (int i = 0; i < locales.Count; i++)
                {
                    var loc = locales[i];
                    if (loc == null) { labels[i] = "(null)"; continue; }
                    labels[i] = FormatLocaleLabel(loc);
                    if (string.Equals(loc.Id.ToString(), current, StringComparison.OrdinalIgnoreCase))
                        selectedIdx = i;
                }

                if (selectedIdx < 0 && !string.IsNullOrEmpty(current))
                {
                    // Current value is not in available locales — show it but allow changing
                    EditorGUILayout.LabelField($"\u26a0 '{current}' (not in LocalizationSettings)",
                        RichLabel);
                }

                EditorGUI.BeginChangeCheck();
                int newIdx = EditorGUILayout.Popup(selectedIdx, labels);
                if (EditorGUI.EndChangeCheck() && newIdx >= 0 && newIdx < locales.Count && locales[newIdx] != null)
                {
                    _baseLocaleProp.stringValue = locales[newIdx].Id.ToString();
                    InvalidateAddableLocales();
                }
            }
            else
            {
                // Fallback: manual text field when no LocalizationSettings found
                EditorGUILayout.PropertyField(_baseLocaleProp, GUIContent.none);
            }

            var prevColor = GUI.color;
            GUI.color = BaseTagColor;
            GUILayout.Label("= Prefab Default", EditorStyles.miniLabel, GUILayout.Width(100));
            GUI.color = prevColor;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ── Locale Selector ─────────────────────────────────────
        private void DrawLocaleSelector()
        {
            EditorGUILayout.BeginVertical(SectionStyle);
            EditorGUILayout.LabelField("Override Locales", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            int count = _snapshotsProp.arraySize;
            if (count == 0)
            {
                EditorGUILayout.LabelField(
                    "No override locales yet. Add overrides for locales that need different layouts.",
                    EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                // Build display names with native name annotation
                string[] displayNames = new string[count];
                var settings = FindLocalizationSettings();
                for (int i = 0; i < count; i++)
                {
                    string code = _snapshotsProp.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("LocaleCode").stringValue;
                    string label = code;
                    if (settings != null)
                    {
                        var locale = FindLocaleByCode(settings, code);
                        if (locale != null)
                            label = FormatLocaleLabel(locale);
                    }
                    displayNames[i] = label;
                }

                int prevIdx = _selectedLocaleIdx;
                _selectedLocaleIdx = Mathf.Clamp(_selectedLocaleIdx, 0, count - 1);

                EditorGUI.BeginChangeCheck();
                _selectedLocaleIdx = EditorGUILayout.Popup(
                    new GUIContent("Editing Locale", "Select which override locale to edit"),
                    _selectedLocaleIdx, displayNames);
                if (EditorGUI.EndChangeCheck() && prevIdx != _selectedLocaleIdx)
                {
                    OnLocaleSwitch(prevIdx, _selectedLocaleIdx);
                }
            }

            EditorGUILayout.Space(4);

            // Add / Remove row
            EditorGUILayout.BeginHorizontal();
            {
                // Build addable locale list from LocalizationSettings
                RebuildAddableLocalesIfNeeded();

                if (_addableLocaleCodes != null && _addableLocaleCodes.Length > 0)
                {
                    _addLocalePopupIdx = EditorGUILayout.Popup(_addLocalePopupIdx, _addableLocaleLabels);
                    _addLocalePopupIdx = Mathf.Clamp(_addLocalePopupIdx, 0, _addableLocaleCodes.Length - 1);

                    if (GUILayout.Button("+ Add Override", GUILayout.Width(110), GUILayout.Height(20)))
                    {
                        AddLocale(_addableLocaleCodes[_addLocalePopupIdx]);
                        InvalidateAddableLocales();
                    }
                }
                else if (_cachedSettings == null)
                {
                    // No settings found — show a help note
                    EditorGUILayout.LabelField(
                        "No LocalizationSettings asset found. Create one to auto-populate locales.",
                        EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "All available locales are already added.",
                        EditorStyles.miniLabel);
                }

                if (count > 0)
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("\u2212 Remove Selected", GUILayout.Width(130), GUILayout.Height(20)))
                    {
                        string removeCode = GetSelectedLocaleCode();
                        if (EditorUtility.DisplayDialog("Remove Override",
                            $"Remove override locale '{removeCode}' and its snapshot data?",
                            "Remove", "Cancel"))
                        {
                            _snapshotsProp.DeleteArrayElementAtIndex(_selectedLocaleIdx);
                            _selectedLocaleIdx = Mathf.Max(0, _selectedLocaleIdx - 1);
                            _diffStale = true;
                            InvalidateAddableLocales();
                        }
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ── Toolbar ─────────────────────────────────────────────
        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(SectionStyle);
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            {
                bool hasOverrides = _snapshotsProp.arraySize > 0;
                bool hasElements = _elementsProp.arraySize > 0;

                string scanLabel = hasElements ? "\u21bb Re-scan" : "\u2609 Scan Hierarchy";
                if (GUILayout.Button(scanLabel, ToolbarBtn))
                {
                    ScanHierarchy();
                    _diffStale = true;
                }

                EditorGUI.BeginDisabledGroup(!hasOverrides || _previewing);
                if (GUILayout.Button("\u2b07 Capture", ToolbarBtn))
                {
                    if (!hasElements) ScanHierarchy();
                    CaptureSnapshot();
                    _diffStale = true;
                }

                if (GUILayout.Button("\u21a9 Revert", ToolbarBtn))
                {
                    ApplySnapshot();
                    _diffStale = true;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!hasOverrides);
                if (!_previewing)
                {
                    if (GUILayout.Button("\u25b6 Preview", ToolbarBtn))
                        EnterPreview();
                }
                else
                {
                    if (GUILayout.Button("\u25a0 Stop Preview", ToolbarBtn))
                        ExitPreview();
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            // Preview locale switcher
            if (_previewing && _snapshotsProp.arraySize > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Switch:", EditorStyles.miniLabel, GUILayout.Width(45));

                if (GUILayout.Button($"[Base] {_baseLocaleProp.stringValue}", EditorStyles.miniButton,
                    GUILayout.MinWidth(60)))
                    PreviewBase();

                for (int i = 0; i < _snapshotsProp.arraySize; i++)
                {
                    string code = _snapshotsProp.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("LocaleCode").stringValue;
                    if (GUILayout.Button(code, EditorStyles.miniButton, GUILayout.MinWidth(40)))
                        PreviewLocale(i);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        // ── Elements List ───────────────────────────────────────
        private void DrawElements()
        {
            int totalCount = _elementsProp.arraySize;
            _showElements = EditorGUILayout.Foldout(_showElements,
                $"Tracked Elements ({totalCount})", true);
            if (!_showElements) return;

            if (totalCount == 0)
            {
                EditorGUILayout.LabelField(
                    "No elements tracked. Click Scan Hierarchy or right-click a component \u2192 Track Layout.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            var layout = (UILocaleLayout)target;

            // Build path info
            var paths = new string[totalCount];
            for (int i = 0; i < totalCount; i++)
            {
                var tgt = _elementsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("Target").objectReferenceValue as RectTransform;
                paths[i] = tgt != null ? GetRelativePath(layout.transform, tgt.transform) : "(null)";
            }

            bool useGrouping = totalCount > GroupingThreshold;
            bool useScroll = totalCount > ScrollThreshold;

            if (useScroll)
                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MaxHeight(350));

            int removeIdx = useGrouping
                ? DrawElementsGrouped(paths, layout)
                : DrawElementsFlat(paths, layout);

            if (removeIdx >= 0)
            {
                _elementsProp.DeleteArrayElementAtIndex(removeIdx);
                for (int s = 0; s < _snapshotsProp.arraySize; s++)
                {
                    var elems = _snapshotsProp.GetArrayElementAtIndex(s).FindPropertyRelative("Elements");
                    if (removeIdx < elems.arraySize)
                        elems.DeleteArrayElementAtIndex(removeIdx);
                }
                _diffStale = true;
            }

            if (useScroll)
                EditorGUILayout.EndScrollView();
        }

        private int DrawElementsFlat(string[] paths, UILocaleLayout layout)
        {
            int removeIdx = -1;
            for (int i = 0; i < _elementsProp.arraySize; i++)
            {
                int r = DrawElementRow(i, paths[i]);
                if (r >= 0) removeIdx = r;
            }
            return removeIdx;
        }

        private int DrawElementsGrouped(string[] paths, UILocaleLayout layout)
        {
            // Build ordered groups by first path segment
            var groupOrder = new List<string>(16);
            var groupIndices = new Dictionary<string, List<int>>(16);
            var shortNames = new string[paths.Length];

            for (int i = 0; i < paths.Length; i++)
            {
                string fullPath = paths[i];
                int slash = fullPath.IndexOf('/');
                string groupKey = slash >= 0 ? fullPath.Substring(0, slash) : fullPath;
                shortNames[i] = slash >= 0 ? fullPath.Substring(slash + 1) : fullPath;

                if (!groupIndices.TryGetValue(groupKey, out var list))
                {
                    list = new List<int>(8);
                    groupIndices[groupKey] = list;
                    groupOrder.Add(groupKey);
                }
                list.Add(i);
            }

            int removeIdx = -1;

            for (int g = 0; g < groupOrder.Count; g++)
            {
                string groupKey = groupOrder[g];
                var indices = groupIndices[groupKey];

                // Single-element groups: show inline without foldout
                if (indices.Count == 1)
                {
                    int r = DrawElementRow(indices[0], paths[indices[0]]);
                    if (r >= 0) removeIdx = r;
                    continue;
                }

                // Multi-element group: foldout header
                if (!_groupFoldouts.TryGetValue(groupKey, out bool expanded))
                {
                    expanded = true;
                    _groupFoldouts[groupKey] = true;
                }

                string statusSuffix = BuildGroupStatus(indices);

                EditorGUILayout.BeginHorizontal();
                _groupFoldouts[groupKey] = EditorGUILayout.Foldout(expanded,
                    $"{groupKey}  ({indices.Count})", true);
                if (!string.IsNullOrEmpty(statusSuffix))
                    GUILayout.Label(statusSuffix, RichLabel, GUILayout.Width(90));
                EditorGUILayout.EndHorizontal();

                if (!_groupFoldouts[groupKey]) continue;

                EditorGUI.indentLevel++;
                for (int j = 0; j < indices.Count; j++)
                {
                    int idx = indices[j];
                    int r = DrawElementRow(idx, shortNames[idx]);
                    if (r >= 0) removeIdx = r;
                }
                EditorGUI.indentLevel--;
            }

            return removeIdx;
        }

        private string BuildGroupStatus(List<int> indices)
        {
            if (_dirtyFlags == null || _previewing) return "";
            int dirty = 0, missing = 0;
            for (int j = 0; j < indices.Count; j++)
            {
                int idx = indices[j];
                var tgt = _elementsProp.GetArrayElementAtIndex(idx)
                    .FindPropertyRelative("Target").objectReferenceValue;
                if (tgt == null) missing++;
                else if (idx < _dirtyFlags.Length && _dirtyFlags[idx]) dirty++;
            }
            if (missing > 0) return $"<color=#cc4444>{missing} missing</color>";
            if (dirty > 0) return $"<color=#ddaa33>{dirty} modified</color>";
            return "<color=#55bb66>all ok</color>";
        }

        private int DrawElementRow(int i, string label)
        {
            var elem = _elementsProp.GetArrayElementAtIndex(i);
            var targetObj = elem.FindPropertyRelative("Target").objectReferenceValue as RectTransform;
            var textProp = elem.FindPropertyRelative("Text");

            Color bgColor = Color.clear;
            string statusIcon = "";
            if (targetObj == null)
            {
                bgColor = MissingColor;
                statusIcon = "<color=#cc4444>\u2716</color>";
            }
            else if (_dirtyFlags != null && i < _dirtyFlags.Length && !_previewing)
            {
                bgColor = _dirtyFlags[i] ? DirtyColor : CleanColor;
                statusIcon = _dirtyFlags[i]
                    ? "<color=#ddaa33>\u25cf</color>"
                    : "<color=#55bb66>\u2714</color>";
            }

            var rect = EditorGUILayout.BeginHorizontal();
            if (bgColor.a > 0) EditorGUI.DrawRect(rect, bgColor);

            // Status icon (compact)
            if (!string.IsNullOrEmpty(statusIcon))
                GUILayout.Label(statusIcon, RichLabel, GUILayout.Width(16));

            // Element name
            EditorGUILayout.LabelField(label ?? "(null)", GUILayout.MinWidth(60));

            // Font size badge
            if (targetObj != null)
            {
                var tmpText = textProp.objectReferenceValue as TMP_Text;
                if (tmpText != null)
                    GUILayout.Label($"{tmpText.fontSize:F0}", EditorStyles.miniLabel, GUILayout.Width(28));
            }

            // Select button (icon with tooltip)
            if (GUILayout.Button(new GUIContent("\u25ce", "Select in Hierarchy"),
                EditorStyles.miniButton, GUILayout.Width(22)))
            {
                if (targetObj != null)
                    Selection.activeGameObject = targetObj.gameObject;
            }

            // Remove button (icon with tooltip)
            EditorGUI.BeginDisabledGroup(_previewing);
            int result = -1;
            if (GUILayout.Button(new GUIContent("\u2715", "Remove from tracking"),
                EditorStyles.miniButton, GUILayout.Width(22)))
                result = i;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
            return result;
        }

        // ── Locale Switch Guard ─────────────────────────────────
        private void OnLocaleSwitch(int fromIdx, int toIdx)
        {
            if (_previewing) return; // Preview mode doesn't dirty

            // Check if current locale has unsaved changes
            RefreshDiff();
            if (_dirtyCount > 0)
            {
                string fromCode = "";
                if (fromIdx >= 0 && fromIdx < _snapshotsProp.arraySize)
                    fromCode = _snapshotsProp.GetArrayElementAtIndex(fromIdx)
                        .FindPropertyRelative("LocaleCode").stringValue;

                int choice = EditorUtility.DisplayDialogComplex(
                    "Unsaved Layout Changes",
                    $"The '{fromCode}' layout has {_dirtyCount} unsaved change(s).\n\nSave before switching?",
                    "Save & Switch",
                    "Discard & Switch",
                    "Cancel");

                switch (choice)
                {
                    case 0: // Save
                        _selectedLocaleIdx = fromIdx;
                        CaptureSnapshot();
                        _selectedLocaleIdx = toIdx;
                        ApplySnapshot();
                        break;
                    case 1: // Discard
                        _selectedLocaleIdx = toIdx;
                        ApplySnapshot();
                        break;
                    case 2: // Cancel
                        _selectedLocaleIdx = fromIdx;
                        break;
                }
            }
            else
            {
                // Apply the target locale's snapshot
                _selectedLocaleIdx = toIdx;
                ApplySnapshot();
            }

            _diffStale = true;
        }

        // ── Preview Mode ────────────────────────────────────────
        private void EnterPreview()
        {
            if (_previewing) return;
            _previewing = true;

            // Backup current scene state
            int count = _elementsProp.arraySize;
            _previewBackup = new ElementSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                var elem = _elementsProp.GetArrayElementAtIndex(i);
                var rt = elem.FindPropertyRelative("Target").objectReferenceValue as RectTransform;
                var tmp = elem.FindPropertyRelative("Text").objectReferenceValue as TMP_Text;
                _previewBackup[i] = ElementSnapshot.Capture(rt, tmp);
            }
        }

        private void ExitPreview()
        {
            if (!_previewing) return;
            _previewing = false;

            // Restore backed-up state
            if (_previewBackup != null)
            {
                int count = Math.Min(_elementsProp.arraySize, _previewBackup.Length);
                for (int i = 0; i < count; i++)
                {
                    var elem = _elementsProp.GetArrayElementAtIndex(i);
                    var rt = elem.FindPropertyRelative("Target").objectReferenceValue as RectTransform;
                    var tmp = elem.FindPropertyRelative("Text").objectReferenceValue as TMP_Text;
                    _previewBackup[i].ApplyTo(rt, tmp);
                    if (rt != null) EditorUtility.SetDirty(rt);
                    if (tmp != null) EditorUtility.SetDirty(tmp);
                }
                _previewBackup = null;
            }
            _diffStale = true;
        }

        private void PreviewLocale(int snapIdx)
        {
            if (!_previewing || snapIdx < 0 || snapIdx >= _snapshotsProp.arraySize) return;

            var elemsProp = _snapshotsProp.GetArrayElementAtIndex(snapIdx).FindPropertyRelative("Elements");
            int count = Math.Min(_elementsProp.arraySize, elemsProp.arraySize);
            for (int i = 0; i < count; i++)
            {
                var elem = _elementsProp.GetArrayElementAtIndex(i);
                var rt = elem.FindPropertyRelative("Target").objectReferenceValue as RectTransform;
                var tmp = elem.FindPropertyRelative("Text").objectReferenceValue as TMP_Text;
                var snap = ReadSnapshot(elemsProp.GetArrayElementAtIndex(i));
                snap.ApplyTo(rt, tmp);
            }
            SceneView.RepaintAll();
        }

        private void PreviewBase()
        {
            if (!_previewing || _previewBackup == null) return;

            int count = Math.Min(_elementsProp.arraySize, _previewBackup.Length);
            for (int i = 0; i < count; i++)
            {
                var elem = _elementsProp.GetArrayElementAtIndex(i);
                var rt = elem.FindPropertyRelative("Target").objectReferenceValue as RectTransform;
                var tmp = elem.FindPropertyRelative("Text").objectReferenceValue as TMP_Text;
                _previewBackup[i].ApplyTo(rt, tmp);
            }
            SceneView.RepaintAll();
        }

        // ── Scan ────────────────────────────────────────────────
        private void ScanHierarchy()
        {
            var layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Scan Hierarchy");

            var existing = new HashSet<RectTransform>();
            for (int i = 0; i < _elementsProp.arraySize; i++)
            {
                var obj = _elementsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("Target").objectReferenceValue as RectTransform;
                if (obj != null) existing.Add(obj);
            }

            int added = 0;

            layout.GetComponentsInChildren(true, SharedTextCache);
            for (int i = 0; i < SharedTextCache.Count; i++)
            {
                var tmp = SharedTextCache[i];
                var rt = tmp.GetComponent<RectTransform>();
                if (rt == null || existing.Contains(rt)) continue;

                int idx = _elementsProp.arraySize;
                _elementsProp.InsertArrayElementAtIndex(idx);
                var entry = _elementsProp.GetArrayElementAtIndex(idx);
                entry.FindPropertyRelative("Target").objectReferenceValue = rt;
                entry.FindPropertyRelative("Text").objectReferenceValue = tmp;
                existing.Add(rt);
                added++;
            }
            SharedTextCache.Clear();

            layout.GetComponentsInChildren(true, SharedImageCache);
            for (int i = 0; i < SharedImageCache.Count; i++)
            {
                var rt = SharedImageCache[i].GetComponent<RectTransform>();
                if (rt == null || existing.Contains(rt)) continue;

                int idx = _elementsProp.arraySize;
                _elementsProp.InsertArrayElementAtIndex(idx);
                var entry = _elementsProp.GetArrayElementAtIndex(idx);
                entry.FindPropertyRelative("Target").objectReferenceValue = rt;
                entry.FindPropertyRelative("Text").objectReferenceValue = null;
                existing.Add(rt);
                added++;
            }
            SharedImageCache.Clear();

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(layout);

            Debug.Log(added > 0
                ? $"[UILocaleLayout] Scanned: added {added} element(s), total {_elementsProp.arraySize}."
                : $"[UILocaleLayout] Scanned: no new elements. Total {_elementsProp.arraySize}.");
        }

        // ── Capture ─────────────────────────────────────────────
        private void CaptureSnapshot()
        {
            if (_snapshotsProp.arraySize == 0 || _selectedLocaleIdx >= _snapshotsProp.arraySize) return;

            var layout = (UILocaleLayout)target;
            Undo.RecordObject(layout, "Capture Locale Snapshot");

            var snapProp = _snapshotsProp.GetArrayElementAtIndex(_selectedLocaleIdx);
            var elemsProp = snapProp.FindPropertyRelative("Elements");

            int count = _elementsProp.arraySize;
            elemsProp.arraySize = count;

            for (int i = 0; i < count; i++)
            {
                var srcElem = _elementsProp.GetArrayElementAtIndex(i);
                var rt = srcElem.FindPropertyRelative("Target").objectReferenceValue as RectTransform;
                var tmp = srcElem.FindPropertyRelative("Text").objectReferenceValue as TMP_Text;

                var snap = ElementSnapshot.Capture(rt, tmp);
                WriteSnapshot(elemsProp.GetArrayElementAtIndex(i), snap);
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(layout);

            string locale = GetSelectedLocaleCode();
            Debug.Log($"[UILocaleLayout] Captured {count} elements for locale '{locale}'.");
        }

        // ── Apply / Revert ──────────────────────────────────────
        private void ApplySnapshot()
        {
            if (_snapshotsProp.arraySize == 0 || _selectedLocaleIdx >= _snapshotsProp.arraySize) return;

            var snapProp = _snapshotsProp.GetArrayElementAtIndex(_selectedLocaleIdx);
            var elemsProp = snapProp.FindPropertyRelative("Elements");
            int count = Math.Min(_elementsProp.arraySize, elemsProp.arraySize);

            for (int i = 0; i < count; i++)
            {
                var rt = _elementsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("Target").objectReferenceValue as RectTransform;
                var tmp = _elementsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("Text").objectReferenceValue as TMP_Text;

                if (rt != null) Undo.RecordObject(rt, "Apply Locale Snapshot");
                if (tmp != null) Undo.RecordObject(tmp, "Apply Locale Snapshot");

                var snap = ReadSnapshot(elemsProp.GetArrayElementAtIndex(i));
                snap.ApplyTo(rt, tmp);

                if (rt != null) EditorUtility.SetDirty(rt);
                if (tmp != null) EditorUtility.SetDirty(tmp);
            }

            string locale = GetSelectedLocaleCode();
            Debug.Log($"[UILocaleLayout] Applied '{locale}' snapshot to {count} elements.");
        }

        // ── Diff ────────────────────────────────────────────────
        private void RefreshDiff()
        {
            _diffStale = false;
            _dirtyCount = 0;
            int count = _elementsProp.arraySize;

            if (_dirtyFlags == null || _dirtyFlags.Length != count)
                _dirtyFlags = new bool[count];
            else
                Array.Clear(_dirtyFlags, 0, _dirtyFlags.Length);

            if (_snapshotsProp.arraySize == 0 || _selectedLocaleIdx >= _snapshotsProp.arraySize) return;

            var snapProp = _snapshotsProp.GetArrayElementAtIndex(_selectedLocaleIdx);
            var elemsProp = snapProp.FindPropertyRelative("Elements");
            int snapCount = elemsProp.arraySize;

            for (int i = 0; i < count; i++)
            {
                var srcElem = _elementsProp.GetArrayElementAtIndex(i);
                var rt = srcElem.FindPropertyRelative("Target").objectReferenceValue as RectTransform;
                var tmp = srcElem.FindPropertyRelative("Text").objectReferenceValue as TMP_Text;

                if (rt == null || i >= snapCount)
                {
                    _dirtyFlags[i] = true;
                    _dirtyCount++;
                    continue;
                }

                var live = ElementSnapshot.Capture(rt, tmp);
                var saved = ReadSnapshot(elemsProp.GetArrayElementAtIndex(i));

                if (!live.ApproximatelyEquals(saved))
                {
                    _dirtyFlags[i] = true;
                    _dirtyCount++;
                }
            }
        }

        // ── Locale Discovery ────────────────────────────────────
        private LocalizationSettings FindLocalizationSettings()
        {
            if (_settingsSearched) return _cachedSettings;
            _settingsSearched = true;

            string[] guids = AssetDatabase.FindAssets("t:LocalizationSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _cachedSettings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(path);
            }
            return _cachedSettings;
        }

        private void RebuildAddableLocalesIfNeeded()
        {
            if (_addableLocaleCodes != null) return;

            var settings = FindLocalizationSettings();
            if (settings == null || settings.AvailableLocales.Count == 0)
            {
                _addableLocaleCodes = Array.Empty<string>();
                _addableLocaleLabels = Array.Empty<string>();
                return;
            }

            string baseLoc = _baseLocaleProp.stringValue;
            var codes = new List<string>();
            var labels = new List<string>();

            for (int i = 0; i < settings.AvailableLocales.Count; i++)
            {
                var locale = settings.AvailableLocales[i];
                if (locale == null) continue;

                string code = locale.Id.ToString();
                if (string.Equals(code, baseLoc, StringComparison.OrdinalIgnoreCase)) continue;
                if (HasLocale(code)) continue;

                codes.Add(code);
                labels.Add(FormatLocaleLabel(locale));
            }

            _addableLocaleCodes = codes.ToArray();
            _addableLocaleLabels = labels.ToArray();
            _addLocalePopupIdx = 0;
        }

        private void InvalidateAddableLocales()
        {
            _addableLocaleCodes = null;
            _addableLocaleLabels = null;
        }

        private static string FormatLocaleLabel(Locale locale)
        {
            string code = locale.Id.ToString();
            string native = locale.NativeName;
            string display = locale.DisplayName;

            // Show "zh-CN (简体中文)" or "en (English)" format
            if (!string.IsNullOrEmpty(native) && !string.Equals(native, code, StringComparison.OrdinalIgnoreCase))
                return $"{code}  ({native})";
            if (!string.IsNullOrEmpty(display) && !string.Equals(display, code, StringComparison.OrdinalIgnoreCase))
                return $"{code}  ({display})";
            return code;
        }

        private static Locale FindLocaleByCode(LocalizationSettings settings, string code)
        {
            for (int i = 0; i < settings.AvailableLocales.Count; i++)
            {
                var loc = settings.AvailableLocales[i];
                if (loc != null && string.Equals(loc.Id.ToString(), code, StringComparison.OrdinalIgnoreCase))
                    return loc;
            }
            return null;
        }

        // ── Helpers ─────────────────────────────────────────────
        private string GetSelectedLocaleCode()
        {
            if (_snapshotsProp.arraySize == 0 || _selectedLocaleIdx >= _snapshotsProp.arraySize) return "";
            return _snapshotsProp.GetArrayElementAtIndex(_selectedLocaleIdx)
                .FindPropertyRelative("LocaleCode").stringValue;
        }

        private bool HasLocale(string code)
        {
            for (int i = 0; i < _snapshotsProp.arraySize; i++)
            {
                if (string.Equals(_snapshotsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("LocaleCode").stringValue, code, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void AddLocale(string code)
        {
            int idx = _snapshotsProp.arraySize;
            _snapshotsProp.InsertArrayElementAtIndex(idx);
            var snap = _snapshotsProp.GetArrayElementAtIndex(idx);
            snap.FindPropertyRelative("LocaleCode").stringValue = code;
            snap.FindPropertyRelative("Elements").arraySize = _elementsProp.arraySize;
            _selectedLocaleIdx = idx;
            serializedObject.ApplyModifiedProperties();
            _diffStale = true;
            InvalidateAddableLocales();
        }

        private static void WriteSnapshot(SerializedProperty prop, in ElementSnapshot snap)
        {
            prop.FindPropertyRelative("FontSize").floatValue = snap.FontSize;
            prop.FindPropertyRelative("LineSpacing").floatValue = snap.LineSpacing;
            prop.FindPropertyRelative("CharacterSpacing").floatValue = snap.CharacterSpacing;
            prop.FindPropertyRelative("AnchoredPosition").vector2Value = snap.AnchoredPosition;
            prop.FindPropertyRelative("SizeDelta").vector2Value = snap.SizeDelta;
        }

        private static ElementSnapshot ReadSnapshot(SerializedProperty prop)
        {
            return new ElementSnapshot
            {
                FontSize = prop.FindPropertyRelative("FontSize").floatValue,
                LineSpacing = prop.FindPropertyRelative("LineSpacing").floatValue,
                CharacterSpacing = prop.FindPropertyRelative("CharacterSpacing").floatValue,
                AnchoredPosition = prop.FindPropertyRelative("AnchoredPosition").vector2Value,
                SizeDelta = prop.FindPropertyRelative("SizeDelta").vector2Value,
            };
        }

        private static string GetRelativePath(Transform root, Transform child)
        {
            if (child == root) return "(self)";
            var parts = new List<string>(8);
            var current = child;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            if (parts.Count == 0) return child.name;
            var sb = new System.Text.StringBuilder(64);
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if (sb.Length > 0) sb.Append('/');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }
    }
}
#endif
