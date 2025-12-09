using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System.Linq;
using System.Reflection;
using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Conditions;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BehaviorTreeView : GraphView
    {
        private const float NODE_X_GAP = 160;
        private const float NODE_Y_GAP = 160;
        private const float NODE_MIN_WIDTH = 130;
        private const float NODE_HEIGHT = 80;
        public new class UxmlFactory : UxmlFactory<BehaviorTreeView, GraphView.UxmlTraits> { }
        public Runtime.BehaviorTree Tree => _tree;
        public Action<BTNodeView> OnNodeSelectionChanged;
        private Runtime.BehaviorTree _tree;
        private List<BTNode> _copiedNodes = new List<BTNode>();
        private Vector2 _copiedTreePosition;
        
        private BTState _lastTreeState = BTState.NOT_ENTERED;
        public BehaviorTreeView()
        {
            Insert(0, new GridBackground());
            var contentZoomer = new ContentZoomer();
            contentZoomer.maxScale = 2.5f;
            contentZoomer.minScale = 0.1f;
            this.AddManipulator(contentZoomer);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var styleSheet = Resources.Load<StyleSheet>("BT_Editor_Style");
            styleSheets.Add(styleSheet);

            Undo.undoRedoPerformed += OnUndoRedo;
            RegisterCallback<MouseDownEvent>(OnMouseDown);
        }
        
        /// <summary>
        /// Handles Alt+Click to delete edges connecting nodes.
        /// </summary>
        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.altKey)
            {
                var target = evt.target as VisualElement;
                
                VisualElement current = target;
                while (current != null && current != this)
                {
                    if (current is Edge edge)
                    {
                        DeleteElements(new[] { edge });
                        evt.StopPropagation();
                        evt.PreventDefault();
                        return;
                    }
                    current = current.parent;
                }
                
                Vector2 localMousePos = evt.localMousePosition;
                if (contentViewContainer != null)
                {
                    localMousePos = contentViewContainer.WorldToLocal(evt.mousePosition);
                }
                
                foreach (var element in graphElements)
                {
                    if (element is Edge edge)
                    {
                        if (IsMouseNearEdge(edge, localMousePos, 15f))
                        {
                            DeleteElements(new[] { edge });
                            evt.StopPropagation();
                            evt.PreventDefault();
                            return;
                        }
                    }
                }
            }
        }
        
        private bool IsMouseNearEdge(Edge edge, Vector2 mousePos, float threshold)
        {
            if (edge?.input == null || edge.output == null) return false;
            
            try
            {
                var inputPort = edge.input;
                var outputPort = edge.output;
                
                var inputLayout = inputPort.layout;
                var outputLayout = outputPort.layout;
                
                Vector2 inputPos = new Vector2(
                    inputLayout.x + inputLayout.width * 0.5f,
                    inputLayout.y + inputLayout.height * 0.5f
                );
                Vector2 outputPos = new Vector2(
                    outputLayout.x + outputLayout.width * 0.5f,
                    outputLayout.y + outputLayout.height * 0.5f
                );
                
                if (inputPort.parent != null && inputPort.parent != contentViewContainer)
                {
                    var parentLayout = inputPort.parent.layout;
                    inputPos.x += parentLayout.x;
                    inputPos.y += parentLayout.y;
                }
                if (outputPort.parent != null && outputPort.parent != contentViewContainer)
                {
                    var parentLayout = outputPort.parent.layout;
                    outputPos.x += parentLayout.x;
                    outputPos.y += parentLayout.y;
                }
                
                float distance = DistanceToLineSegment(mousePos, inputPos, outputPos);
                return distance <= threshold;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Calculates the shortest distance from a point to a line segment.
        /// </summary>
        /// <param name="point">Point to measure distance from</param>
        /// <param name="lineStart">Start of the line segment</param>
        /// <param name="lineEnd">End of the line segment</param>
        /// <returns>Distance from point to line segment</returns>
        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float lineLength = line.magnitude;
            if (lineLength < 0.001f) return Vector2.Distance(point, lineStart);
            
            line /= lineLength;
            Vector2 pointToStart = point - lineStart;
            float projection = Vector2.Dot(pointToStart, line);
            projection = Mathf.Clamp(projection, 0f, lineLength);
            
            Vector2 closestPoint = lineStart + line * projection;
            return Vector2.Distance(point, closestPoint);
        }
        /// <summary>
        /// Clears all graph elements and unsubscribes from events.
        /// </summary>
        public void ClearView()
        {
            graphViewChanged -= OnGraphViewChanged;
            DeleteElements(graphElements);
            _tree = null;
        }
        /// <summary>
        /// Caches node states by GUID to preserve final states across PopulateView calls.
        /// </summary>
        private Dictionary<string, BTState> _stateCache = new Dictionary<string, BTState>();
        
        /// <summary>
        /// Populates the view with the given behavior tree. If the tree is the same reference,
        /// only updates states to preserve node view instances and their cached states.
        /// </summary>
        public void PopulateView(Runtime.BehaviorTree tree)
        {
            if (this._tree == tree)
            {
                UpdateNodeStates();
                return;
            }
            
            SaveStateCache();
            this._tree = tree;
            _lastTreeState = BTState.NOT_ENTERED;
            DrawGraph();
            RestoreStateCache();
        }
        
        /// <summary>
        /// Saves final states (SUCCESS/FAILURE) from existing node views before they are destroyed.
        /// </summary>
        private void SaveStateCache()
        {
            _stateCache.Clear();
            var nodeList = nodes.ToList();
            int nodeCount = nodeList.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                {
                    string guid = nodeView.Node.GUID;
                    if (!string.IsNullOrEmpty(guid))
                    {
                        BTState lastState = nodeView.GetLastKnownState();
                        if (lastState == BTState.SUCCESS || lastState == BTState.FAILURE)
                        {
                            _stateCache[guid] = lastState;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Restores cached final states to newly created node views.
        /// </summary>
        private void RestoreStateCache()
        {
            if (_stateCache.Count == 0) return;
            
            var nodeList = nodes.ToList();
            int nodeCount = nodeList.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                {
                    string guid = nodeView.Node.GUID;
                    if (!string.IsNullOrEmpty(guid) && _stateCache.ContainsKey(guid))
                    {
                        nodeView.RestoreLastKnownState(_stateCache[guid]);
                        nodeView.UpdateState();
                    }
                }
            }
        }

        /// <summary>
        /// Updates all node states. If tree is completed, proactively caches final states
        /// before BehaviorTree.Stop() resets them to NOT_ENTERED.
        /// Uses multi-pass inference to ensure all nodes get their correct states.
        /// Clears state cache when tree restarts from a completed state.
        /// </summary>
        public void UpdateNodeStates()
        {
            if (!Application.isPlaying || _tree == null || _tree.Nodes == null) return;
            
            BTState currentTreeState = _tree.TreeState;
            bool treeRestarted = (_lastTreeState == BTState.SUCCESS || _lastTreeState == BTState.FAILURE) 
                                 && currentTreeState == BTState.RUNNING;
            
            if (treeRestarted)
            {
                ClearAllNodeStateCache();
            }
            
            _lastTreeState = currentTreeState;
            
            bool treeCompleted = currentTreeState == BTState.SUCCESS || currentTreeState == BTState.FAILURE;
            
            var nodeList = nodes.ToList();
            int nodeCount = nodeList.Count;
            
            if (treeCompleted && _tree.Nodes != null)
            {
                int treeNodeCount = _tree.Nodes.Count;
                
                for (int pass = 0; pass < 3; pass++)
                {
                    for (int i = 0; i < treeNodeCount; i++)
                    {
                        var treeNode = _tree.Nodes[i];
                        if (treeNode == null) continue;
                        
                        BTState nodeState = treeNode.State;
                        bool isStarted = treeNode.IsStarted;
                        
                        for (int j = 0; j < nodeCount; j++)
                        {
                            if (nodeList[j] is BTNodeView nodeView && nodeView.Node != null)
                            {
                                var runtimeNode = nodeView.Node;
                                if (runtimeNode != null && runtimeNode.GUID == treeNode.GUID)
                                {
                                    BTState cachedState = nodeView.GetLastKnownState();
                                    
                                    if (nodeState == BTState.SUCCESS || nodeState == BTState.FAILURE)
                                    {
                                        nodeView.RestoreLastKnownState(nodeState);
                                    }
                                    else if (nodeState == BTState.NOT_ENTERED && !isStarted)
                                    {
                                        if (treeNode is BTRootNode)
                                        {
                                            nodeView.RestoreLastKnownState(_tree.TreeState);
                                        }
                                        else if (treeNode is CompositeNode composite)
                                        {
                                            BTState inferredState = InferCompositeNodeState(composite, _tree.TreeState, nodeList, nodeCount);
                                            if (inferredState == BTState.SUCCESS || inferredState == BTState.FAILURE)
                                            {
                                                nodeView.RestoreLastKnownState(inferredState);
                                            }
                                        }
                                        else if (cachedState == BTState.NOT_ENTERED || cachedState == BTState.RUNNING)
                                        {
                                            BTState inferredState = InferLeafNodeState(treeNode, nodeList, nodeCount);
                                            if (inferredState == BTState.SUCCESS || inferredState == BTState.FAILURE)
                                            {
                                                nodeView.RestoreLastKnownState(inferredState);
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                
                for (int i = 0; i < nodeCount; i++)
                {
                    if (nodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                    {
                        var runtimeNode = nodeView.Node;
                        if (runtimeNode == null) continue;
                        
                        BTState currentState = runtimeNode.State;
                        if (currentState == BTState.SUCCESS || currentState == BTState.FAILURE)
                        {
                            nodeView.RestoreLastKnownState(currentState);
                        }
                        else if (currentState == BTState.NOT_ENTERED && !runtimeNode.IsStarted)
                        {
                            if (runtimeNode is BTRootNode)
                            {
                                nodeView.RestoreLastKnownState(_tree.TreeState);
                            }
                            else if (runtimeNode is CompositeNode composite)
                            {
                                BTState inferredState = InferCompositeNodeState(composite, _tree.TreeState, nodeList, nodeCount);
                                if (inferredState == BTState.SUCCESS || inferredState == BTState.FAILURE)
                                {
                                    nodeView.RestoreLastKnownState(inferredState);
                                }
                            }
                            else
                            {
                                BTState cachedState = nodeView.GetLastKnownState();
                                if (cachedState == BTState.NOT_ENTERED || cachedState == BTState.RUNNING)
                                {
                                    BTState inferredState = InferLeafNodeState(runtimeNode, nodeList, nodeCount);
                                    if (inferredState == BTState.SUCCESS || inferredState == BTState.FAILURE)
                                    {
                                        nodeView.RestoreLastKnownState(inferredState);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeList[i] is BTNodeView nodeView && nodeView.Node != null)
                {
                    nodeView.UpdateState();
                }
            }
        }
        
        /// <summary>
        /// Infers the final state of a composite node based on its children's states and tree state.
        /// </summary>
        private BTState InferCompositeNodeState(CompositeNode composite, BTState treeState, List<UnityEditor.Experimental.GraphView.Node> nodeList, int nodeCount)
        {
            if (composite == null || composite.Children == null || composite.Children.Count == 0)
            {
                return BTState.NOT_ENTERED;
            }
            
            if (composite is SequencerNode)
            {
                bool allChildrenSucceeded = true;
                bool hasChildren = false;
                int childrenCount = composite.Children.Count;
                
                for (int i = 0; i < childrenCount; i++)
                {
                    var child = composite.Children[i];
                    if (child == null) continue;
                    hasChildren = true;
                    
                    BTState childState = child.State;
                    BTState childLastKnown = GetChildLastKnownState(child, nodeList, nodeCount);
                    
                    if (childState == BTState.FAILURE || childLastKnown == BTState.FAILURE)
                    {
                        allChildrenSucceeded = false;
                        break;
                    }
                    else if (childState != BTState.SUCCESS && childLastKnown != BTState.SUCCESS)
                    {
                        if (i < childrenCount - 1)
                        {
                            allChildrenSucceeded = false;
                        }
                    }
                }
                
                if (hasChildren && allChildrenSucceeded && treeState == BTState.SUCCESS)
                {
                    return BTState.SUCCESS;
                }
                else if (hasChildren && !allChildrenSucceeded)
                {
                    return BTState.FAILURE;
                }
            }
            else if (composite is SelectorNode)
            {
                bool anyChildSucceeded = false;
                int childrenCount = composite.Children.Count;
                
                for (int i = 0; i < childrenCount; i++)
                {
                    var child = composite.Children[i];
                    if (child == null) continue;
                    
                    BTState childState = child.State;
                    BTState childLastKnown = GetChildLastKnownState(child, nodeList, nodeCount);
                    
                    if (childState == BTState.SUCCESS || childLastKnown == BTState.SUCCESS)
                    {
                        anyChildSucceeded = true;
                        break;
                    }
                }
                
                if (anyChildSucceeded && treeState == BTState.SUCCESS)
                {
                    return BTState.SUCCESS;
                }
            }
            
            return BTState.NOT_ENTERED;
        }
        
        /// <summary>
        /// Infers the final state of a leaf node based on its parent's state.
        /// </summary>
        private BTState InferLeafNodeState(BTNode leafNode, List<UnityEditor.Experimental.GraphView.Node> nodeList, int nodeCount)
        {
            if (leafNode == null || _tree == null || _tree.Nodes == null) return BTState.NOT_ENTERED;
            
            int treeNodeCount = _tree.Nodes.Count;
            for (int i = 0; i < treeNodeCount; i++)
            {
                var treeNode = _tree.Nodes[i];
                if (treeNode == null) continue;
                
                if (treeNode is CompositeNode composite)
                {
                    int childrenCount = composite.Children.Count;
                    for (int j = 0; j < childrenCount; j++)
                    {
                        var child = composite.Children[j];
                        if (child != null && child.GUID == leafNode.GUID)
                        {
                            BTState parentState = treeNode.State;
                            BTState parentLastKnown = GetChildLastKnownState(treeNode, nodeList, nodeCount);
                            
                            if (composite is SequencerNode)
                            {
                                if ((parentState == BTState.SUCCESS || parentLastKnown == BTState.SUCCESS) && _tree.TreeState == BTState.SUCCESS)
                                {
                                    bool isLastChild = (j == childrenCount - 1);
                                    if (isLastChild)
                                    {
                                        return BTState.SUCCESS;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }
            
            return BTState.NOT_ENTERED;
        }
        
        /// <summary>
        /// Gets the last known state of a child node by finding its corresponding node view.
        /// </summary>
        private BTState GetChildLastKnownState(BTNode childNode, List<UnityEditor.Experimental.GraphView.Node> nodeList, int nodeCount)
        {
            if (childNode == null) return BTState.NOT_ENTERED;
            
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
        
        /// <summary>
        /// Clears state cache for all node views when tree restarts.
        /// </summary>
        private void ClearAllNodeStateCache()
        {
            var nodeList = nodes.ToList();
            int nodeCount = nodeList.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeList[i] is BTNodeView nodeView)
                {
                    nodeView.ClearStateCache();
                }
            }
        }
        /// <summary>
        /// Returns all ports that are compatible for connection with the start port.
        /// Ports must have opposite directions and belong to different nodes.
        /// </summary>
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var portList = ports.ToList();
            var compatiblePorts = new List<Port>(portList.Count);
            int portCount = portList.Count;
            for (int i = 0; i < portCount; i++)
            {
                var endPort = portList[i];
                if (endPort.direction != startPort.direction && endPort.node != startPort.node)
                {
                    compatiblePorts.Add(endPort);
                }
            }
            return compatiblePorts;
        }

        public override EventPropagation DeleteSelection()
        {
            var nodes = selection.OfType<BTNodeView>().ToList();
            if (nodes.Any(n => n.Node is BTRootNode))
            {
                EditorUtility.DisplayDialog("Error", "Cannot delete root node", "OK");
                return EventPropagation.Stop;
            }
            EditorUtility.SetDirty(_tree);
            return base.DeleteSelection();
        }
        private void OnUndoRedo()
        {
            if (_tree == null) return;
            DrawGraph();
            _tree.OnValidate();
            EditorUtility.SetDirty(_tree);
        }
        /// <summary>
        /// Draws the entire behavior tree graph by creating node views and connecting edges.
        /// </summary>
        private void DrawGraph()
        {
            graphViewChanged -= OnGraphViewChanged;
            DeleteElements(graphElements);
            graphViewChanged += OnGraphViewChanged;
            if (_tree.Root == null)
            {
                _tree.Root = _tree.CreateNode(typeof(BTRootNode));
                EditorUtility.SetDirty(_tree);
                AssetDatabase.SaveAssets();
            }

            int nodeCount = _tree.Nodes.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                var node = _tree.Nodes[i];
                if (node != null)
                {
                    CreateNodeView(node);
                }
            }
            
            for (int i = 0; i < nodeCount; i++)
            {
                var node = _tree.Nodes[i];
                if (node == null) continue;
                
                var children = _tree.GetChildren(node);
                int childrenCount = children.Count;
                var parentView = FindNodeView(node);
                if (parentView == null) continue;
                
                for (int j = 0; j < childrenCount; j++)
                {
                    var child = children[j];
                    if (child == null) continue;
                    
                    var childView = FindNodeView(child);
                    if (childView == null) continue;
                    
                    if (parentView.OutputPort != null && childView.InputPort != null)
                    {
                        var edge = parentView.OutputPort.ConnectTo(childView.InputPort);
                        AddElement(edge);
                        parentView.UpdatePortConnectionState();
                        childView.UpdatePortConnectionState();
                    }
                }
            }
        }
        private GraphViewChange OnGraphViewChanged(GraphViewChange graphviewChange)
        {
            if (graphviewChange.elementsToRemove != null)
            {
                int removeCount = graphviewChange.elementsToRemove.Count;
                for (int i = 0; i < removeCount; i++)
                {
                    var elem = graphviewChange.elementsToRemove[i];
                    if (elem is BTNodeView nodeView)
                    {
                        _tree.DeleteNode(nodeView.Node);
                    }
                    else if (elem is Edge edge)
                    {
                        if (edge.input?.node is BTNodeView inputNodeView && edge.output?.node is BTNodeView outputNodeView)
                        {
                            _tree.RemoveChild(outputNodeView.Node, inputNodeView.Node);
                            inputNodeView.UpdatePortConnectionState();
                            outputNodeView.UpdatePortConnectionState();
                        }
                    }
                }
            }
            if (graphviewChange.edgesToCreate != null)
            {
                int createCount = graphviewChange.edgesToCreate.Count;
                for (int i = 0; i < createCount; i++)
                {
                    var edge = graphviewChange.edgesToCreate[i];
                    if (edge.input?.node is BTNodeView inputNodeView && edge.output?.node is BTNodeView outputNodeView)
                    {
                        _tree.AddChild(outputNodeView.Node, inputNodeView.Node);
                        inputNodeView.UpdatePortConnectionState();
                        outputNodeView.UpdatePortConnectionState();
                    }
                }
            }
            return graphviewChange;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            var mousePosition = viewTransform.matrix.inverse.MultiplyPoint(evt.localMousePosition);

            var actionTypes = TypeCache.GetTypesDerivedFrom<ActionNode>();
            var conditionTypes = TypeCache.GetTypesDerivedFrom<ConditionNode>();
            var compositeTypes = TypeCache.GetTypesDerivedFrom<CompositeNode>();
            var decoratorTypes = TypeCache.GetTypesDerivedFrom<DecoratorNode>();
            
            var sortedActionTypes = new List<Type>(actionTypes);
            sortedActionTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var type in sortedActionTypes)
            {
                if (type.IsAbstract) continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction($"ActionNode/{category}/{type.Name}", a => CreateNode(type, mousePosition));
            }
            
            var sortedConditionTypes = new List<Type>(conditionTypes);
            sortedConditionTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var type in sortedConditionTypes)
            {
                if (type.IsAbstract) continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction($"ConditionNode/{category}/{type.Name}", a => CreateNode(type, mousePosition));
            }
            
            var sortedCompositeTypes = new List<Type>(compositeTypes);
            sortedCompositeTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var type in sortedCompositeTypes)
            {
                if (type.IsAbstract) continue;
                evt.menu.AppendAction($"CompositeNode/{type.Name}", a => CreateNode(type, mousePosition));
            }
            
            var sortedDecoratorTypes = new List<Type>(decoratorTypes);
            sortedDecoratorTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var type in sortedDecoratorTypes)
            {
                if (type.IsAbstract) continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction($"DecoratorNode/{category}/{type.Name}", a => CreateNode(type, mousePosition));
            }
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Sort Nodes", a => SortNodes());
            evt.menu.AppendAction("Return to Root", a => ReturnToRoot());
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Find Tree", a => EditorGUIUtility.PingObject(_tree));

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Copy", a => CopySelectedNodes(mousePosition));
            if (_copiedNodes.Count > 0)
            {
                evt.menu.AppendAction("Paste", a => PasteCopiedNodes(mousePosition));
            }
        }
        /// <summary>
        /// Copies selected nodes for pasting.
        /// </summary>
        private void CopySelectedNodes(Vector2 mousePosition)
        {
            _copiedNodes.Clear();
            _copiedTreePosition = mousePosition;
            var selectedNodes = selection.OfType<BTNodeView>().Select(n => n.Node).ToList();
            foreach (var node in selectedNodes)
            {
                if (node is BTRootNode)
                {
                    EditorUtility.DisplayDialog("Error", "Cannot copy root node", "OK");
                    return;
                }
                _copiedNodes.Add(node);
            }
        }
        
        /// <summary>
        /// Pastes copied nodes to the tree at the specified position.
        /// </summary>
        private void PasteCopiedNodes(Vector2 mousePosition)
        {
            Vector2 offset = mousePosition - _copiedTreePosition;
            List<BTNode> newNodes = new List<BTNode>();

            foreach (var copiedNode in _copiedNodes)
            {
                var newNode = copiedNode.Clone();
                newNode.Tree = _tree;
                newNode.Position = copiedNode.Position + offset;
                newNode.GUID = copiedNode.GUID;
                newNodes.Add(newNode);
                _tree.AddNode(newNode);
            }
            List<CompositeNode> compositeNodes = new List<CompositeNode>();
            List<DecoratorNode> decoratorNodes = new List<DecoratorNode>();

            foreach (var newNode in newNodes)
            {
                if (newNode is CompositeNode compositeNode)
                {
                    compositeNodes.Add(compositeNode);
                }
                if (newNode is DecoratorNode decoratorNode)
                {
                    decoratorNodes.Add(decoratorNode);
                }
            }

            int compositeCount = compositeNodes.Count;
            for (int i = 0; i < compositeCount; i++)
            {
                var compositeNode = compositeNodes[i];
                CompositeNode copiedCompositeNode = null;
                int copiedNodesCount = _copiedNodes.Count;
                for (int j = 0; j < copiedNodesCount; j++)
                {
                    if (_copiedNodes[j]?.GUID == compositeNode.GUID)
                    {
                        copiedCompositeNode = _copiedNodes[j] as CompositeNode;
                        break;
                    }
                }
                if (copiedCompositeNode == null) continue;
                
                int childrenCount = copiedCompositeNode.Children.Count;
                for (int j = 0; j < childrenCount; j++)
                {
                    var childGuid = copiedCompositeNode.Children[j]?.GUID;
                    if (string.IsNullOrEmpty(childGuid)) continue;
                    
                    int newNodeListCount = newNodes.Count;
                    for (int k = 0; k < newNodeListCount; k++)
                    {
                        if (newNodes[k]?.GUID == childGuid)
                        {
                            compositeNode.Children.Add(newNodes[k]);
                            break;
                        }
                    }
                }
            }

            int decoratorCount = decoratorNodes.Count;
            for (int i = 0; i < decoratorCount; i++)
            {
                var decoratorNode = decoratorNodes[i];
                DecoratorNode copiedDecoratorNode = null;
                int copiedNodesCount = _copiedNodes.Count;
                for (int j = 0; j < copiedNodesCount; j++)
                {
                    if (_copiedNodes[j]?.GUID == decoratorNode.GUID)
                    {
                        copiedDecoratorNode = _copiedNodes[j] as DecoratorNode;
                        break;
                    }
                }
                if (copiedDecoratorNode == null || copiedDecoratorNode.Child == null) continue;
                
                var childGuid = copiedDecoratorNode.Child.GUID;
                int newNodeListCount = newNodes.Count;
                for (int j = 0; j < newNodeListCount; j++)
                {
                    if (newNodes[j]?.GUID == childGuid)
                    {
                        decoratorNode.Child = newNodes[j];
                        break;
                    }
                }
            }

            int newNodesCount = newNodes.Count;
            for (int i = 0; i < newNodesCount; i++)
            {
                newNodes[i].GUID = System.Guid.NewGuid().ToString();
            }

            DrawGraph();
        }
        /// <summary>
        /// Centers the view on the root node and resets zoom level.
        /// </summary>
        public void ReturnToRoot()
        {
            var root = GetNodeByGuid(_tree.Root.GUID) as BTNodeView;
            if (root != null)
            {
                viewTransform.scale = Vector3.one;
                viewTransform.position = -root.GetPosition().position;
            }
        }
        internal BTNodeView FindNodeView(BTNode node)
        {
            return GetNodeByGuid(node.GUID) as BTNodeView;
        }

        #region Sort Nodes
        /// <summary>
        /// Sort all nodes in the tree
        /// </summary>
        public void SortNodes()
        {
            if (_tree.Root == null) return;
            SortNodes(Vector3.zero, _tree.Root);
            int nodeCount = _tree.Nodes.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                var node = _tree.Nodes[i];
                if (node == null) continue;
                var nodeView = FindNodeView(node);
                if (nodeView != null)
                {
                    nodeView.SetPosition(new Rect(node.Position, Vector2.zero));
                }
            }
        }
        /// <summary>
        /// Calculates the total width required for a subtree, including all descendants.
        /// Used for centering parent nodes above their children.
        /// </summary>
        /// <param name="node">Root node of the subtree</param>
        /// <returns>Total width required for the subtree</returns>
        private float CalculateSubtreeWidth(BTNode node)
        {
            List<BTNode> children = _tree.GetChildren(node);
            if (children.Count == 0)
            {
                return NODE_X_GAP;
            }
            
            List<BTNode> childrenCopy = new List<BTNode>(children);
            
            float totalWidth = 0;
            foreach (var child in childrenCopy)
            {
                totalWidth += CalculateSubtreeWidth(child);
            }
            
            return Mathf.Max(totalWidth, NODE_X_GAP);
        }
        
        /// <summary>
        /// Recursively positions nodes in a hierarchical tree layout.
        /// Centers parent nodes above their children and ensures proper spacing.
        /// </summary>
        /// <param name="position">Starting position (x, y)</param>
        /// <param name="node">Node to position</param>
        /// <returns>Vector3 containing (rightmostX, y, subtreeWidth)</returns>
        private Vector3 SortNodes(Vector3 position, BTNode node)
        {
            List<BTNode> children = _tree.GetChildren(node);
            List<BTNode> childrenCopy = new List<BTNode>(children);
            
            if (childrenCopy.Count == 0)
            {
                node.Position = new Vector2(position.x, position.y);
                return new Vector3(position.x + NODE_X_GAP, position.y, NODE_X_GAP);
            }

            if (childrenCopy.Count == 1)
            {
                float childWidth = CalculateSubtreeWidth(childrenCopy[0]);
                Vector3 childResult = SortNodes(new Vector3(position.x, position.y + NODE_Y_GAP, 0), childrenCopy[0]);
                float childCenterX = position.x + childWidth / 2;
                float singleParentX = childCenterX - NODE_X_GAP / 2;
                node.Position = new Vector2(singleParentX, position.y);
                return new Vector3(childResult.x, position.y, childWidth);
            }

            float currentX = position.x;
            float childY = position.y + NODE_Y_GAP;
            float totalWidth = 0;
            
            List<float> subtreeWidths = new List<float>();
            foreach (var child in childrenCopy)
            {
                float width = CalculateSubtreeWidth(child);
                subtreeWidths.Add(width);
                totalWidth += width;
            }
            
            currentX = position.x;
            float rightmostX = position.x;
            for (int i = 0; i < childrenCopy.Count; i++)
            {
                float subtreeWidth = subtreeWidths[i];
                Vector3 childResult = SortNodes(new Vector3(currentX, childY, 0), childrenCopy[i]);
                rightmostX = Mathf.Max(rightmostX, childResult.x);
                currentX += subtreeWidth;
            }
            
            float childrenCenterX = position.x + totalWidth / 2;
            float parentX = childrenCenterX - NODE_X_GAP / 2;
            node.Position = new Vector2(parentX, position.y);
            
            return new Vector3(rightmostX, position.y, totalWidth);
        }
        #endregion
        #region Create Node Methods
        private void CreateNode(Type type, Vector2 position)
        {
            var node = _tree.CreateNode(type);
            EditorUtility.SetDirty(_tree);
            CreateNodeView(node, position);
        }
        private void CreateNodeView(BTNode node, Vector2 position)
        {
            BTNodeView nodeView = new BTNodeView(node, position);
            nodeView.SetTreeView(this);
            nodeView.OnNodeSelected += OnNodeSelectionChanged;
            AddElement(nodeView);
        }
        private void CreateNodeView(BTNode node)
        {
            BTNodeView nodeView = new BTNodeView(node);
            nodeView.SetTreeView(this);
            nodeView.OnNodeSelected += OnNodeSelectionChanged;
            AddElement(nodeView);
        }
        #endregion

        #region Attribute Methods
        /// <summary>
        /// Gets a custom attribute of the specified type from a node type.
        /// </summary>
        private T GetAttribute<T>(Type type) where T : Attribute
        {
            return type.GetCustomAttributes<T>().FirstOrDefault();
        }

        /// <summary>
        /// Gets the category name for a node type from its BTInfoAttribute.
        /// </summary>
        private string GetNodeCategory(Type type)
        {
            var attribute = GetAttribute<BTInfoAttribute>(type);
            return attribute != null ? attribute.Category : "Base";
        }
        #endregion
    }
}