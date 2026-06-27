using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Editor
{
    public class GameplayTagEditorWindow : EditorWindow
    {
        private const float MinWindowWidth = 480f;
        private const float MinWindowHeight = 300f;
        private const float MenuBarHeight = 20f;
        private const float ToolbarSpacing = 4f;
        private const float StatusBarHeight = 18f;
        private const float DetailsPanelMinWindowWidth = 760f;
        private const float DetailsPanelWidth = 300f;
        private const float DetailsPanelGap = 4f;
        private static readonly string[] StatStrs = new string[3];

        private ManagerTreeView _treeView;
        private TreeViewState _treeViewState;
        private GameplayTag _selectedTag;
        private Vector2 _detailsScrollPosition;
        private int _cachedTagCount;
        private double _nextStatsRefresh;
        private string _cachedTagCountStr;
        private string _cachedStatusStr;
        private int _previousTagCount;
        private bool _isRefreshing;

        [MenuItem("Tools/CycloneGames/GameplayTags/Gameplay Tag Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<GameplayTagEditorWindow>("Gameplay Tag Manager");
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
        }

        private void OnEnable()
        {
            _treeViewState = new TreeViewState();
            _treeView = new ManagerTreeView(_treeViewState, OnTagSelected);
            _cachedTagCount = GameplayTagManager.GetAllTags().Length;
            _previousTagCount = _cachedTagCount;
            UpdateCachedStrings();
            GameplayTagManager.OnGameplayTagTreeChanged += OnTreeChanged;
        }

        private void OnDisable()
        {
            GameplayTagManager.OnGameplayTagTreeChanged -= OnTreeChanged;
        }

        private void OnTreeChanged()
        {
            _cachedTagCount = GameplayTagManager.GetAllTags().Length;
            RefreshSelectedTag();
            _treeView?.Reload();
            UpdateCachedStrings();
            Repaint();
        }

        private void UpdateCachedStrings()
        {
            StatStrs[0] = _cachedTagCount.ToString();
            _cachedTagCountStr = _cachedTagCount == 1 ? "1 tag" : $"{_cachedTagCount} tags";
            _cachedStatusStr = _isRefreshing ? "Refreshing..." : "Ready";
        }

        private void OnGUI()
        {
            DrawMenuBar();

            if (_treeView == null) return;

            Rect contentRect = EditorGUILayout.GetControlRect(false, 0, GUILayout.ExpandHeight(true));
            contentRect = new Rect(0, contentRect.y, position.width, position.height - MenuBarHeight - StatusBarHeight);

            if (position.width >= DetailsPanelMinWindowWidth)
            {
                float detailsWidth = Mathf.Min(DetailsPanelWidth, position.width * 0.38f);
                Rect treeRect = contentRect;
                treeRect.width -= detailsWidth + DetailsPanelGap;

                Rect detailsRect = contentRect;
                detailsRect.x = treeRect.xMax + DetailsPanelGap;
                detailsRect.width = detailsWidth;

                _treeView.OnGUI(treeRect);
                DrawDetailsPanel(detailsRect);
            }
            else
            {
                _treeView.OnGUI(contentRect);
            }

            DrawStatusBar();

            HandleKeyboardShortcuts();
        }

        private void DrawMenuBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(MenuBarHeight));

            float availableWidth = position.width - 8f;
            float buttonWidth = 90f;
            int numButtons = 3;
            float totalButtonWidth = numButtons * buttonWidth + (numButtons - 1) * ToolbarSpacing;
            bool isCompact = availableWidth < totalButtonWidth + 150f;

            if (isCompact)
                DrawCompactMenuBar(availableWidth);
            else
                DrawFullMenuBar();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFullMenuBar()
        {
            if (GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                _treeView.ExpandAll();
            }

            if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                _treeView.CollapseAll();
            }

            GUILayout.Space(ToolbarSpacing);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                PerformRefresh();
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField(_cachedTagCountStr, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(60f));
        }

        private void DrawCompactMenuBar()
        {
            if (GUILayout.Button("E", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                _treeView.ExpandAll();
            }

            if (GUILayout.Button("C", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                _treeView.CollapseAll();
            }

            if (GUILayout.Button("R", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                PerformRefresh();
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField(_cachedTagCountStr, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(50f));
        }

        private void DrawCompactMenuBar(float availableWidth)
        {
            if (GUILayout.Button("E", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                _treeView.ExpandAll();
            }

            if (GUILayout.Button("C", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                _treeView.CollapseAll();
            }

            if (GUILayout.Button("R", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                PerformRefresh();
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField(_cachedTagCountStr, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(50f));
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(StatusBarHeight));
            EditorGUILayout.LabelField(_cachedStatusStr, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            if (_cachedTagCount != _previousTagCount)
            {
                EditorGUILayout.LabelField($"Δ {(_cachedTagCount - _previousTagCount):+0;-#}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(40f));
                _previousTagCount = _cachedTagCount;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void PerformRefresh()
        {
            if (_isRefreshing) return;

            _isRefreshing = true;
            UpdateCachedStrings();

            GameplayTagManager.ReloadTags();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            _treeView?.Reload();

            _isRefreshing = false;
            UpdateCachedStrings();
            _nextStatsRefresh = EditorApplication.timeSinceStartup + 0.1d;
        }

        private void OnTagSelected(GameplayTag tag)
        {
            _selectedTag = tag;
            _detailsScrollPosition = Vector2.zero;
            Repaint();
        }

        private void RefreshSelectedTag()
        {
            if (_selectedTag.IsNone)
            {
                _selectedTag = GameplayTag.None;
                return;
            }

            string selectedTagName = _selectedTag.Name;
            _selectedTag = GameplayTagManager.RequestTag(selectedTagName, false);
        }

        private void DrawDetailsPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            Rect innerRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
            GUILayout.BeginArea(innerRect);
            _detailsScrollPosition = EditorGUILayout.BeginScrollView(_detailsScrollPosition);

            if (!_selectedTag.IsValid)
            {
                EditorGUILayout.LabelField("Tag Details", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Select a gameplay tag to inspect its stable ID, hierarchy, source files, and editor metadata.", MessageType.Info);
                EditorGUILayout.LabelField("Manifest", $"0x{GameplayTagManager.CurrentManifestHash:X16}");
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            GameplayTagDefinition definition = _selectedTag.Definition;

            EditorGUILayout.LabelField(_selectedTag.Label, EditorStyles.boldLabel);
            DrawSelectableField("Name", _selectedTag.Name);
            DrawSelectableField("Stable ID", $"0x{_selectedTag.StableId:X16}");
            EditorGUILayout.LabelField("Runtime Index", _selectedTag.RuntimeIndex.ToString());
            EditorGUILayout.LabelField("Manifest", $"0x{GameplayTagManager.CurrentManifestHash:X16}");
            EditorGUILayout.LabelField("Flags", _selectedTag.Flags.ToString());
            EditorGUILayout.LabelField("Leaf", _selectedTag.IsLeaf ? "Yes" : "No");

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Description", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(_selectedTag.Description) ? "No description." : _selectedTag.Description, MessageType.None);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Hierarchy", EditorStyles.boldLabel);
            DrawTagSpan("Parents", _selectedTag.ParentTags);
            DrawTagSpan("Children", _selectedTag.ChildTags);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Sources", EditorStyles.boldLabel);
            if (definition.SourceCount == 0)
            {
                EditorGUILayout.LabelField("No source registered.");
            }
            else
            {
                for (int i = 0; i < definition.SourceCount; i++)
                {
                    IGameplayTagSource source = definition.GetSource(i);
                    string access = source is IDeleteTagHandler ? "Editable" : "Read-only";
                    EditorGUILayout.LabelField(source.Name, access);
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Name"))
            {
                EditorGUIUtility.systemCopyBuffer = _selectedTag.Name;
            }

            if (GUILayout.Button("Copy Stable ID"))
            {
                EditorGUIUtility.systemCopyBuffer = $"0x{_selectedTag.StableId:X16}";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawSelectableField(string label, string value)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(value ?? string.Empty, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        private static void DrawTagSpan(string label, ReadOnlySpan<GameplayTag> tags)
        {
            if (tags.Length == 0)
            {
                EditorGUILayout.LabelField(label, "None");
                return;
            }

            EditorGUILayout.LabelField(label, $"{tags.Length}");
            for (int i = 0; i < tags.Length; i++)
            {
                GameplayTag tag = tags[i];
                EditorGUILayout.LabelField("  " + (tag.IsNone ? "<None>" : tag.Name));
            }
        }

        private void HandleKeyboardShortcuts()
        {
            if (Event.current == null) return;

            if (Event.current.type != EventType.KeyDown) return;

            switch (Event.current.keyCode)
            {
                case KeyCode.E when Event.current.control:
                    _treeView.ExpandAll();
                    Event.current.Use();
                    break;
                case KeyCode.C when Event.current.control:
                    _treeView.CollapseAll();
                    Event.current.Use();
                    break;
                case KeyCode.R when Event.current.control:
                    PerformRefresh();
                    Event.current.Use();
                    break;
            }
        }

        private class ManagerTreeView : GameplayTagTreeViewBase
        {
            private readonly System.Action<GameplayTag> _selectionChanged;

            public ManagerTreeView(TreeViewState state, System.Action<GameplayTag> selectionChanged) : base(state)
            {
                _selectionChanged = selectionChanged;
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                float indent = GetContentIndent(args.item);
                Rect rect = args.rowRect;
                rect.xMin += indent + 2 - (hasSearch ? 14 : 0);

                if (args.item is GameplayTagTreeViewItem item)
                    DoTagRowGUI(rect, item);
            }

            protected override void SelectionChanged(IList<int> selectedIds)
            {
                if (selectedIds.Count == 0)
                {
                    _selectionChanged?.Invoke(GameplayTag.None);
                    return;
                }

                GameplayTagTreeViewItem item = FindItem(selectedIds[0]);
                _selectionChanged?.Invoke(item?.Tag ?? GameplayTag.None);
            }
        }
    }
}
