using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
    public class GameplayTagEditorWindow : EditorWindow
    {
        private const float MinWindowWidth = 480f;
        private const float MinWindowHeight = 300f;
        private const float MenuBarHeight = 20f;
        private const float ToolbarSpacing = 4f;
        private const float StatusBarHeight = 18f;
        private static readonly string[] StatStrs = new string[3];

        private ManagerTreeView _treeView;
        private TreeViewState _treeViewState;
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
            _treeView = new ManagerTreeView(_treeViewState);
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

            Rect treeRect = EditorGUILayout.GetControlRect(false, 0, GUILayout.ExpandHeight(true));
            treeRect = new Rect(treeRect.x, treeRect.y, position.width, position.height - MenuBarHeight - StatusBarHeight);

            _treeView.OnGUI(treeRect);

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
            public ManagerTreeView(TreeViewState state) : base(state)
            {
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                float indent = GetContentIndent(args.item);
                Rect rect = args.rowRect;
                rect.xMin += indent + 2 - (hasSearch ? 14 : 0);

                if (args.item is GameplayTagTreeViewItem item)
                    DoTagRowGUI(rect, item);
            }
        }
    }
}
