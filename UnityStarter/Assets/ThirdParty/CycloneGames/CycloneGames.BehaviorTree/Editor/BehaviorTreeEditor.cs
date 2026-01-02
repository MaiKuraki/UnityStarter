using System;
using System.Text;
using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BehaviorTreeEditor : EditorWindow
    {
        private const string DEBUG_FLAG = "[BehaviorTreeEditor]";
        private BehaviorTreeView _behaviorTreeView;
        private BTInspectorView _inspectorView;
        [MenuItem("Window//Behavior Tree Editor")]
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
        }

        private void Save()
        {
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
                _inspectorView.Clear();
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

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying && _behaviorTreeView != null)
            {
                Repaint();
            }
        }
    }
}