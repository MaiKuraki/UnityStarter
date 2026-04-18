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
        [MenuItem("Tools/CycloneGames/Behavior Tree/Behavior Tree Editor")]
        public static void OpenWindow()
        {
            BehaviorTreeEditor wnd = GetWindow<BehaviorTreeEditor>();
            wnd.titleContent = new GUIContent("Behavior Tree Editor");
        }
        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            if (Selection.activeObject is Runtime.BehaviorTree)
            {
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

            var visualTree = Resources.Load<VisualTreeAsset>("BT_Editor_Layout");
            visualTree.CloneTree(root);

            _behaviorTreeView = root.Q<BehaviorTreeView>();
            _inspectorView = root.Q<BTInspectorView>();

            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.S && evt.ctrlKey)
                {
                    Save();
                }
            });

            _behaviorTreeView.OnNodeSelectionChanged += OnNodeSelectionChange;
            OnSelectionChange();
            UpdateStats();
        }

        private void CreateToolbar(VisualElement root)
        {
            var toolbar = new Toolbar();
            toolbar.style.flexShrink = 0;

            var saveButton = new ToolbarButton(Save) { text = "Save" };
            toolbar.Add(saveButton);

            var validateButton = new ToolbarButton(ValidateCurrentTree) { text = "Validate" };
            toolbar.Add(validateButton);

            var sortButton = new ToolbarButton(() => _behaviorTreeView?.SortNodes()) { text = "Sort" };
            toolbar.Add(sortButton);

            var rootButton = new ToolbarButton(() => _behaviorTreeView?.ReturnToRoot()) { text = "Focus Root" };
            toolbar.Add(rootButton);

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
            AssetDatabase.SaveAssets();
        }

        private void OnEnable()
        {
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
                        if (!Application.isPlaying)
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

            _statsLabel.text = $"Nodes: {_behaviorTreeView.GetNodeCount()}{selectedText}";
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying && _behaviorTreeView != null)
            {
                Repaint();
            }
        }
    }
}
