using System;
using System.Text;
using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BehaviorTreeEditor : EditorWindow
    {
        private const string DEBUG_FLAG = "[BehaviorTreeEditor]";
        private BehaviorTreeView _behaviorTreeView;
        private BTInspectorView _inspectorView;
        private ToolbarSearchField _searchField;
        private Label _statsLabel;
        private ToolbarButton _saveButton;
        private ToolbarButton _sortButton;
        private ToolbarButton _repairAssetButton;
        private ToolbarButton _repairRootButton;
        private bool _authoringReadOnly;
        [MenuItem("Tools/CycloneGames/Behavior Tree/Behavior Tree Editor")]
        public static void OpenWindow()
        {
            BehaviorTreeEditor wnd = GetWindow<BehaviorTreeEditor>();
            wnd.titleContent = new GUIContent("Behavior Tree Editor");
        }
        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceID) is Runtime.BehaviorTree tree)
            {
                Selection.activeObject = tree;
                OpenWindow();
                return true;
            }
            return false;
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            CreateToolbar(root);

            var visualTree = BehaviorTreeEditorResources.EditorLayout;
            if (visualTree == null)
            {
                root.Add(new Label("Behavior Tree editor assets are unavailable. See the Console for the missing asset GUID."));
                return;
            }

            visualTree.CloneTree(root);

            _behaviorTreeView = root.Q<BehaviorTreeView>();
            _inspectorView = root.Q<BTInspectorView>();

            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.S && evt.actionKey)
                {
                    Save();
                    evt.StopPropagation();
                }
            });

            _behaviorTreeView.OnNodeSelectionChanged += OnNodeSelectionChange;
            OnSelectionChange();
            RefreshAuthoringControls();
            UpdateStats();
        }

        private void CreateToolbar(VisualElement root)
        {
            var toolbar = new Toolbar();
            toolbar.style.flexShrink = 0;

            _saveButton = new ToolbarButton(Save) { text = "Save" };
            toolbar.Add(_saveButton);

            var validateButton = new ToolbarButton(ValidateCurrentTree) { text = "Validate" };
            toolbar.Add(validateButton);

            _sortButton = new ToolbarButton(() => _behaviorTreeView?.SortNodes()) { text = "Sort" };
            toolbar.Add(_sortButton);

            var rootButton = new ToolbarButton(() => _behaviorTreeView?.ReturnToRoot()) { text = "Focus Root" };
            toolbar.Add(rootButton);

            _repairAssetButton = new ToolbarButton(RepairAuthoringData) { text = "Repair Asset" };
            _repairAssetButton.tooltip = "Repair null or missing authoring lists and sub-asset ownership with Undo support.";
            toolbar.Add(_repairAssetButton);

            _repairRootButton = new ToolbarButton(RepairMissingRoot) { text = "Repair Root" };
            _repairRootButton.tooltip = "Restore a missing root reference or create a root explicitly.";
            toolbar.Add(_repairRootButton);

            var refreshButton = new ToolbarButton(() =>
            {
                if (_behaviorTreeView?.Tree != null)
                {
                    _behaviorTreeView.PopulateView(_behaviorTreeView.Tree);
                    UpdateStats();
                }
            }) { text = "Refresh" };
            toolbar.Add(refreshButton);

            toolbar.Add(new ToolbarSpacer());

            _searchField = new ToolbarSearchField();
            _searchField.style.minWidth = 220;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _behaviorTreeView?.SetSearchFilter(evt.newValue);
                UpdateStats();
            });
            toolbar.Add(_searchField);

            _statsLabel = new Label("No tree")
            {
                name = "bt-editor-stats"
            };
            _statsLabel.style.marginLeft = 8;
            _statsLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _statsLabel.style.color = new StyleColor(new Color(0.72f, 0.72f, 0.72f));
            toolbar.Add(_statsLabel);

            root.Add(toolbar);
        }

        private void Save()
        {
            if (_authoringReadOnly || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Save skipped: behavior tree authoring is read-only in Play Mode.");
                return;
            }

            if (_behaviorTreeView == null || !_behaviorTreeView.HasTree || _behaviorTreeView.Tree == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Save skipped: no behavior tree is currently loaded.");
                return;
            }

            StringBuilder log = new StringBuilder();
            log.Append(DEBUG_FLAG);
            log.AppendLine("Save Behavior Tree : " + _behaviorTreeView.Tree.name);
            log.AppendLine("Path : " + AssetDatabase.GetAssetPath(_behaviorTreeView.Tree));
            Debug.Log(log.ToString());
            AssetDatabase.SaveAssetIfDirty(_behaviorTreeView.Tree);
        }

        private void RepairMissingRoot()
        {
            if (_behaviorTreeView == null || !_behaviorTreeView.HasTree)
            {
                EditorUtility.DisplayDialog("Behavior Tree Root Repair", "No tree selected.", "OK");
                return;
            }

            bool repaired = _behaviorTreeView.TryRepairMissingRoot(out string message);
            EditorUtility.DisplayDialog(
                repaired ? "Behavior Tree Root Repaired" : "Behavior Tree Root Repair",
                message,
                "OK");

            if (repaired)
            {
                UpdateStats();
            }
        }

        private void RepairAuthoringData()
        {
            if (_behaviorTreeView == null || !_behaviorTreeView.HasTree)
            {
                EditorUtility.DisplayDialog("Behavior Tree Asset Repair", "No tree selected.", "OK");
                return;
            }

            bool repaired = _behaviorTreeView.TryRepairAuthoringData(out string message);
            EditorUtility.DisplayDialog(
                repaired ? "Behavior Tree Asset Repaired" : "Behavior Tree Asset Repair",
                message,
                "OK");

            if (repaired)
            {
                UpdateStats();
            }
        }

        private void OnEnable()
        {
            _authoringReadOnly = EditorApplication.isPlayingOrWillChangePlaymode;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
        }
        private void OnPlayModeStateChanged(PlayModeStateChange modeState)
        {
            _authoringReadOnly = modeState != PlayModeStateChange.EnteredEditMode;
            RefreshAuthoringControls();
            switch (modeState)
            {
                case PlayModeStateChange.EnteredEditMode:
                    BehaviorTreeView.InvalidateRunnerCache();
                    OnSelectionChange();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    BehaviorTreeView.InvalidateRunnerCache();
                    OnSelectionChange();
                    break;
                default:
                    break;
            }
        }

        private void RefreshAuthoringControls()
        {
            bool authoringEnabled = !_authoringReadOnly && !EditorApplication.isPlayingOrWillChangePlaymode;
            _saveButton?.SetEnabled(authoringEnabled);
            _sortButton?.SetEnabled(authoringEnabled);
            _repairAssetButton?.SetEnabled(authoringEnabled);
            _repairRootButton?.SetEnabled(authoringEnabled);
        }

        private Runtime.BehaviorTree _lastTree;

        /// <summary>
        /// Handles selection changes to update the behavior tree view.
        /// Supports both asset selection and BTRunnerComponent selection in play mode.
        /// In play mode, also tries to auto-find a BTRunnerComponent using the viewed tree.
        /// </summary>
        private void OnSelectionChange()
        {
            if (!Application.isPlaying && _lastTree != null)
            {
                _lastTree.SetEditorOwner(null);
            }
            _lastTree = Selection.activeObject as Runtime.BehaviorTree;
            if (!_lastTree)
            {
                if (Selection.activeObject)
                {
                    BTRunnerComponent runner;
                    try
                    {
                        runner = Selection.activeGameObject.GetComponent<BTRunnerComponent>();
                    }
                    catch (Exception)
                    {
                        runner = null;
                    }
                    if (runner)
                    {
                        _lastTree = runner.Tree;
                        if (!Application.isPlaying && _lastTree != null)
                        {
                            _lastTree.SetEditorOwner(runner.gameObject);
                        }
                        // Debug.Log($"Selected BT Runner : {Selection.activeGameObject.name}");
                    }
                }
            }

            if (_behaviorTreeView == null) return;

            if (Application.isPlaying)
            {
                if (_lastTree)
                {
                    Debug.Log($"Open Tree : " + _lastTree.name);
                    _behaviorTreeView.PopulateView(_lastTree);
                    UpdateStats();
                }
                else
                {
                    // In play mode without a tree selected, try to find any running BTRunner
                    // that matches the previously viewed tree
                    TryAutoSelectRunningTree();
                }
            }
            else
            {
                if (_lastTree && AssetDatabase.CanOpenAssetInEditor(_lastTree.GetInstanceID()))
                {
                    Debug.Log($"Open Tree : " + _lastTree.name);
                    _behaviorTreeView.PopulateView(_lastTree);
                    UpdateStats();
                }
            }
        }

        /// <summary>
        /// Attempts to auto-select a running tree from active BTRunnerComponents.
        /// </summary>
        private void TryAutoSelectRunningTree()
        {
            if (!Application.isPlaying || _behaviorTreeView == null) return;

            // If we already have a tree displayed, try to find a runner using it
            if (_behaviorTreeView.Tree != null)
            {
                var runners = BehaviorTreeView.GetCachedRunners();
                int count = runners.Count;
                for (int i = 0; i < count; i++)
                {
                    var runner = runners[i];
                    if (runner != null && runner.Tree != null && runner.RuntimeTree != null)
                    {
                        if (runner.Tree.name == _behaviorTreeView.Tree.name)
                        {
                            _lastTree = runner.Tree;
                            return;
                        }
                    }
                }
            }
        }
        private void OnDestroy()
        {
            EditorApplication.update -= OnEditorUpdate;

            if (_behaviorTreeView != null)
            {
                _behaviorTreeView.OnNodeSelectionChanged -= OnNodeSelectionChange;
                _behaviorTreeView.ClearView();
                _behaviorTreeView = null;
            }

            if (_inspectorView != null)
            {
                _inspectorView.ClearInspector();
                _inspectorView = null;
            }

            if (!Application.isPlaying && _lastTree != null)
            {
                _lastTree.SetEditorOwner(null);
            }
            _lastTree = null;
        }
        private void OnNodeSelectionChange(BTNodeView nodeView)
        {
            _inspectorView.UpdateSelection(nodeView);
            UpdateStats(nodeView);
        }

        /// <summary>
        /// Updates node states every frame in play mode to capture final states
        /// before BehaviorTree.Stop() resets them.
        /// </summary>
        private void OnEditorUpdate()
        {
            if (Application.isPlaying && _behaviorTreeView != null)
            {
                _behaviorTreeView.UpdateNodeStates();
            }
        }

        private void ValidateCurrentTree()
        {
            if (_behaviorTreeView == null || !_behaviorTreeView.HasTree)
            {
                EditorUtility.DisplayDialog("Behavior Tree Validation", "No tree selected.", "OK");
                return;
            }

            string report = _behaviorTreeView.GetValidationReport();
            bool passed = report.StartsWith("Validation passed", StringComparison.Ordinal);
            EditorUtility.DisplayDialog(
                passed ? "Behavior Tree Validation Passed" : "Behavior Tree Validation Report",
                report,
                "OK");
        }

        private void UpdateStats(BTNodeView selectedNode = null)
        {
            if (_statsLabel == null)
            {
                return;
            }

            if (_behaviorTreeView == null || !_behaviorTreeView.HasTree)
            {
                _statsLabel.text = "No tree";
                return;
            }

            string selectedText = selectedNode?.Node != null
                ? $" | Selected: {selectedNode.Node.GetType().Name}"
                : string.Empty;

            string modeText = _authoringReadOnly || EditorApplication.isPlayingOrWillChangePlaymode
                ? " | Play Mode: Read Only"
                : string.Empty;
            _statsLabel.text = $"Nodes: {_behaviorTreeView.GetNodeCount()}{selectedText}{modeText}";
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying && _behaviorTreeView != null)
            {
                Repaint();
            }
        }
    }

    internal static class BehaviorTreeEditorResources
    {
        private const string EditorLayoutGuid = "7cc5d4f0bab93384cb8db3533da654e6";
        private const string EditorStyleGuid = "bf8be869980123747aa53e13b8e51d5b";
        private const string NodeLayoutGuid = "657fad77865cc0f48bd32bf9e8736ff5";
        private const string NodeStyleGuid = "58cd66f66a7c2b243b22592bb0975bf7";

        private static VisualTreeAsset _editorLayout;
        private static StyleSheet _editorStyle;
        private static VisualTreeAsset _nodeLayout;
        private static StyleSheet _nodeStyle;
        private static bool _editorLayoutAttempted;
        private static bool _editorStyleAttempted;
        private static bool _nodeLayoutAttempted;
        private static bool _nodeStyleAttempted;
        private static string _nodeLayoutPath;

        public static VisualTreeAsset EditorLayout => Load(
            ref _editorLayout,
            ref _editorLayoutAttempted,
            EditorLayoutGuid,
            "editor layout");

        public static StyleSheet EditorStyle => Load(
            ref _editorStyle,
            ref _editorStyleAttempted,
            EditorStyleGuid,
            "editor style sheet");

        public static StyleSheet NodeStyle => Load(
            ref _nodeStyle,
            ref _nodeStyleAttempted,
            NodeStyleGuid,
            "node style sheet");

        public static string NodeLayoutPath
        {
            get
            {
                if (_nodeLayoutPath != null)
                {
                    return _nodeLayoutPath;
                }

                VisualTreeAsset layout = Load(
                    ref _nodeLayout,
                    ref _nodeLayoutAttempted,
                    NodeLayoutGuid,
                    "node layout");
                _ = NodeStyle;
                _nodeLayoutPath = layout == null ? string.Empty : AssetDatabase.GetAssetPath(layout);
                return _nodeLayoutPath;
            }
        }

        private static T Load<T>(
            ref T cachedAsset,
            ref bool attempted,
            string guid,
            string description)
            where T : UnityEngine.Object
        {
            if (attempted)
            {
                return cachedAsset;
            }

            attempted = true;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"[BehaviorTreeEditor] Missing {description} asset for GUID '{guid}'.");
                return null;
            }

            cachedAsset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (cachedAsset == null)
            {
                Debug.LogError($"[BehaviorTreeEditor] Failed to load {description} at '{path}' (GUID '{guid}').");
            }

            return cachedAsset;
        }
    }
}
