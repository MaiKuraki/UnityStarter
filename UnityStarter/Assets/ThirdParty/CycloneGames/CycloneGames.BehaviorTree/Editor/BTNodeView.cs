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
        public BTNode Node => _node;
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
        private CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode GetRuntimeNode()
        {
            if (_node == null) return null;

            if (!Application.isPlaying) return null; // Editor mode doesn't have "RuntimeNode" unless debugging

            // Try to find the runner
            var runners = UnityEngine.Object.FindObjectsOfType<BTRunnerComponent>();
            for (int i = 0; i < runners.Length; i++)
            {
                var runner = runners[i];
                if (runner != null && runner.RuntimeTree != null)
                {
                    // Check if this runner is running the tree we are editing
                    // This matching logic might need improvement depending on if we are debugging a specific instance
                    if (runner.Tree == _node.Tree || runner.Tree.name == _node.Tree.name)
                    {
                        var runtimeNode = runner.RuntimeTree.GetNodeByGUID(_node.GUID);
                        if (runtimeNode != null) return runtimeNode;
                    }
                }
            }

            return null;
        }

        private CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode _runtimeNode;
        public CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode RuntimeNode => GetRuntimeNode();

        // Helper to avoid repeated lookups if we cache it per frame? 
        // For now calling GetRuntimeNode() is safer as Runner might change.


        private Label _stateLabel;
        private Label _infoLabel;
        private VisualElement _infoContainer;
        private VisualElement _stateIndicator;

        // Progress bar elements for WaitNode and Sequencer/Selector
        private VisualElement _progressBarContainer;
        private VisualElement _progressBarFill;
        private Label _progressLabel;

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

        private BTState ToBTState(CycloneGames.BehaviorTree.Runtime.Core.RuntimeState state)
        {
            switch (state)
            {
                case CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success: return BTState.SUCCESS;
                case CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Failure: return BTState.FAILURE;
                case CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Running: return BTState.RUNNING;
                default: return BTState.NOT_ENTERED;
            }
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

            BTState currentState = ToBTState(runtimeNode.State);
            bool isStarted = runtimeNode.IsStarted;

            // Note: Composite restart detection logic simplified for now
            // We focus on current state

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
                // Keep last known state if tree finished
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
                    else if (_lastKnownState == BTState.SUCCESS || _lastKnownState == BTState.FAILURE)
                    {
                        AddToClassList(_lastKnownState == BTState.SUCCESS ? "success" : "failure");
                        stateText = _lastKnownState == BTState.SUCCESS ? "SUCCESS" : "FAILURE";
                    }
                    else
                    {
                        AddToClassList("not-entered");
                        stateText = "NOT ENTERED";
                    }
                    break;
                default:
                    AddToClassList("not-entered");
                    stateText = "NOT ENTERED";
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

            // Update progress bar for WaitNode
            UpdateProgressBar();
        }

        /// <summary>
        /// Updates the progress bar fill and label for WaitNode during runtime.
        /// </summary>
        private void UpdateProgressBar()
        {
            if (_progressBarContainer == null || _progressBarFill == null || _progressLabel == null)
                return;

            if (!Application.isPlaying)
            {
                // Show empty bar in editor mode
                _progressBarFill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));

                // Get duration from WaitNode for display
                if (_node is WaitNode waitNode)
                {
                    var durationField = typeof(WaitNode).GetField("_duration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var duration = durationField != null ? (float)durationField.GetValue(waitNode) : 0f;
                    _progressLabel.text = $"{duration:F1}s";
                    _progressBarContainer.style.display = DisplayStyle.Flex;
                }
                return;
            }

            // Runtime mode - get progress from RuntimeWaitNode
            var runtimeNode = GetRuntimeNode();
            if (runtimeNode is CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeWaitNode waitRuntimeNode)
            {
                float duration = waitRuntimeNode.Duration;
                float elapsed = Time.time - waitRuntimeNode.StartTime;
                float progress = duration > 0 ? Mathf.Clamp01(elapsed / duration) : 0f;

                // Set fill width
                _progressBarFill.style.width = new StyleLength(new Length(progress * 100f, LengthUnit.Percent));

                // Update colors based on state
                var state = waitRuntimeNode.State;
                if (state == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Running)
                {
                    _progressBarFill.style.backgroundColor = new Color(0.15f, 0.35f, 0.17f, 0.9f); // Dark green 90% opacity
                    float remaining = Mathf.Max(0f, duration - elapsed);
                    _progressLabel.text = $"{remaining:F1}s / {duration:F1}s";
                }
                else if (state == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success)
                {
                    _progressBarFill.style.backgroundColor = new Color(0.12f, 0.6f, 0.12f, 0.9f); // Green 90% opacity
                    _progressBarFill.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
                    _progressLabel.text = $"Done ({duration:F1}s)";
                }
                else
                {
                    _progressBarFill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
                    _progressLabel.text = $"{duration:F1}s";
                }

                _progressBarContainer.style.display = DisplayStyle.Flex;
            }
        }

        /// <summary>
        /// Gets node-specific runtime information for display (e.g., WaitNode remaining time).
        /// Also shows static configuration info when not in play mode.
        /// </summary>
        private string GetNodeSpecificInfo()
        {
            // Editor mode - show static configuration from BTNode (ScriptableObject)
            if (!Application.isPlaying)
            {
                return GetEditorModeInfo();
            }

            // Runtime mode - show live runtime state from RuntimeNode
            var runtimeNode = GetRuntimeNode();
            if (runtimeNode == null)
            {
                // Fallback to editor info if runtime node not available
                return GetEditorModeInfo();
            }

            return GetRuntimeModeInfo(runtimeNode);
        }

        private string GetEditorModeInfo()
        {
            if (_node == null) return "";

            // Show static configuration based on node type
            switch (_node)
            {
                case DebugLogNode logNode:
                    // Access message via serialized field using reflection
                    var msgField = typeof(DebugLogNode).GetField("_message", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var msg = msgField?.GetValue(logNode) as string ?? "";
                    return $"\"{TruncateText(msg, 20)}\"";

                case WaitNode waitNode:
                    var durationField = typeof(WaitNode).GetField("_duration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var duration = durationField != null ? (float)durationField.GetValue(waitNode) : 0f;
                    return $"Duration: {duration:F2}s";

                case Runtime.Nodes.Actions.BlackBoards.MessagePassNode passNode:
                    var keyFieldP = typeof(Runtime.Nodes.Actions.BlackBoards.MessagePassNode).GetField("_key", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var msgFieldP = typeof(Runtime.Nodes.Actions.BlackBoards.MessagePassNode).GetField("_message", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var keyP = keyFieldP?.GetValue(passNode) as string ?? "";
                    var msgP = msgFieldP?.GetValue(passNode) as string ?? "";
                    return $"[{keyP}] = \"{TruncateText(msgP, 15)}\"";

                case Runtime.Conditions.BlackBoards.MessageReceiveNode receiveNode:
                    var keyFieldR = typeof(Runtime.Conditions.BlackBoards.MessageReceiveNode).GetField("_key", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var msgFieldR = typeof(Runtime.Conditions.BlackBoards.MessageReceiveNode).GetField("_message", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var keyR = keyFieldR?.GetValue(receiveNode) as string ?? "";
                    var msgR = msgFieldR?.GetValue(receiveNode) as string ?? "";
                    return $"[{keyR}] == \"{TruncateText(msgR, 15)}\"";

                case RepeatNode repeatNode:
                    if (repeatNode.RepeatForever)
                        return "Repeat: Forever";
                    else if (repeatNode.UseRandomRepeatCount)
                        return "Repeat: Random";
                    else
                        return $"Repeat: (configured)";

                case CompositeNode composite:
                    return $"Children: {composite.Children.Count}";

                case DecoratorNode decorator:
                    return decorator.Child != null ? "Has Child" : "No Child";
            }

            return "";
        }

        private string GetRuntimeModeInfo(CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode node)
        {
            switch (node)
            {
                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeWaitNode waitNode:
                    float time = (Time.time - waitNode.StartTime);
                    float actualDuration = waitNode.Duration;

                    if (waitNode.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Running)
                    {
                        float remaining = Mathf.Max(0f, actualDuration - time);
                        float progress = actualDuration > 0 ? Mathf.Clamp01(time / actualDuration) * 100f : 0f;
                        return $"Remaining: {remaining:F2}s ({progress:F0}%)";
                    }
                    else if (waitNode.State == CycloneGames.BehaviorTree.Runtime.Core.RuntimeState.Success)
                    {
                        return $"Completed: {actualDuration:F2}s";
                    }
                    return $"Duration: {actualDuration:F2}s";

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeDebugLogNode logNode:
                    return $"\"{TruncateText(logNode.Message, 20)}\"";

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeMessagePassNode passNode:
                    return $"[{passNode.KeyHash}] = \"{TruncateText(passNode.Message, 15)}\"";

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeRepeatNode repeatNode:
                    if (repeatNode.RepeatForever)
                        return $"Count: {repeatNode.CurrentRepeatCount}";
                    else
                        return $"{repeatNode.CurrentRepeatCount}/{repeatNode.RepeatCount}";

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSequencer sequencer:
                    return $"Current: {sequencer.CurrentIndex + 1}/{sequencer.Children.Count}";

                case CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSelector selector:
                    return $"Trying: {selector.CurrentIndex + 1}/{selector.Children.Count}";
            }

            // Fallback to editor info for nodes without runtime-specific display
            return GetEditorModeInfo();
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength - 3) + "...";
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
