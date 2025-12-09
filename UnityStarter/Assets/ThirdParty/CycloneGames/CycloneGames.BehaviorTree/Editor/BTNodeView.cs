using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Components;
using CycloneGames.BehaviorTree.Runtime.Conditions;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    /// <summary>
    /// Visual representation of a behavior tree node in the GraphView editor.
    /// Handles node state visualization, port connections, and runtime state synchronization.
    /// </summary>
    public class BTNodeView : Node
    {
        public Action<BTNodeView> OnNodeSelected;
        public BTNode Node => GetRuntimeNode();
        private BTNode _node;
        public Port InputPort;
        public Port OutputPort;
        
        private BehaviorTreeView _treeView;
        
        internal void SetTreeView(BehaviorTreeView treeView)
        {
            _treeView = treeView;
        }
        
        /// <summary>
        /// Gets the runtime node instance if tree is cloned, otherwise returns the original node.
        /// Uses GUID-based matching to find the corresponding runtime node.
        /// </summary>
        private BTNode GetRuntimeNode()
        {
            if (_node == null) return null;
            
            if (!Application.isPlaying) return _node;
            
            string targetGUID = _node.GUID;
            if (string.IsNullOrEmpty(targetGUID))
            {
                return TryFindByReference();
            }
            
            if (_treeView != null && _treeView.Tree != null)
            {
                var treeViewTree = _treeView.Tree;
                if (treeViewTree.IsCloned && treeViewTree.Nodes != null)
                {
                    var runtimeNode = FindNodeByGUID(treeViewTree, targetGUID);
                    if (runtimeNode != null)
                    {
                        return runtimeNode;
                    }
                }
            }
            
            var nodeTree = _node.Tree;
            if (nodeTree != null && nodeTree.IsCloned && nodeTree.Nodes != null)
            {
                var runtimeNode = FindNodeByGUID(nodeTree, targetGUID);
                if (runtimeNode != null)
                {
                    return runtimeNode;
                }
            }
            
            var runners = UnityEngine.Object.FindObjectsOfType<BTRunnerComponent>();
            for (int i = 0; i < runners.Length; i++)
            {
                var runner = runners[i];
                if (runner != null && runner.Tree != null && runner.Tree.IsCloned)
                {
                    var runtimeNode = FindNodeByGUID(runner.Tree, targetGUID);
                    if (runtimeNode != null)
                    {
                        return runtimeNode;
                    }
                }
            }
            
            return TryFindByReference();
        }
        
        /// <summary>
        /// Attempts to find the runtime node by reference when GUID matching fails.
        /// Used as fallback when GUID is not available.
        /// </summary>
        private BTNode TryFindByReference()
        {
            if (_node == null) return null;
            
            if (_treeView != null && _treeView.Tree != null)
            {
                var treeViewTree = _treeView.Tree;
                if (treeViewTree.IsCloned && treeViewTree.Nodes != null)
                {
                    int nodeCount = treeViewTree.Nodes.Count;
                    for (int i = 0; i < nodeCount; i++)
                    {
                        if (treeViewTree.Nodes[i] == _node)
                        {
                            return _node;
                        }
                    }
                }
            }
            
            var nodeTree = _node.Tree;
            if (nodeTree != null && nodeTree.IsCloned && nodeTree.Nodes != null)
            {
                int nodeCount = nodeTree.Nodes.Count;
                for (int i = 0; i < nodeCount; i++)
                {
                    if (nodeTree.Nodes[i] == _node)
                    {
                        return _node;
                    }
                }
            }
            
            return _node;
        }
        
        /// <summary>
        /// Finds a node in the tree by its GUID.
        /// </summary>
        /// <param name="tree">Behavior tree to search</param>
        /// <param name="guid">GUID of the node to find</param>
        /// <returns>Found node or null</returns>
        private BTNode FindNodeByGUID(Runtime.BehaviorTree tree, string guid)
        {
            if (tree == null || tree.Nodes == null || string.IsNullOrEmpty(guid))
            {
                return null;
            }
            
            int nodeCount = tree.Nodes.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                var node = tree.Nodes[i];
                if (node != null && node.GUID == guid)
                {
                    return node;
                }
            }
            
            return null;
        }
        
        private Label _stateLabel;
        private Label _infoLabel;
        private VisualElement _infoContainer;
        private VisualElement _stateIndicator;
        
        /// <summary>
        /// Caches the last known final state (SUCCESS/FAILURE) to preserve node state
        /// even after BehaviorTree.Stop() resets all node states to NOT_ENTERED.
        /// </summary>
        private BTState _lastKnownState = BTState.NOT_ENTERED;
        
        /// <summary>
        /// Tracks the previous runtime state to detect when CompositeNode restarts.
        /// </summary>
        private BTState _previousRuntimeState = BTState.NOT_ENTERED;
        
        public BTState GetLastKnownState() => _lastKnownState;
        
        /// <summary>
        /// Restores a cached final state. Only final states (SUCCESS/FAILURE) are restored
        /// to prevent overwriting completed node states.
        /// </summary>
        public void RestoreLastKnownState(BTState state)
        {
            if (state == BTState.SUCCESS || state == BTState.FAILURE)
            {
                _lastKnownState = state;
            }
        }
        
        public void ClearStateCache()
        {
            _lastKnownState = BTState.NOT_ENTERED;
            _previousRuntimeState = BTState.NOT_ENTERED;
        }
        
        /// <summary>
        /// Gets the last known state of a child node by finding its corresponding node view.
        /// </summary>
        private BTState GetChildLastKnownState(BTNode childNode)
        {
            if (childNode == null || _treeView == null) return BTState.NOT_ENTERED;
            
            var nodeList = _treeView.nodes.ToList();
            int nodeCount = nodeList.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                {
                    var runtimeNode = nodeView.Node;
                    if (runtimeNode != null && runtimeNode.GUID == childNode.GUID)
                    {
                        return nodeView.GetLastKnownState();
                    }
                }
            }
            
            return childNode.State;
        }
        
        public static string ConvertToReadableName(string name)
        {
            name = name.Replace("Node", "").Replace("(Clone)", "").Replace("BT", "");
            for (int i = 1; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]))
                {
                    name = name.Insert(i, "#");
                    i++;
                }
            }
            name = name.Replace("#", "<br>");
            return name;
        }
        
        public BTNodeView(BTNode node) : base(AssetDatabase.GetAssetPath(Resources.Load<VisualTreeAsset>("BT_Node_Layout")))
        {
            this._node = node;
            this.title = ConvertToReadableName(node.name);
            this.viewDataKey = node.GUID;
            style.left = node.Position.x;
            style.top = node.Position.y;
            CreateInputPorts();
            CreateOutputPorts();
            SetUpClasses();
            CreateInfoElements();
            SetupTooltip();
        }
        
        public BTNodeView(BTNode node, Vector2 position) : base(AssetDatabase.GetAssetPath(Resources.Load<VisualTreeAsset>("BT_Node_Layout")))
        {
            this._node = node;
            this.title = ConvertToReadableName(node.name);
            this.viewDataKey = node.GUID;
            node.Position = position;

            style.left = position.x;
            style.top = position.y;

            CreateInputPorts();
            CreateOutputPorts();
            SetUpClasses();
            CreateInfoElements();
            SetupTooltip();
        }
        
        public override void OnSelected()
        {
            base.OnSelected();
            OnNodeSelected?.Invoke(this);
        }
        
        /// <summary>
        /// Updates the node position and records it for undo/redo.
        /// </summary>
        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Undo.RecordObject(_node, "Behavior Tree(Set Position)");
            _node.Position = new Vector2(newPos.xMin, newPos.yMin);
            EditorUtility.SetDirty(_node);
        }

        /// <summary>
        /// Updates the visual state of the node based on runtime node state.
        /// Caches final states (SUCCESS/FAILURE) to preserve them after tree stops.
        /// </summary>
        public void UpdateState()
        {
            if (_node == null) return;
            
            RemoveFromClassList("success");
            RemoveFromClassList("failure");
            RemoveFromClassList("running");
            RemoveFromClassList("not-entered");
            
            if (!Application.isPlaying)
            {
                if (_stateLabel != null)
                {
                    _stateLabel.text = "";
                    _stateLabel.style.display = DisplayStyle.None;
                }
                if (_infoLabel != null)
                {
                    UpdateInfoLabel();
                }
                return;
            }
            
            var runtimeNode = GetRuntimeNode();
            if (runtimeNode == null) return;
            
            BTState currentState = runtimeNode.State;
            bool isStarted = runtimeNode.IsStarted;
            
            if (runtimeNode is CompositeNode composite)
            {
                bool nodeRestarted = (_previousRuntimeState == BTState.NOT_ENTERED || _previousRuntimeState == BTState.SUCCESS || _previousRuntimeState == BTState.FAILURE)
                                     && currentState == BTState.RUNNING && isStarted;
                
                if (nodeRestarted && _treeView != null)
                {
                    int childrenCount = composite.Children.Count;
                    for (int i = 0; i < childrenCount; i++)
                    {
                        var child = composite.Children[i];
                        if (child == null) continue;
                        
                        var childView = _treeView.FindNodeView(child);
                        if (childView != null)
                        {
                            childView.ClearStateCache();
                        }
                    }
                }
            }
            
            _previousRuntimeState = currentState;
            
            if (currentState == BTState.SUCCESS || currentState == BTState.FAILURE)
            {
                _lastKnownState = currentState;
            }
            else if (currentState == BTState.RUNNING && isStarted)
            {
                if (_lastKnownState != BTState.SUCCESS && _lastKnownState != BTState.FAILURE)
                {
                    _lastKnownState = BTState.RUNNING;
                }
            }
            else if (currentState == BTState.NOT_ENTERED)
            {
                if (_lastKnownState == BTState.SUCCESS || _lastKnownState == BTState.FAILURE)
                {
                    currentState = _lastKnownState;
                }
                else
                {
                    if (_lastKnownState != BTState.SUCCESS && _lastKnownState != BTState.FAILURE)
                    {
                        _lastKnownState = BTState.NOT_ENTERED;
                    }
                }
            }
            else if (currentState == BTState.RUNNING && !isStarted)
            {
                if (_lastKnownState == BTState.SUCCESS || _lastKnownState == BTState.FAILURE)
                {
                    currentState = _lastKnownState;
                }
                else
                {
                    currentState = BTState.NOT_ENTERED;
                    if (_lastKnownState != BTState.SUCCESS && _lastKnownState != BTState.FAILURE)
                    {
                        _lastKnownState = BTState.NOT_ENTERED;
                    }
                }
            }
            
            if (_treeView != null && _treeView.Tree != null)
            {
                var tree = _treeView.Tree;
                bool treeCompleted = tree.TreeState == BTState.SUCCESS || tree.TreeState == BTState.FAILURE;
                
                if (treeCompleted && currentState == BTState.NOT_ENTERED && !isStarted)
                {
                    if (runtimeNode is BTRootNode)
                    {
                        _lastKnownState = tree.TreeState;
                        currentState = tree.TreeState;
                    }
                else if (_lastKnownState == BTState.NOT_ENTERED || _lastKnownState == BTState.RUNNING)
                {
                    if (runtimeNode is CompositeNode compositeNode)
                    {
                        bool allChildrenSucceeded = true;
                        bool hasChildren = false;
                        int childrenCount = compositeNode.Children.Count;
                        for (int i = 0; i < childrenCount; i++)
                        {
                            var child = compositeNode.Children[i];
                            if (child == null) continue;
                            hasChildren = true;
                            
                            BTState childState = child.State;
                            BTState childLastKnown = GetChildLastKnownState(child);
                            
                            if (childState == BTState.FAILURE || childLastKnown == BTState.FAILURE)
                            {
                                allChildrenSucceeded = false;
                                if (compositeNode is SequencerNode)
                                {
                                    break;
                                }
                            }
                            else if (childState != BTState.SUCCESS && childLastKnown != BTState.SUCCESS)
                            {
                                if (i < childrenCount - 1)
                                {
                                    allChildrenSucceeded = false;
                                }
                            }
                        }
                        
                        if (hasChildren)
                        {
                            if (compositeNode is SequencerNode)
                            {
                                if (allChildrenSucceeded && tree.TreeState == BTState.SUCCESS)
                                {
                                    _lastKnownState = BTState.SUCCESS;
                                    currentState = BTState.SUCCESS;
                                }
                                else if (!allChildrenSucceeded)
                                {
                                    _lastKnownState = BTState.FAILURE;
                                    currentState = BTState.FAILURE;
                                }
                            }
                            else if (compositeNode is SelectorNode)
                            {
                                bool anyChildSucceeded = false;
                                for (int i = 0; i < childrenCount; i++)
                                {
                                    var child = compositeNode.Children[i];
                                    if (child == null) continue;
                                    
                                    BTState childState = child.State;
                                    BTState childLastKnown = GetChildLastKnownState(child);
                                    
                                    if (childState == BTState.SUCCESS || childLastKnown == BTState.SUCCESS)
                                    {
                                        anyChildSucceeded = true;
                                        break;
                                    }
                                }
                                
                                if (anyChildSucceeded && tree.TreeState == BTState.SUCCESS)
                                {
                                    _lastKnownState = BTState.SUCCESS;
                                    currentState = BTState.SUCCESS;
                                }
                            }
                        }
                    }
                }
                }
            }
            
            UpdateInfoLabel();
            
            string stateText = "";
            switch (currentState)
            {
                case BTState.SUCCESS:
                    AddToClassList("success");
                    stateText = "SUCCESS";
                    break;
                case BTState.FAILURE:
                    AddToClassList("failure");
                    stateText = "FAILURE";
                    break;
                case BTState.RUNNING:
                    if (isStarted)
                    {
                        AddToClassList("running");
                        stateText = "RUNNING";
                    }
                    else
                    {
                        if (_lastKnownState == BTState.SUCCESS || _lastKnownState == BTState.FAILURE)
                        {
                            AddToClassList(_lastKnownState == BTState.SUCCESS ? "success" : "failure");
                            stateText = _lastKnownState == BTState.SUCCESS ? "SUCCESS" : "FAILURE";
                        }
                        else
                        {
                            AddToClassList("not-entered");
                            stateText = "NOT ENTERED";
                        }
                    }
                    break;
                case BTState.NOT_ENTERED:
                default:
                    if (_lastKnownState == BTState.SUCCESS || _lastKnownState == BTState.FAILURE)
                    {
                        AddToClassList(_lastKnownState == BTState.SUCCESS ? "success" : "failure");
                        stateText = _lastKnownState == BTState.SUCCESS ? "SUCCESS" : "FAILURE";
                        currentState = _lastKnownState;
                    }
                    else
                    {
                        AddToClassList("not-entered");
                        stateText = "NOT ENTERED";
                    }
                    break;
            }
            
            if (_stateLabel != null)
            {
                _stateLabel.text = stateText;
                _stateLabel.style.display = Application.isPlaying ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
        
        private void UpdateInfoLabel()
        {
            if (_infoLabel == null) return;
            
            string infoText = GetNodeSpecificInfo();
            if (!string.IsNullOrEmpty(infoText))
            {
                _infoLabel.text = infoText;
                _infoLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _infoLabel.style.display = DisplayStyle.None;
            }
        }
        
        private static readonly Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        
        private static FieldInfo GetCachedField(Type type, string fieldName)
        {
            string cacheKey = $"{type.FullName}.{fieldName}";
            if (!_fieldCache.TryGetValue(cacheKey, out var field))
            {
                field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    _fieldCache[cacheKey] = field;
                }
            }
            return field;
        }
        
        /// <summary>
        /// Gets node-specific runtime information for display (e.g., WaitNode remaining time).
        /// </summary>
        private string GetNodeSpecificInfo()
        {
            var node = GetRuntimeNode();
            if (node == null) return "";
            
            if (!Application.isPlaying)
            {
                if (node is CompositeNode composite)
                {
                    return $"Children: {composite.Children.Count}";
                }
                if (node is DecoratorNode decorator)
                {
                    return decorator.Child != null ? "Has Child" : "No Child";
                }
                return "";
            }
            
            switch (node)
            {
                case WaitNode waitNode:
                    var timeField = GetCachedField(typeof(WaitNode), "_time");
                    var durationField = GetCachedField(typeof(WaitNode), "_duration");
                    
                    if (timeField != null && durationField != null)
                    {
                        try
                        {
                            object timeObj = timeField.GetValue(waitNode);
                            object durationObj = durationField.GetValue(waitNode);
                            
                            float time = timeObj != null ? (float)timeObj : 0f;
                            float runtimeDuration = durationObj != null ? (float)durationObj : 0f;
                            float configuredDuration = waitNode.Duration;
                            float actualDuration = runtimeDuration > 0f ? runtimeDuration : configuredDuration;
                            
                            if (actualDuration <= 0f)
                            {
                                actualDuration = 1f;
                            }
                            
                            if (waitNode.State == BTState.RUNNING)
                            {
                                float remaining = Mathf.Max(0f, actualDuration - time);
                                float progress = actualDuration > 0 ? Mathf.Clamp01(time / actualDuration) * 100f : 0f;
                                return $"Remaining: {remaining:F2}s ({progress:F0}%)";
                            }
                            else if (waitNode.IsStarted)
                            {
                                float remaining = Mathf.Max(0f, actualDuration - time);
                                float progress = actualDuration > 0 ? Mathf.Clamp01(time / actualDuration) * 100f : 0f;
                                return $"Remaining: {remaining:F2}s ({progress:F0}%)";
                            }
                            else if (waitNode.State == BTState.SUCCESS)
                            {
                                return $"Completed: {actualDuration:F2}s";
                            }
                            else if (waitNode.State == BTState.FAILURE)
                            {
                                return "Failed";
                            }
                            else
                            {
                                return $"Duration: {configuredDuration:F2}s";
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[WaitNode] Reflection failed: {ex.Message}");
                            return $"State: {waitNode.State}";
                        }
                    }
                    break;
                    
                case SequencerNode sequencer:
                    var currentField = GetCachedField(typeof(SequencerNode), "_current");
                    if (currentField != null)
                    {
                        int current = (int)(currentField.GetValue(sequencer) ?? 0);
                        int childrenCount = sequencer.Children.Count;
                        return $"Current: {current + 1}/{childrenCount}";
                    }
                    break;
                    
                case SelectorNode selector:
                    var selectorCurrentField = GetCachedField(typeof(SelectorNode), "_current");
                    if (selectorCurrentField != null)
                    {
                        int selectorCurrent = (int)(selectorCurrentField.GetValue(selector) ?? 0);
                        int selectorChildrenCount = selector.Children.Count;
                        return $"Trying: {selectorCurrent + 1}/{selectorChildrenCount}";
                    }
                    break;
                    
                case ParallelNode parallel:
                    int runningCount = 0;
                    int parallelChildrenCount = parallel.Children.Count;
                    for (int i = 0; i < parallelChildrenCount; i++)
                    {
                        if (parallel.Children[i]?.State == BTState.RUNNING) runningCount++;
                    }
                    return $"Running: {runningCount}/{parallelChildrenCount}";
                    
                case RepeatNode repeat:
                    var repeatCountField = GetCachedField(typeof(RepeatNode), "_currentRepeatCount");
                    var totalCountField = GetCachedField(typeof(RepeatNode), "_repeatCount");
                    var useRandomField = GetCachedField(typeof(RepeatNode), "_useRandomRepeatCount");
                    if (repeatCountField != null && totalCountField != null)
                    {
                        int currentRepeat = (int)(repeatCountField.GetValue(repeat) ?? 0);
                        int totalRepeat = (int)(totalCountField.GetValue(repeat) ?? 1);
                        bool useRandom = useRandomField != null && (bool)(useRandomField.GetValue(repeat) ?? false);
                        
                        if (repeat.RepeatForever)
                        {
                            return $"Loop: {currentRepeat} (∞)";
                        }
                        else if (useRandom)
                        {
                            return $"Loop: {currentRepeat}/{totalRepeat} (Random)";
                        }
                        else
                        {
                            return $"Loop: {currentRepeat}/{totalRepeat}";
                        }
                    }
                    break;
            }
            
            return "";
        }
        
        /// <summary>
        /// Creates visual elements for displaying node state and runtime information.
        /// </summary>
        private void CreateInfoElements()
        {
            var titleContainer = this.Q<VisualElement>("title");
            if (titleContainer == null) return;
            
            _stateLabel = new Label
            {
                name = "state-label",
                text = ""
            };
            _stateLabel.AddToClassList("state-label");
            _stateLabel.style.display = DisplayStyle.None;
            titleContainer.Add(_stateLabel);
            
            _infoContainer = new VisualElement
            {
                name = "info-container"
            };
            _infoContainer.AddToClassList("info-container");
            
            _infoLabel = new Label
            {
                name = "info-label",
                text = ""
            };
            _infoLabel.AddToClassList("info-label");
            _infoContainer.Add(_infoLabel);
            
            if (_node is CompositeNode)
            {
                if (outputContainer != null)
                {
                }
            }
            else
            {
                var contents = this.Q<VisualElement>("contents");
                if (contents != null)
                {
                    contents.Add(_infoContainer);
                }
            }
        }
        
        /// <summary>
        /// Sets up the tooltip for the node using BTInfoAttribute if available.
        /// </summary>
        private void SetupTooltip()
        {
            var attribute = _node.GetType().GetCustomAttribute<BTInfoAttribute>();
            if (attribute != null && !string.IsNullOrEmpty(attribute.Description))
            {
                this.tooltip = $"{attribute.Category}\n{attribute.Description}";
            }
            else
            {
                this.tooltip = _node.GetType().Name;
            }
        }
        
        /// <summary>
        /// Creates input port for child nodes (circular connector at top).
        /// </summary>
        private void CreateInputPorts()
        {
            if (_node is DecoratorNode or CompositeNode or ActionNode or ConditionNode)
            {
                InputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
            }
            if (InputPort != null)
            {
                InputPort.portName = "";
                InputPort.style.flexDirection = FlexDirection.Column;
                InputPort.style.alignItems = Align.Center;
                InputPort.style.justifyContent = Justify.Center;
                InputPort.style.width = 24;
                InputPort.style.height = 24;
                InputPort.style.minWidth = 24;
                InputPort.style.minHeight = 24;
                InputPort.style.maxWidth = 24;
                InputPort.style.maxHeight = 24;
                InputPort.style.flexShrink = 0;
                
                InputPort.AddToClassList("bt-port");
                InputPort.AddToClassList("bt-input-port");
                
                var portElement = InputPort.Q("connector");
                if (portElement != null)
                {
                    portElement.style.width = 14;
                    portElement.style.height = 14;
                    portElement.style.minWidth = 14;
                    portElement.style.minHeight = 14;
                    portElement.style.maxWidth = 14;
                    portElement.style.maxHeight = 14;
                    portElement.style.borderTopLeftRadius = 7;
                    portElement.style.borderTopRightRadius = 7;
                    portElement.style.borderBottomLeftRadius = 7;
                    portElement.style.borderBottomRightRadius = 7;
                    portElement.style.alignSelf = Align.Center;
                    portElement.style.marginLeft = StyleKeyword.Auto;
                    portElement.style.marginRight = StyleKeyword.Auto;
                }
                
                inputContainer.Add(InputPort);
                InputPort.RegisterCallback<AttachToPanelEvent>(evt => UpdatePortConnectionState());
            }
        }

        /// <summary>
        /// Creates output port for parent nodes (rounded rectangle connector at bottom).
        /// Composite nodes support multiple connections, others support single connection.
        /// </summary>
        private void CreateOutputPorts()
        {
            if (_node is CompositeNode)
            {
                OutputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Multi, typeof(bool));
            }
            else if (_node is DecoratorNode or BTRootNode)
            {
                OutputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Single, typeof(bool));
            }
            if (OutputPort != null)
            {
                OutputPort.portName = "";
                OutputPort.style.flexDirection = FlexDirection.ColumnReverse;
                OutputPort.style.alignItems = Align.Center;
                OutputPort.style.justifyContent = Justify.Center;
                
                OutputPort.AddToClassList("bt-port");
                OutputPort.AddToClassList("bt-output-port");
                
                var portElement = OutputPort.Q("connector");
                if (portElement != null)
                {
                    portElement.style.width = 28;
                    portElement.style.height = 18;
                    portElement.style.minWidth = 28;
                    portElement.style.minHeight = 18;
                    portElement.style.maxWidth = 28;
                    portElement.style.maxHeight = 18;
                    portElement.style.borderTopLeftRadius = 8;
                    portElement.style.borderTopRightRadius = 8;
                    portElement.style.borderBottomLeftRadius = 8;
                    portElement.style.borderBottomRightRadius = 8;
                    portElement.style.alignSelf = Align.Center;
                    portElement.style.marginLeft = StyleKeyword.Auto;
                    portElement.style.marginRight = StyleKeyword.Auto;
                }
                
                if (_node is CompositeNode)
                {
                    outputContainer.Add(OutputPort);
                    if (_infoContainer != null && _infoContainer.parent == null)
                    {
                        outputContainer.Add(_infoContainer);
                    }
                }
                else
                {
                    outputContainer.Add(OutputPort);
                }
                OutputPort.RegisterCallback<AttachToPanelEvent>(evt => UpdatePortConnectionState());
            }
        }
        
        /// <summary>
        /// Updates the visual state of ports to reflect their connection status.
        /// </summary>
        public void UpdatePortConnectionState()
        {
            if (InputPort != null)
            {
                bool isConnected = InputPort.connections != null && InputPort.connections.Count() > 0;
                if (isConnected)
                {
                    InputPort.AddToClassList("connected");
                }
                else
                {
                    InputPort.RemoveFromClassList("connected");
                }
            }
            
            if (OutputPort != null)
            {
                bool isConnected = OutputPort.connections != null && OutputPort.connections.Count() > 0;
                if (isConnected)
                {
                    OutputPort.AddToClassList("connected");
                }
                else
                {
                    OutputPort.RemoveFromClassList("connected");
                }
            }
        }
        
        /// <summary>
        /// Adds CSS classes to the node based on its type for styling.
        /// </summary>
        private void SetUpClasses()
        {
            if (_node is CompositeNode)
            {
                AddToClassList("composite");
                return;
            }
            if (_node is BTRootNode)
            {
                AddToClassList("root");
                return;
            }
            if (_node is ActionNode)
            {
                AddToClassList("action");
                return;
            }
            if (_node is ConditionNode)
            {
                AddToClassList("condition");
                return;
            }
            if (_node is DecoratorNode)
            {
                AddToClassList("decorator");
                return;
            }
        }
    }
}
